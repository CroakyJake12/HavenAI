/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/Controls/WorkspaceChromeHost.MagicalTheme.cs, in the Desktop controls layer, containing reusable Avalonia behavior and visual building blocks.
 * What: This file owns WorkspaceChromeHost. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The file keeps one cohesive responsibility in a predictable location so callers can find and replace it without unrelated changes.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Haven.Desktop.Services;

namespace Haven.Desktop.Controls;

/// <summary>
/// Represents workspace chrome host and keeps its related state and behavior together.
/// </summary>
public sealed partial class WorkspaceChromeHost
{
    /// <summary>
    /// Stores magical styles gate locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly object MagicalStylesGate = new();
    /// <summary>
    /// Stores magical styles loaded locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static bool _magicalStylesLoaded;

    /// <summary>
    /// Stores maximize geometry locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly Geometry MaximizeGeometry = StreamGeometry.Parse(
        "M5,5 L19,5 L19,7 L5,7 Z M5,17 L19,17 L19,19 L5,19 Z M5,7 L7,7 L7,17 L5,17 Z M17,7 L19,7 L19,17 L17,17 Z");
    /// <summary>
    /// Stores restore geometry locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly Geometry RestoreGeometry = StreamGeometry.Parse(
        "M7,4 L20,4 L20,17 L17,17 L17,7 L7,7 Z M4,7 L16,7 L16,20 L4,20 Z M6,9 L6,18 L14,18 L14,9 Z");

    /// <summary>
    /// Stores magical backdrop locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private MagicalBackdrop? _magicalBackdrop;
    /// <summary>
    /// Stores motion preferences locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private MotionPreferencesService? _motionPreferences;
    /// <summary>
    /// Stores magical mode rail host locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private Border? _magicalModeRailHost;
    /// <summary>
    /// Stores floating top rail host locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private Border? _floatingTopRailHost;
    /// <summary>
    /// Stores host window locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private Window? _hostWindow;
    /// <summary>
    /// Stores maximize icon locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private PathIcon? _maximizeIcon;
    /// <summary>
    /// Stores maximize button locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private Button? _maximizeButton;

    /// <summary>
    /// Builds magical backdrop from the currently available inputs.
    /// </summary>
    private MagicalBackdrop BuildMagicalBackdrop()
    {
        _magicalBackdrop = new MagicalBackdrop();
        Grid.SetRowSpan(_magicalBackdrop, 2);
        return _magicalBackdrop;
    }

    /// <summary>
    /// Builds floating top rail from the currently available inputs.
    /// </summary>
    private Border BuildFloatingTopRail(Border topBar)
    {
        topBar.Background = Brushes.Transparent;
        topBar.BorderThickness = new Thickness(0);
        topBar.CornerRadius = new CornerRadius(20);
        topBar.Margin = new Thickness(0);

        var windowControls = BuildWindowControls();
        var railContent = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Children = { topBar }
        };
        Grid.SetColumn(windowControls, 1);
        railContent.Children.Add(windowControls);

        var acrylic = new AcrylicSurface
        {
            Content = railContent,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(20),
            BorderBrush = new SolidColorBrush(Color.Parse("#6088F8EC")),
            BorderThickness = new Thickness(1),
            TintColor = Color.Parse("#111A31"),
            FallbackColor = Color.Parse("#F2111A31"),
            TintOpacity = 0.52,
            MaterialOpacity = 0.72
        };

        _floatingTopRailHost = new Border
        {
            Margin = new Thickness(8, 4, 8, 4),
            CornerRadius = new CornerRadius(22),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Child = acrylic
        };
        _floatingTopRailHost.Classes.Add("magicalFloatingRail");
        _floatingTopRailHost.PointerPressed += OnTopRailPointerPressed;
        return _floatingTopRailHost;
    }

    /// <summary>
    /// Builds window controls from the currently available inputs.
    /// </summary>
    private StackPanel BuildWindowControls()
    {
        var minimize = CaptionButton(
            new PathIcon
            {
                Data = StreamGeometry.Parse("M5,11 L19,11 L19,13 L5,13 Z"),
                Width = 13,
                Height = 13
            },
            "Minimize");
        minimize.Click += (_, _) =>
        {
            if (TopLevel.GetTopLevel(this) is Window window)
                window.WindowState = WindowState.Minimized;
        };

        _maximizeIcon = new PathIcon
        {
            Data = MaximizeGeometry,
            Width = 13,
            Height = 13
        };
        var maximize = CaptionButton(_maximizeIcon, "Maximize");
        _maximizeButton = maximize;
        maximize.Click += (_, _) =>
        {
            if (TopLevel.GetTopLevel(this) is not Window window) return;
            ToggleMaximize(window);
        };

        var close = CaptionButton(
            new HavenIcon { IconKey = "close", Width = 13, Height = 13 },
            "Close");
        close.Classes.Add("captionClose");
        close.Click += (_, _) => (TopLevel.GetTopLevel(this) as Window)?.Close();

        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            Margin = new Thickness(2, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Children = { minimize, maximize, close }
        };
    }

    /// <summary>
    /// Performs the caption button step owned by this component.
    /// </summary>
    private static Button CaptionButton(Control content, string tooltip)
    {
        var button = new Button
        {
            Content = content,
            Width = 40,
            Height = 38,
            MinHeight = 38,
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        button.Classes.Add("captionButton");
        ToolTip.SetTip(button, tooltip);
        return button;
    }

    /// <summary>
    /// Performs the initialize magical theme step owned by this component.
    /// </summary>
    private void InitializeMagicalTheme()
    {
        EnsureMagicalStylesLoaded();
        if (!Classes.Contains("magicalShell")) Classes.Add("magicalShell");

        _motionPreferences = MotionPreferencesService.Current;
        _motionPreferences.Changed += OnMotionPreferenceChanged;
        AttachedToVisualTree += OnMagicalAttachedToVisualTree;

        DecorateModeRail();
        ApplyMotionPreference();
    }

    /// <summary>
    /// Performs the dispose magical theme step owned by this component.
    /// </summary>
    private void DisposeMagicalTheme()
    {
        if (_motionPreferences is not null)
            _motionPreferences.Changed -= OnMotionPreferenceChanged;
        AttachedToVisualTree -= OnMagicalAttachedToVisualTree;

        if (_floatingTopRailHost is not null)
            _floatingTopRailHost.PointerPressed -= OnTopRailPointerPressed;
        if (_hostWindow is not null)
            _hostWindow.PropertyChanged -= OnHostWindowPropertyChanged;

        _magicalBackdrop?.Dispose();
        _magicalBackdrop = null;
        _motionPreferences = null;
        _magicalModeRailHost = null;
        _floatingTopRailHost = null;
        _hostWindow = null;
        _maximizeIcon = null;
        _maximizeButton = null;
    }

    /// <summary>
    /// Performs the ensure magical styles loaded step owned by this component.
    /// </summary>
    private static void EnsureMagicalStylesLoaded()
    {
        lock (MagicalStylesGate)
        {
            if (_magicalStylesLoaded || Avalonia.Application.Current is not { } application) return;

            application.Styles.Add(new StyleInclude(new Uri("avares://Haven/"))
            {
                Source = new Uri("avares://Haven/Styles/MagicalTheme.axaml")
            });
            _magicalStylesLoaded = true;
        }
    }

    /// <summary>
    /// Handles the magical attached to visual tree event raised by the UI or runtime.
    /// </summary>
    private void OnMagicalAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        DecorateModeRail();
        ConfigureHostWindow();
    }

    /// <summary>
    /// Performs the configure host window step owned by this component.
    /// </summary>
    private void ConfigureHostWindow()
    {
        if (TopLevel.GetTopLevel(this) is not Window window) return;
        if (!ReferenceEquals(_hostWindow, window))
        {
            if (_hostWindow is not null)
                _hostWindow.PropertyChanged -= OnHostWindowPropertyChanged;
            _hostWindow = window;
            _hostWindow.PropertyChanged += OnHostWindowPropertyChanged;
        }

        // BorderOnly removes the native title bar while preserving the platform resize frame.
        window.WindowDecorations = WindowDecorations.BorderOnly;
        UpdateMaximizeVisual(window);
    }

    /// <summary>
    /// Handles the host window property changed event raised by the UI or runtime.
    /// </summary>
    private void OnHostWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.WindowStateProperty && sender is Window window)
            UpdateMaximizeVisual(window);
    }

    /// <summary>
    /// Handles the top rail pointer pressed event raised by the UI or runtime.
    /// </summary>
    private void OnTopRailPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || IsInsideButton(e.Source)) return;
        if (TopLevel.GetTopLevel(this) is not Window window) return;

        if (e.ClickCount >= 2)
            ToggleMaximize(window);
        else
            window.BeginMoveDrag(e);
        e.Handled = true;
    }

    /// <summary>
    /// Reports whether inside button applies to the current state.
    /// </summary>
    private static bool IsInsideButton(object? source)
    {
        if (source is not Visual visual) return false;
        return visual is Button || visual.GetVisualAncestors().OfType<Button>().Any();
    }

    /// <summary>
    /// Performs the toggle maximize step owned by this component.
    /// </summary>
    private void ToggleMaximize(Window window)
    {
        window.WindowState = window.WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
        UpdateMaximizeVisual(window);
    }

    /// <summary>
    /// Performs the update maximize visual step owned by this component.
    /// </summary>
    private void UpdateMaximizeVisual(Window window)
    {
        if (_maximizeIcon is null || _maximizeButton is null) return;
        var maximized = window.WindowState == WindowState.Maximized;
        _maximizeIcon.Data = maximized ? RestoreGeometry : MaximizeGeometry;
        ToolTip.SetTip(_maximizeButton, maximized ? "Restore" : "Maximize");
    }

    /// <summary>
    /// Performs the decorate mode rail step owned by this component.
    /// </summary>
    private void DecorateModeRail()
    {
        if (_magicalModeRailHost is not null) return;

        var rail = _experienceShell.Children
            .OfType<Border>()
            .FirstOrDefault(candidate => Grid.GetColumn(candidate) == 0);
        if (rail is null) return;

        _experienceShell.Children.Remove(rail);
        rail.Margin = new Thickness(0);
        rail.Width = 62;
        rail.Background = Brushes.Transparent;
        rail.BorderBrush = Brushes.Transparent;
        rail.BorderThickness = new Thickness(0);
        rail.CornerRadius = new CornerRadius(18);

        var acrylic = new AcrylicSurface
        {
            Content = rail,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(20),
            BorderBrush = new SolidColorBrush(Color.Parse("#568AF7E7")),
            BorderThickness = new Thickness(1),
            TintColor = Color.Parse("#101B2D"),
            FallbackColor = Color.Parse("#F2101B2D"),
            TintOpacity = 0.50,
            MaterialOpacity = 0.70
        };

        _magicalModeRailHost = new Border
        {
            Width = 70,
            Margin = new Thickness(7, 8, 3, 10),
            CornerRadius = new CornerRadius(22),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Stretch,
            Child = acrylic
        };
        _magicalModeRailHost.Classes.Add("magicalFloatingRail");
        Grid.SetColumn(_magicalModeRailHost, 0);
        _experienceShell.Children.Insert(0, _magicalModeRailHost);
    }

    /// <summary>
    /// Handles the motion preference changed event raised by the UI or runtime.
    /// </summary>
    private void OnMotionPreferenceChanged(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(ApplyMotionPreference);

    /// <summary>
    /// Performs the apply motion preference step owned by this component.
    /// </summary>
    private void ApplyMotionPreference()
    {
        var reduceAnimations = _motionPreferences?.ReduceAnimations == true;

        if (reduceAnimations)
            Classes.Remove("motionEnabled");
        else if (!Classes.Contains("motionEnabled"))
            Classes.Add("motionEnabled");

        if (_magicalBackdrop is not null)
            _magicalBackdrop.ReduceMotion = reduceAnimations;
    }
}
