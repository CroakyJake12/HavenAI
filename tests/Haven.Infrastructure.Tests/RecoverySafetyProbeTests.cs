/*
 * FILE DOCUMENTATION
 * Where: tests/Haven.Infrastructure.Tests/RecoverySafetyProbeTests.cs, in the automated test suite, where executable examples protect behavior against regressions.
 * What: This file owns RecoverySafetyProbeTests, TestPaths. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The test is intentionally close to the public behavior it protects, making failures describe a user-visible or architectural contract.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Application;
using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

/// <summary>
/// Represents recovery safety probe tests and keeps its related state and behavior together.
/// </summary>
public sealed class RecoverySafetyProbeTests : IDisposable
{
    /// <summary>
    /// Stores paths locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly TestPaths _paths = new();

    /// <summary>
    /// Performs the missing state allows background worker step owned by this component.
    /// </summary>
    [Fact]
    public async Task MissingStateAllowsBackgroundWorker()
    {
        var probe = new RecoverySafetyProbe(_paths);

        var result = await probe.AssessAsync(CancellationToken.None);

        Assert.False(result.IsSafeMode);
        Assert.True(result.StateWasReadable);
        Assert.Equal(0, result.RecentUncleanStarts);
    }

    /// <summary>
    /// Performs the repeated unclean desktop starts block background worker step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the corrupt state fails closed without modifying the file step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the confirmed clean shutdown removes cross process block step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the dispose step owned by this component.
    /// </summary>
    public void Dispose()
    {
        RuntimeSafetyState.DisableSafeMode();
        _paths.Dispose();
    }

    /// <summary>
    /// Represents test paths and keeps its related state and behavior together.
    /// </summary>
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

        /// <summary>
        /// Gets or updates data directory, the bindable or domain state represented by this property.
        /// </summary>
        public string DataDirectory { get; }
        /// <summary>
        /// Gets or updates database path, the bindable or domain state represented by this property.
        /// </summary>
        public string DatabasePath { get; }
        /// <summary>
        /// Gets or updates browser profile directory, the bindable or domain state represented by this property.
        /// </summary>
        public string BrowserProfileDirectory { get; }
        /// <summary>
        /// Gets or updates attachments directory, the bindable or domain state represented by this property.
        /// </summary>
        public string AttachmentsDirectory { get; }
        /// <summary>
        /// Gets or updates logs directory, the bindable or domain state represented by this property.
        /// </summary>
        public string LogsDirectory { get; }
        /// <summary>
        /// Gets or updates legacy state path, the bindable or domain state represented by this property.
        /// </summary>
        public string LegacyStatePath { get; }

        /// <summary>
        /// Performs the dispose step owned by this component.
        /// </summary>
        public void Dispose()
        {
            try { Directory.Delete(DataDirectory, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
