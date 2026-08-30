using System.Collections.Specialized;
using INotifyPropertyChanged = System.ComponentModel.INotifyPropertyChanged;
using PropertyChangedEventArgs = System.ComponentModel.PropertyChangedEventArgs;
using System.Windows.Input;
using UserControl = Avalonia.Controls.UserControl;
using Avalonia.Input;
using Avalonia.Threading;
using Haven.Core;
using Haven.Desktop.HavenUI.Backend;
using Haven.Desktop.ViewModels;
using Haven.Desktop.Views.Pages.StudioProject;
using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;
using HavenText = Haven.UI.Components.Text;

namespace Haven.Desktop.Views;

/// <summary>Avalonia hosts one Haven scene; all visible Studio product UI is Haven.UI.</summary>
public sealed class StudioProjectHavenView : UserControl
{
    private readonly ProjectHavenScene _scene = new();
    private readonly HavenSceneControl _host;
    private StudioProjectPage? _page;

    public StudioProjectHavenView()
    {
        _host = new HavenSceneControl { Root = _scene.Root };
        Content = _host;
        _host.InputSubmitted += input => { if (ReferenceEquals(input, _scene.ComposerInput)) _scene.SubmitComposer(); };
        WireScene();
        DataContextChanged += (_, _) => AttachPage();
        AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(AttachPage);
        DetachedFromVisualTree += (_, _) => DetachPage();
        SizeChanged += (_, e) => _scene.SetViewportWidth(e.NewSize.Width);
        KeyDown += OnKeyDown;
    }

    private void WireScene()
    {
        _scene.BackRequested += (_, _) => Execute(_page?.BackToProjectsCommand);
        _scene.SettingsRequested += (_, _) => { Execute(_page?.SwitchToConfigureCommand); RefreshAll(); };
        _scene.RefreshRequested += async (_, _) => await ExecuteAsync(_page?.RefreshCommand);
        _scene.BuildRequested += async (_, _) => await ExecuteAsync(_page?.BuildCommand);
        _scene.TestRequested += async (_, _) => await ExecuteAsync(_page?.TestCommand);
        _scene.EditorRequested += async (_, _) => await ExecuteAsync(_page?.OpenEditorCommand);
        _scene.TerminalRequested += async (_, _) => await ExecuteAsync(_page?.OpenTerminalCommand);
        _scene.ServerRequested += async (_, _) => await ExecuteAsync(_page?.StartServerCommand);
        _scene.RiskRequested += async (_, _) => { await ExecuteAsync(_page?.ForecastRiskCommand); RefreshAll(); };
        _scene.ReviewErrorRequested += async (_, _) =>
        {
            if (_page is { } page)
                await page.StartChatWithPromptCommand.ExecuteAsync($">Debug Review the latest project error and help me fix it safely.\n\nLatest error:\n{page.LatestError}");
        };
        _scene.SourceControlRefreshRequested += async (_, _) => { await ExecuteAsync(_page?.RefreshSourceControlCommand); RefreshAll(); };
        _scene.SourceControlStageRequested += async change => { if (_page?.StageSourceControlCommand.CanExecute(change) == true) await _page.StageSourceControlCommand.ExecuteAsync(change); RefreshAll(); };
        _scene.SourceControlUnstageRequested += async change => { if (_page?.UnstageSourceControlCommand.CanExecute(change) == true) await _page.UnstageSourceControlCommand.ExecuteAsync(change); RefreshAll(); };
        _scene.SourceControlCheckoutRequested += async branch => { if (_page?.CheckoutSourceControlBranchCommand.CanExecute(branch) == true) await _page.CheckoutSourceControlBranchCommand.ExecuteAsync(branch); RefreshAll(); };
        _scene.SourceControlCreateStashRequested += async message => { if (_page is { } page) { page.StashMessage = message; await page.CreateSourceControlStashCommand.ExecuteAsync(); } RefreshAll(); };
        _scene.SourceControlApplyStashRequested += async stash => { if (_page?.ApplySourceControlStashCommand.CanExecute(stash) == true) await _page.ApplySourceControlStashCommand.ExecuteAsync(stash); RefreshAll(); };
        _scene.ContextRequested += (_, _) =>
        {
            if (_page is { } page)
                _scene.SetTransientStatus($"Project context includes {page.Files.Count} indexed files, repository state, and {page.ProjectConversations.Count} project chats.");
        };
        _scene.SaveSettingsRequested += async (_, _) => { await ExecuteAsync(_page?.SaveProjectSettingsCommand); RefreshAll(); };
        _scene.CancelSettingsRequested += (_, _) => { Execute(_page?.CancelProjectSettingsCommand); RefreshAll(); };
        _scene.GenerateContextRequested += async (_, _) => { await ExecuteAsync(_page?.GenerateProjectContextCommand); RefreshAll(); };
        _scene.ConversationRequested += conversation => Execute(_page?.OpenConversationCommand, conversation);
        _scene.FileRequested += file => Execute(_page?.OpenFileCommand, file);
        _scene.SubmitRequested += async prompt => await SubmitAsync(prompt);
        _scene.ProjectNameChanged += value => { if (_page is not null) _page.ProjectNameDraft = value; };
        _scene.ProjectContextChanged += value => { if (_page is not null) _page.ProjectContextDraft = value; };
    }

    private void AttachPage()
    {
        if (ReferenceEquals(_page, DataContext)) { RefreshAll(); return; }
        DetachPage();
        _page = DataContext as StudioProjectPage;
        if (_page is not null)
        {
            ((INotifyPropertyChanged)_page).PropertyChanged += OnPagePropertyChanged;
            _page.Files.CollectionChanged += OnCollectionChanged;
            _page.ProjectConversations.CollectionChanged += OnCollectionChanged;
            _ = ExecuteAsync(_page.RefreshSourceControlCommand);
        }
        RefreshAll();
    }

    private void DetachPage()
    {
        if (_page is null) return;
        ((INotifyPropertyChanged)_page).PropertyChanged -= OnPagePropertyChanged;
        _page.Files.CollectionChanged -= OnCollectionChanged;
        _page.ProjectConversations.CollectionChanged -= OnCollectionChanged;
        _page = null;
    }

    private void OnPagePropertyChanged(object? sender, PropertyChangedEventArgs e) => Dispatcher.UIThread.Post(RefreshAll);
    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => Dispatcher.UIThread.Post(RefreshAll);

    private void RefreshAll()
    {
        if (_page is not { } page)
        {
            _scene.SetUnavailable("Project unavailable", "The selected project could not be loaded.");
            return;
        }
        _scene.Sync(new StudioSceneState(
            page.ProjectName, page.Status, page.Branch, page.WorkState, page.LastBuild, page.RecommendedAction,
            page.LastCommit, page.LatestError, page.RiskSummary, page.Decisions.Count, page.ActiveAutomations.Count,
            page.RootPath, page.IsInConfigureMode, page.ProjectNameDraft, page.ProjectContextDraft, page.ConfigureStatus,
            page.ProjectConversations.ToArray(), page.Files.ToArray()));
        _scene.SyncSourceControl(page.SourceControl);
    }

    private async Task SubmitAsync(string prompt)
    {
        if (_page is not { } page) return;
        if (string.IsNullOrWhiteSpace(prompt)) await page.StartChatCommand.ExecuteAsync();
        else await page.StartChatWithPromptCommand.ExecuteAsync(prompt.Trim());
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || _page?.IsInConfigureMode != true) return;
        _page.CancelProjectSettingsCommand.Execute(null);
        RefreshAll();
        e.Handled = true;
    }

    private static async Task ExecuteAsync(AsyncRelayCommand? command)
    {
        if (command is not null && command.CanExecute(null)) await command.ExecuteAsync();
    }

    private static void Execute(ICommand? command)
    {
        if (command?.CanExecute(null) == true) command.Execute(null);
    }

    private static void Execute<T>(ICommand? command, T parameter)
    {
        if (command?.CanExecute(parameter) == true) command.Execute(parameter);
    }
}

internal sealed record StudioSceneState(
    string ProjectName,
    string Status,
    string Branch,
    string WorkState,
    string LastBuild,
    string RecommendedAction,
    string LastCommit,
    string LatestError,
    string RiskSummary,
    int DecisionCount,
    int AutomationCount,
    string RootPath,
    bool IsInConfigureMode,
    string ProjectNameDraft,
    string ProjectContextDraft,
    string ConfigureStatus,
    IReadOnlyList<Conversation> Conversations,
    IReadOnlyList<WorkspaceFileItemViewModel> Files);

/// <summary>Haven-native Studio project surface backed by Prefab and DynamicUI.</summary>
internal sealed class StudioHavenScene : IDisposable
{
    private readonly HavenPrefabCatalog _prefabs;
    private readonly DynamicUI _dynamicUi;
    private StudioSceneState? _state;
    private string _query = string.Empty;
    private string _rowsSignature = string.Empty;
    private bool _syncingSearch;

    public StudioHavenScene()
    {
        _prefabs = HavenPrefabCatalog.FromAssembly(typeof(StudioHavenScene).Assembly);
        Root = new Page { Name = "StudioRoot", Layout = HavenLayout.Overlay };
        Root.Accessibility.AccessibleName = "Studio project workspace";
        Workspace = Grid("Workspace", "280px 1fr", "1fr");
        Root.Add(Workspace);

        Sidebar = Grid("Sidebar", "1fr", "Auto Auto Auto 1fr Auto");
        Sidebar.SetValue(HavenProperties.Column, 0);
        Sidebar.SetValue(HavenProperties.Background, "Surface");
        Sidebar.SetValue(HavenProperties.BorderColor, "Border");
        Sidebar.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
        Sidebar.SetValue(HavenProperties.Padding, HavenThickness.Uniform(HavenLength.Px(18)));
        Workspace.Add(Sidebar);

        var back = Nav("Back", "All Projects", "chevron-left"); back.SetValue(HavenProperties.Row, 0); Sidebar.Add(back);
        SidebarProjectName = Label("SidebarProjectName", "Project", TextLevel.H3); SidebarProjectName.SetValue(HavenProperties.Row, 1); Sidebar.Add(SidebarProjectName);
        SidebarSearch = InputField("SidebarSearch", "Search project"); SidebarSearch.SetValue(HavenProperties.Row, 2); Sidebar.Add(SidebarSearch);
        var sidebarScroll = Vertical("SidebarScroll", 8); sidebarScroll.SetValue(HavenProperties.Row, 3); sidebarScroll.SetValue(HavenProperties.Overflow, HavenOverflow.Scroll); Sidebar.Add(sidebarScroll);
        var home = Nav("Home", "Project Home", "home"); sidebarScroll.Add(home);
        sidebarScroll.Add(Caption("Project Chats")); SidebarChats = Runtime("SidebarChats"); sidebarScroll.Add(SidebarChats); SidebarChatsEmpty = Muted("SidebarChatsEmpty", "No project chats yet."); sidebarScroll.Add(SidebarChatsEmpty);
        sidebarScroll.Add(Caption("Project Files")); SidebarFiles = Runtime("SidebarFiles"); sidebarScroll.Add(SidebarFiles); SidebarFilesEmpty = Muted("SidebarFilesEmpty", "No matching files."); sidebarScroll.Add(SidebarFilesEmpty);
        var settings = Nav("Settings", "Project Settings", "settings"); settings.SetValue(HavenProperties.Row, 4); Sidebar.Add(settings);

        Main = Grid("Main", "1fr", "Auto Auto Auto 1fr Auto Auto");
        Main.SetValue(HavenProperties.Column, 1);
        Main.SetValue(HavenProperties.Padding, HavenThickness.Parse("26px 32px 18px 32px"));
        Workspace.Add(Main);
        var header = Grid("Header", "44px 1fr 44px", "44px"); header.SetValue(HavenProperties.Row, 0); Main.Add(header);
        var mainBack = IconButton("MainBack", "chevron-left", "All Projects"); header.Add(mainBack);
        Title = Label("Title", "Project", TextLevel.H1); Title.SetValue(HavenProperties.Column, 1); Title.SetValue(HavenProperties.HorizontalAlignment, HavenHorizontalAlignment.Center); header.Add(Title);
        var mainSettings = IconButton("MainSettings", "settings", "Project Settings"); mainSettings.SetValue(HavenProperties.Column, 2); header.Add(mainSettings);
        MainSearch = InputField("MainSearch", "Search Project"); MainSearch.SetValue(HavenProperties.Row, 1); Main.Add(MainSearch);

        var actions = Wrap("QuickActions", 8); actions.SetValue(HavenProperties.Row, 2); Main.Add(actions);
        foreach (var spec in new[] { ("Refresh", "Refresh", "refresh"), ("Build", "Build", "hammer"), ("Test", "Tests", "check"), ("Editor", "Editor", "edit"), ("Terminal", "Terminal", "terminal"), ("Server", "Local server", "play") })
            actions.Add(Ghost(spec.Item1, spec.Item2, spec.Item3));

        var content = Vertical("ContentScroll", 12); content.SetValue(HavenProperties.Row, 3); content.SetValue(HavenProperties.Overflow, HavenOverflow.Scroll); Main.Add(content);
        StateCards = Grid("StateCards", "1fr 1fr 1fr 1fr", "Auto"); content.Add(StateCards);
        BranchCard = StateCard("BranchCard", "Branch", "BranchValue", out Branch); StateCards.Add(BranchCard);
        WorkStateCard = StateCard("WorkStateCard", "Work state", "WorkStateValue", out WorkState); StateCards.Add(WorkStateCard);
        LastBuildCard = StateCard("LastBuildCard", "Last build", "LastBuildValue", out LastBuild); StateCards.Add(LastBuildCard);
        RecommendedCard = StateCard("RecommendedCard", "Recommended", "RecommendedValue", out Recommended); StateCards.Add(RecommendedCard);
        PlaceStateCards(4);
        content.Add(Caption("Project chats")); MainChats = Runtime("MainChats"); content.Add(MainChats); MainChatsEmpty = Muted("MainChatsEmpty", "Start a project chat to keep project-specific history here."); content.Add(MainChatsEmpty);
        content.Add(Caption("Project files")); MainFiles = Runtime("MainFiles"); content.Add(MainFiles); MainFilesEmpty = Muted("MainFilesEmpty", "No matching project files."); content.Add(MainFilesEmpty);

        Status = Muted("Status", "Loading project state…"); Status.SetValue(HavenProperties.Row, 4); Status.SetValue(HavenProperties.HorizontalAlignment, HavenHorizontalAlignment.Center); Main.Add(Status);
        Composer = _prefabs.Create("Chatbox", "Studio-Chatbox"); Composer.SetValue(HavenProperties.Row, 5); Main.Add(Composer);
        ComposerInput = Composer.GetComponent<Input>("Instruction"); ComposerInput.Placeholder = "Start New Chat"; ComposerInput.Multiline = true; ComposerInput.SubmitOnEnter = true;
        ContextButton = Composer.GetComponent<HavenButton>("AddMenu"); ContextButton.Accessibility.AccessibleName = "Show included project context";
        SendButton = Composer.GetComponent<HavenButton>("Send"); SendButton.Accessibility.AccessibleName = "Start project chat";

        SettingsOverlay = BuildSettingsOverlay(); Root.Add(SettingsOverlay);
        _dynamicUi = new DynamicUI(Root, HavenDynamicUITemplateCatalog.FromAssembly(typeof(StudioHavenScene).Assembly), _prefabs);

        Wire(back, () => BackRequested?.Invoke(this, EventArgs.Empty));
        Wire(mainBack, () => BackRequested?.Invoke(this, EventArgs.Empty));
        Wire(home, () => HomeRequested?.Invoke(this, EventArgs.Empty));
        Wire(settings, () => SettingsRequested?.Invoke(this, EventArgs.Empty));
        Wire(mainSettings, () => SettingsRequested?.Invoke(this, EventArgs.Empty));
        Wire("Refresh", () => RefreshRequested?.Invoke(this, EventArgs.Empty)); Wire("Build", () => BuildRequested?.Invoke(this, EventArgs.Empty)); Wire("Test", () => TestRequested?.Invoke(this, EventArgs.Empty));
        Wire("Editor", () => EditorRequested?.Invoke(this, EventArgs.Empty)); Wire("Terminal", () => TerminalRequested?.Invoke(this, EventArgs.Empty)); Wire("Server", () => ServerRequested?.Invoke(this, EventArgs.Empty));
        Wire("SaveSettings", () => SaveSettingsRequested?.Invoke(this, EventArgs.Empty)); Wire("CancelSettings", () => CancelSettingsRequested?.Invoke(this, EventArgs.Empty)); Wire("GenerateContext", () => GenerateContextRequested?.Invoke(this, EventArgs.Empty));
        Wire(ContextButton, () => ContextRequested?.Invoke(this, EventArgs.Empty)); Wire(SendButton, SubmitComposer);
        SidebarSearch.TextChanged += (_, _) => SyncSearch(SidebarSearch, MainSearch); MainSearch.TextChanged += (_, _) => SyncSearch(MainSearch, SidebarSearch);
        SettingsName.TextChanged += (_, _) => ProjectNameChanged?.Invoke(SettingsName.Text); SettingsContext.TextChanged += (_, _) => ProjectContextChanged?.Invoke(SettingsContext.Text);
        SetViewportWidth(1200);
    }

    public Page Root { get; }
    public Container Workspace { get; }
    public Container Sidebar { get; }
    public Container Main { get; }
    public Container StateCards { get; }
    public Container BranchCard { get; }
    public Container WorkStateCard { get; }
    public Container LastBuildCard { get; }
    public Container RecommendedCard { get; }
    public HavenText Title { get; }
    public HavenText SidebarProjectName { get; }
    public HavenText Status { get; }
    public HavenText Branch = null!;
    public HavenText WorkState = null!;
    public HavenText LastBuild = null!;
    public HavenText Recommended = null!;
    public Input SidebarSearch { get; }
    public Input MainSearch { get; }
    public DynamicUIRuntime SidebarChats { get; }
    public DynamicUIRuntime SidebarFiles { get; }
    public DynamicUIRuntime MainChats { get; }
    public DynamicUIRuntime MainFiles { get; }
    public HavenText SidebarChatsEmpty { get; }
    public HavenText SidebarFilesEmpty { get; }
    public HavenText MainChatsEmpty { get; }
    public HavenText MainFilesEmpty { get; }
    public Prefab Composer { get; }
    public Input ComposerInput { get; }
    public HavenButton ContextButton { get; }
    public HavenButton SendButton { get; }
    public Container SettingsOverlay { get; }
    public Input SettingsName { get; private set; } = null!;
    public Input SettingsContext { get; private set; } = null!;
    public HavenText ConfigureStatus { get; private set; } = null!;
    public HavenText SettingsMeta { get; private set; } = null!;

    public event EventHandler? BackRequested; public event EventHandler? HomeRequested; public event EventHandler? SettingsRequested; public event EventHandler? RefreshRequested;
    public event EventHandler? BuildRequested; public event EventHandler? TestRequested; public event EventHandler? EditorRequested; public event EventHandler? TerminalRequested; public event EventHandler? ServerRequested;
    public event EventHandler? ContextRequested; public event EventHandler? SaveSettingsRequested; public event EventHandler? CancelSettingsRequested; public event EventHandler? GenerateContextRequested;
    public event Action<Conversation>? ConversationRequested; public event Action<WorkspaceFileItemViewModel>? FileRequested; public event Action<string>? SubmitRequested; public event Action<string>? ProjectNameChanged; public event Action<string>? ProjectContextChanged;

    public void Sync(StudioSceneState state)
    {
        _state = state; Title.Content = state.ProjectName; SidebarProjectName.Content = state.ProjectName; Status.Content = state.Status; Branch.Content = state.Branch; WorkState.Content = state.WorkState; LastBuild.Content = state.LastBuild; Recommended.Content = state.RecommendedAction;
        SettingsOverlay.SetValue(HavenProperties.Visibility, state.IsInConfigureMode ? HavenVisibility.Visible : HavenVisibility.Collapsed); ComposerInput.SetValue(HavenProperties.Enabled, !state.IsInConfigureMode); SendButton.SetValue(HavenProperties.Enabled, !state.IsInConfigureMode);
        if (SettingsName.Text != state.ProjectNameDraft) SettingsName.Text = state.ProjectNameDraft; if (SettingsContext.Text != state.ProjectContextDraft) SettingsContext.Text = state.ProjectContextDraft;
        ConfigureStatus.Content = state.ConfigureStatus; ConfigureStatus.SetValue(HavenProperties.Visibility, string.IsNullOrWhiteSpace(state.ConfigureStatus) ? HavenVisibility.Collapsed : HavenVisibility.Visible);
        SettingsMeta.Content = $"Folder: {state.RootPath}\nRepository: {state.Branch} · {state.WorkState}\nIncluded: {state.Files.Count} files · {state.Conversations.Count} chats";
        RenderRows();
    }

    public void SetUnavailable(string title, string status) { _state = null; Title.Content = SidebarProjectName.Content = title; Status.Content = status; Branch.Content = WorkState.Content = LastBuild.Content = "Unavailable"; Recommended.Content = "Return to Projects"; ClearRows(); }
    public void SetTransientStatus(string value) => Status.Content = value;
    public void SubmitComposer() { if (_state?.IsInConfigureMode == true) return; var prompt = ComposerInput.Text.Trim(); SubmitRequested?.Invoke(prompt); ComposerInput.Text = string.Empty; }

    public void SetViewportWidth(double width)
    {
        if (!double.IsFinite(width) || width <= 0) return; var compact = width < 900;
        Sidebar.SetValue(HavenProperties.Visibility, compact ? HavenVisibility.Collapsed : HavenVisibility.Visible); Workspace.Columns = compact ? "1fr" : "280px 1fr"; Main.SetValue(HavenProperties.Column, compact ? 0 : 1); Main.SetValue(HavenProperties.Padding, HavenThickness.Parse(compact ? "14px 14px 10px 14px" : "26px 32px 18px 32px"));
        PlaceStateCards(width < 640 ? 1 : width < 1180 ? 2 : 4);
    }

    private void RenderRows()
    {
        if (_state is not { } state) return; var signature = RowsSignature(state, _query); if (signature == _rowsSignature) return; ClearRows(); _rowsSignature = signature;
        var chats = state.Conversations.Where(x => Match(x.Title)).OrderByDescending(x => x.UpdatedAt).ToArray(); var files = state.Files.Where(x => Match(x.RelativePath)).OrderBy(x => x.RelativePath, StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (var chat in chats.Take(12)) AddChat("SidebarChats", "side-" + chat.Id.ToString("N"), chat, false); foreach (var file in files.Take(16)) AddFile("SidebarFiles", "side-" + Id(file.RelativePath), file, false);
        foreach (var chat in chats) AddChat("MainChats", "main-" + chat.Id.ToString("N"), chat, true); foreach (var file in files.Take(120)) AddFile("MainFiles", "main-" + Id(file.RelativePath), file, true);
        Empty(SidebarChatsEmpty, SidebarChats.Items.Count == 0); Empty(SidebarFilesEmpty, SidebarFiles.Items.Count == 0); Empty(MainChatsEmpty, MainChats.Items.Count == 0); Empty(MainFilesEmpty, MainFiles.Items.Count == 0);
    }

    private void AddChat(string host, string id, Conversation chat, bool detail)
    {
        var item = _dynamicUi.CreateItem("StudioConversationRow", host, id, new Dictionary<string, object?> { ["TITLE"] = chat.Title, ["DETAIL"] = detail ? Relative(chat.UpdatedAt) : string.Empty, ["DETAILVISIBILITY"] = detail ? "Visible" : "Collapsed" });
        var open = item.GetComponent<HavenButton>("Open"); open.Accessibility.AccessibleName = "Open project chat " + chat.Title; Wire(open, () => ConversationRequested?.Invoke(chat));
    }

    private void AddFile(string host, string id, WorkspaceFileItemViewModel file, bool detail)
    {
        var item = _dynamicUi.CreateItem("StudioFileRow", host, id, new Dictionary<string, object?> { ["TITLE"] = file.Name, ["DETAIL"] = detail ? file.RelativePath : string.Empty, ["DETAILVISIBILITY"] = detail ? "Visible" : "Collapsed" });
        var open = item.GetComponent<HavenButton>("Open"); open.Accessibility.AccessibleName = "Open project file " + file.RelativePath; Wire(open, () => FileRequested?.Invoke(file));
    }

    private Container BuildSettingsOverlay()
    {
        var overlay = new Container { Name = "SettingsOverlay", Layout = HavenLayout.Overlay }; Full(overlay); overlay.SetValue(HavenProperties.Background, "Overlay"); overlay.SetValue(HavenProperties.ZIndex, 100); overlay.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
        var card = Vertical("SettingsCard", 12); card.SetValue(HavenProperties.Width, HavenLength.Px(720)); card.SetValue(HavenProperties.MaxWidth, HavenLength.Percent(92)); card.SetValue(HavenProperties.MaxHeight, HavenLength.Percent(86)); card.SetValue(HavenProperties.HorizontalAlignment, HavenHorizontalAlignment.Center); card.SetValue(HavenProperties.VerticalAlignment, HavenVerticalAlignment.Center); card.SetValue(HavenProperties.Overflow, HavenOverflow.Scroll); card.SetValue(HavenProperties.Background, "SurfaceRaised"); card.SetValue(HavenProperties.Padding, HavenThickness.Uniform(HavenLength.Px(28))); card.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(26))); card.SetValue(HavenProperties.Shadow, "Card"); overlay.Add(card);
        var heading = Label("SettingsHeading", "Project Settings", TextLevel.H1); heading.SetValue(HavenProperties.HorizontalAlignment, HavenHorizontalAlignment.Center); card.Add(heading);
        card.Add(Caption("Name")); SettingsName = InputField("SettingsName", "Project name"); card.Add(SettingsName); card.Add(Caption("Context")); SettingsContext = InputField("SettingsContext", "Project context"); SettingsContext.Multiline = true; SettingsContext.SubmitOnEnter = false; SettingsContext.SetValue(HavenProperties.MinHeight, HavenLength.Px(180)); card.Add(SettingsContext);
        card.Add(Ghost("GenerateContext", "Generate Context from Chats", "sparkles")); SettingsMeta = Muted("SettingsMeta", string.Empty); card.Add(SettingsMeta); ConfigureStatus = Muted("ConfigureStatus", string.Empty); card.Add(ConfigureStatus);
        var actions = Wrap("SettingsActions", 8); actions.SetValue(HavenProperties.HorizontalAlignment, HavenHorizontalAlignment.End); actions.Add(Ghost("CancelSettings", "Cancel", string.Empty)); var save = new HavenButton { Name = "SaveSettings", Content = "Save", Variant = ButtonVariant.Primary }; actions.Add(save); card.Add(actions); return overlay;
    }

    private void SyncSearch(Input source, Input target) { if (_syncingSearch) return; _syncingSearch = true; try { _query = source.Text.Trim(); if (target.Text != source.Text) target.Text = source.Text; } finally { _syncingSearch = false; } RenderRows(); }
    private bool Match(string value) => _query.Length == 0 || value.Contains(_query, StringComparison.OrdinalIgnoreCase);
    private void ClearRows() { foreach (var runtime in new[] { SidebarChats, SidebarFiles, MainChats, MainFiles }) runtime.ClearItems(); _rowsSignature = string.Empty; }
    private static string RowsSignature(StudioSceneState state, string query) => query + "|" + string.Join('|', state.Conversations.Select(x => x.Id + ":" + x.Title + ":" + x.UpdatedAt.UtcDateTime.Ticks)) + "|" + string.Join('|', state.Files.Select(x => x.RelativePath));
    private static string Relative(DateTimeOffset time) { var d = DateTimeOffset.UtcNow - time; return d.TotalMinutes < 1 ? "just now" : d.TotalHours < 1 ? $"{(int)d.TotalMinutes}m ago" : d.TotalDays < 1 ? $"{(int)d.TotalHours}h ago" : d.TotalDays < 7 ? $"{(int)d.TotalDays}d ago" : time.LocalDateTime.ToString("d MMM"); }
    private static string Id(string value) { unchecked { var hash = 2166136261u; foreach (var c in value) { hash ^= c; hash *= 16777619u; } return hash.ToString("x8"); } }
    private void PlaceStateCards(int columns) { StateCards.Columns = string.Join(' ', Enumerable.Repeat("1fr", columns)); StateCards.Rows = columns == 4 ? "Auto" : columns == 2 ? "Auto Auto" : "Auto Auto Auto Auto"; var cards = new[] { BranchCard, WorkStateCard, LastBuildCard, RecommendedCard }; for (var i = 0; i < cards.Length; i++) { cards[i].SetValue(HavenProperties.Column, i % columns); cards[i].SetValue(HavenProperties.Row, i / columns); } }

    private T Find<T>(string name) where T : HavenElement => Root.DescendantsAndSelf().OfType<T>().Single(x => x.Name == name);
    private void Wire(string name, Action action) => Wire(Find<HavenButton>(name), action);
    private static void Wire(HavenButton button, Action action) => button.Invoked += (_, _) => action();
    private static void Empty(HavenText text, bool visible) => text.SetValue(HavenProperties.Visibility, visible ? HavenVisibility.Visible : HavenVisibility.Collapsed);
    private static Container Grid(string name, string columns, string rows) { var c = new Container { Name = name, Layout = HavenLayout.Grid, Columns = columns, Rows = rows }; Full(c); c.SetValue(HavenProperties.Gap, HavenLength.Px(10)); return c; }
    private static Container Vertical(string name, double gap) { var c = new Container { Name = name, Layout = HavenLayout.Vertical }; c.SetValue(HavenProperties.Width, HavenLength.Percent(100)); c.SetValue(HavenProperties.Gap, HavenLength.Px(gap)); return c; }
    private static Container Wrap(string name, double gap) { var c = new Container { Name = name, Layout = HavenLayout.Wrap }; c.SetValue(HavenProperties.Width, HavenLength.Percent(100)); c.SetValue(HavenProperties.Gap, HavenLength.Px(gap)); return c; }
    private static DynamicUIRuntime Runtime(string name) { var r = new DynamicUIRuntime { Name = name }; r.SetValue(HavenProperties.Width, HavenLength.Percent(100)); return r; }
    private static HavenText Label(string name, string content, TextLevel level = TextLevel.Paragraph) => new() { Name = name, Content = content, Level = level };
    private static HavenText Caption(string content) { var t = Label(string.Empty, content, TextLevel.Caption); t.SetValue(HavenProperties.Foreground, "TextSecondary"); return t; }
    private static HavenText Muted(string name, string content) { var t = Label(name, content); t.SetValue(HavenProperties.FontSize, 11d); t.SetValue(HavenProperties.Foreground, "TextSecondary"); return t; }
    private static Input InputField(string name, string placeholder) { var i = new Input { Name = name, Placeholder = placeholder }; i.SetValue(HavenProperties.Width, HavenLength.Percent(100)); return i; }
    private static HavenButton Nav(string name, string content, string icon) { var b = new HavenButton { Name = name, Content = content, IconKey = icon, Variant = ButtonVariant.Navigation }; b.SetValue(HavenProperties.Width, HavenLength.Percent(100)); return b; }
    private static HavenButton Ghost(string name, string content, string icon) => new() { Name = name, Content = content, IconKey = icon, Variant = ButtonVariant.Ghost };
    private static HavenButton IconButton(string name, string icon, string accessible) { var b = new HavenButton { Name = name, IconKey = icon, Content = string.Empty, Variant = ButtonVariant.Icon }; b.Accessibility.AccessibleName = accessible; b.SetValue(HavenProperties.Width, HavenLength.Px(42)); b.SetValue(HavenProperties.Height, HavenLength.Px(42)); return b; }
    private static Container StateCard(string name, string title, string valueName, out HavenText value) { var c = Vertical(name, 4); c.SetValue(HavenProperties.Background, "SurfaceRaised"); c.SetValue(HavenProperties.BorderColor, "Border"); c.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1)); c.SetValue(HavenProperties.Padding, HavenThickness.Uniform(HavenLength.Px(14))); c.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(16))); c.Add(Caption(title)); value = Label(valueName, "Not inspected"); value.SetValue(HavenProperties.FontWeight, 700); c.Add(value); return c; }
    private static void Full(HavenElement e) { e.SetValue(HavenProperties.Width, HavenLength.Percent(100)); e.SetValue(HavenProperties.Height, HavenLength.Percent(100)); }
    public void Dispose() => ClearRows();
}
