using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Controls;
using Haven.Desktop.Services;
using Haven.Desktop.ViewModels;
using Haven.Desktop.Views.Pages.Chat;
using Haven.Desktop.Views.Pages.Canvas;
using Haven.Desktop.Views.Pages.Data;
using Haven.Desktop.Views.Pages.Go;
using Haven.Desktop.Views.Pages.Notes;
using Haven.Desktop.Views.Pages.Present;
using Haven.Desktop.Views.Pages.WorkspaceEditor;
using Haven.Desktop.Views.Pages.Write;
using Haven.Desktop.Views.Shell.TopRail;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Views.Shell;

public sealed partial class MainView
{
    private readonly Guid _windowSessionId = Guid.NewGuid();
    private WorkspaceTabViewModel? _secondaryTab;
    private Window? _trackedWorkspaceWindow;

    public WorkspaceTabViewModel? SecondaryTab => _secondaryTab;
    public bool IsSplitView => _secondaryTab is not null;
    internal bool IsDisposed { get; private set; }
    internal Guid WindowSessionId => _windowSessionId;

    private void TrackWorkspaceWindowGeometry()
    {
        if (TopLevel.GetTopLevel(this) is not Window window || ReferenceEquals(_trackedWorkspaceWindow, window)) return;
        _trackedWorkspaceWindow = window;
        window.PositionChanged += (_, _) => QueueWorkspaceSessionSave();
        window.SizeChanged += (_, _) => QueueWorkspaceSessionSave();
    }

    private WorkspaceTabViewModel? ResolveTab(string identity) =>
        Guid.TryParse(identity, out var id)
            ? OpenTabs.FirstOrDefault(tab => tab.SessionId == id)
            : OpenTabs.FirstOrDefault(tab => tab.Key.Equals(identity, StringComparison.OrdinalIgnoreCase));

    private async void OnTopRailTabCommandRequested(object? sender, TabCommandRequestedEventArgs request)
    {
        var tab = ResolveTab(request.Key);
        if (tab is null) return;
        try
        {
            switch (request.Command)
            {
                case "generate-name": await GenerateTabNameAsync(tab); break;
                case "duplicate": await DuplicateTabAsync(tab); break;
                case "move-left": MoveTab(tab, -1); break;
                case "move-right": MoveTab(tab, 1); break;
                case "split": OpenInSplitView(tab); break;
                case "new-window": App.Services?.GetService<WorkspaceWindowService>()?.OpenInNewWindow(this, tab); break;
                case "popup": App.Services?.GetService<WorkspaceWindowService>()?.OpenInPopUp(this, tab); break;
                case "create-group": await CreateTabGroupAsync(tab); break;
                case "remove-group": RemoveFromGroup(tab); break;
                case "rename-group": await RenameTabGroupAsync(tab); break;
                case "toggle-group": ToggleTabGroup(tab); break;
                case "dissolve-group": DissolveTabGroup(tab); break;
                case "close-others": await CloseTabsAsync(OpenTabs.Where(item => !ReferenceEquals(item, tab)).ToArray()); break;
                case "close-left": await CloseTabsAsync(OpenTabs.Take(OpenTabs.IndexOf(tab)).ToArray()); break;
                case "close-right": await CloseTabsAsync(OpenTabs.Skip(OpenTabs.IndexOf(tab) + 1).ToArray()); break;
                default:
                    if (request.Command.StartsWith("move-group:", StringComparison.Ordinal) &&
                        Guid.TryParse(request.Command["move-group:".Length..], out var groupId))
                        MoveTabToGroup(tab, groupId);
                    break;
            }
        }
        catch (OperationCanceledException) when (tab.LifetimeToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            App.Services?.GetService<NotificationService>()?.Show(
                "Tab action failed",
                SensitiveTextRedactor.Redact(ex.Message),
                ToastKind.Error,
                TimeSpan.FromSeconds(10));
        }
    }

    private void MoveTab(WorkspaceTabViewModel tab, int delta)
    {
        var current = OpenTabs.IndexOf(tab);
        if (current < 0) return;
        var target = Math.Clamp(current + delta, 0, OpenTabs.Count - 1);
        if (target == current) return;
        OpenTabs.Move(current, target);
        RefreshTopRailTabs();
        QueueWorkspaceSessionSave();
    }

    private void MoveTabToGroup(WorkspaceTabViewModel tab, Guid groupId)
    {
        var destination = OpenTabs.FirstOrDefault(item => item.GroupId == groupId);
        if (destination is null) return;
        tab.GroupId = groupId;
        tab.GroupName = destination.GroupName;
        tab.IsGroupCollapsed = destination.IsGroupCollapsed;
        var destinationIndex = OpenTabs.IndexOf(destination);
        var current = OpenTabs.IndexOf(tab);
        if (current >= 0 && destinationIndex >= 0 && current != destinationIndex)
            OpenTabs.Move(current, Math.Min(destinationIndex + 1, OpenTabs.Count - 1));
        RefreshTopRailTabs();
        QueueWorkspaceSessionSave();
    }

    public void OpenInSplitView(WorkspaceTabViewModel tab)
    {
        ArgumentNullException.ThrowIfNull(tab);
        if (!OpenTabs.Contains(tab)) throw new InvalidOperationException("Split panes must reference an open tab session.");
        if (ReferenceEquals(tab, SelectedTab))
            tab = OpenTabs.FirstOrDefault(item => !ReferenceEquals(item, SelectedTab)) ?? tab;
        if (ReferenceEquals(tab, SelectedTab)) return;
        if (_secondaryTab?.Page is IActivatablePage oldPage) oldPage.Deactivate();
        SecondaryPageContent.Content = null;
        _secondaryTab = tab;
        PaneHost.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
        PaneHost.ColumnDefinitions[1].Width = new GridLength(8, GridUnitType.Pixel);
        PaneHost.ColumnDefinitions[2].Width = new GridLength(1, GridUnitType.Star);
        SplitDivider.IsVisible = true;
        SecondaryPageContent.IsVisible = true;
        SecondaryPageContent.Content = tab.Page;
        if (tab.Page is IActivatablePage page) _ = page.ActivateAsync(CancellationToken.None);
        RaisePropertyChanged(nameof(SecondaryTab));
        RaisePropertyChanged(nameof(IsSplitView));
        ApplyShellVisualState();
        QueueWorkspaceSessionSave();
    }

    public void RemoveSplitView()
    {
        if (_secondaryTab?.Page is IActivatablePage page) page.Deactivate();
        SecondaryPageContent.Content = null;
        SecondaryPageContent.IsVisible = false;
        SplitDivider.IsVisible = false;
        PaneHost.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
        PaneHost.ColumnDefinitions[1].Width = new GridLength(0, GridUnitType.Pixel);
        PaneHost.ColumnDefinitions[2].Width = new GridLength(0, GridUnitType.Pixel);
        _secondaryTab = null;
        RaisePropertyChanged(nameof(SecondaryTab));
        RaisePropertyChanged(nameof(IsSplitView));
        ApplyShellVisualState();
        QueueWorkspaceSessionSave();
    }

    public void SwapSplitPanes()
    {
        if (SelectedTab is not { } primary || _secondaryTab is not { } secondary) return;
        PageContent.Content = null;
        SecondaryPageContent.Content = null;
        _secondaryTab = primary;
        SelectedTab = secondary;
        SecondaryPageContent.Content = primary.Page;
        RaisePropertyChanged(nameof(SecondaryTab));
        QueueWorkspaceSessionSave();
    }

    internal WorkspaceTabViewModel DetachTabForMove(WorkspaceTabViewModel tab)
    {
        if (!OpenTabs.Contains(tab)) throw new InvalidOperationException("Tab is not owned by this window.");
        if (ReferenceEquals(_secondaryTab, tab)) RemoveSplitView();
        if (ReferenceEquals(SelectedTab, tab))
        {
            PageContent.Content = null;
            var replacement = OpenTabs.FirstOrDefault(item => !ReferenceEquals(item, tab));
            if (replacement is null)
            {
                AddFallbackTab();
                replacement = SelectedTab;
            }
            else SelectedTab = replacement;
        }
        OpenTabs.Remove(tab);
        RefreshTopRailTabs();
        QueueWorkspaceSessionSave();
        return tab;
    }

    internal void AttachTransferredTab(WorkspaceTabViewModel tab, bool replaceExisting)
    {
        ArgumentNullException.ThrowIfNull(tab);
        if (replaceExisting)
        {
            RemoveSplitView();
            PageContent.Content = null;
            foreach (var existing in OpenTabs.ToArray())
            {
                OpenTabs.Remove(existing);
                existing.Dispose();
            }
            _selectedTab = null;
        }
        if (!OpenTabs.Contains(tab)) OpenTabs.Add(tab);
        SelectedTab = tab;
        RefreshTopRailTabs();
        QueueWorkspaceSessionSave();
    }

    private async Task DuplicateTabAsync(WorkspaceTabViewModel source)
    {
        if (source.Page is GoPage go)
        {
            var (instruction, snapshot) = go.CloneTaskState();
            var clone = CreateGoPage();
            clone.RestorePendingTask(instruction, snapshot);
            AddOrSelectTab("go-" + Guid.NewGuid().ToString("N")[..8], source.Title + " copy", clone, true,
                HavenSurface.Go, forceNewTab: true);
            return;
        }
        if (source.Page is NewChatPage chat)
        {
            var original = chat.CurrentConversation;
            var now = DateTimeOffset.UtcNow;
            var branch = original with
            {
                Id = Guid.NewGuid(),
                Title = source.Title + " copy",
                ParentConversationId = original.Id,
                CreatedAt = now,
                UpdatedAt = now
            };
            await _conversations.UpsertConversationAsync(branch, CancellationToken.None);
            var messages = await _conversations.GetMessagesAsync(original.Id, CancellationToken.None);
            foreach (var item in messages)
                await _conversations.AddMessageAsync(item with { Id = Guid.NewGuid(), ConversationId = branch.Id }, CancellationToken.None);
            var page = CreateNewChatPage();
            await ConfigureAddMenuAsync(page);
            await page.LoadConversationAsync(branch);
            AddOrSelectTab(source.Key + "-copy-" + Guid.NewGuid().ToString("N")[..6], branch.Title, page, true,
                source.Surface, forceNewTab: true);
            return;
        }
        var mode = await _modeRegistry.GetModeByKeyAsync(source.AppKey, CancellationToken.None);
        if (mode is not null && mode.IsEnabled)
        {
            await LaunchAppAsync(mode, openInNewTab: true);
            if (SelectedTab is { } duplicate) duplicate.Title = source.Title + " copy";
        }
        else
        {
            AddFallbackTab();
            if (SelectedTab is { } fallback) fallback.Title = source.Title + " copy";
        }
        RefreshTopRailTabs();
        QueueWorkspaceSessionSave();
    }

    private async Task GenerateTabNameAsync(WorkspaceTabViewModel tab)
    {
        var oldName = tab.Title;
        tab.Title = "Generating name…";
        RefreshTopRailTabs();
        var executionId = Guid.NewGuid();
        var actionId = Guid.NewGuid();
        var sink = App.Services?.GetService<IExecutionEventSink>();
        var cancellationToken = tab.LifetimeToken;
        var started = DateTimeOffset.UtcNow;
        sink?.TryPublish(new ExecutionEvent(Guid.NewGuid(), executionId, actionId, null, ExecutionOrigin.Haven,
            ExecutionActionType.ModelExecution, ExecutionActionStatus.Running, "Generate tab name with AI", null,
            $"App: {tab.AppKey}; current title: {oldName}", "tabs", started, started, TabId: tab.SessionId));
        try
        {
            var models = await _ollama.GetModelsAsync(cancellationToken);
            var model = models.FirstOrDefault(item => item.Name.Equals(_preferences.DefaultModel, StringComparison.OrdinalIgnoreCase)) ?? models.FirstOrDefault();
            if (model is null) throw new InvalidOperationException("No configured model is available.");
            var context = tab.Page is NewChatPage chat
                ? $"App: {tab.AppKey}. Existing title: {oldName}. Conversation title: {chat.CurrentConversation.Title}."
                : $"App: {tab.AppKey}. Existing title: {oldName}. Surface: {tab.Surface}.";
            var answer = await _ollama.CompleteAsync(new OllamaChatRequest(model.Name,
                [new OllamaMessage("user", context)], EffortLevel.Low,
                "Return one concise tab name of at most six words. Return only the name."), cancellationToken);
            var generated = answer.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim().Trim('"', '\'', '.', ':');
            if (string.IsNullOrWhiteSpace(generated)) throw new InvalidOperationException("The model returned an empty name.");
            tab.Title = generated.Length <= 80 ? generated : generated[..80].TrimEnd() + "…";
            var ended = DateTimeOffset.UtcNow;
            sink?.TryPublish(new ExecutionEvent(Guid.NewGuid(), executionId, actionId, null, ExecutionOrigin.Haven,
                ExecutionActionType.ModelExecution, ExecutionActionStatus.Completed, "Generate tab name with AI", null,
                tab.Title, "tabs", ended, started, ended, TabId: tab.SessionId));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            var ended = DateTimeOffset.UtcNow;
            sink?.TryPublish(new ExecutionEvent(Guid.NewGuid(), executionId, actionId, null, ExecutionOrigin.Haven,
                ExecutionActionType.ModelExecution, ExecutionActionStatus.Cancelled, "Generate tab name with AI", null,
                "Name generation was cancelled because the tab closed.", "tabs", ended, started, ended, TabId: tab.SessionId));
            return;
        }
        catch (Exception ex)
        {
            tab.Title = oldName;
            var ended = DateTimeOffset.UtcNow;
            sink?.TryPublish(new ExecutionEvent(Guid.NewGuid(), executionId, actionId, null, ExecutionOrigin.Haven,
                ExecutionActionType.ModelExecution, ExecutionActionStatus.Failed, "Generate tab name with AI", null,
                SensitiveTextRedactor.Redact(ex.Message), "tabs", ended, started, ended, TabId: tab.SessionId,
                Failure: new ExecutionFailure("TAB_NAME_GENERATION_FAILED", "Name generation failed", SensitiveTextRedactor.Redact(ex.Message))));
        }
        RefreshTopRailTabs();
        QueueWorkspaceSessionSave();
    }

    private async Task CreateTabGroupAsync(WorkspaceTabViewModel anchor)
    {
        var candidates = OpenTabs.Where(item => item.IsMarkedForGrouping).ToList();
        if (!candidates.Contains(anchor)) candidates.Add(anchor);
        if (candidates.Count < 2)
        {
            var neighbour = OpenTabs.FirstOrDefault(item => !ReferenceEquals(item, anchor));
            if (neighbour is not null) candidates.Add(neighbour);
        }
        if (candidates.Count < 2) return;
        var name = await PromptForTabValueAsync("Create tab group", "Group name", "Tab group");
        if (string.IsNullOrWhiteSpace(name)) return;
        var id = Guid.NewGuid();
        foreach (var item in candidates.Distinct())
        {
            item.GroupId = id;
            item.GroupName = name.Trim();
            item.IsGroupCollapsed = false;
            item.IsMarkedForGrouping = false;
        }
        RefreshTopRailTabs();
        QueueWorkspaceSessionSave();
    }

    private async Task RenameTabGroupAsync(WorkspaceTabViewModel tab)
    {
        if (tab.GroupId is not { } groupId) return;
        var name = await PromptForTabValueAsync("Rename tab group", "Group name", tab.GroupName);
        if (string.IsNullOrWhiteSpace(name)) return;
        foreach (var item in OpenTabs.Where(item => item.GroupId == groupId)) item.GroupName = name.Trim();
        RefreshTopRailTabs();
        QueueWorkspaceSessionSave();
    }

    private void ToggleTabGroup(WorkspaceTabViewModel tab)
    {
        if (tab.GroupId is not { } groupId) return;
        var collapsed = !tab.IsGroupCollapsed;
        foreach (var item in OpenTabs.Where(item => item.GroupId == groupId)) item.IsGroupCollapsed = collapsed;
        RefreshTopRailTabs();
        QueueWorkspaceSessionSave();
    }

    private void RemoveFromGroup(WorkspaceTabViewModel tab)
    {
        tab.GroupId = null; tab.GroupName = string.Empty; tab.IsGroupCollapsed = false;
        RefreshTopRailTabs(); QueueWorkspaceSessionSave();
    }

    private void DissolveTabGroup(WorkspaceTabViewModel tab)
    {
        if (tab.GroupId is not { } groupId) return;
        foreach (var item in OpenTabs.Where(item => item.GroupId == groupId).ToArray()) RemoveFromGroup(item);
    }

    private async Task CloseTabsAsync(IEnumerable<WorkspaceTabViewModel> tabs)
    {
        foreach (var tab in tabs.ToArray())
        {
            if (tab.IsPinned || tab.IsProtected) continue;
            if (!await TryCloseTabAsync(tab)) break;
        }
    }

    private async Task<bool> TryCloseTabAsync(WorkspaceTabViewModel? tab)
    {
        if (tab is null || !tab.IsCloseable || tab.IsPinned || tab.IsProtected || OpenTabs.Count <= 1) return false;
        if (HasUnsavedWork(tab.Page) && !await ConfirmDiscardAsync(tab.Title)) return false;
        if (ReferenceEquals(_secondaryTab, tab)) RemoveSplitView();
        var index = OpenTabs.IndexOf(tab);
        OpenTabs.Remove(tab);
        tab.Dispose();
        if (ReferenceEquals(SelectedTab, tab))
            SelectedTab = OpenTabs.ElementAtOrDefault(Math.Clamp(index - 1, 0, Math.Max(0, OpenTabs.Count - 1))) ?? OpenTabs.FirstOrDefault();
        RaisePropertyChanged(nameof(IsHorizontalTabsVisible));
        RefreshTopRailTabs();
        QueueWorkspaceSessionSave();
        return true;
    }

    private static bool HasUnsavedWork(object page) => page switch
    {
        WorkspaceEditorPage editor => editor.IsDirty,
        WritePage write => write.IsDirty,
        Haven.Desktop.Views.Pages.Notes.NotesPage notes => notes.IsDirty,
        CanvasPage canvas => canvas.IsDirty,
        DataPage data => data.IsDirty,
        PresentPage present => present.IsDirty,
        _ => false
    };

    private async Task<bool> ConfirmDiscardAsync(string title)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner) return false;
        var result = false;
        var dialog = new Window
        {
            Title = "Unsaved work",
            Width = 430,
            Height = 190,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        var cancel = new HavenButton { Content = "Cancel" };
        var discard = new HavenButton { Content = "Close without saving" };
        discard.Classes.Add("danger");
        cancel.Click += (_, _) => dialog.Close();
        discard.Click += (_, _) => { result = true; dialog.Close(); };
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(24), Spacing = 18,
            Children =
            {
                new TextBlock { Text = $"{title} has unsaved changes.", TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, discard } }
            }
        };
        await dialog.ShowDialog(owner);
        return result;
    }

    private async Task<string?> PromptForTabValueAsync(string title, string label, string initial)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner) return null;
        string? result = null;
        var input = new HavenTextInput { Text = initial, MinWidth = 320 };
        var dialog = new Window { Title = title, Width = 430, Height = 190, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var cancel = new HavenButton { Content = "Cancel" };
        var save = new HavenButton { Content = "Save" }; save.Classes.Add("accent");
        cancel.Click += (_, _) => dialog.Close();
        save.Click += (_, _) => { result = input.Text; dialog.Close(); };
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(24), Spacing = 12,
            Children =
            {
                new TextBlock { Text = label }, input,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, save } }
            }
        };
        await dialog.ShowDialog(owner);
        return result;
    }

    internal WorkspaceWindowSnapshot CreateWindowSnapshot(WorkspaceWindowKind kind)
    {
        var panes = new List<WorkspacePaneSnapshot>();
        if (SelectedTab is { } primary) panes.Add(new WorkspacePaneSnapshot(Guid.NewGuid(), primary.SessionId, 0));
        if (_secondaryTab is { } secondary) panes.Add(new WorkspacePaneSnapshot(Guid.NewGuid(), secondary.SessionId, 1));
        var ratio = PaneHost.Bounds.Width <= 0 || _secondaryTab is null ? 1d
            : Math.Clamp(PageContent.Bounds.Width / PaneHost.Bounds.Width, WorkspaceLayoutSnapshot.MinimumPaneRatio, WorkspaceLayoutSnapshot.MaximumPaneRatio);
        return new WorkspaceWindowSnapshot(_windowSessionId, kind,
            new WorkspaceLayoutSnapshot(Guid.NewGuid(), _secondaryTab is null ? WorkspaceLayoutKind.Single : WorkspaceLayoutKind.Split,
                SplitOrientation.Vertical, ratio, panes), OpenTabs.Select(tab => tab.SessionId).ToArray(), SelectedTab?.SessionId,
            CreateWindowBoundsJson(), DateTimeOffset.UtcNow);
    }

    private string? CreateWindowBoundsJson()
    {
        if (TopLevel.GetTopLevel(this) is not Window window) return null;
        return JsonSerializer.Serialize(new WorkspaceWindowBoundsState(
            window.Position.X, window.Position.Y, window.Width, window.Height));
    }

    private sealed record WorkspaceWindowBoundsState(int X, int Y, double Width, double Height);

    internal TabSessionSnapshot CreateTabSnapshot(WorkspaceTabViewModel tab) => CreateDetachedTabSnapshot(tab);

    internal static TabSessionSnapshot CreateDetachedTabSnapshot(WorkspaceTabViewModel tab) => new(
        tab.SessionId, tab.AppKey, tab.Title,
        CreateTabStateJson(tab), null, null,
        tab.GroupId, tab.IsPinned, tab.IsProtected, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    internal IReadOnlyList<TabSessionSnapshot> CreateTabSnapshots() =>
        OpenTabs.Select(CreateTabSnapshot).ToArray();

    private static string CreateTabStateJson(WorkspaceTabViewModel tab) => JsonSerializer.Serialize(new
    {
        tab.Key,
        Surface = tab.Surface.ToString(),
        ConversationId = tab.Page switch
        {
            NewChatPage chat => chat.ConversationId,
            ChatPage chat => chat.ConversationId,
            _ => (Guid?)null
        }
    });

    internal IReadOnlyList<TabGroupSnapshot> CreateGroupSnapshots() => OpenTabs.Where(tab => tab.GroupId is not null)
        .GroupBy(tab => tab.GroupId!.Value)
        .Select(group => new TabGroupSnapshot(group.Key, group.First().GroupName, group.First().IsGroupCollapsed,
            group.Select(tab => tab.SessionId).ToArray())).ToArray();

    private void QueueWorkspaceSessionSave() => App.Services?.GetService<WorkspaceSessionCoordinator>()?.QueueSave();

    public async Task RestoreWorkspaceSessionAsync(CancellationToken cancellationToken)
    {
        var coordinator = App.Services?.GetService<WorkspaceSessionCoordinator>();
        if (coordinator is null) return;
        var snapshot = await coordinator.LoadAsync(cancellationToken);
        if (snapshot is null || snapshot.SchemaVersion > WorkspaceSessionSnapshot.CurrentSchemaVersion) { coordinator.QueueSave(); return; }
        var window = snapshot.Windows.FirstOrDefault(item => item.Kind == WorkspaceWindowKind.Main) ?? snapshot.Windows.FirstOrDefault();
        if (window is null || window.OrderedTabIds.Count == 0) { coordinator.QueueSave(); return; }

        await RestoreWorkspaceWindowAsync(snapshot, window, cancellationToken);
        if (App.Services?.GetService<WorkspaceWindowService>() is { } windows)
            await windows.RestoreAdditionalWindowsAsync(snapshot, Edition, cancellationToken);
        coordinator.QueueSave();
    }

    internal async Task RestoreWorkspaceWindowAsync(WorkspaceSessionSnapshot snapshot, WorkspaceWindowSnapshot window, CancellationToken cancellationToken)
    {
        var ordered = window.OrderedTabIds.Select(id => snapshot.Tabs.FirstOrDefault(tab => tab.Id == id)).Where(tab => tab is not null).Cast<TabSessionSnapshot>().ToArray();
        if (ordered.Length == 0) return;

        RemoveSplitView();
        PageContent.Content = null;
        foreach (var existing in OpenTabs.ToArray()) { OpenTabs.Remove(existing); existing.Dispose(); }
        _selectedTab = null;
        _goPage = null;
        _homePage = null;
        _newDashboardPage = null;
        _newChatPage = null;
        _planPage = null;

        var groups = snapshot.Groups.ToDictionary(item => item.Id);
        foreach (var saved in ordered)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WorkspaceTabViewModel? restored = null;
            var state = ReadTabState(saved.StateJson);
            try
            {
                if (saved.AppKey.Equals("go", StringComparison.OrdinalIgnoreCase))
                {
                    var page = CreateGoPage();
                    AddOrSelectTab(state.Key ?? "go-" + Guid.NewGuid().ToString("N")[..8], saved.Title, page, true, HavenSurface.Go, forceNewTab: true);
                    restored = SelectedTab;
                }
                else if (saved.AppKey.Equals("home", StringComparison.OrdinalIgnoreCase))
                {
                    var page = CreateHomePage();
                    AddOrSelectTab(state.Key ?? "home-" + Guid.NewGuid().ToString("N")[..8], saved.Title, page, true, HavenSurface.Home, forceNewTab: true);
                    restored = SelectedTab;
                }
                else if (saved.AppKey is "chat" or "new" or "new-chat")
                {
                    var page = CreateNewChatPage();
                    await ConfigureAddMenuAsync(page);
                    if (state.ConversationId is { } conversationId && await _conversations.GetAsync(conversationId, cancellationToken) is { } conversation)
                        await page.LoadConversationAsync(conversation);
                    else
                        await page.StartFreshConversationAsync(HavenMode.Chat, null);
                    AddOrSelectTab(state.Key ?? "new-chat-" + Guid.NewGuid().ToString("N")[..8], saved.Title, page, true, HavenSurface.Chat, forceNewTab: true);
                    restored = SelectedTab;
                }
                else
                {
                    var mode = await _modeRegistry.GetModeByKeyAsync(saved.AppKey, cancellationToken);
                    if (mode is not null && mode.IsEnabled)
                    {
                        var before = OpenTabs.Count;
                        await LaunchAppAsync(mode, openInNewTab: before > 0);
                        restored = SelectedTab;
                        if (OpenTabs.Count == before && restored is not null && ordered.Count(item => item.AppKey.Equals(saved.AppKey, StringComparison.OrdinalIgnoreCase)) > 1)
                            restored = null;
                    }
                }
            }
            catch
            {
                restored = null;
            }
            if (restored is null)
            {
                AddFallbackTab();
                restored = SelectedTab;
            }
            if (restored is null) continue;
            restored.RestoreIdentity(saved.Id, saved.AppKey);
            restored.Title = saved.Title;
            restored.IsPinned = saved.IsPinned;
            restored.IsProtected = saved.IsProtected;
            restored.GroupId = saved.GroupId;
            if (saved.GroupId is { } groupId && groups.TryGetValue(groupId, out var group))
            {
                restored.GroupName = group.Name;
                restored.IsGroupCollapsed = group.IsCollapsed;
            }
        }

        if (window.SelectedTabId is { } selectedId && OpenTabs.FirstOrDefault(tab => tab.SessionId == selectedId) is { } selected)
            SelectedTab = selected;
        if (window.Layout.Kind == WorkspaceLayoutKind.Split && window.Layout.Panes.OrderBy(pane => pane.Order).Skip(1).FirstOrDefault() is { } secondaryPane &&
            OpenTabs.FirstOrDefault(tab => tab.SessionId == secondaryPane.TabId) is { } secondary && !ReferenceEquals(secondary, SelectedTab))
        {
            OpenInSplitView(secondary);
            var ratio = Math.Clamp(window.Layout.PrimaryRatio, WorkspaceLayoutSnapshot.MinimumPaneRatio, WorkspaceLayoutSnapshot.MaximumPaneRatio);
            PaneHost.ColumnDefinitions[0].Width = new GridLength(ratio, GridUnitType.Star);
            PaneHost.ColumnDefinitions[2].Width = new GridLength(1 - ratio, GridUnitType.Star);
        }
        if (TopLevel.GetTopLevel(this) is Window hostWindow)
            WorkspaceWindowService.ApplyWindowBounds(hostWindow, window.BoundsJson);
        RefreshTopRailTabs();
    }

    private static (string? Key, Guid? ConversationId) ReadTabState(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var key = root.TryGetProperty("key", out var keyElement) ? keyElement.GetString()
                : root.TryGetProperty("Key", out keyElement) ? keyElement.GetString() : null;
            Guid? conversationId = null;
            if ((root.TryGetProperty("conversationId", out var conversationElement) || root.TryGetProperty("ConversationId", out conversationElement)) &&
                conversationElement.ValueKind == JsonValueKind.String && Guid.TryParse(conversationElement.GetString(), out var parsed)) conversationId = parsed;
            return (key, conversationId);
        }
        catch (JsonException) { return (null, null); }
    }
}
