using System.Diagnostics;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;
using Haven.PluginFixture;

namespace Haven.Infrastructure.Tests;

public sealed class ExtensionPluginEndToEndTests
{
    private const string PackageId = "worker28.fixture";
    private const string ValidRepositoryUri = "https://github.com/example/haven-worker28-fixture";
    private const string InvalidRepositoryUri = "https://github.com/example/haven-worker28-invalid";
    private const string UnderdeclaredRepositoryUri = "https://github.com/example/haven-worker28-underdeclared";
    private static readonly ExtensionPermission RequiredPermissions =
        ExtensionPermission.ProcessExecution | ExtensionPermission.ProjectRead;

    [Fact]
    public async Task GitHub_style_discovery_and_permission_review_are_atomic()
    {
        using var paths = new TestPaths();
        var fixtureRoot = await CreateRepositoryAsync(paths, "valid-source", CreateManifest());
        var transport = new FixtureSourceTransport(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [ValidRepositoryUri] = fixtureRoot
        });
        var database = await InitializeDatabaseAsync(paths);
        var extensions = new ExtensionRepository(database);
        var capabilities = new CapabilityRepository(database);
        await using var events = new ExecutionEventHub(new ExecutionEventRepository(database));
        await using var runtime = new NativePluginRuntime(capabilities, new CatalogRepository(database), new NativePluginProcessFactory(), events);
        var manager = new ExtensionManager(extensions, transport, new ExtensionManifestValidator(), runtime, paths);
        var source = CreateSource(ValidRepositoryUri);

        await manager.AddSourceAsync(source, CancellationToken.None);
        var candidate = Assert.Single(await manager.RefreshAsync(source.Id, CancellationToken.None));
        Assert.Equal(PackageId, candidate.Manifest.PackageId);
        Assert.Equal(ExtensionInstallState.Available, candidate.State);
        Assert.False(string.IsNullOrWhiteSpace(candidate.ContentHash));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            manager.InstallAsync(candidate, ExtensionPermission.ProcessExecution, CancellationToken.None));

        Assert.Empty(await extensions.GetInstalledAsync(CancellationToken.None));
        Assert.False(Directory.Exists(Path.Combine(paths.DataDirectory, "extensions", PackageId)));
    }

    [Fact]
    public async Task Invalid_and_underdeclared_manifests_are_rejected_before_installation()
    {
        using var paths = new TestPaths();
        var invalidRoot = await CreateRepositoryAsync(
            paths,
            "invalid-source",
            new ExtensionManifestDocument(99, Array.Empty<ExtensionPackageManifest>()),
            copyFixture: false);
        var underdeclaredRoot = await CreateRepositoryAsync(
            paths,
            "underdeclared-source",
            CreateManifest(
                requestedPermissions: ExtensionPermission.ProjectRead,
                capabilityPermissions: ExtensionPermission.ProjectRead));
        var transport = new FixtureSourceTransport(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [InvalidRepositoryUri] = invalidRoot,
            [UnderdeclaredRepositoryUri] = underdeclaredRoot
        });
        var database = await InitializeDatabaseAsync(paths);
        var extensions = new ExtensionRepository(database);
        var capabilities = new CapabilityRepository(database);
        await using var events = new ExecutionEventHub(new ExecutionEventRepository(database));
        await using var runtime = new NativePluginRuntime(capabilities, new CatalogRepository(database), new NativePluginProcessFactory(), events);
        var manager = new ExtensionManager(extensions, transport, new ExtensionManifestValidator(), runtime, paths);

        var invalidSource = CreateSource(InvalidRepositoryUri);
        await manager.AddSourceAsync(invalidSource, CancellationToken.None);
        await Assert.ThrowsAsync<InvalidDataException>(() => manager.RefreshAsync(invalidSource.Id, CancellationToken.None));

        var underdeclaredSource = CreateSource(UnderdeclaredRepositoryUri);
        await manager.AddSourceAsync(underdeclaredSource, CancellationToken.None);
        var permissionError = await Assert.ThrowsAsync<InvalidDataException>(() =>
            manager.RefreshAsync(underdeclaredSource.Id, CancellationToken.None));
        Assert.Contains("process execution", permissionError.Message, StringComparison.OrdinalIgnoreCase);

        Assert.Empty(await extensions.GetInstalledAsync(CancellationToken.None));
        Assert.False(Directory.Exists(Path.Combine(paths.DataDirectory, "extensions", PackageId)));
    }

    [Fact]
    public async Task Native_plugin_runs_through_registry_authorization_provenance_failure_recovery_and_teardown()
    {
        using var paths = new TestPaths();
        var fixtureRoot = await CreateRepositoryAsync(paths, "lifecycle-source", CreateManifest());
        var database = await InitializeDatabaseAsync(paths);
        var extensionRepository = new ExtensionRepository(database);
        var capabilityRepository = new CapabilityRepository(database);
        var executionRepository = new ExecutionEventRepository(database);
        await using var events = new ExecutionEventHub(executionRepository);
        await using var runtime = new NativePluginRuntime(
            capabilityRepository,
            new CatalogRepository(database),
            new NativePluginProcessFactory(),
            events);
        var manager = new ExtensionManager(
            extensionRepository,
            new FixtureSourceTransport(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [ValidRepositoryUri] = fixtureRoot
            }),
            new ExtensionManifestValidator(),
            runtime,
            paths);
        var registry = new CapabilityRegistryService(capabilityRepository);
        var source = CreateSource(ValidRepositoryUri);

        await manager.AddSourceAsync(source, CancellationToken.None);
        var candidate = Assert.Single(await manager.RefreshAsync(source.Id, CancellationToken.None));
        var installed = await manager.InstallAsync(candidate, RequiredPermissions, CancellationToken.None);

        var persisted = Assert.Single(await extensionRepository.GetInstalledAsync(CancellationToken.None));
        Assert.Equal(candidate.ContentHash, persisted.ContentHash);
        Assert.Equal(installed.InstallPath, persisted.InstallPath);
        Assert.Contains(
            await registry.DiscoverAsync(CapabilityPlatform.Windows, CancellationToken.None),
            item => item.Key == $"extension.{PackageId}.fixture.echo");

        var deniedMarker = NewMarker();
        var deniedExecution = Guid.NewGuid();
        var deniedArguments = JsonSerializer.Serialize(new
        {
            message = "must-not-run",
            markerName = Path.GetFileName(deniedMarker)
        });
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            runtime.InvokeAsync(
                PackageId,
                "fixture.echo",
                deniedArguments,
                ExtensionPermission.ProjectRead,
                deniedExecution,
                null,
                CancellationToken.None));
        Assert.False(File.Exists(deniedMarker));
        Assert.Empty(await executionRepository.GetExecutionAsync(deniedExecution, CancellationToken.None));

        const string rawSecret = "worker28-input-secret-123";
        var successMarker = NewMarker();
        var successExecution = Guid.NewGuid();
        var result = await runtime.InvokeAsync(
            PackageId,
            "fixture.echo",
            JsonSerializer.Serialize(new { message = "hello", token = rawSecret, markerName = Path.GetFileName(successMarker) }),
            RequiredPermissions,
            successExecution,
            null,
            CancellationToken.None);

        Assert.True(File.Exists(successMarker));
        using (var resultDocument = JsonDocument.Parse(result))
        {
            var root = resultDocument.RootElement;
            Assert.Equal("hello", root.GetProperty("message").GetString());
            Assert.True(root.GetProperty("token").GetString() == "<redacted>");
            Assert.Equal(PackageId, root.GetProperty("packageId").GetString());
            Assert.False((root.GetProperty("commandLine").GetString() ?? string.Empty).Contains(rawSecret, StringComparison.Ordinal));
            Assert.False(IsProcessAlive(root.GetProperty("processId").GetInt32()));
        }
        Assert.False(result.Contains(rawSecret, StringComparison.Ordinal));
        var successEvents = await WaitForEventsAsync(executionRepository, successExecution, 2);
        Assert.Equal(
            new[] { ExecutionActionStatus.Running, ExecutionActionStatus.Completed },
            successEvents.Select(item => item.Status).ToArray());
        Assert.All(successEvents, item =>
        {
            Assert.Equal(ExecutionOrigin.NativePlugin, item.Origin);
            Assert.Equal(PackageId, item.ComponentId);
        });
        Assert.False(JsonSerializer.Serialize(successEvents).Contains(rawSecret, StringComparison.Ordinal));

        var crashMarker = NewMarker();
        var crashExecution = Guid.NewGuid();
        var crash = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runtime.InvokeAsync(
                PackageId,
                "fixture.crash",
                JsonSerializer.Serialize(new { markerName = Path.GetFileName(crashMarker) }),
                RequiredPermissions,
                crashExecution,
                null,
                CancellationToken.None));
        Assert.True(File.Exists(crashMarker));
        Assert.False(crash.Message.Contains("fixture-process-secret-456", StringComparison.Ordinal));
        var failedEvents = await WaitForEventsAsync(executionRepository, crashExecution, 2);
        Assert.Equal(
            new[] { ExecutionActionStatus.Running, ExecutionActionStatus.Failed },
            failedEvents.Select(item => item.Status).ToArray());
        Assert.DoesNotContain(failedEvents, item => item.Status == ExecutionActionStatus.Completed);
        Assert.False(JsonSerializer.Serialize(failedEvents).Contains("fixture-process-secret-456", StringComparison.Ordinal));

        var retryMarker = NewMarker();
        var retry = await runtime.InvokeAsync(
            PackageId,
            "fixture.echo",
            JsonSerializer.Serialize(new { message = "retry-ok", markerName = Path.GetFileName(retryMarker) }),
            RequiredPermissions,
            Guid.NewGuid(),
            null,
            CancellationToken.None);
        Assert.Contains("retry-ok", retry, StringComparison.Ordinal);
        Assert.True(File.Exists(retryMarker));

        await manager.SetEnabledAsync(installed.Id, false, CancellationToken.None);
        Assert.DoesNotContain(
            await registry.DiscoverAsync(CapabilityPlatform.Windows, CancellationToken.None),
            item => item.Key == $"extension.{PackageId}.fixture.echo");
        var disabledMarker = NewMarker();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runtime.InvokeAsync(
                PackageId,
                "fixture.echo",
                JsonSerializer.Serialize(new { markerName = Path.GetFileName(disabledMarker) }),
                RequiredPermissions,
                Guid.NewGuid(),
                null,
                CancellationToken.None));
        Assert.False(File.Exists(disabledMarker));

        await manager.SetEnabledAsync(installed.Id, true, CancellationToken.None);
        Assert.Contains(
            await registry.DiscoverAsync(CapabilityPlatform.Windows, CancellationToken.None),
            item => item.Key == $"extension.{PackageId}.fixture.echo");

        await manager.UninstallAsync(installed.Id, CancellationToken.None);
        Assert.Empty(await extensionRepository.GetInstalledAsync(CancellationToken.None));
        Assert.False(Directory.Exists(installed.InstallPath));
        Assert.DoesNotContain(
            await registry.DiscoverAsync(CapabilityPlatform.Windows, CancellationToken.None),
            item => item.Key == $"extension.{PackageId}.fixture.echo");
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runtime.InvokeAsync(
                PackageId,
                "fixture.echo",
                "{}",
                RequiredPermissions,
                Guid.NewGuid(),
                null,
                CancellationToken.None));

        DeleteMarker(deniedMarker);
        DeleteMarker(successMarker);
        DeleteMarker(crashMarker);
        DeleteMarker(retryMarker);
        DeleteMarker(disabledMarker);
    }

    [Fact]
    public async Task Tampered_installed_content_is_blocked_before_the_plugin_process_can_run()
    {
        using var paths = new TestPaths();
        var fixtureRoot = await CreateRepositoryAsync(paths, "tamper-source", CreateManifest());
        var database = await InitializeDatabaseAsync(paths);
        var extensionRepository = new ExtensionRepository(database);
        var capabilityRepository = new CapabilityRepository(database);
        var executionRepository = new ExecutionEventRepository(database);
        await using var events = new ExecutionEventHub(executionRepository);
        await using var runtime = new NativePluginRuntime(
            capabilityRepository,
            new CatalogRepository(database),
            new NativePluginProcessFactory(),
            events);
        var manager = new ExtensionManager(
            extensionRepository,
            new FixtureSourceTransport(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [ValidRepositoryUri] = fixtureRoot
            }),
            new ExtensionManifestValidator(),
            runtime,
            paths);
        var source = CreateSource(ValidRepositoryUri);

        await manager.AddSourceAsync(source, CancellationToken.None);
        var candidate = Assert.Single(await manager.RefreshAsync(source.Id, CancellationToken.None));
        var installed = await manager.InstallAsync(candidate, RequiredPermissions, CancellationToken.None);
        var pluginPath = Path.Combine(installed.InstallPath, "bin", "Haven.PluginFixture.dll");
        await File.AppendAllTextAsync(pluginPath, "tampered");

        var marker = NewMarker();
        var executionId = Guid.NewGuid();
        var failure = await Assert.ThrowsAsync<InvalidDataException>(() =>
            runtime.InvokeAsync(
                PackageId,
                "fixture.echo",
                JsonSerializer.Serialize(new { markerName = Path.GetFileName(marker) }),
                RequiredPermissions,
                executionId,
                null,
                CancellationToken.None));

        Assert.Contains("integrity", failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(marker));
        var trace = await WaitForEventsAsync(executionRepository, executionId, 2);
        Assert.Equal(
            new[] { ExecutionActionStatus.Running, ExecutionActionStatus.Failed },
            trace.Select(item => item.Status).ToArray());
        Assert.DoesNotContain(trace, item => item.Status == ExecutionActionStatus.Completed);

        await manager.SetEnabledAsync(installed.Id, false, CancellationToken.None);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            manager.SetEnabledAsync(installed.Id, true, CancellationToken.None));
        var persisted = Assert.Single(await extensionRepository.GetInstalledAsync(CancellationToken.None));
        Assert.False(persisted.IsEnabled);
        Assert.Equal(ExtensionInstallState.Disabled, persisted.State);

        DeleteMarker(marker);
    }

    private static ExtensionSource CreateSource(string repositoryUri) => new(
        Guid.NewGuid(),
        ExtensionSourceType.GitHubRepository,
        "Worker 28 fixture",
        repositoryUri,
        "main",
        false,
        null,
        ExtensionUpdateMode.Manual,
        true,
        null,
        null);

    private static ExtensionManifestDocument CreateManifest(
        ExtensionPermission? requestedPermissions = null,
        ExtensionPermission? capabilityPermissions = null)
    {
        var requested = requestedPermissions ?? RequiredPermissions;
        var required = capabilityPermissions ?? RequiredPermissions;
        var entryPoint = "bin/Haven.PluginFixture.dll";
        return new ExtensionManifestDocument(
            1,
            [
                new ExtensionPackageManifest(
                    PackageId,
                    "packages/plugin",
                    "Worker 28 fixture",
                    ExtensionPackageType.Plugin,
                    "1.0.0",
                    ">=0.2",
                    "Deterministic native plugin used to validate the production extension path.",
                    "Haven tests",
                    "Haven tests",
                    null,
                    "MIT",
                    requested,
                    [],
                    [
                        new ExtensionCapabilityManifest(
                            "fixture.echo",
                            "Fixture echo",
                            "Echoes safe fixture input.",
                            entryPoint,
                            ["echo"],
                            required),
                        new ExtensionCapabilityManifest(
                            "fixture.crash",
                            "Fixture crash",
                            "Fails deterministically to validate truthful execution provenance.",
                            entryPoint,
                            ["test failure"],
                            required)
                    ],
                    [],
                    null)
            ]);
    }

    private static async Task<string> CreateRepositoryAsync(
        TestPaths paths,
        string name,
        ExtensionManifestDocument document,
        bool copyFixture = true)
    {
        var root = Path.Combine(paths.DataDirectory, name);
        Directory.CreateDirectory(root);

        if (copyFixture)
        {
            var bin = Path.Combine(root, "packages", "plugin", "bin");
            Directory.CreateDirectory(bin);
            var fixtureAssembly = typeof(FixtureMarker).Assembly.Location;
            var targetAssembly = Path.Combine(bin, "Haven.PluginFixture.dll");
            File.Copy(fixtureAssembly, targetAssembly, overwrite: true);

            foreach (var extension in new[] { ".runtimeconfig.json", ".deps.json" })
            {
                var source = Path.ChangeExtension(fixtureAssembly, extension);
                if (File.Exists(source))
                    File.Copy(source, Path.ChangeExtension(targetAssembly, extension), overwrite: true);
            }

            var runtimeConfig = Path.ChangeExtension(targetAssembly, ".runtimeconfig.json");
            if (!File.Exists(runtimeConfig))
            {
                await File.WriteAllTextAsync(
                    runtimeConfig,
                    JsonSerializer.Serialize(new
                    {
                        runtimeOptions = new
                        {
                            tfm = "net10.0",
                            framework = new
                            {
                                name = "Microsoft.NETCore.App",
                                version = $"{Environment.Version.Major}.{Environment.Version.Minor}.0"
                            }
                        }
                    })).ConfigureAwait(false);
            }
        }

        await File.WriteAllTextAsync(
            Path.Combine(root, "haven.repository.json"),
            JsonSerializer.Serialize(document, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }))
            .ConfigureAwait(false);
        return root;
    }

    private static async Task<SqliteDatabase> InitializeDatabaseAsync(TestPaths paths)
    {
        var database = new SqliteDatabase(paths);
        await new ConversationProductionDatabase(database).InitializeAsync(CancellationToken.None);
        return database;
    }

    private static async Task<IReadOnlyList<ExecutionEvent>> WaitForEventsAsync(
        IExecutionEventRepository repository,
        Guid executionId,
        int minimumCount)
    {
        for (var attempt = 0; attempt < 80; attempt++)
        {
            var events = await repository.GetExecutionAsync(executionId, CancellationToken.None);
            if (events.Count >= minimumCount)
                return events;
            await Task.Delay(25);
        }

        return await repository.GetExecutionAsync(executionId, CancellationToken.None);
    }

    private static string NewMarker() =>
        Path.Combine(Path.GetTempPath(), "haven-worker28-plugin-" + Guid.NewGuid().ToString("N") + ".marker");

    private static void DeleteMarker(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
    }

    private static bool IsProcessAlive(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private sealed class FixtureSourceTransport(IReadOnlyDictionary<string, string> repositories) : IExtensionSourceTransport
    {
        public Task<string> MaterializeAsync(
            ExtensionSource source,
            string destination,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!repositories.TryGetValue(source.RepositoryUri, out var root))
                throw new InvalidOperationException("Fixture repository was not configured.");

            CopyDirectory(root, destination);
            return Task.FromResult(destination);
        }

        private static void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(destination, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target, overwrite: true);
            }
        }
    }

    private sealed class TestPaths : IAppPaths, IDisposable
    {
        public TestPaths()
        {
            DataDirectory = Path.Combine(Path.GetTempPath(), "haven-worker28-extension-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DataDirectory);
            DatabasePath = Path.Combine(DataDirectory, "haven.db");
            BrowserProfileDirectory = Path.Combine(DataDirectory, "browser");
            AttachmentsDirectory = Path.Combine(DataDirectory, "attachments");
            LogsDirectory = Path.Combine(DataDirectory, "logs");
            LegacyStatePath = Path.Combine(DataDirectory, "legacy.json");
        }

        public string DataDirectory { get; }
        public string DatabasePath { get; }
        public string BrowserProfileDirectory { get; }
        public string AttachmentsDirectory { get; }
        public string LogsDirectory { get; }
        public string LegacyStatePath { get; }

        public void Dispose()
        {
            try { Directory.Delete(DataDirectory, recursive: true); }
            catch (IOException) { }
        }
    }
}
