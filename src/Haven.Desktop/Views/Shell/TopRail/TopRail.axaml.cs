using Avalonia.Controls;
using Avalonia.Interactivity;
using Haven.Desktop.Events;

namespace Haven.Desktop.Views.Shell.TopRail;

public sealed partial class TopRail : UserControl
{
    private readonly HavenEventBus _eventBus;

    public TopRail()
    {
        _eventBus = new HavenEventBus();
        InitializeComponent();
        WireEvents();
    }

    public TopRail(HavenEventBus eventBus)
    {
        _eventBus = eventBus ?? new HavenEventBus();
        InitializeComponent();
        WireEvents();
    }

    private void WireEvents()
    {
        if (LogoButton is not null)
            _eventBus.WirePointerEvents("TopRail.Logo", LogoButton);
        if (AddTabButton is not null)
            _eventBus.WirePointerEvents("TopRail.Actions.AddTab", AddTabButton);
        if (TabViewButton is not null)
            _eventBus.WirePointerEvents("TopRail.Actions.TabView", TabViewButton);
        if (BackButton is not null)
            _eventBus.WirePointerEvents("TopRail.Actions.Back", BackButton);
        if (AppsButton is not null)
            _eventBus.WirePointerEvents("TopRail.Actions.Apps", AppsButton);
        if (RecentButton is not null)
            _eventBus.WirePointerEvents("TopRail.Actions.Recent", RecentButton);
        if (NotificationsButton is not null)
        {
            _eventBus.WirePointerEvents("TopRail.Actions.Notifications", NotificationsButton);
            NotificationsButton.Click += OnNotificationsClicked;
        }

        if (ActionToolbar is not null)
        {
            ActionToolbar.ActionsClicked += OnActionToolbarActionsClicked;
        }
    }

    private void OnNotificationsClicked(object? sender, RoutedEventArgs e)
    {
        NotificationCentreOverlay?.Toggle();
    }

    private void OnActionToolbarActionsClicked(object? sender, System.EventArgs e)
    {
        // The DynamicActionToolbar manages its own flyout.
    }
}
