using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Haven.Application;
using Haven.Core;

namespace Haven.Desktop.Services;

public sealed class GenerativeUiThemeRuntime(
    IGenerativeThemeStore store,
    IProductionDiagnostics diagnostics) : IGenerativeUiRuntime
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly List<IStyle> _generatedStyles = [];
    private GenerativeThemePack? _previewTheme;
    private GenerativeThemeAppearance? _previewAppearance;
    private bool _initialized;

    public GenerativeThemePack ActiveTheme { get; private set; } = null!;
    public GenerativeThemeAppearance Appearance { get; private set; } = GenerativeThemeAppearance.Dark;
    public event EventHandler? ThemeChanged;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized) return;
            var selection = await store.GetSelectionAsync(cancellationToken).ConfigureAwait(false);
            var theme = await store.GetActiveThemeAsync(cancellationToken).ConfigureAwait(false);
            await ApplyVisualsAsync(theme, selection.Appearance, cancellationToken).ConfigureAwait(false);
            ActiveTheme = theme;
            Appearance = selection.Appearance;
            _initialized = true;
        }
        finally { _gate.Release(); }
    }

    public async Task ApplyAsync(Guid themeId, GenerativeThemeAppearance appearance, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await store.SelectAsync(themeId, appearance, cancellationToken).ConfigureAwait(false);
            var theme = await store.GetActiveThemeAsync(cancellationToken).ConfigureAwait(false);
            _previewTheme = null;
            _previewAppearance = null;
            ActiveTheme = theme;
            Appearance = appearance;
            await ApplyVisualsAsync(theme, appearance, cancellationToken).ConfigureAwait(false);
            await diagnostics.WriteAsync(
                ReliabilitySeverity.Information,
                "generative-ui",
                "theme-applied",
                "A validated Generative UI theme was applied.",
                new Dictionary<string, string>
                {
                    ["themeId"] = theme.Id.ToString("D"),
                    ["appearance"] = appearance.ToString()
                },
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async Task PreviewAsync(GenerativeThemePack theme, GenerativeThemeAppearance appearance, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(theme);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _previewTheme = theme;
            _previewAppearance = appearance;
            ActiveTheme = theme;
            Appearance = appearance;
            await ApplyVisualsAsync(theme, appearance, cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async Task RevertPreviewAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_previewTheme is null) return;
            var selection = await store.GetSelectionAsync(cancellationToken).ConfigureAwait(false);
            var theme = await store.GetActiveThemeAsync(cancellationToken).ConfigureAwait(false);
            _previewTheme = null;
            _previewAppearance = null;
            ActiveTheme = theme;
            Appearance = selection.Appearance;
            await ApplyVisualsAsync(theme, selection.Appearance, cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public IReadOnlyList<GenerativeUiPlacement> GetPlacements(string region)
    {
        if (string.IsNullOrWhiteSpace(region) || ActiveTheme is null) return [];
        return ActiveTheme.Layout.Placements
            .Where(placement => placement.IsVisible && placement.Region.Equals(region, StringComparison.OrdinalIgnoreCase))
            .OrderBy(placement => placement.Order)
            .ToArray();
    }

    public IReadOnlyList<GeneratedPageDefinition> GetPages()
    {
        if (ActiveTheme is null) return [];
        var hidden = ActiveTheme.Layout.HiddenPageIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return ActiveTheme.Pages
            .Where(page => !hidden.Contains(page.Id))
            .OrderBy(page => page.Order)
            .ToArray();
    }

    private async Task ApplyVisualsAsync(
        GenerativeThemePack theme,
        GenerativeThemeAppearance appearance,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var application = Application.Current ?? throw new InvalidOperationException("Avalonia application resources are unavailable.");
            application.RequestedThemeVariant = appearance switch
            {
                GenerativeThemeAppearance.Light => ThemeVariant.Light,
                GenerativeThemeAppearance.Dark => ThemeVariant.Dark,
                _ => ThemeVariant.Default
            };
            var effectivePalette = appearance switch
            {
                GenerativeThemeAppearance.Light => theme.Light,
                GenerativeThemeAppearance.Dark => theme.Dark,
                _ when application.ActualThemeVariant == ThemeVariant.Light => theme.Light,
                _ => theme.Dark
            };
            ApplyPalette(application, effectivePalette, theme.Shape.ShowCardBorders);
            ApplyGeneratedStyles(application, theme);
            ThemeChanged?.Invoke(this, EventArgs.Empty);
        }, DispatcherPriority.Send, cancellationToken);
    }

    private static void ApplyPalette(Application application, GenerativeThemePalette palette, bool showCardBorders)
    {
        SetBrush(application, "HavenBackgroundBrush", palette.Background);
        SetBrush(application, "HavenElevatedBrush", palette.Elevated);
        SetBrush(application, "HavenPanelBrush", palette.Panel);
        SetBrush(application, "HavenPanel2Brush", palette.Panel2);
        SetBrush(application, "HavenPanel3Brush", palette.Panel3);
        SetBrush(application, "HavenPanelHoverBrush", palette.PanelHover);
        SetBrush(application, "HavenTextBrush", palette.Text);
        SetBrush(application, "HavenTextSoftBrush", palette.TextSoft);
        SetBrush(application, "HavenMutedBrush", palette.Muted);
        SetBrush(application, "HavenMuted2Brush", palette.Muted2);
        SetBrush(application, "HavenAccentBrush", palette.Accent);
        SetBrush(application, "HavenAccentInkBrush", palette.AccentInk);
        SetBrush(application, "HavenAccentSoftBrush", palette.AccentSoft);
        SetBrush(application, "HavenBlueBrush", palette.Blue);
        SetBrush(application, "HavenBlueSoftBrush", palette.BlueSoft);
        SetBrush(application, "HavenDangerBrush", palette.Danger);
        SetBrush(application, "HavenWarningBrush", palette.Warning);
        SetBrush(application, "HavenLineBrush", showCardBorders ? palette.Line : "#00000000");
        SetBrush(application, "HavenLineStrongBrush", showCardBorders ? palette.LineStrong : "#00000000");
        SetBrush(application, "HavenNubBrush", palette.Nub);
        SetBrush(application, "HavenButtonBrush", palette.Button);
        SetBrush(application, "HavenButtonHoverBrush", palette.ButtonHover);
        SetBrush(application, "HavenButtonPressedBrush", palette.ButtonPressed);
        SetBrush(application, "HavenFocusBrush", palette.Focus);
        SetBrush(application, "PrimaryBrush", palette.Accent);
        SetBrush(application, "StrokeBrush", showCardBorders ? palette.LineStrong : "#00000000");
        SetBrush(application, "SurfaceCardBrush", palette.Panel);
        SetBrush(application, "TextPrimaryBrush", palette.Text);
        application.Resources["HavenAcrylicTintColor"] = Color.Parse(palette.AcrylicTint);
        application.Resources["HavenAcrylicFallbackColor"] = Color.Parse(palette.AcrylicFallback);
    }

    private void ApplyGeneratedStyles(Application application, GenerativeThemePack theme)
    {
        foreach (var style in _generatedStyles) application.Styles.Remove(style);
        _generatedStyles.Clear();

        var windowStyle = new Style(selector => selector.OfType<Window>());
        windowStyle.Setters.Add(new Setter(TemplatedControl.FontFamilyProperty, new FontFamily(theme.Typography.FontFamily)));
        windowStyle.Setters.Add(new Setter(TemplatedControl.FontSizeProperty, theme.Typography.BaseFontSize));
        _generatedStyles.Add(windowStyle);

        var buttonStyle = new Style(selector => selector.OfType<Button>());
        buttonStyle.Setters.Add(new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(theme.Shape.ControlRadius)));
        _generatedStyles.Add(buttonStyle);

        var cardStyle = new Style(selector => selector.OfType<Border>().Class("card"));
        cardStyle.Setters.Add(new Setter(Border.CornerRadiusProperty, new CornerRadius(theme.Shape.CardRadius)));
        _generatedStyles.Add(cardStyle);

        var composerStyle = new Style(selector => selector.OfType<Border>().Class("composer"));
        composerStyle.Setters.Add(new Setter(Border.CornerRadiusProperty, new CornerRadius(theme.Shape.SurfaceRadius)));
        _generatedStyles.Add(composerStyle);

        foreach (var style in _generatedStyles) application.Styles.Add(style);
    }

    private static void SetBrush(Application application, string key, string colour) =>
        application.Resources[key] = new SolidColorBrush(Color.Parse(colour));
}
