using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;
using Xunit;

namespace Haven.Infrastructure.Tests;

public sealed class SafeGenerativeThemeStoreTests : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly TestPaths _paths = new();

    [Fact]
    public async Task ActiveThemeLookupRepairsMissingThemeAndUndefinedAppearance()
    {
        await using var diagnostics = new ProductionDiagnostics(_paths);
        var inner = new GenerativeThemeStore(_paths, new GenerativeThemeValidator(), diagnostics);
        var safe = new SafeGenerativeThemeStore(inner, diagnostics);
        _ = await inner.GetSelectionAsync(CancellationToken.None);

        var missingThemeId = Guid.NewGuid();
        var invalid = new GenerativeThemeSelection(
            1,
            missingThemeId,
            (GenerativeThemeAppearance)999,
            DateTimeOffset.UtcNow);
        var selectionPath = Path.Combine(_paths.DataDirectory, "GenerativeUi", "selection.json");
        await File.WriteAllTextAsync(
            selectionPath,
            JsonSerializer.Serialize(invalid, JsonOptions),
            CancellationToken.None);

        var active = await safe.GetActiveThemeAsync(CancellationToken.None);
        var repaired = await safe.GetSelectionAsync(CancellationToken.None);

        Assert.True(active.IsBuiltIn);
        Assert.Equal(active.Id, repaired.ActiveThemeId);
        Assert.Equal(GenerativeThemeAppearance.Dark, repaired.Appearance);
        Assert.NotEqual(missingThemeId, repaired.ActiveThemeId);
        var events = await diagnostics.ReadRecentAsync(20, CancellationToken.None);
        Assert.Contains(events, item => item.EventName == "selection-repaired");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(999)]
    public async Task PublicAppearanceMutationsRejectUndefinedValues(int rawAppearance)
    {
        await using var diagnostics = new ProductionDiagnostics(_paths);
        var inner = new GenerativeThemeStore(_paths, new GenerativeThemeValidator(), diagnostics);
        var safe = new SafeGenerativeThemeStore(inner, diagnostics);
        var builtIn = (await safe.GetThemesAsync(CancellationToken.None)).First(theme => theme.IsBuiltIn);
        var appearance = (GenerativeThemeAppearance)rawAppearance;

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            safe.SelectAsync(builtIn.Id, appearance, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            safe.SetAppearanceAsync(appearance, CancellationToken.None));
    }

    public void Dispose() => _paths.Dispose();

    private sealed class TestPaths : IAppPaths, IDisposable
    {
        public TestPaths()
        {
            DataDirectory = Path.Combine(
                Path.GetTempPath(),
                "haven-safe-generative-theme-tests-" + Guid.NewGuid().ToString("N"));
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
            try
            {
                Directory.Delete(DataDirectory, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
