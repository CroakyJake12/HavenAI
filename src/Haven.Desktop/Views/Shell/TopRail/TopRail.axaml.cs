using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Haven.Desktop.Controls;
using Haven.Desktop.Events;
using Haven.Core;

namespace Haven.Desktop.Views.Shell.TopRail;

/// <summary>
/// Owns the compact product header. The shell supplies tab state and responds to
/// semantic events; this control never reaches into a shell view-model.
/// </summary>
public sealed partial class TopRail : UserControl, IDisposable
{
    private HavenEventBus? _eventBus;
    private NotificationCentre? _notificationCentre;
    private Flyout? _notificationFlyout;
    private Flyout? _appLauncherFlyout;
    private AppLauncherControl? _appLauncherControl;
    private bool _eventsWired;
    private bool _disposed;

    public TopRail()
    {
        InitializeComponent();
        WireControlEvents();
    }

    public TopRail(HavenEventBus eventBus) : this() => AttachEventBus(eventBus);

    public event EventHandler? HomeRequested;
    public event EventHandler? NewTabRequested;
    public event EventHandler? TabOverviewRequested;
    public event EventHandler? BackRequested;
    public event EventHandler? AppsRequested;
    public event EventHandler? RecentRequested;
    public event EventHandler? ActionsRequested;
    public event EventHandler<string>? TabSelected;
    public event EventHandler<string>? TabCloseRequested;
    public event EventHandler<TabRenameRequestedEventArgs>? TabRenameRequested;

    /// <summary>Attaches the one application event bus used by the entire shell.</summary>
    public void AttachEventBus(HavenEventBus eventBus)
    {
        ArgumentNullException.ThrowIfNull(eventBus);
        if (ReferenceEquals(_eventBus, eventBus)) return;
        _eventBus = eventBus;

        Register("TopRail.Logo", LogoButton);
        Register("TopRail.Tabs.Add", AddTabButton);
        Register("TopRail.Tabs.Overview", TabViewButton);
        Register("TopRail.Actions.Back", BackButton);
        Register("TopRail.Actions.Apps", AppsButton);
        Register("TopRail.Actions.Recent", RecentButton);
        Register("TopRail.Actions.Notifications", NotificationsButton);
    }

    /// <summary>Rebuilds the visual tab strip from the shell's current tab snapshot.</summary>
    public void SetTabs(IReadOnlyList<TopRailTab> tabs)
    {
        TabStrip.Children.Clear();
        foreach (var tab in tabs)
            TabStrip.Children.Add(BuildTab(tab));
    }

    /// <summary>Opens the searchable Actions menu, including for Ctrl+K.</summary>
    public void ShowActions() => ActionToolbar.ShowActionsFlyout();

    /// <summary>Replaces the Actions catalogue with the shell's current semantic commands.</summary>
    public void SetActions(IReadOnlyList<DynamicActionToolbar.ToolbarAction> actions) =>
        ActionToolbar.SetActions(actions);

    public void SetEditActionsHandler(Action onExecute) => ActionToolbar.SetEditActionsHandler(onExecute);

    public void ShowNotifications()
    {
        _notificationCentre ??= CreateNotificationCentre();
        _notificationFlyout ??= new Flyout
        {
            Placement = PlacementMode.BottomEdgeAlignedRight,
            Content = _notificationCentre
        };
        _notificationCentre.Open();
        _notificationFlyout.ShowAt(NotificationsButton);
    }

    public void ShowAppLauncher(
        IReadOnlyList<ModeDefinition> apps,
        IReadOnlySet<Guid> pinnedIds,
        bool openInNewTab,
        Action<ModeDefinition, bool> launch,
        Action manage)
    {
        _appLauncherFlyout?.Hide();
        _appLauncherFlyout?.Content = null;
        _appLauncherControl = new AppLauncherControl();
        _appLauncherControl.Configure(apps, pinnedIds, openInNewTab, launch, manage);
        _appLauncherFlyout = new Flyout
        {
            Placement = PlacementMode.BottomEdgeAlignedRight,
            Content = _appLauncherControl
        };
        _appLauncherFlyout.ShowAt(AppsButton);
    }

    private void WireControlEvents()
    {
        if (_eventsWired) return;
        _eventsWired = true;

        LogoButton.Click += OnLogoClicked;
        AddTabButton.Click += OnAddTabClicked;
        TabViewButton.Click += OnTabOverviewClicked;
        BackButton.Click += OnBackClicked;
        AppsButton.Click += OnAppsClicked;
        RecentButton.Click += OnRecentClicked;
        NotificationsButton.Click += OnNotificationsClicked;
        ActionToolbar.ActionsClicked += OnActionsClicked;
    }

    private Control BuildTab(TopRailTab tab)
    {
        var title = new TextBlock
        {
            Text = tab.Title,
            FontSize = 14,
            FontWeight = tab.IsSelected ? FontWeight.ExtraBold : FontWeight.Bold,
            Foreground = tab.IsSelected ? ResourceBrush("HavenAccentBrush", Colors.Black) : ResourceBrush("HavenTextSecondaryBrush", Colors.Gray),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 180
        };

        var underline = new Border
        {
            Height = 3,
            Width = Math.Clamp((tab.Title.Length * 7.2) + 12, 30, 170),
            Margin = new Thickness(0, 1, 0, 0),
            CornerRadius = new CornerRadius(2),
            Background = tab.IsSelected
                ? ResourceBrush("HavenAccentBrush", Colors.Black)
                : Brushes.Transparent,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var titleAndUnderline = new StackPanel
        {
            Spacing = 0,
            HorizontalAlignment = HorizontalAlignment.Center,
            Children = { title, underline }
        };

        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new HavenIcon
                {
                    IconKey = tab.IconKey,
                    Width = 16,
                    Height = 16,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = tab.IsSelected ? ResourceBrush("HavenAccentBrush", Colors.Black) : ResourceBrush("HavenTextSecondaryBrush", Colors.Gray)
                },
                titleAndUnderline
            }
        };

        var button = new Button
        {
            Content = content,
            MinWidth = 92,
            MaxWidth = 230,
            Height = 48,
            Padding = new Thickness(12, 5, 12, 3),
            Background = tab.IsSelected
                ? new SolidColorBrush(Color.FromArgb(30, 255, 255, 255))
                : Brushes.Transparent,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(11),
            Tag = tab.Key
        };
        ToolTip.SetTip(button, tab.Title);
        button.Click += (_, _) =>
        {
            Fire("TopRail.Tabs.TabClicked");
            TabSelected?.Invoke(this, tab.Key);
        };
        button.PointerPressed += (_, args) =>
        {
            if (args.GetCurrentPoint(button).Properties.PointerUpdateKind != PointerUpdateKind.RightButtonPressed)
                return;
            args.Handled = true;
            BuildTabMenu(tab).ShowAt(button);
        };
        return button;
    }

    private Flyout BuildTabMenu(TopRailTab tab)
    {
        var rename = new Button
        {
            Content = BuildMenuContent("edit", "Rename tab"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        rename.Classes.Add("sidebar");

        var close = new Button
        {
            Content = BuildMenuContent("close", "Close tab"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            IsEnabled = tab.IsCloseable
        };
        close.Classes.Add("sidebar");

        var menu = new Flyout
        {
            Placement = PlacementMode.BottomEdgeAlignedLeft,
            Content = new StackPanel
            {
                Width = 260,
                Spacing = 3,
                Margin = new Thickness(12),
                Children = { rename, close }
            }
        };
        rename.Click += (_, _) =>
        {
            menu.Hide();
            ShowRenameFlyout(tab);
        };
        close.Click += (_, _) =>
        {
            menu.Hide();
            Fire("TopRail.Tabs.CloseTab");
            TabCloseRequested?.Invoke(this, tab.Key);
        };
        return menu;
    }

    private void ShowRenameFlyout(TopRailTab tab)
    {
        var input = new TextBox
        {
            Text = tab.Title,
            MinWidth = 240,
            SelectionStart = 0,
            SelectionEnd = tab.Title.Length
        };
        var save = new Button { Content = "Rename", HorizontalAlignment = HorizontalAlignment.Stretch };
        save.Classes.Add("primary");
        var flyout = new Flyout
        {
            Placement = PlacementMode.Bottom,
            Content = new StackPanel
            {
                Width = 260,
                Spacing = 3,
                Margin = new Thickness(12),
                Children =
                {
                    new TextBlock { Text = "Rename tab", FontSize = 20, FontWeight = FontWeight.ExtraBold, Margin = new Thickness(10, 5, 10, 8) },
                    input,
                    save
                }
            }
        };
        save.Click += (_, _) =>
        {
            var title = input.Text?.Trim();
            if (string.IsNullOrWhiteSpace(title)) return;
            flyout.Hide();
            TabRenameRequested?.Invoke(this, new TabRenameRequestedEventArgs(tab.Key, title));
        };
        input.KeyDown += (_, args) =>
        {
            if (args.Key != Key.Enter) return;
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            args.Handled = true;
        };
        flyout.ShowAt(this);
        input.Focus();
    }

    private static Control BuildMenuContent(string icon, string label) => new StackPanel
    {
        Orientation = Orientation.Horizontal,
        Spacing = 9,
        Children =
        {
            new HavenIcon { IconKey = icon, Width = 15, Height = 15, Opacity = 0.72 },
            new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center }
        }
    };

    private void OnLogoClicked(object? sender, RoutedEventArgs e)
    {
        Fire("TopRail.Logo.Click");
        HomeRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnAddTabClicked(object? sender, RoutedEventArgs e)
    {
        Fire("TopRail.Tabs.AddTab");
        NewTabRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnTabOverviewClicked(object? sender, RoutedEventArgs e)
    {
        Fire("TopRail.Tabs.Overview");
        TabOverviewRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnBackClicked(object? sender, RoutedEventArgs e)
    {
        Fire("TopRail.Actions.Back.Click");
        BackRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnAppsClicked(object? sender, RoutedEventArgs e)
    {
        Fire("TopRail.Actions.Apps.Click");
        AppsRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnRecentClicked(object? sender, RoutedEventArgs e)
    {
        Fire("TopRail.Actions.Recent.Click");
        RecentRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnNotificationsClicked(object? sender, RoutedEventArgs e)
    {
        Fire("TopRail.Actions.Notifications.Click");
        ShowNotifications();
    }

    private NotificationCentre CreateNotificationCentre()
    {
        var centre = new NotificationCentre { Height = 520 };
        centre.CloseRequested += (_, _) => _notificationFlyout?.Hide();
        return centre;
    }

    private void OnActionsClicked(object? sender, EventArgs e)
    {
        Fire("TopRail.Actions.Open");
        ActionsRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Register(string name, Control control)
    {
        _eventBus?.RegisterElement(name, control);
        _eventBus?.WirePointerEvents(name, control);
    }

    private void Fire(string name) => _eventBus?.Fire(name);

    private static IBrush ResourceBrush(string key, Color fallback) =>
        Avalonia.Application.Current?.Resources[key] as IBrush ?? new SolidColorBrush(fallback);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _notificationCentre?.Dispose();
        _appLauncherFlyout?.Hide();
        ActionToolbar.Dispose();
    }
}

public sealed record TopRailTab(
    string Key,
    string Title,
    string IconKey,
    bool IsSelected,
    bool IsCloseable);

public sealed class TabRenameRequestedEventArgs(string key, string title) : EventArgs
{
    public string Key { get; } = key;
    public string Title { get; } = title;
}
