using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;
using Xunit;

namespace Haven.Infrastructure.Tests;

public sealed class GenerativeThemeStoreTests : IDisposable
{
    private readonly TestPaths _paths = new();

    [Fact]
    public async Task NewStoreProvidesImmutableDualVariantBuiltInsAndDarkSelection()
    {
        await using var diagnostics = new ProductionDiagnostics(_paths);
        var store = CreateStore(diagnostics);

        var themes = await store.GetThemesAsync(CancellationToken.None);
        var selection = await store.GetSelectionAsync(CancellationToken.None);
        var active = await store.GetActiveThemeAsync(CancellationToken.None);

        Assert.True(themes.Count >= 2);
        Assert.All(themes.Where(theme => theme.IsBuiltIn), theme =>
        {
            Assert.False(string.IsNullOrWhiteSpace(theme.Light.Background));
            Assert.False(string.IsNullOrWhiteSpace(theme.Dark.Background));
            Assert.NotEqual(theme.Light.Background, theme.Dark.Background);
        });
        Assert.Equal(GenerativeThemeAppearance.Dark, selection.Appearance);
        Assert.Equal(selection.ActiveThemeId, active.Id);
        Assert.True(active.IsBuiltIn);
    }

    [Fact]
    public async Task CustomThemeRoundTripsRenameSelectionExportImportAndDelete()
    {
        await using var diagnostics = new ProductionDiagnostics(_paths);
        var store = CreateStore(diagnostics);
        var custom = ValidTheme("Ocean workspace");

        await store.SaveAsync(custom, CancellationToken.None);
        await store.SelectAsync(custom.Id, GenerativeThemeAppearance.Light, CancellationToken.None);
        await store.RenameAsync(custom.Id, "Ocean workspace renamed", CancellationToken.None);
        var exportDirectory = Path.Combine(_paths.DataDirectory, "Exports");
        var exported = await store.ExportAsync(custom.Id, exportDirectory, CancellationToken.None);
        var imported = await store.ImportAsync(exported, CancellationToken.None);

        var themes = await store.GetThemesAsync(CancellationToken.None);
        var selection = await store.GetSelectionAsync(CancellationToken.None);
        Assert.Contains(themes, theme => theme.Id == custom.Id && theme.Name == "Ocean workspace renamed");
        Assert.Contains(themes, theme => theme.Id == imported.Id && theme.Origin == GenerativeThemeOrigin.Imported);
        Assert.NotEqual(custom.Id, imported.Id);
        Assert.Equal(custom.Id, selection.ActiveThemeId);
        Assert.Equal(GenerativeThemeAppearance.Light, selection.Appearance);
        Assert.True(File.Exists(exported));
        Assert.EndsWith(".haven-theme.json", exported, StringComparison.OrdinalIgnoreCase);

        await store.DeleteAsync(custom.Id, CancellationToken.None);
        var afterDelete = await store.GetThemesAsync(CancellationToken.None);
        var fallback = await store.GetSelectionAsync(CancellationToken.None);
        Assert.DoesNotContain(afterDelete, theme => theme.Id == custom.Id);
        Assert.Contains(afterDelete, theme => theme.Id == imported.Id);
        Assert.NotEqual(custom.Id, fallback.ActiveThemeId);
        Assert.Contains(afterDelete, theme => theme.Id == fallback.ActiveThemeId && theme.IsBuiltIn);
    }

    [Fact]
    public async Task BuiltInThemesCannotBeRenamedDeletedOrOverwritten()
    {
        await using var diagnostics = new ProductionDiagnostics(_paths);
        var store = CreateStore(diagnostics);
        var builtIn = (await store.GetThemesAsync(CancellationToken.None)).First(theme => theme.IsBuiltIn);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.RenameAsync(builtIn.Id, "Tampered", CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.DeleteAsync(builtIn.Id, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.SaveAsync(builtIn with { Name = "Tampered" }, CancellationToken.None));

        Assert.Contains(await store.GetThemesAsync(CancellationToken.None),
            theme => theme.Id == builtIn.Id && theme.Name == builtIn.Name);
    }

    [Fact]
    public async Task CorruptCustomThemeIsQuarantinedAndOtherThemesStillLoad()
    {
        await using var diagnostics = new ProductionDiagnostics(_paths);
        var store = CreateStore(diagnostics);
        var themesDirectory = Path.Combine(_paths.DataDirectory, "GenerativeUi", "Themes");
        Directory.CreateDirectory(themesDirectory);
        var corruptPath = Path.Combine(themesDirectory, "broken.haven-theme.json");
        await File.WriteAllTextAsync(corruptPath, "{ definitely broken", CancellationToken.None);

        var themes = await store.GetThemesAsync(CancellationToken.None);

        Assert.DoesNotContain(themes, theme => theme.Name.Contains("broken", StringComparison.OrdinalIgnoreCase));
        Assert.False(File.Exists(corruptPath));
        Assert.Contains(
            Directory.EnumerateFiles(themesDirectory, "broken.haven-theme.json.corrupt-*"),
            path => File.Exists(path));
        Assert.Contains(themes, theme => theme.IsBuiltIn);
        var events = await diagnostics.ReadRecentAsync(20, CancellationToken.None);
        Assert.Contains(events, item => item.EventName == "theme-quarantined");
    }

    [Fact]
    public async Task CorruptSelectionIsQuarantinedAndResetToBuiltInTheme()
    {
        await using var diagnostics = new ProductionDiagnostics(_paths);
        var store = CreateStore(diagnostics);
        _ = await store.GetSelectionAsync(CancellationToken.None);
        var selectionPath = Path.Combine(_paths.DataDirectory, "GenerativeUi", "selection.json");
        await File.WriteAllTextAsync(selectionPath, "not-json", CancellationToken.None);

        var recovered = await store.GetSelectionAsync(CancellationToken.None);
        var themes = await store.GetThemesAsync(CancellationToken.None);

        Assert.Contains(themes, theme => theme.Id == recovered.ActiveThemeId && theme.IsBuiltIn);
        Assert.Contains(
            Directory.EnumerateFiles(Path.GetDirectoryName(selectionPath)!, "selection.json.corrupt-*"),
            path => File.Exists(path));
        var events = await diagnostics.ReadRecentAsync(20, CancellationToken.None);
        Assert.Contains(events, item => item.EventName == "selection-quarantined");
    }

    [Fact]
    public async Task InvalidImportedLayoutNeverCreatesAStoredTheme()
    {
        await using var diagnostics = new ProductionDiagnostics(_paths);
        var store = CreateStore(diagnostics);
        var exportDirectory = Path.Combine(_paths.DataDirectory, "Imports");
        Directory.CreateDirectory(exportDirectory);
        var path = Path.Combine(exportDirectory, "unsafe.haven-theme.json");
        var unsafeTheme = ValidTheme("Unsafe") with
        {
            Layout = new GenerativeLayoutManifest(
                [new("arbitrary.xaml.binding", GenerativeUiCatalog.ShellHeaderRight, 1)],
                [])
        };
        await File.WriteAllTextAsync(
            path,
            System.Text.Json.JsonSerializer.Serialize(
                unsafeTheme,
                new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)),
            CancellationToken.None);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            store.ImportAsync(path, CancellationToken.None));

        var themes = await store.GetThemesAsync(CancellationToken.None);
        Assert.DoesNotContain(themes, theme => theme.Name == "Unsafe");
        var storedDirectory = Path.Combine(_paths.DataDirectory, "GenerativeUi", "Themes");
        var storedFiles = Directory.Exists(storedDirectory)
            ? Directory.EnumerateFiles(storedDirectory, "*.haven-theme.json")
            : Enumerable.Empty<string>();
        Assert.Empty(storedFiles);
    }

    public void Dispose() => _paths.Dispose();

    private GenerativeThemeStore CreateStore(IProductionDiagnostics diagnostics) =>
        new(_paths, new GenerativeThemeValidator(), diagnostics);

    private static GenerativeThemePack ValidTheme(string name)
    {
        var now = DateTimeOffset.UtcNow;
        return new GenerativeThemePack(
            1,
            Guid.NewGuid(),
            name,
            "A complete custom theme.",
            "Tests",
            GenerativeThemeOrigin.Manual,
            false,
            now,
            now,
            Palette(light: true, "#FFF7FAFC", "#FF0B68A3"),
            Palette(light: false, "#FF101820", "#FF0B68A3"),
            new GenerativeThemeTypography("Segoe UI", 14, 1.35, 0),
            new GenerativeThemeShape(10, 14, 16, 1.1, true, true),
            GenerativeUiCatalog.DefaultLayout,
            []);
    }

    private static GenerativeThemePalette Palette(bool light, string background, string accent) => new(
        background,
        light ? "#FFF7F9FC" : "#FF182129",
        light ? "#FFFFFFFF" : "#FF202A33",
        light ? "#FFF1F4F8" : "#FF26323C",
        light ? "#FFE8EDF3" : "#FF2C3A46",
        light ? "#FFE1E7EE" : "#FF344552",
        light ? "#FF16191E" : "#FFFFFFFF",
        light ? "#FF343A44" : "#FFD9E2EA",
        light ? "#FF5F6875" : "#FFA8B5C1",
        light ? "#FF7D8795" : "#FF748391",
        accent,
        "#FFFFFFFF",
        light ? "#FFDCEEFF" : "#FF203A45",
        light ? "#FF0067B8" : "#FF60CDFF",
        light ? "#FFD8ECFA" : "#FF1E3A50",
        "#FFFF99A4",
        "#FFFCE4A6",
        light ? "#24000000" : "#22FFFFFF",
        light ? "#3D000000" : "#44FFFFFF",
        accent,
        light ? "#FFF4F6F9" : "#FF182234",
        light ? "#FFF4F6F9" : "#F2182234",
        light ? "#E6FFFFFF" : "#2EFFFFFF",
        light ? "#FFFFFFFF" : "#46FFFFFF",
        light ? "#FFE7EBF0" : "#20FFFFFF",
        "#A060CDFF");

    private sealed class TestPaths : IAppPaths, IDisposable
    {
        public TestPaths()
        {
            DataDirectory = Path.Combine(
                Path.GetTempPath(),
                "haven-generative-theme-tests-" + Guid.NewGuid().ToString("N"));
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
