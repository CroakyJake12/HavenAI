using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Threading;
using Haven.Desktop.Services;

namespace Haven.Desktop.Controls;

public sealed partial class WorkspaceChromeHost
{
    private static readonly object MagicalStylesGate = new();
    private static bool _magicalStylesLoaded;

    private MagicalBackdrop? _magicalBackdrop;
    private MotionPreferencesService? _motionPreferences;
    private Border? _motionSettingCard;
    private ToggleSwitch? _reduceAnimationsToggle;
    private Button? _quickMotionButton;
    private Border? _magicalModeRailHost;

    private MagicalBackdrop BuildMagicalBackdrop()
    {
        _magicalBackdrop = new MagicalBackdrop();
        Grid.SetRowSpan(_magicalBackdrop, 2);
        return _magicalBackdrop;
    }

    private Border BuildFloatingTopRail(Border topBar)
    {
        topBar.Background = Brushes.Transparent;
        topBar.BorderThickness = new Thickness(0);
        topBar.CornerRadius = new CornerRadius(20);

        var acrylic = new AcrylicSurface
        {
            Content = topBar,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(20),
            BorderBrush = new SolidColorBrush(Color.Parse("#6088F8EC")),
            BorderThickness = new Thickness(1),
            TintColor = Color.Parse("#111A31"),
            FallbackColor = Color.Parse("#F2111A31"),
            TintOpacity = 0.52,
            MaterialOpacity = 0.72
        };

        var host = new Border
        {
            Margin = new Thickness(14, 9, 14, 6),
            CornerRadius = new CornerRadius(22),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Child = acrylic
        };
        host.Classes.Add("magicalFloatingRail");
        return host;
    }

    private void InitializeMagicalTheme()
    {
        EnsureMagicalStylesLoaded();
        if (!Classes.Contains("magicalShell")) Classes.Add("magicalShell");

        _motionPreferences = MotionPreferencesService.Current;
        _motionPreferences.Changed += OnMotionPreferenceChanged;

        DecorateModeRail();
        BuildMotionSettingCard();

        _actionsSearch.TextChanged += OnMagicalActionsSearchChanged;
        if (_actionsFlyout is not null) _actionsFlyout.Opened += OnMagicalActionsFlyoutOpened;
        _railAuditTimer.Tick += OnMagicalAuditTick;

        ApplyMotionPreference();
        EnsureMotionSettingCard();
    }

    private void DisposeMagicalTheme()
    {
        if (_motionPreferences is not null)
            _motionPreferences.Changed -= OnMotionPreferenceChanged;

        _actionsSearch.TextChanged -= OnMagicalActionsSearchChanged;
        if (_actionsFlyout is not null) _actionsFlyout.Opened -= OnMagicalActionsFlyoutOpened;
        _railAuditTimer.Tick -= OnMagicalAuditTick;

        if (_reduceAnimationsToggle is not null)
            _reduceAnimationsToggle.Click -= OnReduceAnimationsToggleClicked;
        if (_quickMotionButton is not null)
            _quickMotionButton.Click -= OnQuickMotionClicked;

        _magicalBackdrop?.Dispose();
        _magicalBackdrop = null;
        _motionPreferences = null;
        _motionSettingCard = null;
        _reduceAnimationsToggle = null;
        _quickMotionButton = null;
        _magicalModeRailHost = null;
    }

    private static void EnsureMagicalStylesLoaded()
    {
        lock (MagicalStylesGate)
        {
            if (_magicalStylesLoaded || Application.Current is not { } application) return;

            application.Styles.Add(new StyleInclude(new Uri("avares://Haven.Desktop/"))
            {
                Source = new Uri("avares://Haven.Desktop/Styles/MagicalTheme.axaml")
            });
            _magicalStylesLoaded = true;
        }
    }

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
        AddQuickMotionSetting(rail);

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

    private void AddQuickMotionSetting(Border rail)
    {
        if (rail.Child is not Grid layout) return;
        var footer = layout.Children
            .OfType<StackPanel>()
            .FirstOrDefault(panel => Grid.GetRow(panel) == 2);
        if (footer is null) return;

        var orb = new Border
        {
            Width = 16,
            Height = 16,
            CornerRadius = new CornerRadius(8),
            BorderBrush = new SolidColorBrush(Color.Parse("#90FFFFFF")),
            BorderThickness = new Thickness(1),
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops =
                [
                    new GradientStop(Color.Parse("#2BE7C8"), 0),
                    new GradientStop(Color.Parse("#2D7CFF"), 0.34),
                    new GradientStop(Color.Parse("#A45CFF"), 0.62),
                    new GradientStop(Color.Parse("#FF5FA2"), 1)
                ]
            }
        };

        _quickMotionButton = new Button
        {
            Content = orb,
            Width = 44,
            Height = 36,
            MinHeight = 36,
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        _quickMotionButton.Classes.Add("chrome");
        _quickMotionButton.Click += OnQuickMotionClicked;
        footer.Children.Insert(0, _quickMotionButton);
    }

    private void BuildMotionSettingCard()
    {
        _reduceAnimationsToggle = new ToggleSwitch
        {
            IsChecked = _motionPreferences?.ReduceAnimations == true,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        _reduceAnimationsToggle.Classes.Add("motionSetting");
        _reduceAnimationsToggle.Click += OnReduceAnimationsToggleClicked;

        var text = new StackPanel
        {
            Spacing = 2,
            Children =
            {
                new TextBlock { Text = "Reduce animations", FontWeight = FontWeight.SemiBold },
                new TextBlock
                {
                    Text = "Freeze the aurora and make interface state changes immediate.",
                    Classes = { "muted" },
                    FontSize = 10,
                    TextWrapping = TextWrapping.Wrap
                }
            }
        };

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 14,
            Children =
            {
                text,
                WithModernColumn(_reduceAnimationsToggle, 1)
            }
        };

        _motionSettingCard = new Border { Child = grid };
        _motionSettingCard.Classes.Add("magicalSettingsCard");
    }

    private void EnsureMotionSettingCard()
    {
        if (_motionSettingCard is null || _actionsSections.Children.Contains(_motionSettingCard)) return;
        _actionsSections.Children.Insert(0, _motionSettingCard);
    }

    private void OnMotionPreferenceChanged(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(ApplyMotionPreference);

    private void OnMagicalActionsSearchChanged(object? sender, TextChangedEventArgs e) =>
        Dispatcher.UIThread.Post(EnsureMotionSettingCard);

    private void OnMagicalActionsFlyoutOpened(object? sender, EventArgs e) => EnsureMotionSettingCard();

    private void OnMagicalAuditTick(object? sender, EventArgs e) =>
        EnsureMotionSettingCard();

    private void OnReduceAnimationsToggleClicked(object? sender, RoutedEventArgs e)
    {
        if (_reduceAnimationsToggle is null) return;
        _motionPreferences?.SetReduceAnimations(_reduceAnimationsToggle.IsChecked == true);
    }

    private void OnQuickMotionClicked(object? sender, RoutedEventArgs e)
    {
        if (_motionPreferences is null) return;
        _motionPreferences.SetReduceAnimations(!_motionPreferences.ReduceAnimations);
    }

    private void ApplyMotionPreference()
    {
        var reduceAnimations = _motionPreferences?.ReduceAnimations == true;

        if (reduceAnimations)
            Classes.Remove("motionEnabled");
        else if (!Classes.Contains("motionEnabled"))
            Classes.Add("motionEnabled");

        if (_magicalBackdrop is not null)
            _magicalBackdrop.ReduceMotion = reduceAnimations;
        if (_reduceAnimationsToggle is not null)
            _reduceAnimationsToggle.IsChecked = reduceAnimations;
        if (_quickMotionButton is not null)
        {
            _quickMotionButton.Opacity = reduceAnimations ? 0.62 : 1;
            ToolTip.SetTip(
                _quickMotionButton,
                reduceAnimations
                    ? "Reduced animations are on · click to restore motion"
                    : "Reduce animations");
        }
    }
}
