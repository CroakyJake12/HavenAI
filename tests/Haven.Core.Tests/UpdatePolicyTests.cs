using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;
namespace Haven.Core.Tests;
public sealed class UpdatePolicyTests
{
    private static readonly string ValidSha256 = Convert.ToHexString(SHA256.HashData(new byte[] { 1, 2, 3, 4 })).ToLowerInvariant();
    private static string ManifestJson(
        string version = "1.2.4",
        string channel = "stable",
        string url = "https://updates.example/haven-1.2.4.zip",
        string? sha256 = null,
        long sizeBytes = 2048,
        DateTimeOffset? publishedAt = null)
    {
        var payload = new
        {
            version,
            channel,
            downloadUrl = url,
            sha256 = sha256 ?? ValidSha256,
            sizeBytes,
            releaseNotes = "routine release",
            publishedAt = (publishedAt ?? DateTimeOffset.UtcNow.AddHours(-2)).ToString("O"),
        };
        return JsonSerializer.Serialize(payload);
    }
    [Fact]
    public void ValidatorRejectsHttpDownloadUrl()
    {
        var json = ManifestJson(url: "http://updates.example/haven-1.2.4.zip");
        Assert.Throws<InvalidDataException>(() => UpdateManifestValidator.ParseAndValidate(json, DateTimeOffset.UtcNow));
    }
    [Fact]
    public void ValidatorRejectsNonHexDigest()
    {
        var badSha = new string('z', 64);
        var json = ManifestJson(sha256: badSha);
        Assert.Throws<InvalidDataException>(() => UpdateManifestValidator.ParseAndValidate(json, DateTimeOffset.UtcNow));
    }
    [Fact]
    public void ValidatorRejectsShortDigest()
    {
        var json = ManifestJson(sha256: "abcd");
        Assert.Throws<InvalidDataException>(() => UpdateManifestValidator.ParseAndValidate(json, DateTimeOffset.UtcNow));
    }
    [Fact]
    public void ValidatorRejectsImplausibleFuturePublishDate()
    {
        var json = ManifestJson(publishedAt: DateTimeOffset.UtcNow.AddDays(3));
        Assert.Throws<InvalidDataException>(() => UpdateManifestValidator.ParseAndValidate(json, DateTimeOffset.UtcNow));
    }
    [Fact]
    public void ValidatorRejectsOversizedMetadataPayload()
    {
        var json = ManifestJson(version: "1.2.4") + new string(' ', UpdateManifestValidator.MaxManifestBytes + 1);
        Assert.Throws<InvalidDataException>(() => UpdateManifestValidator.ParseAndValidate(json, DateTimeOffset.UtcNow));
    }
    [Fact]
    public void ValidatorAcceptsWellFormedManifestAndNormalizesDigest()
    {
        var manifest = UpdateManifestValidator.ParseAndValidate(ManifestJson(), DateTimeOffset.UtcNow);
        Assert.Equal("1.2.4", manifest.Version);
        Assert.Equal("stable", manifest.Channel);
        Assert.Equal(ValidSha256, manifest.Sha256);
        Assert.StartsWith("https://", manifest.DownloadUrl, StringComparison.Ordinal);
    }
    [Theory]
    [InlineData("1.2.4", "1.2.3", true)]
    [InlineData("1.2.3", "1.2.3", false)]
    [InlineData("1.2.2", "1.2.3", false)]
    [InlineData("v1.3", "1.2.9", true)]
    [InlineData("10.0", "9.99.99", true)]
    public void VersionComparisonUsesNormalizedSemver(string candidate, string current, bool expectedNewer)
    {
        Assert.Equal(expectedNewer, HavenDirectUpdateProvider.IsNewerVersion(candidate, current));
    }
    [Fact]
    public void VersionComparisonFallsBackToStringForPrereleaseSuffixes()
    {
        Assert.True(HavenDirectUpdateProvider.IsNewerVersion("1.2.4-beta", "1.2.3"));
        Assert.False(HavenDirectUpdateProvider.IsNewerVersion("1.2.3-beta", "1.2.4"));
        Assert.False(HavenDirectUpdateProvider.IsNewerVersion("1.2.4-beta", "1.2.4-BETA"));
    }
    [Fact]
    public async Task PreferencesRoundTripThroughVersionedStoreWithDefaultsWhenEmpty()
    {
        var store = new VersionedUpdatePreferenceStore(new FakeVersionedSettingsStore());
        var defaults = await store.LoadAsync(CancellationToken.None);
        Assert.True(defaults.BackgroundChecksEnabled);
        Assert.Equal(UpdateChannel.Stable, defaults.PreferredChannel);
        await store.SaveAsync(new UpdatePreferences(false, UpdateChannel.Preview), CancellationToken.None);
        var loaded = await store.LoadAsync(CancellationToken.None);
        Assert.Equal(new UpdatePreferences(false, UpdateChannel.Preview), loaded);
    }
    [Fact]
    public async Task OrchestratorPicksStoreProviderForMicrosoftStoreSource()
    {
        var direct = new RecordingProvider();
        var storeProvider = new RecordingProvider();
        var orchestrator = CreateOrchestrator(
            () => new InstallationInfo(InstallationSource.MicrosoftStore, "Haven.Example_abc123"),
            storeProvider,
            direct);
        var status = await orchestrator.GetStatusAsync(CancellationToken.None);
        Assert.True(status.StoreManaged);
        await orchestrator.CheckInBackgroundAsync(CancellationToken.None);
        Assert.Equal(1, storeProvider.CheckCalls);
        Assert.Equal(0, direct.CheckCalls);
        var after = await orchestrator.GetStatusAsync(CancellationToken.None);
        Assert.Equal(UpdateState.UpToDate, after.State);
        Assert.True(after.StoreManaged);
        Assert.Contains("Microsoft Store", after.Message, StringComparison.Ordinal);
    }
    [Fact]
    public async Task OrchestratorTreatsUnknownSourceAsDirectAndReportsUncertainty()
    {
        var direct = new RecordingProvider { CheckResult = new UpdateManifest("2.0.0", "stable", "https://updates.example/x.zip", ValidSha256, 1024, "", DateTimeOffset.UtcNow.AddHours(-1)) };
        var orchestrator = CreateOrchestrator(
            () => new InstallationInfo(InstallationSource.Unknown, "Haven.Example_abc123"),
            storeProvider: new RecordingProvider(),
            directProvider: direct);
        await orchestrator.CheckInBackgroundAsync(CancellationToken.None);
        var status = await orchestrator.GetStatusAsync(CancellationToken.None);
        Assert.Equal(1, direct.CheckCalls);
        Assert.Equal(UpdateState.Available, status.State);
        Assert.Equal("2.0.0", status.AvailableVersion);
        Assert.Contains("unconfirmed", status.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(status.StoreManaged);
    }
    [Fact]
    public async Task StagingDeletesTempFileWhenHashMismatchIsDetected()
    {
        var dataDirectory = CreateTempDataDirectory();
        try
        {
            var bytes = new byte[2048];
            Random.Shared.NextBytes(bytes);
            var wrongSha = Convert.ToHexString(SHA256.HashData(new byte[] { 9, 9, 9 })).ToLowerInvariant();
            var provider = new HavenDirectUpdateProvider(new DirectUpdateOptions { DataDirectory = dataDirectory });
            var manifest = new UpdateManifest("9.9.9", "stable", "https://updates.example/p.zip", wrongSha, bytes.Length, "", DateTimeOffset.UtcNow.AddHours(-1));
            await Assert.ThrowsAsync<InvalidDataException>(
                () => provider.StageFromStreamAsync(new MemoryStream(bytes), manifest, progress: null, CancellationToken.None));
            Assert.Empty(Directory.GetFiles(Path.Combine(dataDirectory, "updates", "staging")));
            Assert.Empty(Directory.GetFiles(Path.Combine(dataDirectory, "updates", "pending")));
        }
        finally { TryDeleteDirectory(dataDirectory); }
    }
    [Fact]
    public async Task StagingMovesVerifiedPackageToPendingNamedByVersion()
    {
        var dataDirectory = CreateTempDataDirectory();
        try
        {
            var bytes = new byte[4096];
            Random.Shared.NextBytes(bytes);
            var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            var provider = new HavenDirectUpdateProvider(new DirectUpdateOptions { DataDirectory = dataDirectory });
            var manifest = new UpdateManifest("9.9.9", "stable", "https://updates.example/p.zip", sha, bytes.Length, "", DateTimeOffset.UtcNow.AddHours(-1));
            var stagedPath = await provider.StageFromStreamAsync(new MemoryStream(bytes), manifest, progress: null, CancellationToken.None);
            Assert.True(File.Exists(stagedPath));
            Assert.EndsWith("9.9.9.zip", stagedPath, StringComparison.Ordinal);
            var persistedSha = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(stagedPath, CancellationToken.None))).ToLowerInvariant();
            Assert.Equal(sha, persistedSha);
        }
        finally { TryDeleteDirectory(dataDirectory); }
    }
    private static UpdateOrchestrator CreateOrchestrator(
        Func<InstallationInfo> detector,
        IUpdateProvider storeProvider,
        IUpdateProvider directProvider)
    {
        var providers = new Dictionary<InstallationSource, IUpdateProvider>
        {
            [InstallationSource.MicrosoftStore] = storeProvider,
            [InstallationSource.DirectInstall] = directProvider,
        };
        return new UpdateOrchestrator(detector, providers, new VersionedUpdatePreferenceStore(new FakeVersionedSettingsStore()), () => "1.0.0");
    }
    private static string CreateTempDataDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "haven-update-policy-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(path);
        return path;
    }
    private static void TryDeleteDirectory(string path)
    {
        try { Directory.Delete(path, recursive: true); }
        catch (IOException) { }
    }
    private sealed class FakeVersionedSettingsStore : IVersionedSettingsStore
    {
        private readonly Dictionary<string, string> _settings = new(StringComparer.OrdinalIgnoreCase);
        public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken) where T : class
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_settings.TryGetValue(key, out var json) ? JsonSerializer.Deserialize<T>(json) : null);
        }
        public Task SetAsync<T>(string key, T value, CancellationToken cancellationToken) where T : class
        {
            cancellationToken.ThrowIfCancellationRequested();
            _settings[key] = JsonSerializer.Serialize(value);
            return Task.CompletedTask;
        }
        public Task RemoveAsync(string key, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _settings.Remove(key);
            return Task.CompletedTask;
        }
        public Task<SettingsExportManifest> ExportAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new SettingsExportManifest { Settings = new Dictionary<string, string>(_settings) });
        }
        public Task<SettingsImportResult> ImportAsync(SettingsExportManifest manifest, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var (key, value) in manifest.Settings) _settings[key] = value;
            return Task.FromResult(new SettingsImportResult(true, new Dictionary<string, string>(_settings), $"Imported {manifest.Settings.Count} settings"));
        }
    }
    private sealed class RecordingProvider : IUpdateProvider
    {
        public int StatusCalls;
        public int CheckCalls;
        public int DownloadCalls;
        public UpdateManifest? CheckResult;
        public Task<UpdateStatusReport> GetStatusAsync(CancellationToken cancellationToken)
        {
            StatusCalls++;
            return Task.FromResult(new UpdateStatusReport(InstallationSource.Unknown, UpdateChannel.Stable, "fake", null, UpdateState.Idle, null, "Updates are managed by the Microsoft Store.", true));
        }
        public Task<UpdateManifest?> CheckForUpdateAsync(string currentVersion, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CheckCalls++;
            return Task.FromResult(CheckResult);
        }
        public Task<string> DownloadAndStageAsync(UpdateManifest manifest, IProgress<int>? progress, CancellationToken cancellationToken)
        {
            DownloadCalls++;
            throw new NotSupportedException("Recording provider never stages.");
        }
    }
}
