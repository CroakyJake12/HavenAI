/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/Services/GenerativeUiThemeRuntime.cs, in the Desktop services layer, adapting application behavior to Windows and Avalonia concerns.
 * What: This file owns GenerativeUiThemeRuntime. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The file keeps one cohesive responsibility in a predictable location so callers can find and replace it without unrelated changes.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Controls;

namespace Haven.Desktop.Services;

/// <summary>
/// Represents generative ui theme runtime and keeps its related state and behavior together.
/// </summary>
public sealed class GenerativeUiThemeRuntime(
    IGenerativeThemeStore store,
    IProductionDiagnostics diagnostics) : IGenerativeUiRuntime
{
    /// <summary>
    /// Stores gate locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly SemaphoreSlim _gate = new(1, 1);
    /// <summary>
    /// Stores generated styles locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly List<IStyle> _generatedStyles = [];
    /// <summary>
    /// Stores initialized locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _initialized;

    /// <summary>
    /// Gets or updates active theme, the bindable or domain state represented by this property.
    /// </summary>
    public GenerativeThemePack ActiveTheme { get; private set; } = null!;
    /// <summary>
    /// Gets or updates appearance, the bindable or domain state represented by this property.
    /// </summary>
    public GenerativeThemeAppearance Appearance { get; private set; } = GenerativeThemeAppearance.Dark;
    /// <summary>
    /// Stores theme changed locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    public event EventHandler? ThemeChanged;

    /// <summary>
    /// Performs initialize async asynchronously so I/O does not block the caller's thread.
    /// </summary>
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
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Performs apply async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task ApplyAsync(
        Guid themeId,
        GenerativeThemeAppearance appearance,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var previousSelection = await store.GetSelectionAsync(cancellationToken).ConfigureAwait(false);
            var previousTheme = _initialized
                ? ActiveTheme
                : await store.GetActiveThemeAsync(cancellationToken).ConfigureAwait(false);
            var previousAppearance = _initialized ? Appearance : previousSelection.Appearance;

            await store.SelectAsync(themeId, appearance, cancellationToken).ConfigureAwait(false);
            var theme = await store.GetActiveThemeAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await ApplyVisualsAsync(theme, appearance, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception applyFailure)
            {
                try
                {
                    await store.SelectAsync(
                        previousSelection.ActiveThemeId,
                        previousSelection.Appearance,
                        CancellationToken.None).ConfigureAwait(false);
                    await ApplyVisualsAsync(
                        previousTheme,
                        previousAppearance,
                        CancellationToken.None).ConfigureAwait(false);
                    ActiveTheme = previousTheme;
                    Appearance = previousAppearance;
                }
                catch (Exception rollbackFailure)
                {
                    await TryWriteDiagnosticAsync(
                        ReliabilitySeverity.Critical,
                        "theme-rollback-failed",
                        "A Generative UI theme failed to apply and the previous visual state could not be fully restored.",
                        new Dictionary<string, string>
                        {
                            ["requestedThemeId"] = themeId.ToString("D"),
                            ["previousThemeId"] = previousSelection.ActiveThemeId.ToString("D"),
                            ["applyExceptionType"] = applyFailure.GetType().FullName ?? applyFailure.GetType().Name,
                            ["rollbackExceptionType"] = rollbackFailure.GetType().FullName ?? rollbackFailure.GetType().Name
                        }).ConfigureAwait(false);
                    throw new AggregateException(
                        "The Generative UI theme could not be applied and rollback also failed.",
                        applyFailure,
                        rollbackFailure);
                }

                throw;
            }

            ActiveTheme = theme;
            Appearance = appearance;
            await TryWriteDiagnosticAsync(
                ReliabilitySeverity.Information,
                "theme-applied",
                "A validated Generative UI theme was applied.",
                new Dictionary<string, string>
                {
                    ["themeId"] = theme.Id.ToString("D"),
                    ["appearance"] = appearance.ToString()
                }).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Performs preview async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task PreviewAsync(
        GenerativeThemePack theme,
        GenerativeThemeAppearance appearance,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(theme);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var previousTheme = ActiveTheme;
            var previousAppearance = Appearance;
            try
            {
                await ApplyVisualsAsync(theme, appearance, cancellationToken).ConfigureAwait(false);
                ActiveTheme = theme;
                Appearance = appearance;
            }
            catch
            {
                if (_initialized && previousTheme is not null)
                {
                    await ApplyVisualsAsync(
                        previousTheme,
                        previousAppearance,
                        CancellationToken.None).ConfigureAwait(false);
                    ActiveTheme = previousTheme;
                    Appearance = previousAppearance;
                }
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Performs revert preview async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task RevertPreviewAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Always reload the persisted selection. This also recovers correctly when
            // the active custom theme was deleted while no transient preview existed.
            var selection = await store.GetSelectionAsync(cancellationToken).ConfigureAwait(false);
            var theme = await store.GetActiveThemeAsync(cancellationToken).ConfigureAwait(false);
            await ApplyVisualsAsync(theme, selection.Appearance, cancellationToken).ConfigureAwait(false);
            ActiveTheme = theme;
            Appearance = selection.Appearance;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Retrieves placements for the current operation.
    /// </summary>
    public IReadOnlyList<GenerativeUiPlacement> GetPlacements(string region)
    {
        if (string.IsNullOrWhiteSpace(region) || ActiveTheme is null) return [];
        return ActiveTheme.Layout.Placements
            .Where(placement => placement.IsVisible && placement.Region.Equals(region, StringComparison.OrdinalIgnoreCase))
            .OrderBy(placement => placement.Order)
            .ToArray();
    }

    /// <summary>
    /// Retrieves pages for the current operation.
    /// </summary>
    public IReadOnlyList<GeneratedPageDefinition> GetPages()
    {
        if (ActiveTheme is null) return [];
        var hidden = ActiveTheme.Layout.HiddenPageIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return ActiveTheme.Pages
            .Where(page => !hidden.Contains(page.Id))
            .OrderBy(page => page.Order)
            .ToArray();
    }

    /// <summary>
    /// Performs apply visuals async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task ApplyVisualsAsync(
        GenerativeThemePack theme,
        GenerativeThemeAppearance appearance,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var handlerFailures = new List<Exception>();
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var application = Avalonia.Application.Current
                              ?? throw new InvalidOperationException("Avalonia application resources are unavailable.");
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
            ApplyPalette(application, effectivePalette, theme.Shape);
            ApplyGeneratedStyles(application, theme, effectivePalette);

            if (ThemeChanged is null) return;
            foreach (EventHandler handler in ThemeChanged.GetInvocationList())
            {
                try { handler(this, EventArgs.Empty); }
                catch (Exception ex) { handlerFailures.Add(ex); }
            }
        }, DispatcherPriority.Send, cancellationToken);

        if (handlerFailures.Count > 0)
        {
            await TryWriteDiagnosticAsync(
                ReliabilitySeverity.Warning,
                "theme-change-handler-failed",
                "One or more UI listeners failed after a Generative UI theme change.",
                new Dictionary<string, string>
                {
                    ["failureCount"] = handlerFailures.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["firstExceptionType"] = handlerFailures[0].GetType().FullName ?? handlerFailures[0].GetType().Name
                }).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Attempts to write diagnostic async and reports the result without using failure for normal control flow.
    /// </summary>
    private async ValueTask TryWriteDiagnosticAsync(
        ReliabilitySeverity severity,
        string eventName,
        string message,
        IReadOnlyDictionary<string, string>? data = null)
    {
        try
        {
            await diagnostics.WriteAsync(
                severity,
                "generative-ui",
                eventName,
                message,
                data,
                cancellationToken: CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Diagnostics must never turn a successfully applied or restored theme
            // into a user-visible failure.
        }
    }

    /// <summary>
    /// Performs the apply palette step owned by this component.
    /// </summary>
    private static void ApplyPalette(
        Avalonia.Application application,
        GenerativeThemePalette palette,
        GenerativeThemeShape shape)
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
        SetBrush(application, "HavenAccentInkBrush", ChooseReadableInk(palette.Accent, palette.AccentInk));
        SetBrush(application, "HavenAccentSoftBrush", palette.AccentSoft);
        SetBrush(application, "HavenBlueBrush", palette.Blue);
        SetBrush(application, "HavenBlueSoftBrush", palette.BlueSoft);
        SetBrush(application, "HavenDangerBrush", palette.Danger);
        SetBrush(application, "HavenWarningBrush", palette.Warning);
        SetBrush(application, "HavenLineBrush", shape.ShowCardBorders ? palette.Line : "#00000000");
        SetBrush(application, "HavenLineStrongBrush", shape.ShowCardBorders ? palette.LineStrong : "#00000000");
        SetBrush(application, "HavenNubBrush", palette.Nub);
        SetBrush(application, "HavenButtonBrush", palette.Button);
        SetBrush(application, "HavenButtonHoverBrush", palette.ButtonHover);
        SetBrush(application, "HavenButtonPressedBrush", palette.ButtonPressed);
        SetBrush(application, "HavenFocusBrush", palette.Focus);
        SetBrush(application, "PrimaryBrush", palette.Accent);
        SetBrush(application, "StrokeBrush", shape.ShowCardBorders ? palette.LineStrong : "#00000000");
        SetBrush(application, "SurfaceCardBrush", palette.Panel);
        SetBrush(application, "TextPrimaryBrush", palette.Text);

        var acrylicTint = shape.UseAcrylic ? palette.AcrylicTint : palette.Panel;
        var acrylicFallback = shape.UseAcrylic ? palette.AcrylicFallback : palette.Panel;
        application.Resources["HavenAcrylicTintColor"] = Color.Parse(acrylicTint);
        application.Resources["HavenAcrylicFallbackColor"] = Color.Parse(acrylicFallback);
    }

    /// <summary>
    /// Performs the apply generated styles step owned by this component.
    /// </summary>
    private void ApplyGeneratedStyles(
        Avalonia.Application application,
        GenerativeThemePack theme,
        GenerativeThemePalette palette)
    {
        foreach (var style in _generatedStyles) application.Styles.Remove(style);
        _generatedStyles.Clear();

        var windowStyle = new Style(selector => selector.OfType<Window>());
        windowStyle.Setters.Add(new Setter(TemplatedControl.FontFamilyProperty, new FontFamily(theme.Typography.FontFamily)));
        windowStyle.Setters.Add(new Setter(TemplatedControl.FontSizeProperty, theme.Typography.BaseFontSize));
        _generatedStyles.Add(windowStyle);

        var textStyle = new Style(selector => selector.OfType<TextBlock>());
        textStyle.Setters.Add(new Setter(TextBlock.LetterSpacingProperty, theme.Typography.LetterSpacing));
        _generatedStyles.Add(textStyle);

        var headingStyle = new Style(selector => selector.OfType<TextBlock>().Class("heading"));
        headingStyle.Setters.Add(new Setter(TextBlock.FontSizeProperty,
            theme.Typography.BaseFontSize * theme.Typography.HeadingScale * 1.4d));
        _generatedStyles.Add(headingStyle);

        var sectionHeadingStyle = new Style(selector => selector.OfType<TextBlock>().Class("sectionHeading"));
        sectionHeadingStyle.Setters.Add(new Setter(TextBlock.FontSizeProperty,
            theme.Typography.BaseFontSize * theme.Typography.HeadingScale));
        _generatedStyles.Add(sectionHeadingStyle);

        var buttonStyle = new Style(selector => selector.OfType<Button>());
        buttonStyle.Setters.Add(new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(theme.Shape.ControlRadius)));
        _generatedStyles.Add(buttonStyle);

        var cardStyle = new Style(selector => selector.OfType<Border>().Class("card"));
        cardStyle.Setters.Add(new Setter(Border.CornerRadiusProperty, new CornerRadius(theme.Shape.CardRadius)));
        _generatedStyles.Add(cardStyle);

        var composerStyle = new Style(selector => selector.OfType<Border>().Class("composer"));
        composerStyle.Setters.Add(new Setter(Border.CornerRadiusProperty, new CornerRadius(theme.Shape.SurfaceRadius)));
        _generatedStyles.Add(composerStyle);

        var acrylicStyle = new Style(selector => selector.OfType<AcrylicSurface>());
        acrylicStyle.Setters.Add(new Setter(AcrylicSurface.TintOpacityProperty, theme.Shape.UseAcrylic ? 0.78d : 1d));
        acrylicStyle.Setters.Add(new Setter(AcrylicSurface.MaterialOpacityProperty, theme.Shape.UseAcrylic ? 0.62d : 1d));
        acrylicStyle.Setters.Add(new Setter(
            AcrylicSurface.TintColorProperty,
            Color.Parse(theme.Shape.UseAcrylic ? palette.AcrylicTint : palette.Panel)));
        acrylicStyle.Setters.Add(new Setter(
            AcrylicSurface.FallbackColorProperty,
            Color.Parse(theme.Shape.UseAcrylic ? palette.AcrylicFallback : palette.Panel)));
        _generatedStyles.Add(acrylicStyle);

        foreach (var style in _generatedStyles) application.Styles.Add(style);
    }

    /// <summary>
    /// Performs the choose readable ink step owned by this component.
    /// </summary>
    private static string ChooseReadableInk(string background, string requested)
    {
        if (ContrastRatio(background, requested) >= 4.5d) return requested;
        var black = "#FF000000";
        var white = "#FFFFFFFF";
        return ContrastRatio(background, black) >= ContrastRatio(background, white) ? black : white;
    }

    /// <summary>
    /// Performs the contrast ratio step owned by this component.
    /// </summary>
    private static double ContrastRatio(string first, string second)
    {
        var firstLuminance = RelativeLuminance(Color.Parse(first));
        var secondLuminance = RelativeLuminance(Color.Parse(second));
        var lighter = Math.Max(firstLuminance, secondLuminance);
        var darker = Math.Min(firstLuminance, secondLuminance);
        return (lighter + 0.05d) / (darker + 0.05d);
    }

    /// <summary>
    /// Performs the relative luminance step owned by this component.
    /// </summary>
    private static double RelativeLuminance(Color colour)
    {
        static double Linear(byte channel)
        {
            var value = channel / 255d;
            return value <= 0.04045d
                ? value / 12.92d
                : Math.Pow((value + 0.055d) / 1.055d, 2.4d);
        }

        return 0.2126d * Linear(colour.R)
               + 0.7152d * Linear(colour.G)
               + 0.0722d * Linear(colour.B);
    }

    /// <summary>
    /// Performs the set brush step owned by this component.
    /// </summary>
    private static void SetBrush(Avalonia.Application application, string key, string colour) =>
        application.Resources[key] = new SolidColorBrush(Color.Parse(colour));
}
