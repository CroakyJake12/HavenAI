using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;

namespace Haven.Desktop.Tests;

public sealed class GenerativeUiThemeRollbackTests
{
    [AvaloniaFact]
    public async Task FailedVisualApplyRestoresPersistedAndVisibleTheme()
    {
        var valid = CreateTheme("Valid", "#FF101820");
        var invalid = CreateTheme("Invalid", "not-a-colour");
        var store = new MultiThemeStore(valid, invalid);
        await using var diagnostics = new RecordingDiagnostics();
        var runtime = new GenerativeUiThemeRuntime(store, diagnostics);
        await runtime.InitializeAsync(CancellationToken.None);
        var originalColour = BrushColour("HavenBackgroundBrush");

        await Assert.ThrowsAnyAsync<Exception>(() => runtime.ApplyAsync(
            invalid.Id,
            GenerativeThemeAppearance.Dark,
            CancellationToken.None));

        Assert.Equal(valid.Id, store.Selection.ActiveThemeId);
        Assert.Equal(valid.Id, runtime.ActiveTheme.Id);
        Assert.Equal(originalColour, BrushColour("HavenBackgroundBrush"));
    }

    [AvaloniaFact]
    public async Task ThrowingThemeChangedListenerDoesNotFailSuccessfulApply()
    {
        var first = CreateTheme("First", "#FF101820");
        var second = CreateTheme("Second", "#FF243447");
        var store = new MultiThemeStore(first, second);
        await using var diagnostics = new RecordingDiagnostics();
        var runtime = new GenerativeUiThemeRuntime(store, diagnostics);
        await runtime.InitializeAsync(CancellationToken.None);
        runtime.ThemeChanged += (_, _) => throw new InvalidOperationException("Listener failure");

        await runtime.ApplyAsync(
            second.Id,
            GenerativeThemeAppearance.Dark,
            CancellationToken.None);

        Assert.Equal(second.Id, runtime.ActiveTheme.Id);
        Assert.Equal(second.Id, store.Selection.ActiveThemeId);
        Assert.Contains(diagnostics.Events, item => item.EventName == "theme-change-handler-failed");
    }

    private static Color BrushColour(string key)
    {
        Assert.True(Avalonia.Application.Current!.Resources.TryGetValue(key, out var value));
        return Assert.IsType<SolidColorBrush>(value).Color;
    }

    private static GenerativeThemePack CreateTheme(string name, string darkBackground)
    {
        var now = DateTimeOffset.UtcNow;
        return new GenerativeThemePack(
            1,
            Guid.NewGuid(),
            name,
            "Rollback test theme.",
            "Tests",
            GenerativeThemeOrigin.Manual,
            false,
            now,
            now,
            Palette("#FFF4F7FA", "#FF171A1F", "#FF0067B8", "#FFFFFFFF"),
            Palette(darkBackground, "#FFF5F7FA", "#FF52A9E8", "#FF000000"),
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

    private sealed class MultiThemeStore(params GenerativeThemePack[] themes) : IGenerativeThemeStore
    {
        private readonly Dictionary<Guid, GenerativeThemePack> _themes = themes.ToDictionary(item => item.Id);

        public GenerativeThemeSelection Selection { get; private set; } = new(
            1,
            themes[0].Id,
            GenerativeThemeAppearance.Dark,
            DateTimeOffset.UtcNow);

        public Task<IReadOnlyList<GenerativeThemePack>> GetThemesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<GenerativeThemePack>>(_themes.Values.ToArray());

        public Task<GenerativeThemeSelection> GetSelectionAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Selection);

        public Task<GenerativeThemePack> GetActiveThemeAsync(CancellationToken cancellationToken) =>
            Task.FromResult(_themes[Selection.ActiveThemeId]);

        public Task SaveAsync(GenerativeThemePack theme, CancellationToken cancellationToken)
        {
            _themes[theme.Id] = theme;
            return Task.CompletedTask;
        }

        public Task RenameAsync(Guid themeId, string name, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task DeleteAsync(Guid themeId, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task SelectAsync(
            Guid themeId,
            GenerativeThemeAppearance appearance,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_themes.ContainsKey(themeId)) throw new FileNotFoundException();
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
            Selection = Selection with { Appearance = appearance, UpdatedAt = DateTimeOffset.UtcNow };
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
            Task.FromResult(_themes.Values.First());
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
            Task.FromResult<IReadOnlyList<ReliabilityEvent>>(Events.TakeLast(limit).ToArray());

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
