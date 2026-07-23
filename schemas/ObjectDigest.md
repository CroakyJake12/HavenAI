# ObjectDigest

Event tokens for all UI proxy classes. Tokens are generated dynamically from `ElementProxy.cs`.

---

## TopRail

### Actions
TopRail.Actions.HomeClick() - Click Home action button
TopRail.Actions.ChatClick() - Click Chat action button
TopRail.Actions.CallClick() - Click Call action button
TopRail.Actions.DoClick() - Click Do action button
TopRail.Actions.StudioClick() - Click Studio action button
TopRail.Actions.BrowserClick() - Click Browser action button
TopRail.Actions.PlanClick() - Click Plan action button
TopRail.Actions.TrainingClick() - Click Training action button
TopRail.Actions.SettingsClick() - Click Settings action button
TopRail.Actions.SidebarToggle() - Toggle sidebar visibility
TopRail.Actions.CommandPaletteToggle() - Toggle command palette

### Search
TopRail.Search.Focused() - Search input focused
TopRail.Search.Blurred() - Search input blurred
TopRail.Search.Submitted() - Search submitted

### Status
TopRail.Status.OllamaClick() - Click Ollama status indicator

### Menu
TopRail.Menu.FileClick() - Click File menu
TopRail.Menu.EditClick() - Click Edit menu
TopRail.Menu.ViewClick() - Click View menu

### Tabs
TopRail.Tabs.AddTab() - Add new tab
TopRail.Tabs.CloseTab() - Close current tab
TopRail.Tabs.TabClicked() - Click on a tab
TopRail.Tabs.TabHover() - Hover over a tab

---

## SideRail

### Navigation
SideRail.Navigation.HomeClick() - Click Home navigation
SideRail.Navigation.ChatClick() - Click Chat navigation
SideRail.Navigation.CallClick() - Click Call navigation
SideRail.Navigation.DoClick() - Click Do navigation
SideRail.Navigation.StudioClick() - Click Studio navigation
SideRail.Navigation.BrowserClick() - Click Browser navigation
SideRail.Navigation.PlanClick() - Click Plan navigation
SideRail.Navigation.TrainingClick() - Click Training navigation

### Actions
SideRail.Actions.NewChat() - Create new chat
SideRail.Actions.Archive() - Open archive
SideRail.Actions.Settings() - Open settings

---

## Sidebar

### Conversations
Sidebar.Conversations.ItemClick() - Click conversation item
Sidebar.Conversations.ItemHover() - Hover conversation item
Sidebar.Conversations.ItemRightClick() - Right-click conversation item
Sidebar.Conversations.ItemPin() - Pin conversation
Sidebar.Conversations.ItemRename() - Rename conversation
Sidebar.Conversations.ItemDelete() - Delete conversation

### Containers
Sidebar.Containers.ItemClick() - Click container item
Sidebar.Containers.ItemHover() - Hover container item
Sidebar.Containers.CreateClick() - Create new container

### Pins
Sidebar.Pins.ItemClick() - Click pinned item
Sidebar.Pins.ItemRemove() - Remove pinned item

### Search
Sidebar.Search.Focused() - Search input focused
Sidebar.Search.Blurred() - Search input blurred
Sidebar.Search.QueryChanged() - Search query changed

---

## Home

### Header
Home.Header.CustomizeClick() - Click customize button
Home.Header.RefreshClick() - Click refresh button

### Dashboard
Home.Dashboard.Tile(index).Click() - Click dashboard tile
Home.Dashboard.Tile(index).Hover() - Hover dashboard tile
Home.Dashboard.Tile(index).Leave() - Leave dashboard tile
Home.Dashboard.Tile(index).Open() - Open dashboard tile
Home.Dashboard.Tile(index).MoveEarlier() - Move tile earlier
Home.Dashboard.Tile(index).MoveLater() - Move tile later
Home.Dashboard.Tile(index).Toggle() - Toggle tile

### Agenda
Home.Agenda.Item(index).Click() - Click agenda item
Home.Agenda.Item(index).Hover() - Hover agenda item

### RecentWork
Home.RecentWork.Item(index).Click() - Click recent work item
Home.RecentWork.Item(index).Hover() - Hover recent work item

---

## Chat

### Composer
Chat.Composer.TextChanged() - Composer text changed
Chat.Composer.Focused() - Composer focused
Chat.Composer.Blurred() - Composer blurred
Chat.Composer.SendClick() - Click send button
Chat.Composer.StopClick() - Click stop button
Chat.Composer.AttachClick() - Click attach button
Chat.Composer.DictateClick() - Click dictate button
Chat.Composer.KeyDown() - Key pressed in composer

### Messages
Chat.Messages.Message(index).Click() - Click message
Chat.Messages.Message(index).Hover() - Hover message
Chat.Messages.Message(index).ActionsClick() - Click message actions
Chat.Messages.Message(index).Copy() - Copy message
Chat.Messages.Message(index).Branch() - Branch from message

### Toolbar
Chat.Toolbar.BranchClick() - Click branch button
Chat.Toolbar.CompactClick() - Click compact button
Chat.Toolbar.ArchiveClick() - Click archive button
Chat.Toolbar.CopyLastClick() - Click copy last button
Chat.Toolbar.UndoClick() - Click undo button
Chat.Toolbar.RedoClick() - Click redo button

### Model
Chat.Model.PickerClick() - Click model picker
Chat.Model.ModelSelected() - Model selected

### Attachments
Chat.Attachments.ItemClick() - Click attachment item
Chat.Attachments.ItemRemove() - Remove attachment item

### Pickers
Chat.Pickers.PluginOpen() - Plugin picker opened
Chat.Pickers.PluginDismiss() - Plugin picker dismissed
Chat.Pickers.PluginSelect() - Plugin selected
Chat.Pickers.PromptOpen() - Prompt picker opened
Chat.Pickers.PromptDismiss() - Prompt picker dismissed
Chat.Pickers.PromptSelect() - Prompt selected
Chat.Pickers.ModelOpen() - Model picker opened
Chat.Pickers.ModelDismiss() - Model picker dismissed
Chat.Pickers.ModelSelect() - Model selected

### Sidebar
Chat.Sidebar.ContainerClick() - Click container in sidebar
Chat.Sidebar.ContainerHover() - Hover container in sidebar
Chat.Sidebar.ContainerDelete() - Delete container
Chat.Sidebar.ContainerCreate() - Create new container
Chat.Sidebar.LessonClick() - Click lesson in sidebar
Chat.Sidebar.LessonHover() - Hover lesson in sidebar
Chat.Sidebar.LessonCreate() - Create new lesson
Chat.Sidebar.LessonDelete() - Delete lesson
Chat.Sidebar.QuickChatsClick() - Click quick chats section

---

## Call

### Controls
Call.Controls.StartClick() - Click start call button
Call.Controls.EndClick() - Click end call button
Call.Controls.MuteClick() - Click mute button
Call.Controls.UnmuteClick() - Click unmute button

### Status
Call.Status.Active() - Call active
Call.Status.Ended() - Call ended

---

## Plan

### Tasks
Plan.Tasks.Item(index).Click() - Click task item
Plan.Tasks.Item(index).Hover() - Hover task item
Plan.Tasks.Item(index).Leave() - Leave task item
Plan.Tasks.Item(index).Complete() - Complete task
Plan.Tasks.Item(index).Edit() - Edit task
Plan.Tasks.Item(index).Subtask() - Create subtask
Plan.Tasks.Item(index).Start() - Start task
Plan.Tasks.Item(index).Delete() - Delete task
Plan.Tasks.Item(index).Drag() - Drag task

### Events
Plan.Events.Item(index).Click() - Click event item
Plan.Events.Item(index).Hover() - Hover event item
Plan.Events.Item(index).Leave() - Leave event item
Plan.Events.Item(index).Edit() - Edit event
Plan.Events.Item(index).Delete() - Delete event

### Collections
Plan.Collections.Item(index).Click() - Click collection item
Plan.Collections.Item(index).Hover() - Hover collection item
Plan.Collections.Item(index).Leave() - Leave collection item
Plan.Collections.Item(index).MoveUp() - Move collection up
Plan.Collections.Item(index).MoveDown() - Move collection down

### Calendar
Plan.Calendar.Provider(index).Connect() - Connect calendar provider
Plan.Calendar.Provider(index).Sync() - Sync calendar provider
Plan.Calendar.Provider(index).Disconnect() - Disconnect calendar provider
Plan.Conflict(index).KeepHaven() - Keep Haven version (conflict)
Plan.Conflict(index).KeepProvider() - Keep provider version (conflict)
Plan.Conflict(index).Duplicate() - Keep both versions (conflict)

### Views
Plan.Views.Item(index).Click() - Click view item
Plan.Views.Item(index).Hover() - Hover view item

### Board
Plan.Board.Task(index).Hover() - Hover board task
Plan.Board.Task(index).Leave() - Leave board task
Plan.Board.Task(index).Edit() - Edit board task
Plan.Board.Task(index).Start() - Start board task
Plan.Board.Task(index).Done() - Mark board task done
Plan.Board.Task(index).Drag() - Drag board task

### Ai
Plan.Ai.PromptFocused() - AI prompt focused
Plan.Ai.PromptBlurred() - AI prompt blurred
Plan.Ai.AskClick() - Click ask AI button
Plan.Actions.DismissProposal() - Dismiss AI proposal
Plan.Actions.ApplyProposal() - Apply AI proposal

### Actions
Plan.Actions.CreateTask() - Create new task
Plan.Actions.CreateEvent() - Create new event
Plan.Actions.CreateCollection() - Create new collection
Plan.Actions.RenameCollection() - Rename collection
Plan.Actions.RequestArchiveCollection() - Request archive collection
Plan.Actions.CancelArchiveCollection() - Cancel archive collection
Plan.Actions.ConfirmArchiveCollection() - Confirm archive collection
Plan.Actions.PreviousPeriod() - Navigate to previous period
Plan.Actions.NextPeriod() - Navigate to next period
Plan.Actions.Today() - Navigate to today
Plan.Actions.AskAi() - Ask AI
Plan.Actions.CloseTaskEditor() - Close task editor
Plan.Actions.SaveTask() - Save task
Plan.Actions.CloseEventEditor() - Close event editor
Plan.Actions.SaveEvent() - Save event
Plan.Actions.Refresh() - Refresh plan

---

## Browser

### Navigation
Browser.Navigation.BackClick() - Click back button
Browser.Navigation.ForwardClick() - Click forward button
Browser.Navigation.HomeClick() - Click home button
Browser.Navigation.RefreshClick() - Click refresh button
Browser.Navigation.HardRefreshClick() - Click hard refresh button
Browser.Navigation.StopClick() - Click stop button
Browser.Navigation.UrlSubmit() - Submit URL
Browser.Navigation.GoClick() - Click go button

### Toolbar
Browser.Toolbar.BookmarkClick() - Click bookmark button
Browser.Toolbar.MenuClick() - Click menu button
Browser.Toolbar.SafetyClick() - Click safety button
Browser.Toolbar.DevToolsClick() - Click dev tools button
Browser.Toolbar.PrintClick() - Click print button

### Tabs
Browser.Tabs.Tab(index).Click() - Click browser tab
Browser.Tabs.Tab(index).Hover() - Hover browser tab
Browser.Tabs.Tab(index).Close() - Close browser tab
Browser.Tabs.NewTab() - Open new tab
Browser.Tabs.NewPrivateTab() - Open new private tab

### Bookmarks
Browser.Bookmarks.Item(index).Click() - Click bookmark item
Browser.Bookmarks.Item(index).Hover() - Hover bookmark item
Browser.Bookmarks.Item(index).Delete() - Delete bookmark
Browser.Bookmarks.AddBookmark() - Add bookmark
Browser.Bookmarks.TogglePanel() - Toggle bookmarks panel
Browser.Bookmarks.ManageClick() - Click manage bookmarks

### History
Browser.History.Item(index).Click() - Click history item
Browser.History.Item(index).Hover() - Hover history item
Browser.History.ClearHistory() - Clear browsing history
Browser.History.TogglePanel() - Toggle history panel

### Extensions
Browser.Extensions.Item(index).Click() - Click extension item
Browser.Extensions.Item(index).Toggle() - Toggle extension
Browser.Extensions.Item(index).Delete() - Delete extension
Browser.Extensions.ImportClick() - Import extensions
Browser.Extensions.ConvertChromeClick() - Convert Chrome extensions
Browser.Extensions.TogglePanel() - Toggle extensions panel

### Logins
Browser.Logins.Item(index).Click() - Click login item
Browser.Logins.Item(index).Delete() - Delete login
Browser.Logins.Item(index).Autofill() - Autofill login
Browser.Logins.SaveLogin() - Save login
Browser.Logins.TogglePanel() - Toggle logins panel

### Assistant
Browser.Assistant.SummariseClick() - Click summarise button
Browser.Assistant.AskClick() - Click ask button
Browser.Assistant.InputChanged() - Assistant input changed
Browser.Assistant.TogglePanel() - Toggle assistant panel

### Settings
Browser.Settings.SaveClick() - Click save settings
Browser.Settings.TogglePanel() - Toggle settings panel
Browser.Settings.CreateGroupClick() - Click create group

---

## Settings

### General
Settings.General.SaveClick() - Click save button
Settings.General.ResetClick() - Click reset button

### Model
Settings.Model.ProviderChanged() - Provider changed
Settings.Model.ModelChanged() - Model changed

### Appearance
Settings.Appearance.ThemeChanged() - Theme changed

---

## Training

### Controls
Training.Controls.StartClick() - Click start training
Training.Controls.StopClick() - Click stop training

### Status
Training.Status.ProgressUpdate() - Training progress update

---

## Catalog

### List
Catalog.List.ItemClick() - Click catalog item
Catalog.List.ItemHover() - Hover catalog item
Catalog.List.ItemEdit() - Edit catalog item
Catalog.List.ItemDelete() - Delete catalog item

### Actions
Catalog.Actions.CreateClick() - Click create button
Catalog.Actions.ImportClick() - Click import button

---

## Automations

### List
Automations.List.ItemClick() - Click automation item
Automations.List.ItemToggle() - Toggle automation
Automations.List.ItemRun() - Run automation

### Actions
Automations.Actions.CreateClick() - Click create button

---

## Archive

### List
Archive.List.ItemClick() - Click archive item
Archive.List.ItemRestore() - Restore archive item
Archive.List.ItemDelete() - Delete archive item

### Actions
Archive.Actions.SearchChanged() - Search query changed
Archive.Actions.Refresh() - Refresh archive

---

## Macros

### List
Macros.List.Item(index).Click() - Click macro item
Macros.List.Item(index).Hover() - Hover macro item
Macros.List.Item(index).Leave() - Leave macro item
Macros.List.Item(index).Run() - Run macro
Macros.List.Item(index).Delete() - Delete macro

### Actions
Macros.Actions.Refresh() - Refresh macros
Macros.Actions.Create() - Create new macro

---

## ActivityLog

### List
ActivityLog.List.Item(index).Click() - Click log item
ActivityLog.List.Item(index).Hover() - Hover log item
ActivityLog.List.Item(index).Leave() - Leave log item

### Actions
ActivityLog.Actions.Refresh() - Refresh activity log

### Search
ActivityLog.Search.QueryChanged() - Search query changed

---

## ModeLibrary

### List
ModeLibrary.List.Item(index).Click() - Click mode item
ModeLibrary.List.Item(index).Hover() - Hover mode item
ModeLibrary.List.Item(index).Leave() - Leave mode item
ModeLibrary.List.Item(index).Pin() - Pin mode

### Actions
ModeLibrary.Actions.Refresh() - Refresh mode library
ModeLibrary.Actions.CreateInStudio() - Create mode in Studio

### Search
ModeLibrary.Search.QueryChanged() - Search query changed

---

## LessonSettings

### Actions
LessonSettings.Actions.Save() - Save lesson settings

---

## ContainerSettings

### Actions
ContainerSettings.Actions.Save() - Save container settings
ContainerSettings.Actions.Archive() - Archive container
ContainerSettings.Actions.RequestDelete() - Request delete container
ContainerSettings.Actions.CancelDelete() - Cancel delete
ContainerSettings.Actions.Delete() - Delete container
ContainerSettings.Actions.Discard() - Discard changes

---

## WorkspaceHome

### List
WorkspaceHome.List.Item(index).Click() - Click workspace item
WorkspaceHome.List.Item(index).Hover() - Hover workspace item
WorkspaceHome.List.Item(index).Leave() - Leave workspace item
WorkspaceHome.List.Item(index).Open() - Open workspace
WorkspaceHome.List.Item(index).Archive() - Archive workspace

### Actions
WorkspaceHome.Actions.Refresh() - Refresh workspace list
WorkspaceHome.Actions.Create() - Create new workspace

---

## StudioProject

### Header
StudioProject.Header.StartChatClick() - Click start chat button
StudioProject.Header.EditorClick() - Click editor button
StudioProject.Header.TerminalClick() - Click terminal button
StudioProject.Header.ServerClick() - Click server button
StudioProject.Header.BuildClick() - Click build button
StudioProject.Header.TestClick() - Click test button

### Files
StudioProject.Files.Item(index).Click() - Click file item
StudioProject.Files.Item(index).Hover() - Hover file item
StudioProject.Files.Item(index).Leave() - Leave file item
StudioProject.Files.Item(index).Open() - Open file
StudioProject.Files.Item(index).AskAi() - Ask AI about file
StudioProject.Files.Item(index).Reveal() - Reveal file in explorer

### Actions
StudioProject.Actions.Refresh() - Refresh project
StudioProject.Actions.OverviewClick() - Click overview
StudioProject.Actions.CreateClick() - Click create
StudioProject.Actions.ConfigureClick() - Click configure
StudioProject.Actions.ArchiveClick() - Click archive

### Create
StudioProject.Create.ModeClick() - Click mode creation
StudioProject.Create.PluginClick() - Click plugin creation
StudioProject.Create.AgentClick() - Click agent creation
StudioProject.Create.PromptClick() - Click prompt creation
StudioProject.Create.SubmitClick() - Click submit
StudioProject.Create.AiDraftClick() - Click AI draft
StudioProject.Create.CancelClick() - Click cancel

### Git
StudioProject.Git.InitializeClick() - Click initialize git
StudioProject.Git.ConnectClick() - Click connect to remote
StudioProject.Git.UrlChanged() - Git URL changed

### Decision
StudioProject.Decision.Item(index).Click() - Click decision item
StudioProject.Decision.Item(index).Hover() - Hover decision item
StudioProject.Decision.Item(index).Delete() - Delete decision item
StudioProject.Decision.SaveClick() - Click save decisions

---

## WorkspaceEditor

### Toolbar
WorkspaceEditor.Toolbar.SaveClick() - Click save
WorkspaceEditor.Toolbar.UndoClick() - Click undo
WorkspaceEditor.Toolbar.RedoClick() - Click redo
WorkspaceEditor.Toolbar.RollbackClick() - Click rollback
WorkspaceEditor.Toolbar.RollforwardClick() - Click rollforward
WorkspaceEditor.Toolbar.DiffToggle() - Toggle diff view
WorkspaceEditor.Toolbar.VersionSelected() - Version selected

### Editor
WorkspaceEditor.Editor.TextChanged() - Editor text changed
WorkspaceEditor.Editor.SelectionChanged() - Editor selection changed
WorkspaceEditor.Editor.Focused() - Editor focused
WorkspaceEditor.Editor.Blurred() - Editor blurred

### Sidebar
WorkspaceEditor.Sidebar.AddCommentClick() - Click add comment
WorkspaceEditor.Sidebar.BranchAfterRollbackClick() - Click branch after rollback
WorkspaceEditor.Sidebar.CommentPromptChanged() - Comment prompt changed

### Status
WorkspaceEditor.Status.ReloadClick() - Click reload
WorkspaceEditor.Status.InterruptClick() - Click interrupt
