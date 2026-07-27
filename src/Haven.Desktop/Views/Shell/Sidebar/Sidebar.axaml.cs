using Avalonia.Controls;
using Haven.Desktop.Events;

namespace Haven.Desktop.Views.Shell.Sidebar;

public sealed partial class Sidebar : UserControl
{
    private readonly HavenEventBus _eventBus;

    public Sidebar()
    {
        _eventBus = new HavenEventBus();
        InitializeComponent();
        WireEvents();
    }

    public Sidebar(HavenEventBus eventBus)
    {
        _eventBus = eventBus ?? new HavenEventBus();
        InitializeComponent();
        WireEvents();
    }

    private void WireEvents()
    {
        if (ChatTypeButton is not null)
            _eventBus.WirePointerEvents("Sidebar.ChatType", ChatTypeButton);
        if (ProjectIdentityButton is not null)
            _eventBus.WirePointerEvents("Sidebar.ProjectIdentity", ProjectIdentityButton);
        if (NewChatFullButton is not null)
            _eventBus.WirePointerEvents("Sidebar.NewChat", NewChatFullButton);
        if (NewProjectChatButton is not null)
            _eventBus.WirePointerEvents("Sidebar.NewProjectChat", NewProjectChatButton);
        if (ProjectHomeButton is not null)
            _eventBus.WirePointerEvents("Sidebar.ProjectHome", ProjectHomeButton);
        if (SearchBox is not null)
            _eventBus.WirePointerEvents("Sidebar.Search", SearchBox);
        if (QuickChatsButton is not null)
            _eventBus.WirePointerEvents("Sidebar.QuickChats", QuickChatsButton);
        if (NewContainerButton is not null)
            _eventBus.WirePointerEvents("Sidebar.NewContainer", NewContainerButton);
        if (RefreshFilesButton is not null)
            _eventBus.WirePointerEvents("Sidebar.RefreshFiles", RefreshFilesButton);
        if (BuildButton is not null)
            _eventBus.WirePointerEvents("Sidebar.Build", BuildButton);
        if (TestButton is not null)
            _eventBus.WirePointerEvents("Sidebar.Test", TestButton);
        if (ProjectSettingsButton is not null)
            _eventBus.WirePointerEvents("Sidebar.ProjectSettings", ProjectSettingsButton);
        if (StudioHomeButton is not null)
            _eventBus.WirePointerEvents("Sidebar.StudioHome", StudioHomeButton);
        if (AgentsFullButton is not null)
            _eventBus.WirePointerEvents("Sidebar.Agents", AgentsFullButton);
        if (PluginsButton is not null)
            _eventBus.WirePointerEvents("Sidebar.Plugins", PluginsButton);
        if (PromptsButton is not null)
            _eventBus.WirePointerEvents("Sidebar.Prompts", PromptsButton);
        if (MoreToolsButton is not null)
            _eventBus.WirePointerEvents("Sidebar.MoreTools", MoreToolsButton);
        if (ArchiveButton is not null)
            _eventBus.WirePointerEvents("Sidebar.Archive", ArchiveButton);
        if (SettingsFullButton is not null)
            _eventBus.WirePointerEvents("Sidebar.Settings", SettingsFullButton);
    }
}
