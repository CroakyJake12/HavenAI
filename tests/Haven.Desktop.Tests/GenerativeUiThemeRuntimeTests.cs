using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;

namespace Haven.Desktop.Tests;

public sealed class GenerativeUiThemeRuntimeTests
{
    [AvaloniaFact]
    public async Task ApplyingLightAndDarkUpdatesTheResourcesUsedByDynamicBindings()
    {
        var theme = CreateTheme();
        var store = new InMemoryThemeStore(
            theme,
            new GenerativeThemeSelection(
                1,
                theme.Id,
                GenerativeThemeAppearance.Light,
                DateTimeOffset.UtcNow));
        await using var diagnostics = new RecordingDiagnostics();
        var runtime = new GenerativeUiThemeRuntime(store, diagnostics);

        await runtime.InitializeAsync(CancellationToken.None);

        Assert.Equal(ThemeVariant.Light, Application.Current!.RequestedThemeVariant);
        Assert.Equal(Color.Parse(theme.Light.Background), BrushColour("HavenBackgroundBrush"));
        Assert.Equal(Color.Parse(theme.Light.Text), BrushColour("HavenTextBrush"));
        Assert.Equal(Color.Parse(theme.Light.Accent), BrushColour("HavenAccentBrush"));

        await runtime.ApplyAsync(
            theme.Id,
            GenerativeThemeAppearance.Dark,
            CancellationToken.None);

        Assert.Equal(ThemeVariant.Dark, Application.Current.RequestedThemeVariant);
        Assert.Equal(Color.Parse(theme.Dark.Background), BrushColour("HavenBackgroundBrush"));
        Assert.Equal(Color.Parse(theme.Dark.Text), BrushColour("HavenTextBrush"));
        Assert.Equal(Color.Parse(theme.Dark.Accent), BrushColour("HavenAccentBrush"));
        Assert.Equal(GenerativeThemeAppearance.Dark, store.Selection.Appearance);
        Assert.Contains(diagnostics.Events, item => item.EventName == "theme-applied");
    }

    [AvaloniaFact]
    public async Task PreviewChangesResourcesWithoutPersistingSelectionAndRevertRestoresSavedTheme()
    {
        var saved = CreateTheme();
        var preview = saved with
        {
            Id = Guid.NewGuid(),
            Name = "Preview",
            Dark = saved.Dark with { Background = "#FF27112F", Accent = "#FF9C52D4" }
        };
        var store = new InMemoryThemeStore(
            saved,
            new GenerativeThemeSelection(
                1,
                saved.Id,
                GenerativeThemeAppearance.Dark,
                DateTimeOffset.UtcNow));
        await using var diagnostics = new RecordingDiagnostics();
        var runtime = new GenerativeUiThemeRuntime(store, diagnostics);
        await runtime.InitializeAsync(CancellationToken.None);

        await runtime.PreviewAsync(
            preview,
            GenerativeThemeAppearance.Dark,
            CancellationToken.None);

        Assert.Equal(preview.Id, runtime.ActiveTheme.Id);
        Assert.Equal(Color.Parse(preview.Dark.Background), BrushColour("HavenBackgroundBrush"));
        Assert.Equal(saved.Id, store.Selection.ActiveThemeId);

        await runtime.RevertPreviewAsync(CancellationToken.None);

        Assert.Equal(saved.Id, runtime.ActiveTheme.Id);
        Assert.Equal(Color.Parse(saved.Dark.Background), BrushColour("HavenBackgroundBrush"));
        Assert.Equal(saved.Id, store.Selection.ActiveThemeId);
    }

    private static Color BrushColour(string key)
    {
        Assert.True(Application.Current!.Resources.TryGetValue(key, out var value));
        return Assert.IsType<SolidColorBrush>(value).Color;
    }

    private static GenerativeThemePack CreateTheme()
    {
        var now = DateTimeOffset.UtcNow;
        return new GenerativeThemePack(
            1,
            Guid.NewGuid(),
            "Runtime test",
            "Verifies live root resource replacement.",
            "Tests",
            GenerativeThemeOrigin.Manual,
            false,
            now,
            now,
            Palette(
                "#FFF4F7FA",
                "#FF171A1F",
                "#FF0067B8",
                "#FFFFFFFF"),
            Palette(
                "#FF111923",
                "#FFF5F7FA",
                "#FF52A9E8",
                "#FF000000"),
            new GenerativeThemeTypography("Segoe UI", 14, 1.35, 0),
            new GenerativeThemeShape(10, 14, 16, 1, true, true),
            GenerativeUiCatalog.DefaultLayout,
            []);
    }

    private static GenerativeThemePalette Palette(
        string background,
        string text,
        string accent,
        string accentInk) => new(
        background,
        background,
        background,
        background,
        background,
        background,
        text,
        text,
        text,
        text,
        accent,
        accentInk,
        background,
        accent,
        background,
        "#FFFF99A4",
        "#FFFCE4A6",
        "#33000000",
        "#55000000",
        accent,
        background,
        background,
        background,
        background,
        background,
        accent);

    private sealed class InMemoryThemeStore(
        GenerativeThemePack theme,
        GenerativeThemeSelection selection) : IGenerativeThemeStore
    {
        public GenerativeThemePack Theme { get; private set; } = theme;
        public GenerativeThemeSelection Selection { get; private set; } = selection;

        public Task<IReadOnlyList<GenerativeThemePack>> GetThemesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<GenerativeThemePack>>([Theme]);

        public Task<GenerativeThemeSelection> GetSelectionAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Selection);

        public Task<GenerativeThemePack> GetActiveThemeAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Theme);

        public Task SaveAsync(GenerativeThemePack value, CancellationToken cancellationToken)
        {
            Theme = value;
            return Task.CompletedTask;
        }

        public Task RenameAsync(Guid themeId, string name, CancellationToken cancellationToken)
        {
            Theme = Theme with { Name = name };
            return Task.CompletedTask;
        }

        public Task DeleteAsync(Guid themeId, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SelectAsync(
            Guid themeId,
            GenerativeThemeAppearance appearance,
            CancellationToken cancellationToken)
        {
            Selection = Selection with
            {
                ActiveThemeId = themeId,
                Appearance = appearance,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            return Task.CompletedTask;
        }

        public Task SetAppearanceAsync(
            GenerativeThemeAppearance appearance,
            CancellationToken cancellationToken)
        {
            Selection = Selection with
            {
                Appearance = appearance,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            return Task.CompletedTask;
        }

        public Task<string> ExportAsync(
            Guid themeId,
            string destinationDirectory,
            CancellationToken cancellationToken) =>
            Task.FromResult(Path.Combine(destinationDirectory, "theme.haven-theme.json"));

        public Task<GenerativeThemePack> ImportAsync(
            string sourcePath,
            CancellationToken cancellationToken) =>
            Task.FromResult(Theme);
    }

    private sealed class RecordingDiagnostics : IProductionDiagnostics
    {
        public List<ReliabilityEvent> Events { get; } = [];

        public ValueTask WriteAsync(
            ReliabilitySeverity severity,
            string component,
            string eventName,
            string message,
            IReadOnlyDictionary<string, string>? data = null,
            string? correlationId = null,
            CancellationToken cancellationToken = default)
        {
            Events.Add(new ReliabilityEvent(
                DateTimeOffset.UtcNow,
                severity,
                component,
                eventName,
                message,
                correlationId ?? Guid.NewGuid().ToString("N"),
                data ?? new Dictionary<string, string>()));
            return ValueTask.CompletedTask;
        }

        public Task<IReadOnlyList<ReliabilityEvent>> ReadRecentAsync(
            int limit,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ReliabilityEvent>>(Events.TakeLast(limit).Reverse().ToArray());

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}