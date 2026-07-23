# Object Digest

Auto-generated reference for all events exposed by the Haven event system.

---

## TopRail

### Actions
- `TopRail.Actions.HomeClick()` — Click the Home mode button
- `TopRail.Actions.ChatClick()` — Click the Chat mode button
- `TopRail.Actions.CallClick()` — Click the Call mode button
- `TopRail.Actions.DoClick()` — Click the Do mode button
- `TopRail.Actions.StudioClick()` — Click the Studio mode button
- `TopRail.Actions.BrowserClick()` — Click the Browser mode button
- `TopRail.Actions.PlanClick()` — Click the Plan mode button
- `TopRail.Actions.TrainingClick()` — Click the Training mode button
- `TopRail.Actions.SettingsClick()` — Click the Settings button
- `TopRail.Actions.SidebarToggle()` — Toggle the sidebar open/closed
- `TopRail.Actions.CommandPaletteToggle()` — Toggle the command palette

### Search
- `TopRail.Search.Focused()` — Search box focused
- `TopRail.Search.Blurred()` — Search box blurred
- `TopRail.Search.Submitted()` — Search query submitted

### Status
- `TopRail.Status.OllamaClick()` — Click the Ollama status indicator

### Menu
- `TopRail.Menu.FileClick()` — Click the File menu
- `TopRail.Menu.EditClick()` — Click the Edit menu
- `TopRail.Menu.ViewClick()` — Click the View menu

### Tabs
- `TopRail.Tabs.AddTab()` — Click the add tab button
- `TopRail.Tabs.CloseTab()` — Click the close tab button
- `TopRail.Tabs.TabClicked()` — Click a tab
- `TopRail.Tabs.TabHover()` — Hover over a tab

---

## SideRail

### Navigation
- `SideRail.Navigation.HomeClick()` — Click the Home mode button
- `SideRail.Navigation.ChatClick()` — Click the Chat mode button
- `SideRail.Navigation.CallClick()` — Click the Call mode button
- `SideRail.Navigation.DoClick()` — Click the Do mode button
- `SideRail.Navigation.StudioClick()` — Click the Studio mode button
- `SideRail.Navigation.BrowserClick()` — Click the Browser mode button
- `SideRail.Navigation.PlanClick()` — Click the Plan mode button
- `SideRail.Navigation.TrainingClick()` — Click the Training mode button

### Actions
- `SideRail.Actions.NewChat()` — Click the New Chat button
- `SideRail.Actions.Archive()` — Click the Archive button
- `SideRail.Actions.Settings()` — Click the Settings button

---

## Sidebar

### Conversations
- `Sidebar.Conversations.ItemClick()` — Click a conversation item
- `Sidebar.Conversations.ItemHover()` — Hover over a conversation item
- `Sidebar.Conversations.ItemRightClick()` — Right-click a conversation item
- `Sidebar.Conversations.ItemPin()` — Click pin on a conversation item
- `Sidebar.Conversations.ItemRename()` — Click rename on a conversation item
- `Sidebar.Conversations.ItemDelete()` — Click delete on a conversation item

### Containers
- `Sidebar.Containers.ItemClick()` — Click a container item
- `Sidebar.Containers.ItemHover()` — Hover over a container item
- `Sidebar.Containers.CreateClick()` — Click the create container button

### Pins
- `Sidebar.Pins.ItemClick()` — Click a pinned item
- `Sidebar.Pins.ItemRemove()` — Click remove on a pinned item

### Search
- `Sidebar.Search.Focused()` — Search box focused
- `Sidebar.Search.Blurred()` — Search box blurred
- `Sidebar.Search.QueryChanged()` — Search query changed

---

## Home

### Header
- `Home.Header.CustomizeClick()` — Click the Customize button
- `Home.Header.RefreshClick()` — Click the Refresh button

### Dashboard
- `Home.Dashboard.Tile(index).Click()` — Click dashboard tile at index
- `Home.Dashboard.Tile(index).Hover()` — Hover over dashboard tile at index
- `Home.Dashboard.Tile(index).Leave()` — Leave dashboard tile at index
- `Home.Dashboard.Tile(index).Open()` — Open dashboard tile at index
- `Home.Dashboard.Tile(index).MoveEarlier()` — Move dashboard tile earlier
- `Home.Dashboard.Tile(index).MoveLater()` — Move dashboard tile later
- `Home.Dashboard.Tile(index).Toggle()` — Toggle dashboard tile at index

### Agenda
- `Home.Agenda.Item(index).Click()` — Click agenda item at index
- `Home.Agenda.Item(index).Hover()` — Hover over agenda item at index

### RecentWork
- `Home.RecentWork.Item(index).Click()` — Click recent work item at index
- `Home.RecentWork.Item(index).Hover()` — Hover over recent work item at index

---

## Chat

### Composer
- `Chat.Composer.TextChanged()` — Composer text changed
- `Chat.Composer.Focused()` — Composer focused
- `Chat.Composer.Blurred()` — Composer blurred
- `Chat.Composer.SendClick()` — Click the Send button
- `Chat.Composer.StopClick()` — Click the Stop button
- `Chat.Composer.AttachClick()` — Click the Attach button
- `Chat.Composer.DictateClick()` — Click the Dictate button
- `Chat.Composer.KeyDown()` — Key pressed in composer

### Messages
- `Chat.Messages.Message(index).Click()` — Click message at index
- `Chat.Messages.Message(index).Hover()` — Hover over message at index
- `Chat.Messages.Message(index).ActionsClick()` — Click actions on message at index
- `Chat.Messages.Message(index).Copy()` — Copy message at index
- `Chat.Messages.Message(index).Branch()` — Branch from message at index

### Toolbar
- `Chat.Toolbar.BranchClick()` — Click the Branch button
- `Chat.Toolbar.CompactClick()` — Click the Compact button
- `Chat.Toolbar.ArchiveClick()` — Click the Archive button
- `Chat.Toolbar.CopyLastClick()` — Click the Copy Last button
- `Chat.Toolbar.UndoClick()` — Click the Undo button
- `Chat.Toolbar.RedoClick()` — Click the Redo button

### Model
- `Chat.Model.PickerClick()` — Click the model picker
- `Chat.Model.ModelSelected()` — A model was selected

### Attachments
- `Chat.Attachments.ItemClick()` — Click an attachment item
- `Chat.Attachments.ItemRemove()` — Click remove on an attachment item

### Pickers
- `Chat.Pickers.PluginOpen()` — Plugin picker opened
- `Chat.Pickers.PluginDismiss()` — Plugin picker dismissed
- `Chat.Pickers.PluginSelect()` — Plugin selected
- `Chat.Pickers.PromptOpen()` — Prompt picker opened
- `Chat.Pickers.PromptDismiss()` — Prompt picker dismissed
- `Chat.Pickers.PromptSelect()` — Prompt selected
- `Chat.Pickers.ModelOpen()` — Model picker opened
- `Chat.Pickers.ModelDismiss()` — Model picker dismissed
- `Chat.Pickers.ModelSelect()` — Model selected

### Sidebar
- `Chat.Sidebar.ContainerClick()` — Click a container in the sidebar
- `Chat.Sidebar.ContainerHover()` — Hover over a container in the sidebar
- `Chat.Sidebar.ContainerDelete()` — Delete a container in the sidebar
- `Chat.Sidebar.ContainerCreate()` — Click create container in the sidebar
- `Chat.Sidebar.LessonClick()` — Click a lesson in the sidebar
- `Chat.Sidebar.LessonHover()` — Hover over a lesson in the sidebar
- `Chat.Sidebar.LessonCreate()` — Click create lesson in the sidebar
- `Chat.Sidebar.LessonDelete()` — Delete a lesson in the sidebar
- `Chat.Sidebar.QuickChatsClick()` — Click Quick Chats button

---

## Call

### Controls
- `Call.Controls.StartClick()` — Click the Start button
- `Call.Controls.EndClick()` — Click the End button
- `Call.Controls.MuteClick()` — Click the Mute button
- `Call.Controls.UnmuteClick()` — Click the Unmute button

### Status
- `Call.Status.Active()` — Call became active
- `Call.Status.Ended()` — Call ended

---

## Plan

### Tasks
- `Plan.Tasks.Item(index).Click()` — Click task item at index
- `Plan.Tasks.Item(index).Hover()` — Hover over task item at index
- `Plan.Tasks.Item(index).Leave()` — Leave task item at index
- `Plan.Tasks.Item(index).Complete()` — Complete task item at index
- `Plan.Tasks.Item(index).Edit()` — Edit task item at index
- `Plan.Tasks.Item(index).Subtask()` — Create subtask for task item at index
- `Plan.Tasks.Item(index).Start()` — Start task item at index
- `Plan.Tasks.Item(index).Delete()` — Delete task item at index
- `Plan.Tasks.Item(index).Drag()` — Drag task item at index

### Events
- `Plan.Events.Item(index).Click()` — Click event item at index
- `Plan.Events.Item(index).Hover()` — Hover over event item at index
- `Plan.Events.Item(index).Leave()` — Leave event item at index
- `Plan.Events.Item(index).Edit()` — Edit event item at index
- `Plan.Events.Item(index).Delete()` — Delete event item at index

### Collections
- `Plan.Collections.Item(index).Click()` — Click collection item at index
- `Plan.Collections.Item(index).Hover()` — Hover over collection item at index
- `Plan.Collections.Item(index).Leave()` — Leave collection item at index
- `Plan.Collections.Item(index).MoveUp()` — Move collection item up
- `Plan.Collections.Item(index).MoveDown()` — Move collection item down

### Calendar
- `Plan.Calendar.Provider(index).Connect()` — Connect calendar provider at index
- `Plan.Calendar.Provider(index).Sync()` — Sync calendar provider at index
- `Plan.Calendar.Provider(index).Disconnect()` — Disconnect calendar provider at index
- `Plan.Conflict(index).KeepHaven()` — Keep Haven version for conflict at index
- `Plan.Conflict(index).KeepProvider()` — Keep provider version for conflict at index
- `Plan.Conflict(index).Duplicate()` — Duplicate for conflict at index

### Views
- `Plan.Views.Item(index).Click()` — Click view item at index
- `Plan.Views.Item(index).Hover()` — Hover over view item at index

### Board
- `Plan.Board.Task(index).Hover()` — Hover over board task at index
- `Plan.Board.Task(index).Leave()` — Leave board task at index
- `Plan.Board.Task(index).Edit()` — Edit board task at index
- `Plan.Board.Task(index).Start()` — Start board task at index
- `Plan.Board.Task(index).Done()` — Mark board task as done
- `Plan.Board.Task(index).Drag()` — Drag board task at index

### Ai
- `Plan.Ai.PromptFocused()` — AI prompt focused
- `Plan.Ai.PromptBlurred()` — AI prompt blurred
- `Plan.Ai.AskClick()` — Click the Ask AI button
- `Plan.Ai.DismissProposal()` — Dismiss AI proposal
- `Plan.Ai.ApplyProposal()` — Apply AI proposal

### Actions
- `Plan.Actions.CreateTask()` — Click Create Task
- `Plan.Actions.CreateEvent()` — Click Create Event
- `Plan.Actions.CreateCollection()` — Click Create Collection
- `Plan.Actions.RenameCollection()` — Click Rename Collection
- `Plan.Actions.RequestArchiveCollection()` — Request archive collection
- `Plan.Actions.CancelArchiveCollection()` — Cancel archive collection
- `Plan.Actions.ConfirmArchiveCollection()` — Confirm archive collection
- `Plan.Actions.PreviousPeriod()` — Navigate to previous period
- `Plan.Actions.NextPeriod()` — Navigate to next period
- `Plan.Actions.Today()` — Navigate to today
- `Plan.Actions.AskAi()` — Click Ask AI
- `Plan.Actions.CloseTaskEditor()` — Close task editor
- `Plan.Actions.SaveTask()` — Save task
- `Plan.Actions.CloseEventEditor()` — Close event editor
- `Plan.Actions.SaveEvent()` — Save event
- `Plan.Actions.Refresh()` — Refresh plan data

---

## Browser

### Navigation
- `Browser.Navigation.BackClick()` — Click the Back button
- `Browser.Navigation.ForwardClick()` — Click the Forward button
- `Browser.Navigation.HomeClick()` — Click the Home button
- `Browser.Navigation.RefreshClick()` — Click the Refresh button
- `Browser.Navigation.HardRefreshClick()` — Click the Hard Refresh button
- `Browser.Navigation.StopClick()` — Click the Stop button
- `Browser.Navigation.UrlSubmit()` — Submit URL
- `Browser.Navigation.GoClick()` — Click the Go button

### Toolbar
- `Browser.Toolbar.BookmarkClick()` — Click the Bookmark button
- `Browser.Toolbar.MenuClick()` — Click the Menu button
- `Browser.Toolbar.SafetyClick()` — Click the Safety button
- `Browser.Toolbar.DevToolsClick()` — Click the DevTools button
- `Browser.Toolbar.PrintClick()` — Click the Print button

### Tabs
- `Browser.Tabs.Tab(index).Click()` — Click browser tab at index
- `Browser.Tabs.Tab(index).Hover()` — Hover over browser tab at index
- `Browser.Tabs.Tab(index).Close()` — Close browser tab at index
- `Browser.Tabs.NewTab()` — Click the New Tab button
- `Browser.Tabs.NewPrivateTab()` — Click the New Private Tab button

### Bookmarks
- `Browser.Bookmarks.Item(index).Click()` — Click bookmark item at index
- `Browser.Bookmarks.Item(index).Hover()` — Hover over bookmark item at index
- `Browser.Bookmarks.Item(index).Delete()` — Delete bookmark item at index
- `Browser.Bookmarks.AddBookmark()` — Click Add Bookmark
- `Browser.Bookmarks.TogglePanel()` — Toggle bookmarks panel
- `Browser.Bookmarks.ManageClick()` — Click Manage Bookmarks

### History
- `Browser.History.Item(index).Click()` — Click history item at index
- `Browser.History.Item(index).Hover()` — Hover over history item at index
- `Browser.History.ClearHistory()` — Click Clear History
- `Browser.History.TogglePanel()` — Toggle history panel

### Extensions
- `Browser.Extensions.Item(index).Click()` — Click extension item at index
- `Browser.Extensions.Item(index).Toggle()` — Toggle extension item at index
- `Browser.Extensions.Item(index).Delete()` — Delete extension item at index
- `Browser.Extensions.ImportClick()` — Click Import Extensions
- `Browser.Extensions.ConvertChromeClick()` — Click Convert Chrome Extensions
- `Browser.Extensions.TogglePanel()` — Toggle extensions panel

### Logins
- `Browser.Logins.Item(index).Click()` — Click login item at index
- `Browser.Logins.Item(index).Delete()` — Delete login item at index
- `Browser.Logins.Item(index).Autofill()` — Autofill login item at index
- `Browser.Logins.SaveLogin()` — Click Save Login
- `Browser.Logins.TogglePanel()` — Toggle logins panel

### Assistant
- `Browser.Assistant.SummariseClick()` — Click Summarise
- `Browser.Assistant.AskClick()` — Click Ask
- `Browser.Assistant.InputChanged()` — Assistant input changed
- `Browser.Assistant.TogglePanel()` — Toggle assistant panel

### Settings
- `Browser.Settings.SaveClick()` — Click Save Settings
- `Browser.Settings.TogglePanel()` — Toggle settings panel
- `Browser.Settings.CreateGroupClick()` — Click Create Group

---

## Settings

### General
- `Settings.General.SaveClick()` — Click Save
- `Settings.General.ResetClick()` — Click Reset

### Model
- `Settings.Model.ProviderChanged()` — Provider selection changed
- `Settings.Model.ModelChanged()` — Model selection changed

### Appearance
- `Settings.Appearance.ThemeChanged()` — Theme selection changed

---

## Training

### Controls
- `Training.Controls.StartClick()` — Click the Start button
- `Training.Controls.StopClick()` — Click the Stop button

### Status
- `Training.Status.ProgressUpdate()` — Training progress updated

---

## Catalog

### List
- `Catalog.List.ItemClick()` — Click a catalog item
- `Catalog.List.ItemHover()` — Hover over a catalog item
- `Catalog.List.ItemEdit()` — Click edit on a catalog item
- `Catalog.List.ItemDelete()` — Click delete on a catalog item

### Actions
- `Catalog.Actions.CreateClick()` — Click Create
- `Catalog.Actions.ImportClick()` — Click Import

---

## Automations

### List
- `Automations.List.ItemClick()` — Click an automation item
- `Automations.List.ItemToggle()` — Toggle an automation item
- `Automations.List.ItemRun()` — Run an automation item

### Actions
- `Automations.Actions.CreateClick()` — Click Create

---

## Archive

### List
- `Archive.List.ItemClick()` — Click an archived item
- `Archive.List.ItemRestore()` — Click Restore on an archived item
- `Archive.List.ItemDelete()` — Click Delete on an archived item

### Actions
- `Archive.Actions.SearchChanged()` — Search query changed
- `Archive.Actions.Refresh()` — Click Refresh

---

## Macros

### List
- `Macros.List.Item(index).Click()` — Click macro item at index
- `Macros.List.Item(index).Hover()` — Hover over macro item at index
- `Macros.List.Item(index).Leave()` — Leave macro item at index
- `Macros.List.Item(index).Run()` — Run macro item at index
- `Macros.List.Item(index).Delete()` — Delete macro item at index

### Actions
- `Macros.Actions.Refresh()` — Click Refresh
- `Macros.Actions.Create()` — Click Create

---

## ActivityLog

### List
- `ActivityLog.List.Item(index).Click()` — Click log item at index
- `ActivityLog.List.Item(index).Hover()` — Hover over log item at index
- `ActivityLog.List.Item(index).Leave()` — Leave log item at index

### Actions
- `ActivityLog.Actions.Refresh()` — Click Refresh

### Search
- `ActivityLog.Search.QueryChanged()` — Search query changed

---

## ModeLibrary

### List
- `ModeLibrary.List.Item(index).Click()` — Click mode item at index
- `ModeLibrary.List.Item(index).Hover()` — Hover over mode item at index
- `ModeLibrary.List.Item(index).Leave()` — Leave mode item at index
- `ModeLibrary.List.Item(index).Pin()` — Pin mode item at index

### Actions
- `ModeLibrary.Actions.Refresh()` — Click Refresh
- `ModeLibrary.Actions.CreateInStudio()` — Click Create in Studio

### Search
- `ModeLibrary.Search.QueryChanged()` — Search query changed

---

## LessonSettings

### Actions
- `LessonSettings.Actions.Save()` — Click Save

---

## ContainerSettings

### Actions
- `ContainerSettings.Actions.Save()` — Click Save
- `ContainerSettings.Actions.Archive()` — Click Archive
- `ContainerSettings.Actions.RequestDelete()` — Request delete
- `ContainerSettings.Actions.CancelDelete()` — Cancel delete
- `ContainerSettings.Actions.Delete()` — Confirm delete
- `ContainerSettings.Actions.Discard()` — Click Discard

---

## WorkspaceHome

### List
- `WorkspaceHome.List.Item(index).Click()` — Click workspace item at index
- `WorkspaceHome.List.Item(index).Hover()` — Hover over workspace item at index
- `WorkspaceHome.List.Item(index).Leave()` — Leave workspace item at index
- `WorkspaceHome.List.Item(index).Open()` — Open workspace item at index
- `WorkspaceHome.List.Item(index).Archive()` — Archive workspace item at index

### Actions
- `WorkspaceHome.Actions.Refresh()` — Click Refresh
- `WorkspaceHome.Actions.Create()` — Click Create

---

## StudioProject

### Header
- `StudioProject.Header.StartChatClick()` — Click Start Chat
- `StudioProject.Header.EditorClick()` — Click Editor
- `StudioProject.Header.TerminalClick()` — Click Terminal
- `StudioProject.Header.ServerClick()` — Click Server
- `StudioProject.Header.BuildClick()` — Click Build
- `StudioProject.Header.TestClick()` — Click Test

### Files
- `StudioProject.Files.Item(index).Click()` — Click file item at index
- `StudioProject.Files.Item(index).Hover()` — Hover over file item at index
- `StudioProject.Files.Item(index).Leave()` — Leave file item at index
- `StudioProject.Files.Item(index).Open()` — Open file item at index
- `StudioProject.Files.Item(index).AskAi()` — Ask AI about file item at index
- `StudioProject.Files.Item(index).Reveal()` — Reveal file item at index

### Actions
- `StudioProject.Actions.Refresh()` — Click Refresh
- `StudioProject.Actions.OverviewClick()` — Click Overview
- `StudioProject.Actions.CreateClick()` — Click Create
- `StudioProject.Actions.ConfigureClick()` — Click Configure
- `StudioProject.Actions.ArchiveClick()` — Click Archive

### Create
- `StudioProject.Create.ModeClick()` — Click Mode
- `StudioProject.Create.PluginClick()` — Click Plugin
- `StudioProject.Create.AgentClick()` — Click Agent
- `StudioProject.Create.PromptClick()` — Click Prompt
- `StudioProject.Create.SubmitClick()` — Click Submit
- `StudioProject.Create.AiDraftClick()` — Click AI Draft
- `StudioProject.Create.CancelClick()` — Click Cancel

### Git
- `StudioProject.Git.InitializeClick()` — Click Initialize
- `StudioProject.Git.ConnectClick()` — Click Connect
- `StudioProject.Git.UrlChanged()` — Git URL changed

### Decision
- `StudioProject.Decision.Item(index).Click()` — Click decision item at index
- `StudioProject.Decision.Item(index).Hover()` — Hover over decision item at index
- `StudioProject.Decision.Item(index).Delete()` — Delete decision item at index
- `StudioProject.Decision.SaveClick()` — Click Save

---

## WorkspaceEditor

### Toolbar
- `WorkspaceEditor.Toolbar.SaveClick()` — Click Save
- `WorkspaceEditor.Toolbar.UndoClick()` — Click Undo
- `WorkspaceEditor.Toolbar.RedoClick()` — Click Redo
- `WorkspaceEditor.Toolbar.RollbackClick()` — Click Rollback
- `WorkspaceEditor.Toolbar.RollforwardClick()` — Click Rollforward
- `WorkspaceEditor.Toolbar.DiffToggle()` — Toggle diff view
- `WorkspaceEditor.Toolbar.VersionSelected()` — Version selected

### Editor
- `WorkspaceEditor.Editor.TextChanged()` — Editor text changed
- `WorkspaceEditor.Editor.SelectionChanged()` — Editor selection changed
- `WorkspaceEditor.Editor.Focused()` — Editor focused
- `WorkspaceEditor.Editor.Blurred()` — Editor blurred

### Sidebar
- `WorkspaceEditor.Sidebar.AddCommentClick()` — Click Add Comment
- `WorkspaceEditor.Sidebar.BranchAfterRollbackClick()` — Click Branch After Rollback
- `WorkspaceEditor.Sidebar.CommentPromptChanged()` — Comment prompt changed

### Status
- `WorkspaceEditor.Status.ReloadClick()` — Click Reload
- `WorkspaceEditor.Status.InterruptClick()` — Click Interrupt
