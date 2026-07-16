using Haven.Application;
using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

public sealed class RecoverySafetyProbeTests : IDisposable
{
    private readonly TestPaths _paths = new();

    [Fact]
    public async Task MissingStateAllowsBackgroundWorker()
    {
        var probe = new RecoverySafetyProbe(_paths);

        var result = await probe.AssessAsync(CancellationToken.None);

        Assert.False(result.IsSafeMode);
        Assert.True(result.StateWasReadable);
        Assert.Equal(0, result.RecentUncleanStarts);
    }

    [Fact]
    public async Task RepeatedUncleanDesktopStartsBlockBackgroundWorker()
    {
        await using var diagnostics = new ProductionDiagnostics(_paths);
        for (var index = 0; index < 4; index++)
        {
            var coordinator = new StartupRecoveryCoordinator(_paths, diagnostics);
            await coordinator.BeginStartupAsync(CancellationToken.None);
        }
        var probe = new RecoverySafetyProbe(_paths);

        var result = await probe.AssessAsync(CancellationToken.None);

        Assert.True(result.IsSafeMode);
        Assert.True(result.StateWasReadable);
        Assert.True(result.RecentUncleanStarts >= 3);
        Assert.Contains("automation", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CorruptStateFailsClosedWithoutModifyingTheFile()
    {
        var statePath = Path.Combine(_paths.DataDirectory, "startup-recovery.json");
        const string corrupt = "{ definitely not valid json";
        await File.WriteAllTextAsync(statePath, corrupt, CancellationToken.None);
        var probe = new RecoverySafetyProbe(_paths);

        var result = await probe.AssessAsync(CancellationToken.None);

        Assert.True(result.IsSafeMode);
        Assert.False(result.StateWasReadable);
        Assert.Equal(corrupt, await File.ReadAllTextAsync(statePath, CancellationToken.None));
    }

    [Fact]
    public async Task ConfirmedCleanShutdownRemovesCrossProcessBlock()
    {
        await using var diagnostics = new ProductionDiagnostics(_paths);
        var coordinator = new StartupRecoveryCoordinator(_paths, diagnostics);
        for (var index = 0; index < 4; index++)
        {
            coordinator = new StartupRecoveryCoordinator(_paths, diagnostics);
            await coordinator.BeginStartupAsync(CancellationToken.None);
        }
        Assert.True((await new RecoverySafetyProbe(_paths).AssessAsync(CancellationToken.None)).IsSafeMode);

        await new CleanResetStartupRecoveryCoordinator(coordinator, _paths)
            .MarkCleanShutdownAsync(CancellationToken.None);
        var result = await new RecoverySafetyProbe(_paths).AssessAsync(CancellationToken.None);

        Assert.False(result.IsSafeMode);
        Assert.True(result.StateWasReadable);
        Assert.Equal(0, result.RecentUncleanStarts);
    }

    public void Dispose()
    {
        RuntimeSafetyState.DisableSafeMode();
        _paths.Dispose();
    }

    private sealed class TestPaths : IAppPaths, IDisposable
    {
        public TestPaths()
        {
            DataDirectory = Path.Combine(Path.GetTempPath(), "haven-recovery-probe-tests-" + Guid.NewGuid().ToString("N"));
            DatabasePath = Path.Combine(DataDirectory, "haven.db");
            BrowserProfileDirectory = Path.Combine(DataDirectory, "browser");
            AttachmentsDirectory = Path.Combine(DataDirectory, "attachments");
            LogsDirectory = Path.Combine(DataDirectory, "logs");
            LegacyStatePath = Path.Combine(DataDirectory, "missing.json");
            Directory.CreateDirectory(DataDirectory);
            Directory.CreateDirectory(LogsDirectory);
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
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
