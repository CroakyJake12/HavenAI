using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Haven.Core;
using Haven.Desktop.Controls;
using Haven.Desktop.Services;
using Haven.Desktop.ViewModels;

#if ANDROID
using Android.Content;
using Android.Content.PM;
#endif

namespace Haven.Desktop.Views.Shell;

public sealed partial class MainView
{

    private bool _mobileLayoutApplied;
    private Border? _mobileHeader;
    private StackPanel? _mobileTabs;
    private Border? _mobileBottomAffordance;
    private Border? _mobileHomeFooter;
    private Border? _mobileDrawer;
    private StackPanel? _mobileDrawerContent;
    private TextBox? _mobileGoInput;
    private double? _mobileSwipeStartY;

    /// <summary>
    /// Reflows the existing Haven shell for a touch-first Android viewport.
    /// The current pages, repositories, commands, tabs, and event bus remain
    /// the source of truth; this method only replaces desktop chrome.
    /// </summary>
    public void ApplyMobileLayout()
    {
        if (_mobileLayoutApplied)
            return;

        _mobileLayoutApplied = true;

        if (Content is not Grid root)
            throw new InvalidOperationException("Haven's mobile shell requires the MainView root grid.");

        var body = root.Children
            .OfType<Grid>()
            .FirstOrDefault(candidate => Grid.GetRow(candidate) == 1)
            ?? throw new InvalidOperationException("Haven's main content grid was not found.");

        var contentHost = body.Children
            .OfType<Grid>()
            .FirstOrDefault(candidate => Grid.GetColumn(candidate) == 1)
            ?? throw new InvalidOperationException("Haven's content host was not found.");

        TopRail.IsVisible = false;
        SidebarControl.IsVisible = false;
        NativeSidebarHost.IsVisible = false;
        ShellContextBar.IsVisible = false;

        body.ColumnDefinitions = new ColumnDefinitions("*");
        body.Margin = new Thickness(0);
        Grid.SetColumn(contentHost, 0);
        Grid.SetColumnSpan(contentHost, 1);

        ContentArea.BorderThickness = new Thickness(0);
        ContentArea.CornerRadius = new CornerRadius(0);
        ContentArea.Background = ResourceBrush("HavenBackgroundBrush");
        PageContent.Margin = new Thickness(0, 0, 0, 62);

        _mobileHeader = BuildMobileHeader();
        Grid.SetRow(_mobileHeader, 0);
        Panel.SetZIndex(_mobileHeader, 30);
        root.Children.Add(_mobileHeader);

        _mobileBottomAffordance = BuildHistoryAffordance();
        Grid.SetRow(_mobileBottomAffordance, 1);
        Panel.SetZIndex(_mobileBottomAffordance, 40);
        root.Children.Add(_mobileBottomAffordance);

        _mobileHomeFooter = BuildHomeFooter();
        Grid.SetRow(_mobileHomeFooter, 1);
        Panel.SetZIndex(_mobileHomeFooter, 45);
        root.Children.Add(_mobileHomeFooter);

        _mobileDrawerContent = new StackPanel { Spacing = 10 };
        _mobileDrawer = BuildDrawer(_mobileDrawerContent);
        Grid.SetRowSpan(_mobileDrawer, 2);
        Panel.SetZIndex(_mobileDrawer, 100);
        root.Children.Add(_mobileDrawer);

        OpenTabs.CollectionChanged += OnMobileTabsChanged;
        Notifications.CollectionChanged += OnMobileNotificationsChanged;
        PropertyChanged += OnMobileShellPropertyChanged;

        RefreshMobileChrome();
    }

    private Border BuildMobileHeader()
    {
        var brand = MobileButton(
            "Haven",
            "home",
            () => _ = OpenHomeAsync(),
            horizontalPadding: 10);

        var actions = MobileButton(
            "Actions",
            "commands",
            () =>
            {
                if (OpenCommandPaletteCommand.CanExecute(null))
                    OpenCommandPaletteCommand.Execute(null);
            });

        var modes = MobileButton(
            "Modes",
            "apps",
            () => _ = ShowMobileLauncherAsync());

        var notifications = MobileButton(
            "Alerts",
            "notification",
            ShowMobileNotifications);

        var firstRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto,Auto"),
            ColumnSpacing = 6,
            Margin = new Thickness(8, 8, 8, 4)
        };
        firstRow.Children.Add(brand);
        Grid.SetColumn(actions, 2);
        Grid.SetColumn(modes, 3);
        Grid.SetColumn(notifications, 4);
        firstRow.Children.Add(actions);
        firstRow.Children.Add(modes);
        firstRow.Children.Add(notifications);

        _mobileTabs = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Margin = new Thickness(8, 0, 8, 8)
        };

        var tabsScroller = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _mobileTabs
        };

        return new Border
        {
            Background = ResourceBrush("HavenElevatedBrush"),
            BorderBrush = ResourceBrush("HavenLineBrush"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = new StackPanel
            {
                Spacing = 2,
                Children = { firstRow, tabsScroller }
            }
        };
    }

    private Border BuildHistoryAffordance()
    {
        var handle = new Border
        {
            Width = 44,
            Height = 4,
            CornerRadius = new CornerRadius(99),
            Background = ResourceBrush("HavenMutedBrush"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Opacity = 0.55
        };

        var content = new StackPanel
        {
            Spacing = 5,
            Children =
            {
                handle,
                new TextBlock
                {
                    Text = "Swipe up for chats & this screen",
                    FontSize = 11,
                    FontWeight = FontWeight.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Foreground = ResourceBrush("HavenTextSoftBrush")
                }
            }
        };

        var border = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(12, 0, 12, 8),
            Padding = new Thickness(18, 8),
            CornerRadius = new CornerRadius(22),
            Background = ResourceBrush("HavenElevatedBrush"),
            BorderBrush = ResourceBrush("HavenLineBrush"),
            BorderThickness = new Thickness(1),
            Child = content
        };

        border.PointerPressed += OnMobileAffordancePointerPressed;
        border.PointerReleased += OnMobileAffordancePointerReleased;
        border.Tapped += (_, _) => _ = OpenMobileContextDrawerAsync();
        return border;
    }

    private Border BuildHomeFooter()
    {
        var dashboard = MobileButton("Dashboard", "dashboard", () => _ = OpenDashboardAsync(), 10);
        var launcher = MobileButton("Launcher", "apps", () => _ = ShowMobileLauncherAsync(), 10);

        var bubble = new Border
        {
            Padding = new Thickness(3),
            CornerRadius = new CornerRadius(999),
            Background = ResourceBrush("HavenElevatedBrush"),
            BorderBrush = ResourceBrush("HavenLineStrongBrush"),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 2,
                Children = { dashboard, launcher }
            }
        };

        _mobileGoInput = new TextBox
        {
            Watermark = "Go — ask about this screen or open something",
            MinHeight = 48,
            CornerRadius = new CornerRadius(24),
            Padding = new Thickness(16, 10),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        _mobileGoInput.KeyDown += OnMobileGoKeyDown;

        var send = MobileButton("Go", "send", () => _ = SubmitMobileGoAsync(), 14);
        send.MinHeight = 44;
        send.CornerRadius = new CornerRadius(22);

        var chatBar = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 6,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        chatBar.Children.Add(_mobileGoInput);
        Grid.SetColumn(send, 1);
        chatBar.Children.Add(send);

        var footer = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            ColumnSpacing = 8,
            Margin = new Thickness(8, 0, 8, 8),
            VerticalAlignment = VerticalAlignment.Bottom
        };
        footer.Children.Add(bubble);
        Grid.SetColumn(chatBar, 1);
        footer.Children.Add(chatBar);

        return new Border
        {
            VerticalAlignment = VerticalAlignment.Bottom,
            Background = ResourceBrush("HavenBackgroundBrush"),
            BorderBrush = ResourceBrush("HavenLineBrush"),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(0, 8, 0, 0),
            Child = footer
        };
    }

    private Border BuildDrawer(StackPanel content)
    {
        var close = MobileButton("Close", "close", CloseMobileDrawer, 12);
        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Children =
            {
                new TextBlock
                {
                    Text = "Haven",
                    FontSize = 20,
                    FontWeight = FontWeight.Bold,
                    VerticalAlignment = VerticalAlignment.Center
                }
            }
        };
        Grid.SetColumn(close, 1);
        header.Children.Add(close);

        var stack = new StackPanel
        {
            Spacing = 12,
            Children =
            {
                new Border
                {
                    Width = 52,
                    Height = 5,
                    CornerRadius = new CornerRadius(99),
                    Background = ResourceBrush("HavenMutedBrush"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Opacity = 0.55
                },
                header,
                new ScrollViewer
                {
                    MaxHeight = 560,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Content = content
                }
            }
        };

        var drawer = new Border
        {
            IsVisible = false,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(8),
            Padding = new Thickness(16, 10, 16, 18),
            CornerRadius = new CornerRadius(28),
            Background = ResourceBrush("HavenElevatedBrush"),
            BorderBrush = ResourceBrush("HavenLineStrongBrush"),
            BorderThickness = new Thickness(1),
            BoxShadow = new BoxShadows(new BoxShadow
            {
                Blur = 28,
                OffsetY = -4,
                Color = Color.FromArgb(50, 0, 0, 0)
            }),
            Child = stack
        };

        drawer.PointerPressed += (_, e) => _mobileSwipeStartY = e.GetPosition(this).Y;
        drawer.PointerReleased += (_, e) =>
        {
            if (_mobileSwipeStartY is double start
                && e.GetPosition(this).Y - start > 36)
            {
                CloseMobileDrawer();
            }
            _mobileSwipeStartY = null;
        };

        return drawer;
    }

}
