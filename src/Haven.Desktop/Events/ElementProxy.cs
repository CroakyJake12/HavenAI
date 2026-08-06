namespace Haven.Desktop.Events;

/// <summary>
/// Base class for element proxy objects. Provides common pointer event tokens.
/// </summary>
public abstract class ElementProxy
{
    protected string QualifiedName { get; }

    protected ElementProxy(string qualifiedName)
    {
        QualifiedName = qualifiedName;
    }

    public EventToken Click() => new($"{QualifiedName}.Click");
    public EventToken DoubleClick() => new($"{QualifiedName}.DoubleClick");
    public EventToken RightClick() => new($"{QualifiedName}.RightClick");
    public EventToken Hover() => new($"{QualifiedName}.Hover");
    public EventToken Leave() => new($"{QualifiedName}.Leave");
    public EventToken Press() => new($"{QualifiedName}.Press");
    public EventToken Release() => new($"{QualifiedName}.Release");
    public EventToken Move() => new($"{QualifiedName}.Move");
    public EventToken Wheel() => new($"{QualifiedName}.Wheel");
}

/// <summary>
/// Proxy for a child element that can be accessed by index or name.
/// </summary>
public sealed class ChildElementProxy : ElementProxy
{
    public ChildElementProxy(string parentName, string childName)
        : base($"{parentName}.{childName}") { }
}

// ============================================================
//  SHELL PROXIES
// ============================================================

/// <summary>
/// Proxy for the TopRail section. Accessible as TopRail.*
/// </summary>
public static class TopRail
{
    public static readonly TopRailActionsProxy Actions = new();
    public static readonly TopRailSearchProxy Search = new();
    public static readonly TopRailStatusProxy Status = new();
    public static readonly TopRailMenuProxy Menu = new();
    public static readonly TopRailTabsProxy Tabs = new();
}

public sealed class TopRailActionsProxy : ElementProxy
{
    public TopRailActionsProxy() : base("TopRail.Actions") { }

    public EventToken HomeClick() => new("TopRail.Actions.HomeClick");
    public EventToken ChatClick() => new("TopRail.Actions.ChatClick");
    public EventToken CallClick() => new("TopRail.Actions.CallClick");
    public EventToken TasksClick() => new("TopRail.Actions.TasksClick");
    public EventToken StudioClick() => new("TopRail.Actions.StudioClick");
    public EventToken BrowserClick() => new("TopRail.Actions.BrowserClick");
    public EventToken PlanClick() => new("TopRail.Actions.PlanClick");
    public EventToken TrainingClick() => new("TopRail.Actions.TrainingClick");
    public EventToken SettingsClick() => new("TopRail.Actions.SettingsClick");
    public EventToken SidebarToggle() => new("TopRail.Actions.SidebarToggle");
    public EventToken CommandPaletteToggle() => new("TopRail.Actions.CommandPaletteToggle");
}

public sealed class TopRailSearchProxy : ElementProxy
{
    public TopRailSearchProxy() : base("TopRail.Search") { }
    public EventToken Focused() => new("TopRail.Search.Focused");
    public EventToken Blurred() => new("TopRail.Search.Blurred");
    public EventToken Submitted() => new("TopRail.Search.Submitted");
}

public sealed class TopRailStatusProxy : ElementProxy
{
    public TopRailStatusProxy() : base("TopRail.Status") { }
    public EventToken OllamaClick() => new("TopRail.Status.OllamaClick");
}

public sealed class TopRailMenuProxy : ElementProxy
{
    public TopRailMenuProxy() : base("TopRail.Menu") { }
    public EventToken FileClick() => new("TopRail.Menu.FileClick");
    public EventToken EditClick() => new("TopRail.Menu.EditClick");
    public EventToken ViewClick() => new("TopRail.Menu.ViewClick");
}

public sealed class TopRailTabsProxy : ElementProxy
{
    public TopRailTabsProxy() : base("TopRail.Tabs") { }
    public EventToken AddTab() => new("TopRail.Tabs.AddTab");
    public EventToken CloseTab() => new("TopRail.Tabs.CloseTab");
    public EventToken TabClicked() => new("TopRail.Tabs.TabClicked");
    public EventToken TabHover() => new("TopRail.Tabs.TabHover");
}

// ============================================================
//  SIDE RAIL PROXIES
// ============================================================

/// <summary>
/// Proxy for the SideRail section. Accessible as SideRail.*
/// </summary>
public static class SideRail
{
    public static readonly SideRailNavigationProxy Navigation = new();
    public static readonly SideRailActionsProxy Actions = new();
}

public sealed class SideRailNavigationProxy : ElementProxy
{
    public SideRailNavigationProxy() : base("SideRail.Navigation") { }

    public EventToken HomeClick() => new("SideRail.Navigation.HomeClick");
    public EventToken ChatClick() => new("SideRail.Navigation.ChatClick");
    public EventToken CallClick() => new("SideRail.Navigation.CallClick");
    public EventToken TasksClick() => new("SideRail.Navigation.TasksClick");
    public EventToken StudioClick() => new("SideRail.Navigation.StudioClick");
    public EventToken BrowserClick() => new("SideRail.Navigation.BrowserClick");
    public EventToken PlanClick() => new("SideRail.Navigation.PlanClick");
    public EventToken TrainingClick() => new("SideRail.Navigation.TrainingClick");
}

public sealed class SideRailActionsProxy : ElementProxy
{
    public SideRailActionsProxy() : base("SideRail.Actions") { }
    public EventToken NewChat() => new("SideRail.Actions.NewChat");
    public EventToken Archive() => new("SideRail.Actions.Archive");
    public EventToken Settings() => new("SideRail.Actions.Settings");
}

// ============================================================
//  SIDEBAR PROXIES
// ============================================================

/// <summary>
/// Proxy for the Sidebar section. Accessible as Sidebar.*
/// </summary>
public static class Sidebar
{
    public static readonly SidebarConversationsProxy Conversations = new();
    public static readonly SidebarContainersProxy Containers = new();
    public static readonly SidebarPinsProxy Pins = new();
    public static readonly SidebarSearchProxy Search = new();
}

public sealed class SidebarConversationsProxy : ElementProxy
{
    public SidebarConversationsProxy() : base("Sidebar.Conversations") { }
    public EventToken ItemClick() => new("Sidebar.Conversations.ItemClick");
    public EventToken ItemHover() => new("Sidebar.Conversations.ItemHover");
    public EventToken ItemRightClick() => new("Sidebar.Conversations.ItemRightClick");
    public EventToken ItemPin() => new("Sidebar.Conversations.ItemPin");
    public EventToken ItemRename() => new("Sidebar.Conversations.ItemRename");
    public EventToken ItemDelete() => new("Sidebar.Conversations.ItemDelete");
}

public sealed class SidebarContainersProxy : ElementProxy
{
    public SidebarContainersProxy() : base("Sidebar.Containers") { }
    public EventToken ItemClick() => new("Sidebar.Containers.ItemClick");
    public EventToken ItemHover() => new("Sidebar.Containers.ItemHover");
    public EventToken CreateClick() => new("Sidebar.Containers.CreateClick");
}

public sealed class SidebarPinsProxy : ElementProxy
{
    public SidebarPinsProxy() : base("Sidebar.Pins") { }
    public EventToken ItemClick() => new("Sidebar.Pins.ItemClick");
    public EventToken ItemRemove() => new("Sidebar.Pins.ItemRemove");
}

public sealed class SidebarSearchProxy : ElementProxy
{
    public SidebarSearchProxy() : base("Sidebar.Search") { }
    public EventToken Focused() => new("Sidebar.Search.Focused");
    public EventToken Blurred() => new("Sidebar.Search.Blurred");
    public EventToken QueryChanged() => new("Sidebar.Search.QueryChanged");
}

// ============================================================
//  HOME PAGE PROXIES
// ============================================================

/// <summary>
/// Proxy for the Home page. Accessible as Home.*
/// </summary>
public static class Home
{
    public static readonly HomeDashboardProxy Dashboard = new();
    public static readonly HomeAgendaProxy Agenda = new();
    public static readonly HomeRecentWorkProxy RecentWork = new();
    public static readonly HomeHeaderProxy Header = new();
}

public sealed class HomeHeaderProxy : ElementProxy
{
    public HomeHeaderProxy() : base("Home.Header") { }
    public EventToken CustomizeClick() => new("Home.Header.CustomizeClick");
    public EventToken RefreshClick() => new("Home.Header.RefreshClick");
}

public sealed class HomeDashboardProxy : ElementProxy
{
    public HomeDashboardProxy() : base("Home.Dashboard") { }

    public ChildElementProxy Tile(int index) => new("Home.Dashboard", $"Tile{index}");
    public EventToken TileClick(int index) => new($"Home.Dashboard.Tile{index}.Click");
    public EventToken TileHover(int index) => new($"Home.Dashboard.Tile{index}.Hover");
    public EventToken TileLeave(int index) => new($"Home.Dashboard.Tile{index}.Leave");
    public EventToken TileOpen(int index) => new($"Home.Dashboard.Tile{index}.Open");
    public EventToken TileMoveEarlier(int index) => new($"Home.Dashboard.Tile{index}.MoveEarlier");
    public EventToken TileMoveLater(int index) => new($"Home.Dashboard.Tile{index}.MoveLater");
    public EventToken TileToggle(int index) => new($"Home.Dashboard.Tile{index}.Toggle");
}

public sealed class HomeAgendaProxy : ElementProxy
{
    public HomeAgendaProxy() : base("Home.Agenda") { }

    public ChildElementProxy Item(int index) => new("Home.Agenda", $"Item{index}");
    public EventToken ItemClick(int index) => new($"Home.Agenda.Item{index}.Click");
    public EventToken ItemHover(int index) => new($"Home.Agenda.Item{index}.Hover");
}

public sealed class HomeRecentWorkProxy : ElementProxy
{
    public HomeRecentWorkProxy() : base("Home.RecentWork") { }

    public ChildElementProxy Item(int index) => new("Home.RecentWork", $"Item{index}");
    public EventToken ItemClick(int index) => new($"Home.RecentWork.Item{index}.Click");
    public EventToken ItemHover(int index) => new($"Home.RecentWork.Item{index}.Hover");
}

// ============================================================
//  CHAT PAGE PROXIES
// ============================================================

/// <summary>
/// Proxy for the Chat page. Accessible as Chat.*
/// </summary>
public static class Chat
{
    public static readonly ChatComposerProxy Composer = new();
    public static readonly ChatMessagesProxy Messages = new();
    public static readonly ChatToolbarProxy Toolbar = new();
    public static readonly ChatModelProxy Model = new();
    public static readonly ChatAttachmentsProxy Attachments = new();
    public static readonly ChatPickersProxy Pickers = new();
    public static readonly ChatSidebarProxy Sidebar = new();
}

public sealed class ChatComposerProxy : ElementProxy
{
    public ChatComposerProxy() : base("Chat.Composer") { }
    public EventToken TextChanged() => new("Chat.Composer.TextChanged");
    public EventToken Focused() => new("Chat.Composer.Focused");
    public EventToken Blurred() => new("Chat.Composer.Blurred");
    public EventToken SendClick() => new("Chat.Composer.SendClick");
    public EventToken StopClick() => new("Chat.Composer.StopClick");
    public EventToken AttachClick() => new("Chat.Composer.AttachClick");
    public EventToken DictateClick() => new("Chat.Composer.DictateClick");
    public EventToken KeyDown() => new("Chat.Composer.KeyDown");
}

public sealed class ChatMessagesProxy : ElementProxy
{
    public ChatMessagesProxy() : base("Chat.Messages") { }

    public ChildElementProxy Message(int index) => new("Chat.Messages", $"Message{index}");
    public EventToken MessageClick(int index) => new($"Chat.Messages.Message{index}.Click");
    public EventToken MessageHover(int index) => new($"Chat.Messages.Message{index}.Hover");
    public EventToken MessageActionsClick(int index) => new($"Chat.Messages.Message{index}.ActionsClick");
    public EventToken MessageCopy(int index) => new($"Chat.Messages.Message{index}.Copy");
    public EventToken MessageBranch(int index) => new($"Chat.Messages.Message{index}.Branch");
}

public sealed class ChatToolbarProxy : ElementProxy
{
    public ChatToolbarProxy() : base("Chat.Toolbar") { }
    public EventToken BranchClick() => new("Chat.Toolbar.BranchClick");
    public EventToken CompactClick() => new("Chat.Toolbar.CompactClick");
    public EventToken ArchiveClick() => new("Chat.Toolbar.ArchiveClick");
    public EventToken CopyLastClick() => new("Chat.Toolbar.CopyLastClick");
    public EventToken UndoClick() => new("Chat.Toolbar.UndoClick");
    public EventToken RedoClick() => new("Chat.Toolbar.RedoClick");
}

public sealed class ChatModelProxy : ElementProxy
{
    public ChatModelProxy() : base("Chat.Model") { }
    public EventToken PickerClick() => new("Chat.Model.PickerClick");
    public EventToken ModelSelected() => new("Chat.Model.ModelSelected");
}

public sealed class ChatAttachmentsProxy : ElementProxy
{
    public ChatAttachmentsProxy() : base("Chat.Attachments") { }
    public EventToken ItemClick() => new("Chat.Attachments.ItemClick");
    public EventToken ItemRemove() => new("Chat.Attachments.ItemRemove");
}

public sealed class ChatPickersProxy : ElementProxy
{
    public ChatPickersProxy() : base("Chat.Pickers") { }
    public EventToken PluginOpen() => new("Chat.Pickers.PluginOpen");
    public EventToken PluginDismiss() => new("Chat.Pickers.PluginDismiss");
    public EventToken PluginSelect() => new("Chat.Pickers.PluginSelect");
    public EventToken PromptOpen() => new("Chat.Pickers.PromptOpen");
    public EventToken PromptDismiss() => new("Chat.Pickers.PromptDismiss");
    public EventToken PromptSelect() => new("Chat.Pickers.PromptSelect");
    public EventToken ModelOpen() => new("Chat.Pickers.ModelOpen");
    public EventToken ModelDismiss() => new("Chat.Pickers.ModelDismiss");
    public EventToken ModelSelect() => new("Chat.Pickers.ModelSelect");
}

public sealed class ChatSidebarProxy : ElementProxy
{
    public ChatSidebarProxy() : base("Chat.Sidebar") { }
    public EventToken ContainerClick() => new("Chat.Sidebar.ContainerClick");
    public EventToken ContainerHover() => new("Chat.Sidebar.ContainerHover");
    public EventToken ContainerDelete() => new("Chat.Sidebar.ContainerDelete");
    public EventToken ContainerCreate() => new("Chat.Sidebar.ContainerCreate");
    public EventToken LessonClick() => new("Chat.Sidebar.LessonClick");
    public EventToken LessonHover() => new("Chat.Sidebar.LessonHover");
    public EventToken LessonCreate() => new("Chat.Sidebar.LessonCreate");
    public EventToken LessonDelete() => new("Chat.Sidebar.LessonDelete");
    public EventToken QuickChatsClick() => new("Chat.Sidebar.QuickChatsClick");
}

// ============================================================
//  CALL PAGE PROXIES
// ============================================================

/// <summary>
/// Proxy for the Call page. Accessible as Call.*
/// </summary>
public static class Call
{
    public static readonly CallControlsProxy Controls = new();
    public static readonly CallStatusProxy Status = new();
}

public sealed class CallControlsProxy : ElementProxy
{
    public CallControlsProxy() : base("Call.Controls") { }
    public EventToken StartClick() => new("Call.Controls.StartClick");
    public EventToken EndClick() => new("Call.Controls.EndClick");
    public EventToken MuteClick() => new("Call.Controls.MuteClick");
    public EventToken unmuteClick() => new("Call.Controls.UnmuteClick");
}

public sealed class CallStatusProxy : ElementProxy
{
    public CallStatusProxy() : base("Call.Status") { }
    public EventToken Active() => new("Call.Status.Active");
    public EventToken Ended() => new("Call.Status.Ended");
}

// ============================================================
//  PLAN PAGE PROXIES
// ============================================================

/// <summary>
/// Proxy for the Plan page. Accessible as Plan.*
/// </summary>
public static class Plan
{
    public static readonly PlanTasksProxy Tasks = new();
    public static readonly PlanEventsProxy Events = new();
    public static readonly PlanCollectionsProxy Collections = new();
    public static readonly PlanCalendarProxy Calendar = new();
    public static readonly PlanViewsProxy Views = new();
    public static readonly PlanBoardProxy Board = new();
    public static readonly PlanAiProxy Ai = new();
    public static readonly PlanActionsProxy Actions = new();
}

public sealed class PlanTasksProxy : ElementProxy
{
    public PlanTasksProxy() : base("Plan.Tasks") { }

    public ChildElementProxy Item(int index) => new("Plan.Tasks", $"Item{index}");
    public EventToken ItemClick(int index) => new($"Plan.Tasks.Item{index}.Click");
    public EventToken ItemHover(int index) => new($"Plan.Tasks.Item{index}.Hover");
    public EventToken ItemLeave(int index) => new($"Plan.Tasks.Item{index}.Leave");
    public EventToken ItemComplete(int index) => new($"Plan.Tasks.Item{index}.Complete");
    public EventToken ItemEdit(int index) => new($"Plan.Tasks.Item{index}.Edit");
    public EventToken ItemSubtask(int index) => new($"Plan.Tasks.Item{index}.Subtask");
    public EventToken ItemStart(int index) => new($"Plan.Tasks.Item{index}.Start");
    public EventToken ItemDelete(int index) => new($"Plan.Tasks.Item{index}.Delete");
    public EventToken ItemDrag(int index) => new($"Plan.Tasks.Item{index}.Drag");
}

public sealed class PlanEventsProxy : ElementProxy
{
    public PlanEventsProxy() : base("Plan.Events") { }

    public ChildElementProxy Item(int index) => new("Plan.Events", $"Item{index}");
    public EventToken ItemClick(int index) => new($"Plan.Events.Item{index}.Click");
    public EventToken ItemHover(int index) => new($"Plan.Events.Item{index}.Hover");
    public EventToken ItemLeave(int index) => new($"Plan.Events.Item{index}.Leave");
    public EventToken ItemEdit(int index) => new($"Plan.Events.Item{index}.Edit");
    public EventToken ItemDelete(int index) => new($"Plan.Events.Item{index}.Delete");
}

public sealed class PlanCollectionsProxy : ElementProxy
{
    public PlanCollectionsProxy() : base("Plan.Collections") { }

    public ChildElementProxy Item(int index) => new("Plan.Collections", $"Item{index}");
    public EventToken ItemClick(int index) => new($"Plan.Collections.Item{index}.Click");
    public EventToken ItemHover(int index) => new($"Plan.Collections.Item{index}.Hover");
    public EventToken ItemLeave(int index) => new($"Plan.Collections.Item{index}.Leave");
    public EventToken ItemMoveUp(int index) => new($"Plan.Collections.Item{index}.MoveUp");
    public EventToken ItemMoveDown(int index) => new($"Plan.Collections.Item{index}.MoveDown");
}

public sealed class PlanCalendarProxy : ElementProxy
{
    public PlanCalendarProxy() : base("Plan.Calendar") { }

    public EventToken ProviderConnect(int index) => new($"Plan.Calendar.Provider{index}.Connect");
    public EventToken ProviderSync(int index) => new($"Plan.Calendar.Provider{index}.Sync");
    public EventToken ProviderDisconnect(int index) => new($"Plan.Calendar.Provider{index}.Disconnect");
    public EventToken ConflictKeepHaven(int index) => new($"Plan.Conflict{index}.KeepHaven");
    public EventToken ConflictKeepProvider(int index) => new($"Plan.Conflict{index}.KeepProvider");
    public EventToken ConflictDuplicate(int index) => new($"Plan.Conflict{index}.Duplicate");
}

public sealed class PlanViewsProxy : ElementProxy
{
    public PlanViewsProxy() : base("Plan.Views") { }

    public ChildElementProxy Item(int index) => new("Plan.Views", $"Item{index}");
    public EventToken ItemClick(int index) => new($"Plan.Views.Item{index}.Click");
    public EventToken ItemHover(int index) => new($"Plan.Views.Item{index}.Hover");
}

public sealed class PlanBoardProxy : ElementProxy
{
    public PlanBoardProxy() : base("Plan.Board") { }

    public ChildElementProxy Task(int index) => new("Plan.Board", $"Task{index}");
    public EventToken TaskHover(int index) => new($"Plan.Board.Task{index}.Hover");
    public EventToken TaskLeave(int index) => new($"Plan.Board.Task{index}.Leave");
    public EventToken TaskEdit(int index) => new($"Plan.Board.Task{index}.Edit");
    public EventToken TaskStart(int index) => new($"Plan.Board.Task{index}.Start");
    public EventToken TaskDone(int index) => new($"Plan.Board.Task{index}.Done");
    public EventToken TaskDrag(int index) => new($"Plan.Board.Task{index}.Drag");
}

public sealed class PlanAiProxy : ElementProxy
{
    public PlanAiProxy() : base("Plan.Ai") { }
    public EventToken PromptFocused() => new("Plan.Ai.PromptFocused");
    public EventToken PromptBlurred() => new("Plan.Ai.PromptBlurred");
    public EventToken AskClick() => new("Plan.Ai.AskClick");
    public EventToken DismissProposal() => new("Plan.Actions.DismissProposal");
    public EventToken ApplyProposal() => new("Plan.Actions.ApplyProposal");
}

public sealed class PlanActionsProxy : ElementProxy
{
    public PlanActionsProxy() : base("Plan.Actions") { }
    public EventToken CreateTask() => new("Plan.Actions.CreateTask");
    public EventToken CreateEvent() => new("Plan.Actions.CreateEvent");
    public EventToken CreateCollection() => new("Plan.Actions.CreateCollection");
    public EventToken RenameCollection() => new("Plan.Actions.RenameCollection");
    public EventToken RequestArchiveCollection() => new("Plan.Actions.RequestArchiveCollection");
    public EventToken CancelArchiveCollection() => new("Plan.Actions.CancelArchiveCollection");
    public EventToken ConfirmArchiveCollection() => new("Plan.Actions.ConfirmArchiveCollection");
    public EventToken PreviousPeriod() => new("Plan.Actions.PreviousPeriod");
    public EventToken NextPeriod() => new("Plan.Actions.NextPeriod");
    public EventToken Today() => new("Plan.Actions.Today");
    public EventToken AskAi() => new("Plan.Actions.AskAi");
    public EventToken CloseTaskEditor() => new("Plan.Actions.CloseTaskEditor");
    public EventToken SaveTask() => new("Plan.Actions.SaveTask");
    public EventToken CloseEventEditor() => new("Plan.Actions.CloseEventEditor");
    public EventToken SaveEvent() => new("Plan.Actions.SaveEvent");
    public EventToken Refresh() => new("Plan.Actions.Refresh");
}

// ============================================================
//  BROWSER PAGE PROXIES
// ============================================================

/// <summary>
/// Proxy for the Browser page. Accessible as Browser.*
/// </summary>
public static class Browser
{
    public static readonly BrowserNavigationProxy Navigation = new();
    public static readonly BrowserToolbarProxy Toolbar = new();
    public static readonly BrowserTabsProxy Tabs = new();
    public static readonly BrowserBookmarksProxy Bookmarks = new();
    public static readonly BrowserHistoryProxy History = new();
    public static readonly BrowserExtensionsProxy Extensions = new();
    public static readonly BrowserLoginsProxy Logins = new();
    public static readonly BrowserAssistantProxy Assistant = new();
    public static readonly BrowserSettingsProxy Settings = new();
}

public sealed class BrowserNavigationProxy : ElementProxy
{
    public BrowserNavigationProxy() : base("Browser.Navigation") { }
    public EventToken BackClick() => new("Browser.Navigation.BackClick");
    public EventToken ForwardClick() => new("Browser.Navigation.ForwardClick");
    public EventToken HomeClick() => new("Browser.Navigation.HomeClick");
    public EventToken RefreshClick() => new("Browser.Navigation.RefreshClick");
    public EventToken HardRefreshClick() => new("Browser.Navigation.HardRefreshClick");
    public EventToken StopClick() => new("Browser.Navigation.StopClick");
    public EventToken UrlSubmit() => new("Browser.Navigation.UrlSubmit");
    public EventToken GoClick() => new("Browser.Navigation.GoClick");
}

public sealed class BrowserToolbarProxy : ElementProxy
{
    public BrowserToolbarProxy() : base("Browser.Toolbar") { }
    public EventToken BookmarkClick() => new("Browser.Toolbar.BookmarkClick");
    public EventToken MenuClick() => new("Browser.Toolbar.MenuClick");
    public EventToken SafetyClick() => new("Browser.Toolbar.SafetyClick");
    public EventToken DevToolsClick() => new("Browser.Toolbar.DevToolsClick");
    public EventToken PrintClick() => new("Browser.Toolbar.PrintClick");
}

public sealed class BrowserTabsProxy : ElementProxy
{
    public BrowserTabsProxy() : base("Browser.Tabs") { }

    public ChildElementProxy Tab(int index) => new("Browser.Tabs", $"Tab{index}");
    public EventToken TabClick(int index) => new($"Browser.Tabs.Tab{index}.Click");
    public EventToken TabHover(int index) => new($"Browser.Tabs.Tab{index}.Hover");
    public EventToken TabClose(int index) => new($"Browser.Tabs.Tab{index}.Close");
    public EventToken NewTab() => new("Browser.Tabs.NewTab");
    public EventToken NewPrivateTab() => new("Browser.Tabs.NewPrivateTab");
}

public sealed class BrowserBookmarksProxy : ElementProxy
{
    public BrowserBookmarksProxy() : base("Browser.Bookmarks") { }

    public ChildElementProxy Item(int index) => new("Browser.Bookmarks", $"Item{index}");
    public EventToken ItemClick(int index) => new($"Browser.Bookmarks.Item{index}.Click");
    public EventToken ItemHover(int index) => new($"Browser.Bookmarks.Item{index}.Hover");
    public EventToken ItemDelete(int index) => new($"Browser.Bookmarks.Item{index}.Delete");
    public EventToken AddBookmark() => new("Browser.Bookmarks.AddBookmark");
    public EventToken TogglePanel() => new("Browser.Bookmarks.TogglePanel");
    public EventToken ManageClick() => new("Browser.Bookmarks.ManageClick");
}

public sealed class BrowserHistoryProxy : ElementProxy
{
    public BrowserHistoryProxy() : base("Browser.History") { }

    public ChildElementProxy Item(int index) => new("Browser.History", $"Item{index}");
    public EventToken ItemClick(int index) => new($"Browser.History.Item{index}.Click");
    public EventToken ItemHover(int index) => new($"Browser.History.Item{index}.Hover");
    public EventToken ClearHistory() => new("Browser.History.ClearHistory");
    public EventToken TogglePanel() => new("Browser.History.TogglePanel");
}

public sealed class BrowserExtensionsProxy : ElementProxy
{
    public BrowserExtensionsProxy() : base("Browser.Extensions") { }

    public ChildElementProxy Item(int index) => new("Browser.Extensions", $"Item{index}");
    public EventToken ItemClick(int index) => new($"Browser.Extensions.Item{index}.Click");
    public EventToken ItemToggle(int index) => new($"Browser.Extensions.Item{index}.Toggle");
    public EventToken ItemDelete(int index) => new($"Browser.Extensions.Item{index}.Delete");
    public EventToken ImportClick() => new("Browser.Extensions.ImportClick");
    public EventToken ConvertChromeClick() => new("Browser.Extensions.ConvertChromeClick");
    public EventToken TogglePanel() => new("Browser.Extensions.TogglePanel");
}

public sealed class BrowserLoginsProxy : ElementProxy
{
    public BrowserLoginsProxy() : base("Browser.Logins") { }

    public ChildElementProxy Item(int index) => new("Browser.Logins", $"Item{index}");
    public EventToken ItemClick(int index) => new($"Browser.Logins.Item{index}.Click");
    public EventToken ItemDelete(int index) => new($"Browser.Logins.Item{index}.Delete");
    public EventToken ItemAutofill(int index) => new($"Browser.Logins.Item{index}.Autofill");
    public EventToken SaveLogin() => new("Browser.Logins.SaveLogin");
    public EventToken TogglePanel() => new("Browser.Logins.TogglePanel");
}

public sealed class BrowserAssistantProxy : ElementProxy
{
    public BrowserAssistantProxy() : base("Browser.Assistant") { }
    public EventToken SummariseClick() => new("Browser.Assistant.SummariseClick");
    public EventToken AskClick() => new("Browser.Assistant.AskClick");
    public EventToken InputChanged() => new("Browser.Assistant.InputChanged");
    public EventToken TogglePanel() => new("Browser.Assistant.TogglePanel");
}

public sealed class BrowserSettingsProxy : ElementProxy
{
    public BrowserSettingsProxy() : base("Browser.Settings") { }
    public EventToken SaveClick() => new("Browser.Settings.SaveClick");
    public EventToken TogglePanel() => new("Browser.Settings.TogglePanel");
    public EventToken CreateGroupClick() => new("Browser.Settings.CreateGroupClick");
}

// ============================================================
//  SETTINGS PAGE PROXIES
// ============================================================

/// <summary>
/// Proxy for the Settings page. Accessible as Settings.*
/// </summary>
public static class Settings
{
    public static readonly SettingsGeneralProxy General = new();
    public static readonly SettingsModelProxy Model = new();
    public static readonly SettingsAppearanceProxy Appearance = new();
}

public sealed class SettingsGeneralProxy : ElementProxy
{
    public SettingsGeneralProxy() : base("Settings.General") { }
    public EventToken SaveClick() => new("Settings.General.SaveClick");
    public EventToken ResetClick() => new("Settings.General.ResetClick");
}

public sealed class SettingsModelProxy : ElementProxy
{
    public SettingsModelProxy() : base("Settings.Model") { }
    public EventToken ProviderChanged() => new("Settings.Model.ProviderChanged");
    public EventToken ModelChanged() => new("Settings.Model.ModelChanged");
}

public sealed class SettingsAppearanceProxy : ElementProxy
{
    public SettingsAppearanceProxy() : base("Settings.Appearance") { }
    public EventToken ThemeChanged() => new("Settings.Appearance.ThemeChanged");
}

// ============================================================
//  TRAINING PAGE PROXIES
// ============================================================

/// <summary>
/// Proxy for the Training page. Accessible as Training.*
/// </summary>
public static class Training
{
    public static readonly TrainingControlsProxy Controls = new();
    public static readonly TrainingStatusProxy Status = new();
}

public sealed class TrainingControlsProxy : ElementProxy
{
    public TrainingControlsProxy() : base("Training.Controls") { }
    public EventToken StartClick() => new("Training.Controls.StartClick");
    public EventToken StopClick() => new("Training.Controls.StopClick");
}

public sealed class TrainingStatusProxy : ElementProxy
{
    public TrainingStatusProxy() : base("Training.Status") { }
    public EventToken ProgressUpdate() => new("Training.Status.ProgressUpdate");
}

// ============================================================
//  CATALOG PAGE PROXIES
// ============================================================

/// <summary>
/// Proxy for the Catalog page. Accessible as Catalog.*
/// </summary>
public static class Catalog
{
    public static readonly CatalogListProxy List = new();
    public static readonly CatalogActionsProxy Actions = new();
}

public sealed class CatalogListProxy : ElementProxy
{
    public CatalogListProxy() : base("Catalog.List") { }
    public EventToken ItemClick() => new("Catalog.List.ItemClick");
    public EventToken ItemHover() => new("Catalog.List.ItemHover");
    public EventToken ItemEdit() => new("Catalog.List.ItemEdit");
    public EventToken ItemDelete() => new("Catalog.List.ItemDelete");
}

public sealed class CatalogActionsProxy : ElementProxy
{
    public CatalogActionsProxy() : base("Catalog.Actions") { }
    public EventToken CreateClick() => new("Catalog.Actions.CreateClick");
    public EventToken ImportClick() => new("Catalog.Actions.ImportClick");
}

// ============================================================
//  AUTOMATIONS PAGE PROXIES
// ============================================================

/// <summary>
/// Proxy for the Automations page. Accessible as Automations.*
/// </summary>
public static class Automations
{
    public static readonly AutomationsListProxy List = new();
    public static readonly AutomationsActionsProxy Actions = new();
}

public sealed class AutomationsListProxy : ElementProxy
{
    public AutomationsListProxy() : base("Automations.List") { }
    public EventToken ItemClick() => new("Automations.List.ItemClick");
    public EventToken ItemToggle() => new("Automations.List.ItemToggle");
    public EventToken ItemRun() => new("Automations.List.ItemRun");
}

public sealed class AutomationsActionsProxy : ElementProxy
{
    public AutomationsActionsProxy() : base("Automations.Actions") { }
    public EventToken CreateClick() => new("Automations.Actions.CreateClick");
}

// ============================================================
//  ARCHIVE PAGE PROXIES
// ============================================================

/// <summary>
/// Proxy for the Archive page. Accessible as Archive.*
/// </summary>
public static class Archive
{
    public static readonly ArchiveListProxy List = new();
    public static readonly ArchiveActionsProxy Actions = new();
}

public sealed class ArchiveListProxy : ElementProxy
{
    public ArchiveListProxy() : base("Archive.List") { }
    public EventToken ItemClick() => new("Archive.List.ItemClick");
    public EventToken ItemRestore() => new("Archive.List.ItemRestore");
    public EventToken ItemDelete() => new("Archive.List.ItemDelete");
}

public sealed class ArchiveActionsProxy : ElementProxy
{
    public ArchiveActionsProxy() : base("Archive.Actions") { }
    public EventToken SearchChanged() => new("Archive.Actions.SearchChanged");
    public EventToken Refresh() => new("Archive.Actions.Refresh");
}

// ============================================================
//  ACTIVITY LOG PAGE PROXIES
// ============================================================

/// <summary>
/// Proxy for the Activity Log page. Accessible as ActivityLog.*
/// </summary>
public static class ActivityLog
{
    public static readonly ActivityLogListProxy List = new();
    public static readonly ActivityLogActionsProxy Actions = new();
    public static readonly ActivityLogSearchProxy Search = new();
}

public sealed class ActivityLogListProxy : ElementProxy
{
    public ActivityLogListProxy() : base("ActivityLog.List") { }

    public ChildElementProxy Item(int index) => new("ActivityLog.List", $"Item{index}");
    public EventToken ItemClick(int index) => new($"ActivityLog.List.Item{index}.Click");
    public EventToken ItemHover(int index) => new($"ActivityLog.List.Item{index}.Hover");
    public EventToken ItemLeave(int index) => new($"ActivityLog.List.Item{index}.Leave");
}

public sealed class ActivityLogActionsProxy : ElementProxy
{
    public ActivityLogActionsProxy() : base("ActivityLog.Actions") { }
    public EventToken Refresh() => new("ActivityLog.Actions.Refresh");
}

public sealed class ActivityLogSearchProxy : ElementProxy
{
    public ActivityLogSearchProxy() : base("ActivityLog.Search") { }
    public EventToken QueryChanged() => new("ActivityLog.Search.QueryChanged");
}

// ============================================================
//  MODE LIBRARY PAGE PROXIES
// ============================================================

/// <summary>
/// Proxy for the Mode Library page. Accessible as ModeLibrary.*
/// </summary>
public static class ModeLibrary
{
    public static readonly ModeLibraryListProxy List = new();
    public static readonly ModeLibraryActionsProxy Actions = new();
    public static readonly ModeLibrarySearchProxy Search = new();
}

public sealed class ModeLibraryListProxy : ElementProxy
{
    public ModeLibraryListProxy() : base("ModeLibrary.List") { }

    public ChildElementProxy Item(int index) => new("ModeLibrary.List", $"Item{index}");
    public EventToken ItemClick(int index) => new($"ModeLibrary.List.Item{index}.Click");
    public EventToken ItemHover(int index) => new($"ModeLibrary.List.Item{index}.Hover");
    public EventToken ItemLeave(int index) => new($"ModeLibrary.List.Item{index}.Leave");
    public EventToken ItemPin(int index) => new($"ModeLibrary.List.Item{index}.Pin");
}

public sealed class ModeLibraryActionsProxy : ElementProxy
{
    public ModeLibraryActionsProxy() : base("ModeLibrary.Actions") { }
    public EventToken Refresh() => new("ModeLibrary.Actions.Refresh");
    public EventToken CreateInStudio() => new("ModeLibrary.Actions.CreateInStudio");
}

public sealed class ModeLibrarySearchProxy : ElementProxy
{
    public ModeLibrarySearchProxy() : base("ModeLibrary.Search") { }
    public EventToken QueryChanged() => new("ModeLibrary.Search.QueryChanged");
}

// ============================================================
//  LESSON SETTINGS PAGE PROXIES
// ============================================================

/// <summary>
/// Proxy for the Lesson Settings page. Accessible as LessonSettings.*
/// </summary>
public static class LessonSettings
{
    public static readonly LessonSettingsActionsProxy Actions = new();
}

public sealed class LessonSettingsActionsProxy : ElementProxy
{
    public LessonSettingsActionsProxy() : base("LessonSettings.Actions") { }
    public EventToken Save() => new("LessonSettings.Actions.Save");
}

// ============================================================
//  CONTAINER SETTINGS PAGE PROXIES
// ============================================================

/// <summary>
/// Proxy for the Container Settings page. Accessible as ContainerSettings.*
/// </summary>
public static class ContainerSettings
{
    public static readonly ContainerSettingsActionsProxy Actions = new();
}

public sealed class ContainerSettingsActionsProxy : ElementProxy
{
    public ContainerSettingsActionsProxy() : base("ContainerSettings.Actions") { }
    public EventToken Save() => new("ContainerSettings.Actions.Save");
    public EventToken Archive() => new("ContainerSettings.Actions.Archive");
    public EventToken RequestDelete() => new("ContainerSettings.Actions.RequestDelete");
    public EventToken CancelDelete() => new("ContainerSettings.Actions.CancelDelete");
    public EventToken Delete() => new("ContainerSettings.Actions.Delete");
    public EventToken Discard() => new("ContainerSettings.Actions.Discard");
}

// ============================================================
//  WORKSPACE HOME PAGE PROXIES
// ============================================================

/// <summary>
/// Proxy for the Workspace Home page. Accessible as WorkspaceHome.*
/// </summary>
public static class WorkspaceHome
{
    public static readonly WorkspaceHomeListProxy List = new();
    public static readonly WorkspaceHomeActionsProxy Actions = new();
}

public sealed class WorkspaceHomeListProxy : ElementProxy
{
    public WorkspaceHomeListProxy() : base("WorkspaceHome.List") { }

    public ChildElementProxy Item(int index) => new("WorkspaceHome.List", $"Item{index}");
    public EventToken ItemClick(int index) => new($"WorkspaceHome.List.Item{index}.Click");
    public EventToken ItemHover(int index) => new($"WorkspaceHome.List.Item{index}.Hover");
    public EventToken ItemLeave(int index) => new($"WorkspaceHome.List.Item{index}.Leave");
    public EventToken ItemOpen(int index) => new($"WorkspaceHome.List.Item{index}.Open");
    public EventToken ItemArchive(int index) => new($"WorkspaceHome.List.Item{index}.Archive");
}

public sealed class WorkspaceHomeActionsProxy : ElementProxy
{
    public WorkspaceHomeActionsProxy() : base("WorkspaceHome.Actions") { }
    public EventToken Refresh() => new("WorkspaceHome.Actions.Refresh");
    public EventToken Create() => new("WorkspaceHome.Actions.Create");
}

// ============================================================
//  STUDIO PROJECT PAGE PROXIES
// ============================================================

/// <summary>
/// Proxy for the Studio Project page. Accessible as StudioProject.*
/// </summary>
public static class StudioProject
{
    public static readonly StudioProjectHeaderProxy Header = new();
    public static readonly StudioProjectFilesProxy Files = new();
    public static readonly StudioProjectActionsProxy Actions = new();
    public static readonly StudioProjectCreateProxy Create = new();
    public static readonly StudioProjectGitProxy Git = new();
    public static readonly StudioProjectDecisionProxy Decision = new();
}

public sealed class StudioProjectHeaderProxy : ElementProxy
{
    public StudioProjectHeaderProxy() : base("StudioProject.Header") { }
    public EventToken StartChatClick() => new("StudioProject.Header.StartChatClick");
    public EventToken EditorClick() => new("StudioProject.Header.EditorClick");
    public EventToken TerminalClick() => new("StudioProject.Header.TerminalClick");
    public EventToken ServerClick() => new("StudioProject.Header.ServerClick");
    public EventToken BuildClick() => new("StudioProject.Header.BuildClick");
    public EventToken TestClick() => new("StudioProject.Header.TestClick");
}

public sealed class StudioProjectFilesProxy : ElementProxy
{
    public StudioProjectFilesProxy() : base("StudioProject.Files") { }

    public ChildElementProxy Item(int index) => new("StudioProject.Files", $"Item{index}");
    public EventToken ItemClick(int index) => new($"StudioProject.Files.Item{index}.Click");
    public EventToken ItemHover(int index) => new($"StudioProject.Files.Item{index}.Hover");
    public EventToken ItemLeave(int index) => new($"StudioProject.Files.Item{index}.Leave");
    public EventToken ItemOpen(int index) => new($"StudioProject.Files.Item{index}.Open");
    public EventToken ItemAskAi(int index) => new($"StudioProject.Files.Item{index}.AskAi");
    public EventToken ItemReveal(int index) => new($"StudioProject.Files.Item{index}.Reveal");
}

public sealed class StudioProjectActionsProxy : ElementProxy
{
    public StudioProjectActionsProxy() : base("StudioProject.Actions") { }
    public EventToken Refresh() => new("StudioProject.Actions.Refresh");
    public EventToken OverviewClick() => new("StudioProject.Actions.OverviewClick");
    public EventToken CreateClick() => new("StudioProject.Actions.CreateClick");
    public EventToken ConfigureClick() => new("StudioProject.Actions.ConfigureClick");
    public EventToken ArchiveClick() => new("StudioProject.Actions.ArchiveClick");
}

public sealed class StudioProjectCreateProxy : ElementProxy
{
    public StudioProjectCreateProxy() : base("StudioProject.Create") { }
    public EventToken ModeClick() => new("StudioProject.Create.ModeClick");
    public EventToken PluginClick() => new("StudioProject.Create.PluginClick");
    public EventToken AgentClick() => new("StudioProject.Create.AgentClick");
    public EventToken PromptClick() => new("StudioProject.Create.PromptClick");
    public EventToken SubmitClick() => new("StudioProject.Create.SubmitClick");
    public EventToken AiDraftClick() => new("StudioProject.Create.AiDraftClick");
    public EventToken CancelClick() => new("StudioProject.Create.CancelClick");
}

public sealed class StudioProjectGitProxy : ElementProxy
{
    public StudioProjectGitProxy() : base("StudioProject.Git") { }
    public EventToken InitializeClick() => new("StudioProject.Git.InitializeClick");
    public EventToken ConnectClick() => new("StudioProject.Git.ConnectClick");
    public EventToken UrlChanged() => new("StudioProject.Git.UrlChanged");
}

public sealed class StudioProjectDecisionProxy : ElementProxy
{
    public StudioProjectDecisionProxy() : base("StudioProject.Decision") { }

    public ChildElementProxy Item(int index) => new("StudioProject.Decision", $"Item{index}");
    public EventToken ItemClick(int index) => new($"StudioProject.Decision.Item{index}.Click");
    public EventToken ItemHover(int index) => new($"StudioProject.Decision.Item{index}.Hover");
    public EventToken ItemDelete(int index) => new($"StudioProject.Decision.Item{index}.Delete");
    public EventToken SaveClick() => new("StudioProject.Decision.SaveClick");
}

// ============================================================
//  WORKSPACE EDITOR PAGE PROXIES
// ============================================================

/// <summary>
/// Proxy for the Workspace Editor page. Accessible as WorkspaceEditor.*
/// </summary>
public static class WorkspaceEditor
{
    public static readonly WorkspaceEditorToolbarProxy Toolbar = new();
    public static readonly WorkspaceEditorEditorProxy Editor = new();
    public static readonly WorkspaceEditorSidebarProxy Sidebar = new();
    public static readonly WorkspaceEditorStatusProxy Status = new();
}

public sealed class WorkspaceEditorToolbarProxy : ElementProxy
{
    public WorkspaceEditorToolbarProxy() : base("WorkspaceEditor.Toolbar") { }
    public EventToken SaveClick() => new("WorkspaceEditor.Toolbar.SaveClick");
    public EventToken UndoClick() => new("WorkspaceEditor.Toolbar.UndoClick");
    public EventToken RedoClick() => new("WorkspaceEditor.Toolbar.RedoClick");
    public EventToken RollbackClick() => new("WorkspaceEditor.Toolbar.RollbackClick");
    public EventToken RollforwardClick() => new("WorkspaceEditor.Toolbar.RollforwardClick");
    public EventToken DiffToggle() => new("WorkspaceEditor.Toolbar.DiffToggle");
    public EventToken VersionSelected() => new("WorkspaceEditor.Toolbar.VersionSelected");
}

public sealed class WorkspaceEditorEditorProxy : ElementProxy
{
    public WorkspaceEditorEditorProxy() : base("WorkspaceEditor.Editor") { }
    public EventToken TextChanged() => new("WorkspaceEditor.Editor.TextChanged");
    public EventToken SelectionChanged() => new("WorkspaceEditor.Editor.SelectionChanged");
    public EventToken Focused() => new("WorkspaceEditor.Editor.Focused");
    public EventToken Blurred() => new("WorkspaceEditor.Editor.Blurred");
}

public sealed class WorkspaceEditorSidebarProxy : ElementProxy
{
    public WorkspaceEditorSidebarProxy() : base("WorkspaceEditor.Sidebar") { }
    public EventToken AddCommentClick() => new("WorkspaceEditor.Sidebar.AddCommentClick");
    public EventToken BranchAfterRollbackClick() => new("WorkspaceEditor.Sidebar.BranchAfterRollbackClick");
    public EventToken CommentPromptChanged() => new("WorkspaceEditor.Sidebar.CommentPromptChanged");
}

public sealed class WorkspaceEditorStatusProxy : ElementProxy
{
    public WorkspaceEditorStatusProxy() : base("WorkspaceEditor.Status") { }
    public EventToken ReloadClick() => new("WorkspaceEditor.Status.ReloadClick");
    public EventToken InterruptClick() => new("WorkspaceEditor.Status.InterruptClick");
}
