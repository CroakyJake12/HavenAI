/*
 * FILE DOCUMENTATION
 * Where: tests/Haven.Desktop.Tests/GenerativeUiThemeRollbackTests.cs, in the automated test suite, where executable examples protect behavior against regressions.
 * What: This file owns GenerativeUiThemeRollbackTests, MultiThemeStore, RecordingDiagnostics. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The test is intentionally close to the public behavior it protects, making failures describe a user-visible or architectural contract.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;

namespace Haven.Desktop.Tests;

/// <summary>
/// Represents generative ui theme rollback tests and keeps its related state and behavior together.
/// </summary>
public sealed class GenerativeUiThemeRollbackTests
{
    /// <summary>
    /// Performs the failed visual apply restores persisted and visible theme step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the throwing theme changed listener does not fail successful apply step owned by this component.
    /// </summary>
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
    /// Represents multi theme store and keeps its related state and behavior together.
    /// </summary>
    private sealed class MultiThemeStore(params GenerativeThemePack[] themes) : IGenerativeThemeStore
    {
        /// <summary>
        /// Stores themes locally so this component can preserve the dependency, cache, or state between member calls.
        /// </summary>
        private readonly Dictionary<Guid, GenerativeThemePack> _themes = themes.ToDictionary(item => item.Id);

        /// <summary>
        /// Gets or updates selection, the bindable or domain state represented by this property.
        /// </summary>
        public GenerativeThemeSelection Selection { get; private set; } = new(
            1,
            themes[0].Id,
            GenerativeThemeAppearance.Dark,
            DateTimeOffset.UtcNow);

        /// <summary>
        /// Retrieves themes async for the current operation.
        /// </summary>
        public Task<IReadOnlyList<GenerativeThemePack>> GetThemesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<GenerativeThemePack>>(_themes.Values.ToArray());

        /// <summary>
        /// Retrieves selection async for the current operation.
        /// </summary>
        public Task<GenerativeThemeSelection> GetSelectionAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Selection);

        /// <summary>
        /// Retrieves active theme async for the current operation.
        /// </summary>
        public Task<GenerativeThemePack> GetActiveThemeAsync(CancellationToken cancellationToken) =>
            Task.FromResult(_themes[Selection.ActiveThemeId]);

        /// <summary>
        /// Performs save asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task SaveAsync(GenerativeThemePack theme, CancellationToken cancellationToken)
        {
            _themes[theme.Id] = theme;
            return Task.CompletedTask;
        }

        /// <summary>
        /// Performs rename asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task RenameAsync(Guid themeId, string name, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        /// <summary>
        /// Performs delete asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task DeleteAsync(Guid themeId, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        /// <summary>
        /// Performs select asynchronously so I/O does not block the caller's thread.
        /// </summary>
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

        /// <summary>
        /// Performs set appearance asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task SetAppearanceAsync(
            GenerativeThemeAppearance appearance,
            CancellationToken cancellationToken)
        {
            Selection = Selection with { Appearance = appearance, UpdatedAt = DateTimeOffset.UtcNow };
            return Task.CompletedTask;
        }

        /// <summary>
        /// Performs export asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task<string> ExportAsync(
            Guid themeId,
            string destinationDirectory,
            CancellationToken cancellationToken) =>
            Task.FromResult(Path.Combine(destinationDirectory, "theme.haven-theme.json"));

        /// <summary>
        /// Performs import asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task<GenerativeThemePack> ImportAsync(
            string sourcePath,
            CancellationToken cancellationToken) =>
            Task.FromResult(_themes.Values.First());
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
        /// Performs write asynchronously so I/O does not block the caller's thread.
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
        /// Performs read recent asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task<IReadOnlyList<ReliabilityEvent>> ReadRecentAsync(
            int limit,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ReliabilityEvent>>(Events.TakeLast(limit).ToArray());

        /// <summary>
        /// Performs dispose asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
