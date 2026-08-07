using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using ScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Haven.Core;
using Haven.Desktop.Controls;

namespace Haven.Desktop.Views.Shell;

public sealed partial class MainView
{
    private bool _mobileLayoutApplied;
    private Border? _mobileHeader;
    private StackPanel? _mobileTabs;
    private Border? _mobileBottomAffordance;
    private Border? _mobileHomeFooter;
    private Border? _mobileDrawerScrim;
    private Border? _mobileDrawer;
    private StackPanel? _mobileDrawerContent;
    private TextBox? _mobileGoInput;
    private double? _mobileSwipeStartY;

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
        PageContent.Margin = new Thickness(0, 0, 0, 92);

        _mobileHeader = BuildMobileHeader();
        Grid.SetRow(_mobileHeader, 0);
        _mobileHeader.ZIndex = 30;
        root.Children.Add(_mobileHeader);

        _mobileBottomAffordance = BuildHistoryAffordance();
        Grid.SetRow(_mobileBottomAffordance, 1);
        _mobileBottomAffordance.ZIndex = 40;
        root.Children.Add(_mobileBottomAffordance);

        _mobileHomeFooter = BuildHomeFooter();
        Grid.SetRow(_mobileHomeFooter, 1);
        _mobileHomeFooter.ZIndex = 45;
        root.Children.Add(_mobileHomeFooter);

        _mobileDrawerScrim = new Border
        {
            IsVisible = false,
            Background = new SolidColorBrush(Color.FromArgb(105, 0, 0, 0))
        };
        Grid.SetRowSpan(_mobileDrawerScrim, 2);
        _mobileDrawerScrim.ZIndex = 90;
        _mobileDrawerScrim.Tapped += (_, _) => CloseMobileDrawer();
        root.Children.Add(_mobileDrawerScrim);

        _mobileDrawerContent = new StackPanel { Spacing = 10 };
        _mobileDrawer = BuildDrawer(_mobileDrawerContent);
        Grid.SetRowSpan(_mobileDrawer, 2);
        _mobileDrawer.ZIndex = 100;
        root.Children.Add(_mobileDrawer);

        OpenTabs.CollectionChanged += OnMobileTabsChanged;
        Notifications.CollectionChanged += OnMobileNotificationsChanged;
        PropertyChanged += OnMobileShellPropertyChanged;
        RefreshMobileChrome();
    }

    private Border BuildMobileHeader()
    {
        var brand = MobileButton(ModelNameText.Text ?? _preferences.DefaultModel ?? "Model", "haven", ShowModelSelector, 10);
        var actions = MobileButton("Actions", "commands", ShowMobileActions, 8);
        var apps = MobileButton("Apps", "apps", () => _ = ShowMobileLauncherAsync(), 8);
        var notifications = MobileIconButton("notification", ShowMobileNotifications, "Alerts");

        var firstRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto,Auto"),
            ColumnSpacing = 6,
            Margin = new Thickness(8, 8, 8, 4)
        };
        firstRow.Children.Add(brand);
        Grid.SetColumn(actions, 2);
        Grid.SetColumn(apps, 3);
        Grid.SetColumn(notifications, 4);
        firstRow.Children.Add(actions);
        firstRow.Children.Add(apps);
        firstRow.Children.Add(notifications);

        _mobileTabs = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 5,
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
            Width = 62,
            Height = 5,
            CornerRadius = new CornerRadius(99),
            Background = ResourceBrush("HavenMutedBrush"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Opacity = 0.65
        };
        var newChat = MobileButton("New chat", "plus", () => _ = OpenNewChatAsync(forceNewTab: true), 10);
        var newGroup = MobileButton("New group", "folder", () =>
        {
            if (NewContainerCommand.CanExecute(null))
                NewContainerCommand.Execute(null);
        }, 10);
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Center,
            Children = { newChat, newGroup }
        };
        var content = new StackPanel
        {
            Spacing = 6,
            Children =
            {
                handle,
                buttons,
                new TextBlock
                {
                    Text = "Swipe up for all chats",
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
            Margin = new Thickness(10, 0, 10, 8),
            Padding = new Thickness(12, 7),
            MinWidth = 250,
            CornerRadius = new CornerRadius(24),
            Background = ResourceBrush("HavenElevatedBrush"),
            BorderBrush = ResourceBrush("HavenAccentBorderBrush"),
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
        var home = MobileIconButton("home", () => _ = OpenHomeAsync(), "Go");
        home.Background = ResourceBrush("HavenAccentSoftBrush");
        home.BorderBrush = ResourceBrush("HavenAccentBorderBrush");
        home.BorderThickness = new Thickness(1);

        _mobileGoInput = new TextBox
        {
            PlaceholderText = "Go — ask Haven",
            MinHeight = 48,
            MinWidth = 0,
            CornerRadius = new CornerRadius(24),
            Padding = new Thickness(14, 10),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        _mobileGoInput.KeyDown += OnMobileGoKeyDown;

        var send = MobileIconButton("send", () => _ = SubmitMobileGoAsync(), "Go");
        send.MinHeight = 44;
        send.MinWidth = 44;
        send.CornerRadius = new CornerRadius(22);

        var chatBar = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 5,
            MinWidth = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        chatBar.Children.Add(_mobileGoInput);
        Grid.SetColumn(send, 1);
        chatBar.Children.Add(send);

        var footer = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            ColumnSpacing = 6,
            Margin = new Thickness(6, 0, 6, 8),
            MinWidth = 0,
            VerticalAlignment = VerticalAlignment.Bottom
        };
        footer.Children.Add(home);
        Grid.SetColumn(chatBar, 1);
        footer.Children.Add(chatBar);

        return new Border
        {
            VerticalAlignment = VerticalAlignment.Bottom,
            Background = ResourceBrush("HavenBackgroundBrush"),
            BorderBrush = ResourceBrush("HavenAccentBorderBrush"),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(0, 7, 0, 0),
            Child = footer
        };
    }
    private Border BuildDrawer(StackPanel content)
    {
        var close = MobileIconButton("close", CloseMobileDrawer, "Close");
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
                    Width = 58,
                    Height = 5,
                    CornerRadius = new CornerRadius(99),
                    Background = ResourceBrush("HavenMutedBrush"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Opacity = 0.65
                },
                header,
                new ScrollViewer
                {
                    MaxHeight = 610,
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
            BorderBrush = ResourceBrush("HavenAccentBorderBrush"),
            BorderThickness = new Thickness(1),
            BoxShadow = new BoxShadows(new BoxShadow
            {
                Blur = 28,
                OffsetY = -4,
                Color = Color.FromArgb(65, 0, 0, 0)
            }),
            Child = stack
        };
        drawer.PointerPressed += (_, e) => _mobileSwipeStartY = e.GetPosition(this).Y;
        drawer.PointerReleased += (_, e) =>
        {
            if (_mobileSwipeStartY is double start && e.GetPosition(this).Y - start > 36)
                CloseMobileDrawer();
            _mobileSwipeStartY = null;
        };
        return drawer;
    }
}
