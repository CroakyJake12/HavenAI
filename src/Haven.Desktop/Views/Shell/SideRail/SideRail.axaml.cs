using Avalonia.Controls;
using Haven.Desktop.Events;

namespace Haven.Desktop.Views.Shell.SideRail;

public sealed partial class SideRail : UserControl
{
    private readonly HavenEventBus _eventBus;

    public SideRail()
    {
        _eventBus = new HavenEventBus();
        InitializeComponent();
        WireEvents();
    }

    public SideRail(HavenEventBus eventBus)
    {
        _eventBus = eventBus ?? new HavenEventBus();
        InitializeComponent();
        WireEvents();
    }

    private void WireEvents()
    {
        if (ExpandButton is not null)
            _eventBus.WirePointerEvents("SideRail.Expand", ExpandButton);
        if (NewChatButton is not null)
            _eventBus.WirePointerEvents("SideRail.NewChat", NewChatButton);
        if (HomeButton is not null)
            _eventBus.WirePointerEvents("SideRail.Home", HomeButton);
        if (AgentsButton is not null)
            _eventBus.WirePointerEvents("SideRail.Agents", AgentsButton);
        if (SettingsButton is not null)
            _eventBus.WirePointerEvents("SideRail.Settings", SettingsButton);
    }
}
