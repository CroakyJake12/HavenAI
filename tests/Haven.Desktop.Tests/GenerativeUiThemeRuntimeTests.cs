/*
 * FILE DOCUMENTATION
 * Where: tests/Haven.Desktop.Tests/GenerativeUiThemeRuntimeTests.cs, in the automated test suite, where executable examples protect behavior against regressions.
 * What: This file owns GenerativeUiThemeRuntimeTests, InMemoryThemeStore, RecordingDiagnostics. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The test is intentionally close to the public behavior it protects, making failures describe a user-visible or architectural contract.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;

namespace Haven.Desktop.Tests;

/// <summary>
/// Represents generative ui theme runtime tests and keeps its related state and behavior together.
/// </summary>
public sealed class GenerativeUiThemeRuntimeTests
{
    /// <summary>
    /// Performs the applying light and dark updates the resources used by dynamic bindings step owned by this component.
    /// </summary>
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

        Assert.Equal(ThemeVariant.Light, Avalonia.Application.Current!.RequestedThemeVariant);
        Assert.Equal(Color.Parse(theme.Light.Background), BrushColour("HavenBackgroundBrush"));
        Assert.Equal(Color.Parse(theme.Light.Text), BrushColour("HavenTextBrush"));
        Assert.Equal(Color.Parse(theme.Light.Accent), BrushColour("HavenAccentBrush"));

        await runtime.ApplyAsync(
            theme.Id,
            GenerativeThemeAppearance.Dark,
            CancellationToken.None);

        Assert.Equal(ThemeVariant.Dark, Avalonia.Application.Current.RequestedThemeVariant);
        Assert.Equal(Color.Parse(theme.Dark.Background), BrushColour("HavenBackgroundBrush"));
        Assert.Equal(Color.Parse(theme.Dark.Text), BrushColour("HavenTextBrush"));
        Assert.Equal(Color.Parse(theme.Dark.Accent), BrushColour("HavenAccentBrush"));
        Assert.Equal(GenerativeThemeAppearance.Dark, store.Selection.Appearance);
        Assert.Contains(diagnostics.Events, item => item.EventName == "theme-applied");
    }

    /// <summary>
    /// Performs the preview changes resources without persisting selection and revert restores saved theme step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the brush colour step owned by this component.
    /// </summary>
    private static Color BrushColour(string key)
    {
        Assert.True(Avalonia.Application.Current!.Resources.TryGetValue(key, out var value));
        return Assert.IsType<SolidColorBrush>(value).Color;
    }

    /// <summary>
    /// Creates theme with the invariants required by its callers.
    /// </summary>
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

    /// <summary>
    /// Performs the palette step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Represents in memory theme store and keeps its related state and behavior together.
    /// </summary>
    private sealed class InMemoryThemeStore(
        GenerativeThemePack theme,
        GenerativeThemeSelection selection) : IGenerativeThemeStore
    {
        /// <summary>
        /// Gets or updates theme, the bindable or domain state represented by this property.
        /// </summary>
        public GenerativeThemePack Theme { get; private set; } = theme;
        /// <summary>
        /// Gets or updates selection, the bindable or domain state represented by this property.
        /// </summary>
        public GenerativeThemeSelection Selection { get; private set; } = selection;

        /// <summary>
        /// Retrieves themes async for the current operation.
        /// </summary>
        public Task<IReadOnlyList<GenerativeThemePack>> GetThemesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<GenerativeThemePack>>([Theme]);

        /// <summary>
        /// Retrieves selection async for the current operation.
        /// </summary>
        public Task<GenerativeThemeSelection> GetSelectionAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Selection);

        /// <summary>
        /// Retrieves active theme async for the current operation.
        /// </summary>
        public Task<GenerativeThemePack> GetActiveThemeAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Theme);

        /// <summary>
        /// Performs save async asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task SaveAsync(GenerativeThemePack value, CancellationToken cancellationToken)
        {
            Theme = value;
            return Task.CompletedTask;
        }

        /// <summary>
        /// Performs rename async asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task RenameAsync(Guid themeId, string name, CancellationToken cancellationToken)
        {
            Theme = Theme with { Name = name };
            return Task.CompletedTask;
        }

        /// <summary>
        /// Performs delete async asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task DeleteAsync(Guid themeId, CancellationToken cancellationToken) => Task.CompletedTask;

        /// <summary>
        /// Performs select async asynchronously so I/O does not block the caller's thread.
        /// </summary>
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

        /// <summary>
        /// Performs set appearance async asynchronously so I/O does not block the caller's thread.
        /// </summary>
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

        /// <summary>
        /// Performs export async asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task<string> ExportAsync(
            Guid themeId,
            string destinationDirectory,
            CancellationToken cancellationToken) =>
            Task.FromResult(Path.Combine(destinationDirectory, "theme.haven-theme.json"));

        /// <summary>
        /// Performs import async asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task<GenerativeThemePack> ImportAsync(
            string sourcePath,
            CancellationToken cancellationToken) =>
            Task.FromResult(Theme);
    }

    /// <summary>
    /// Represents recording diagnostics and keeps its related state and behavior together.
    /// </summary>
    private sealed class RecordingDiagnostics : IProductionDiagnostics
    {
        /// <summary>
        /// Gets or updates events, the bindable or domain state represented by this property.
        /// </summary>
        public List<ReliabilityEvent> Events { get; } = [];

        /// <summary>
        /// Performs write async asynchronously so I/O does not block the caller's thread.
        /// </summary>
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

        /// <summary>
        /// Performs read recent async asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task<IReadOnlyList<ReliabilityEvent>> ReadRecentAsync(
            int limit,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ReliabilityEvent>>(Events.TakeLast(limit).Reverse().ToArray());

        /// <summary>
        /// Performs dispose async asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}