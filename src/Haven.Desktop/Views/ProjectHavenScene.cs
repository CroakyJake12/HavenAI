using Haven.Core;
using Haven.Desktop.HavenUI.Backend;
using Haven.Desktop.ViewModels;
using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;
using HavenText = Haven.UI.Components.Text;

namespace Haven.Desktop.Views;

/// <summary>Haven-native integrated Project workspace. Internal Studio model names remain compatibility-only.</summary>
internal sealed partial class ProjectHavenScene : IDisposable
{
    private readonly HavenPrefabCatalog _prefabs;
    private readonly DynamicUI _dynamicUi;
    private StudioSceneState? _state;
    private string _query = string.Empty;
    private string _rowsSignature = string.Empty;
    private bool _syncingSearch;

    public ProjectHavenScene()
    {
        _prefabs = HavenPrefabCatalog.FromAssembly(typeof(ProjectHavenScene).Assembly);
        Root = new Page { Name = "Project.Root", Layout = HavenLayout.Overlay };
        Root.Accessibility.AccessibleName = "Project integrated workspace";

        Workspace = Grid("Project.Workspace", "250px 1fr", "1fr");
        Root.Add(Workspace);

        Explorer = Grid("Project.Explorer", "1fr", "Auto Auto Auto 1fr Auto");
        Explorer.SetValue(HavenProperties.Column, 0);
        Explorer.SetValue(HavenProperties.Background, "Surface");
        Explorer.SetValue(HavenProperties.BorderColor, "Border");
        Explorer.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
        Explorer.SetValue(HavenProperties.Padding, HavenThickness.Parse("14px 12px"));
        Workspace.Add(Explorer);

        var back = Nav("Project.Back", "All Projects", "chevron-left");
        back.SetValue(HavenProperties.Row, 0);
        Explorer.Add(back);
        ProjectName = Label("Project.Explorer.Name", "Project", TextLevel.H3);
        ProjectName.SetValue(HavenProperties.Row, 1);
        Explorer.Add(ProjectName);
        ExplorerSearch = InputField("Project.Explorer.Search", "Search files and project chats");
        ExplorerSearch.SetValue(HavenProperties.Row, 2);
        Explorer.Add(ExplorerSearch);

        var explorerScroll = Vertical("Project.Explorer.Scroll", 8);
        explorerScroll.SetValue(HavenProperties.Row, 3);
        explorerScroll.SetValue(HavenProperties.Overflow, HavenOverflow.Scroll);
        Explorer.Add(explorerScroll);
        explorerScroll.Add(Caption("PROJECT ITEMS"));
        explorerScroll.Add(Caption("[Chat] Project chats"));
        ExplorerChats = Runtime("Project.Explorer.Chats");
        explorerScroll.Add(ExplorerChats);
        ExplorerChatsEmpty = Muted("Project.Explorer.Chats.Empty", "No project chats yet.");
        explorerScroll.Add(ExplorerChatsEmpty);
        explorerScroll.Add(Caption("Repository files"));
        ExplorerFiles = Runtime("Project.Explorer.Files");
        explorerScroll.Add(ExplorerFiles);
        ExplorerFilesEmpty = Muted("Project.Explorer.Files.Empty", "No matching repository files.");
        explorerScroll.Add(ExplorerFilesEmpty);
        var settings = Nav("Project.Settings", "Project Settings", "settings");
        settings.SetValue(HavenProperties.Row, 4);
        Explorer.Add(settings);

        Main = Grid("Project.Main", "1fr", "Auto 1fr Auto");
        Main.SetValue(HavenProperties.Column, 1);
        Main.SetValue(HavenProperties.Padding, HavenThickness.Parse("12px 14px 10px 14px"));
        Workspace.Add(Main);

        var toolbar = Wrap("Project.Toolbar", 6);
        toolbar.SetValue(HavenProperties.Row, 0);
        Main.Add(toolbar);
        var mainBack = IconButton("Project.Toolbar.Back", "chevron-left", "All Projects");
        toolbar.Add(mainBack);
        Title = Label("Project.Title", "Project", TextLevel.H2);
        toolbar.Add(Title);
        toolbar.Add(Ghost("Project.Refresh", "Refresh", "refresh"));
        toolbar.Add(Ghost("Project.Build", "Build", "hammer"));
        toolbar.Add(Ghost("Project.Tests", "Tests", "check"));
        toolbar.Add(Ghost("Project.Terminal", "Terminal", "terminal"));
        toolbar.Add(Ghost("Project.Server", "Local server", "play"));
        toolbar.Add(IconButton("Project.Toolbar.Settings", "settings", "Project Settings"));

        WorkArea = Grid("Project.WorkArea", "1fr 320px", "1fr");
        WorkArea.SetValue(HavenProperties.Row, 1);
        Main.Add(WorkArea);

        EditorPane = Grid("Project.Editor", "1fr", "Auto 1fr");
        EditorPane.SetValue(HavenProperties.Column, 0);
        EditorPane.SetValue(HavenProperties.Background, "SurfaceRaised");
        EditorPane.SetValue(HavenProperties.BorderColor, "Border");
        EditorPane.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
        EditorPane.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(14)));
        EditorPane.SetValue(HavenProperties.Clip, true);
        WorkArea.Add(EditorPane);

        var tabs = Wrap("Project.Editor.Tabs", 4);
        tabs.SetValue(HavenProperties.Row, 0);
        tabs.SetValue(HavenProperties.Padding, HavenThickness.Parse("6px 8px"));
        tabs.SetValue(HavenProperties.Background, "Surface");
        tabs.Add(Caption("EDITOR"));
        ActiveTab = Label("Project.Editor.ActiveTab", "No file open", TextLevel.Caption);
        tabs.Add(ActiveTab);
        EditorPane.Add(tabs);

        var editorBody = Vertical("Project.Editor.Body", 10);
        editorBody.SetValue(HavenProperties.Row, 1);
        editorBody.SetValue(HavenProperties.Padding, HavenThickness.Uniform(HavenLength.Px(16)));
        editorBody.SetValue(HavenProperties.Overflow, HavenOverflow.Scroll);
        EditorPane.Add(editorBody);

        CompactExplorer = Vertical("Project.CompactExplorer", 8);
        CompactExplorer.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
        CompactExplorer.SetValue(HavenProperties.MaxHeight, HavenLength.Px(230));
        CompactExplorer.SetValue(HavenProperties.Overflow, HavenOverflow.Scroll);
        CompactExplorer.Add(Caption("PROJECT FILES"));
        CompactSearch = InputField("Project.CompactExplorer.Search", "Search repository files");
        CompactExplorer.Add(CompactSearch);
        CompactFiles = Runtime("Project.CompactExplorer.Files");
        CompactExplorer.Add(CompactFiles);
        CompactFilesEmpty = Muted("Project.CompactExplorer.Files.Empty", "No matching repository files.");
        CompactExplorer.Add(CompactFilesEmpty);
        editorBody.Add(CompactExplorer);

        var editorLanding = Card("Project.Editor.Landing");
        editorLanding.Add(Label("Project.Editor.Landing.Title", "Editor workspace", TextLevel.H2));
        editorLanding.Add(Muted("Project.Editor.Landing.Help", "Choose a repository file in Explorer to open it with Haven's save, diff and history tools."));
        var openEditor = Ghost("Project.Editor.Open", "Open project editor", "edit");
        editorLanding.Add(openEditor);
        editorBody.Add(editorLanding);

        AssistantPanel = Grid("Project.Assistant", "1fr", "Auto 1fr Auto");
        AssistantPanel.SetValue(HavenProperties.Column, 1);
        AssistantPanel.SetValue(HavenProperties.Background, "Surface");
        AssistantPanel.SetValue(HavenProperties.BorderColor, "Border");
        AssistantPanel.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
        AssistantPanel.SetValue(HavenProperties.Padding, HavenThickness.Parse("12px"));
        AssistantPanel.Add(Caption("PROJECT AI"));
        ChatHistory = Runtime("Project.Assistant.Chats");
        ChatHistory.SetValue(HavenProperties.Row, 1);
        ChatHistory.SetValue(HavenProperties.Overflow, HavenOverflow.Scroll);
        AssistantPanel.Add(ChatHistory);
        ChatHistoryEmpty = Muted("Project.Assistant.Chats.Empty", "Start a project chat. It stays attached to this project.");
        ChatHistoryEmpty.SetValue(HavenProperties.Row, 1);
        AssistantPanel.Add(ChatHistoryEmpty);

        Composer = _prefabs.Create("Chatbox", "Project-Chatbox");
        Composer.SetValue(HavenProperties.Row, 2);
        AssistantPanel.Add(Composer);
        ComposerInput = Composer.GetComponent<Input>("Instruction");
        ComposerInput.Placeholder = "Ask Haven about this project";
        ComposerInput.Multiline = true;
        ComposerInput.SubmitOnEnter = true;
        ContextButton = Composer.GetComponent<HavenButton>("AddMenu");
        ContextButton.Accessibility.AccessibleName = "Show included project context";
        SendButton = Composer.GetComponent<HavenButton>("Send");
        SendButton.Accessibility.AccessibleName = "Start project chat";
        WorkArea.Add(AssistantPanel);

        ToolDock = Vertical("Project.ToolDock", 6);
        ToolDock.SetValue(HavenProperties.Row, 2);
        ToolDock.SetValue(HavenProperties.Background, "Surface");
        ToolDock.SetValue(HavenProperties.BorderColor, "Border");
        ToolDock.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
        ToolDock.SetValue(HavenProperties.Padding, HavenThickness.Parse("8px 10px"));
        Main.Add(ToolDock);
        StateStrip = Grid("Project.StateStrip", "1fr 1fr 1fr 1fr", "Auto");
        Branch = StatusValue("Project.State.Branch", "Branch");
        WorkState = StatusValue("Project.State.Work", "Work tree");
        LastBuild = StatusValue("Project.State.Build", "Build");
        Recommended = StatusValue("Project.State.Next", "Next");
        StateStrip.Add(Branch);
        WorkState.SetValue(HavenProperties.Column, 1); StateStrip.Add(WorkState);
        LastBuild.SetValue(HavenProperties.Column, 2); StateStrip.Add(LastBuild);
        Recommended.SetValue(HavenProperties.Column, 3); StateStrip.Add(Recommended);
        ToolDock.Add(StateStrip);

        var intelligence = Grid("Project.Intelligence", "1fr 1fr 1fr", "Auto Auto");
        LastCommit = StatusValue("Project.Intelligence.Commit", "Commit");
        LatestError = StatusValue("Project.Intelligence.Error", "Latest error");
        Risk = StatusValue("Project.Intelligence.Risk", "Release risk");
        intelligence.Add(LastCommit);
        LatestError.SetValue(HavenProperties.Column, 1); intelligence.Add(LatestError);
        Risk.SetValue(HavenProperties.Column, 2); intelligence.Add(Risk);
        DecisionCount = StatusValue("Project.Intelligence.Decisions", "Decisions");
        DecisionCount.SetValue(HavenProperties.Row, 1); intelligence.Add(DecisionCount);
        AutomationCount = StatusValue("Project.Intelligence.Automations", "Automations");
        AutomationCount.SetValue(HavenProperties.Column, 1); AutomationCount.SetValue(HavenProperties.Row, 1); intelligence.Add(AutomationCount);
        var intelligenceActions = Wrap("Project.Intelligence.Actions", 6);
        intelligenceActions.SetValue(HavenProperties.Column, 2); intelligenceActions.SetValue(HavenProperties.Row, 1);
        intelligenceActions.Add(Ghost("Project.Intelligence.ForecastRisk", "Forecast risk", "shield-check"));
        intelligenceActions.Add(Ghost("Project.Intelligence.AskError", "Ask Haven about error", "sparkles"));
        intelligence.Add(intelligenceActions);
        ToolDock.Add(intelligence);

        Status = Muted("Project.Status", "Loading project state…");
        ToolDock.Add(Status);

        SettingsOverlay = BuildSettingsOverlay();
        Root.Add(SettingsOverlay);
        _dynamicUi = new DynamicUI(Root, HavenDynamicUITemplateCatalog.FromAssembly(typeof(ProjectHavenScene).Assembly), _prefabs);

        Wire(back, () => BackRequested?.Invoke(this, EventArgs.Empty));
        Wire(mainBack, () => BackRequested?.Invoke(this, EventArgs.Empty));
        Wire(settings, () => SettingsRequested?.Invoke(this, EventArgs.Empty));
        Wire("Project.Toolbar.Settings", () => SettingsRequested?.Invoke(this, EventArgs.Empty));
        Wire("Project.Refresh", () => RefreshRequested?.Invoke(this, EventArgs.Empty));
        Wire("Project.Build", () => BuildRequested?.Invoke(this, EventArgs.Empty));
        Wire("Project.Tests", () => TestRequested?.Invoke(this, EventArgs.Empty));
        Wire("Project.Terminal", () => TerminalRequested?.Invoke(this, EventArgs.Empty));
        Wire("Project.Server", () => ServerRequested?.Invoke(this, EventArgs.Empty));
        Wire("Project.Intelligence.ForecastRisk", () => RiskRequested?.Invoke(this, EventArgs.Empty));
        Wire("Project.Intelligence.AskError", () => ReviewErrorRequested?.Invoke(this, EventArgs.Empty));
        Wire(openEditor, () => EditorRequested?.Invoke(this, EventArgs.Empty));
        Wire("Project.Settings.Save", () => SaveSettingsRequested?.Invoke(this, EventArgs.Empty));
        Wire("Project.Settings.Cancel", () => CancelSettingsRequested?.Invoke(this, EventArgs.Empty));
        Wire("Project.Settings.GenerateContext", () => GenerateContextRequested?.Invoke(this, EventArgs.Empty));
        Wire(ContextButton, () => ContextRequested?.Invoke(this, EventArgs.Empty));
        Wire(SendButton, SubmitComposer);
        ExplorerSearch.TextChanged += (_, _) => SyncSearch(ExplorerSearch, CompactSearch);
        CompactSearch.TextChanged += (_, _) => SyncSearch(CompactSearch, ExplorerSearch);
        SettingsName.TextChanged += (_, _) => ProjectNameChanged?.Invoke(SettingsName.Text);
        SettingsContext.TextChanged += (_, _) => ProjectContextChanged?.Invoke(SettingsContext.Text);
        InitializeHavenDevJourney();
        SetViewportWidth(1280);
    }

    public Page Root { get; }
    public Container Workspace { get; }
    public Container Explorer { get; }
    public Container Main { get; }
    public Container WorkArea { get; }
    public Container EditorPane { get; }
    public Container AssistantPanel { get; }
    public Container CompactExplorer { get; }
    public Container ToolDock { get; }
    public Container StateStrip { get; }
    public HavenText Title { get; }
    public HavenText ProjectName { get; }
    public HavenText ActiveTab { get; }
    public HavenText Status { get; }
    public HavenText Branch { get; }
    public HavenText WorkState { get; }
    public HavenText LastBuild { get; }
    public HavenText Recommended { get; }
    public HavenText LastCommit { get; }
    public HavenText LatestError { get; }
    public HavenText Risk { get; }
    public HavenText DecisionCount { get; }
    public HavenText AutomationCount { get; }
    public Input ExplorerSearch { get; }
    public Input CompactSearch { get; }
    public DynamicUIRuntime ExplorerChats { get; }
    public DynamicUIRuntime ExplorerFiles { get; }
    public DynamicUIRuntime ChatHistory { get; }
    public DynamicUIRuntime CompactFiles { get; }
    public HavenText ExplorerChatsEmpty { get; }
    public HavenText ExplorerFilesEmpty { get; }
    public HavenText ChatHistoryEmpty { get; }
    public HavenText CompactFilesEmpty { get; }
    public Prefab Composer { get; }
    public Input ComposerInput { get; }
    public HavenButton ContextButton { get; }
    public HavenButton SendButton { get; }
    public Container SettingsOverlay { get; }
    public Input SettingsName { get; private set; } = null!;
    public Input SettingsContext { get; private set; } = null!;
    public HavenText ConfigureStatus { get; private set; } = null!;
    public HavenText SettingsMeta { get; private set; } = null!;

    public event EventHandler? BackRequested;
    public event EventHandler? SettingsRequested;
    public event EventHandler? RefreshRequested;
    public event EventHandler? BuildRequested;
    public event EventHandler? TestRequested;
    public event EventHandler? EditorRequested;
    public event EventHandler? TerminalRequested;
    public event EventHandler? ServerRequested;
    public event EventHandler? RiskRequested;
    public event EventHandler? ReviewErrorRequested;
    public event EventHandler? ContextRequested;
    public event EventHandler? SaveSettingsRequested;
    public event EventHandler? CancelSettingsRequested;
    public event EventHandler? GenerateContextRequested;
    public event Action<Conversation>? ConversationRequested;
    public event Action<WorkspaceFileItemViewModel>? FileRequested;
    public event Action<string>? SubmitRequested;
    public event Action<string>? ProjectNameChanged;
    public event Action<string>? ProjectContextChanged;

    public void Sync(StudioSceneState state)
    {
        _state = state;
        Title.Content = state.ProjectName;
        ProjectName.Content = state.ProjectName;
        Status.Content = state.Status;
        Branch.Content = "Branch · " + state.Branch;
        WorkState.Content = "Work tree · " + state.WorkState;
        LastBuild.Content = "Build · " + state.LastBuild;
        Recommended.Content = "Next · " + state.RecommendedAction;
        LastCommit.Content = "Commit · " + state.LastCommit;
        LatestError.Content = "Latest error · " + state.LatestError;
        Risk.Content = "Release risk · " + state.RiskSummary;
        DecisionCount.Content = $"Decisions · {state.DecisionCount}";
        AutomationCount.Content = $"Automations · {state.AutomationCount}";
        SettingsOverlay.SetValue(HavenProperties.Visibility, state.IsInConfigureMode ? HavenVisibility.Visible : HavenVisibility.Collapsed);
        ComposerInput.SetValue(HavenProperties.Enabled, !state.IsInConfigureMode);
        SendButton.SetValue(HavenProperties.Enabled, !state.IsInConfigureMode);
        if (SettingsName.Text != state.ProjectNameDraft) SettingsName.Text = state.ProjectNameDraft;
        if (SettingsContext.Text != state.ProjectContextDraft) SettingsContext.Text = state.ProjectContextDraft;
        ConfigureStatus.Content = state.ConfigureStatus;
        ConfigureStatus.SetValue(HavenProperties.Visibility, string.IsNullOrWhiteSpace(state.ConfigureStatus) ? HavenVisibility.Collapsed : HavenVisibility.Visible);
        SettingsMeta.Content = $"Folder: {state.RootPath}\nRepository: {state.Branch} · {state.WorkState}\nIncluded: {state.Files.Count} files · {state.Conversations.Count} project chats";
        RenderRows();
    }

    public void SetUnavailable(string title, string status)
    {
        _state = null;
        Title.Content = ProjectName.Content = title;
        Status.Content = status;
        Branch.Content = "Branch · unavailable";
        WorkState.Content = "Work tree · unavailable";
        LastBuild.Content = "Build · unavailable";
        Recommended.Content = "Next · return to Projects";
        LastCommit.Content = "Commit · unavailable";
        LatestError.Content = "Latest error · unavailable";
        Risk.Content = "Release risk · unavailable";
        DecisionCount.Content = "Decisions · 0";
        AutomationCount.Content = "Automations · 0";
        ClearRows();
    }

    public void SetTransientStatus(string value) => Status.Content = value;

    public void SubmitComposer()
    {
        if (_state?.IsInConfigureMode == true) return;
        var prompt = ComposerInput.Text.Trim();
        SubmitRequested?.Invoke(prompt);
        ComposerInput.Text = string.Empty;
    }

    public void SetViewportWidth(double width)
    {
        if (!double.IsFinite(width) || width <= 0) return;
        var compactExplorer = width < 760;
        var stackAssistant = width < 1100;

        Explorer.SetValue(HavenProperties.Visibility, compactExplorer ? HavenVisibility.Collapsed : HavenVisibility.Visible);
        CompactExplorer.SetValue(HavenProperties.Visibility, compactExplorer ? HavenVisibility.Visible : HavenVisibility.Collapsed);
        Workspace.Columns = compactExplorer ? "1fr" : "250px 1fr";
        Main.SetValue(HavenProperties.Column, compactExplorer ? 0 : 1);
        Main.SetValue(HavenProperties.Padding, HavenThickness.Parse(width < 600 ? "8px" : "12px 14px 10px 14px"));

        WorkArea.Columns = stackAssistant ? "1fr" : "1fr 320px";
        WorkArea.Rows = stackAssistant ? "1fr 280px" : "1fr";
        AssistantPanel.SetValue(HavenProperties.Column, stackAssistant ? 0 : 1);
        AssistantPanel.SetValue(HavenProperties.Row, stackAssistant ? 1 : 0);

        StateStrip.Columns = width < 650 ? "1fr 1fr" : "1fr 1fr 1fr 1fr";
        StateStrip.Rows = width < 650 ? "Auto Auto" : "Auto";
        WorkState.SetValue(HavenProperties.Column, width < 650 ? 1 : 1);
        WorkState.SetValue(HavenProperties.Row, 0);
        LastBuild.SetValue(HavenProperties.Column, width < 650 ? 0 : 2);
        LastBuild.SetValue(HavenProperties.Row, width < 650 ? 1 : 0);
        Recommended.SetValue(HavenProperties.Column, width < 650 ? 1 : 3);
        Recommended.SetValue(HavenProperties.Row, width < 650 ? 1 : 0);
    }

    private void RenderRows()
    {
        if (_state is not { } state) return;
        var signature = RowsSignature(state, _query);
        if (signature == _rowsSignature) return;
        ClearRows();
        _rowsSignature = signature;

        var chats = state.Conversations.Where(x => Match(x.Title)).OrderByDescending(x => x.UpdatedAt).ToArray();
        var files = state.Files.Where(x => Match(x.RelativePath)).OrderBy(x => x.RelativePath, StringComparer.OrdinalIgnoreCase).ToArray();

        foreach (var chat in chats)
        {
            AddChat("Project.Explorer.Chats", "explorer-" + chat.Id.ToString("N"), chat, false);
            AddChat("Project.Assistant.Chats", "assistant-" + chat.Id.ToString("N"), chat, true);
        }
        foreach (var file in files)
        {
            AddFile("Project.Explorer.Files", "explorer-" + Id(file.RelativePath), file, false);
            AddFile("Project.CompactExplorer.Files", "compact-" + Id(file.RelativePath), file, true);
        }

        Empty(ExplorerChatsEmpty, ExplorerChats.Items.Count == 0);
        Empty(ExplorerFilesEmpty, ExplorerFiles.Items.Count == 0);
        Empty(ChatHistoryEmpty, ChatHistory.Items.Count == 0);
        Empty(CompactFilesEmpty, CompactFiles.Items.Count == 0);
    }

    private void AddChat(string host, string id, Conversation chat, bool detail)
    {
        var item = _dynamicUi.CreateItem("StudioConversationRow", host, id, new Dictionary<string, object?>
        {
            ["TITLE"] = "[Chat] " + chat.Title,
            ["DETAIL"] = detail ? Relative(chat.UpdatedAt) : string.Empty,
            ["DETAILVISIBILITY"] = detail ? "Visible" : "Collapsed"
        });
        var open = item.GetComponent<HavenButton>("Open");
        open.Accessibility.AccessibleName = "Open project chat " + chat.Title;
        Wire(open, () => ConversationRequested?.Invoke(chat));
    }

    private void AddFile(string host, string id, WorkspaceFileItemViewModel file, bool detail)
    {
        var item = _dynamicUi.CreateItem("StudioFileRow", host, id, new Dictionary<string, object?>
        {
            ["TITLE"] = file.Name,
            ["DETAIL"] = detail ? file.RelativePath : string.Empty,
            ["DETAILVISIBILITY"] = detail ? "Visible" : "Collapsed"
        });
        var open = item.GetComponent<HavenButton>("Open");
        open.Accessibility.AccessibleName = "Open project file " + file.RelativePath;
        Wire(open, () => FileRequested?.Invoke(file));
    }

    private Container BuildSettingsOverlay()
    {
        var overlay = new Container { Name = "Project.Settings.Overlay", Layout = HavenLayout.Overlay };
        Full(overlay);
        overlay.SetValue(HavenProperties.Background, "Overlay");
        overlay.SetValue(HavenProperties.ZIndex, 100);
        overlay.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
        var card = Vertical("Project.Settings.Card", 12);
        card.SetValue(HavenProperties.Width, HavenLength.Px(720));
        card.SetValue(HavenProperties.MaxWidth, HavenLength.Percent(92));
        card.SetValue(HavenProperties.MaxHeight, HavenLength.Percent(86));
        card.SetValue(HavenProperties.HorizontalAlignment, HavenHorizontalAlignment.Center);
        card.SetValue(HavenProperties.VerticalAlignment, HavenVerticalAlignment.Center);
        card.SetValue(HavenProperties.Overflow, HavenOverflow.Scroll);
        card.SetValue(HavenProperties.Background, "SurfaceRaised");
        card.SetValue(HavenProperties.Padding, HavenThickness.Uniform(HavenLength.Px(28)));
        card.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(22)));
        card.SetValue(HavenProperties.Shadow, "Card");
        overlay.Add(card);
        card.Add(Label("Project.Settings.Title", "Project Settings", TextLevel.H1));
        card.Add(Caption("Name"));
        SettingsName = InputField("Project.Settings.Name", "Project name");
        card.Add(SettingsName);
        card.Add(Caption("Project context"));
        SettingsContext = InputField("Project.Settings.Context", "Project context");
        SettingsContext.Multiline = true;
        SettingsContext.SubmitOnEnter = false;
        SettingsContext.SetValue(HavenProperties.MinHeight, HavenLength.Px(180));
        card.Add(SettingsContext);
        card.Add(Ghost("Project.Settings.GenerateContext", "Generate context from chats", "sparkles"));
        SettingsMeta = Muted("Project.Settings.Meta", string.Empty);
        card.Add(SettingsMeta);
        ConfigureStatus = Muted("Project.Settings.Status", string.Empty);
        card.Add(ConfigureStatus);
        var actions = Wrap("Project.Settings.Actions", 8);
        actions.SetValue(HavenProperties.HorizontalAlignment, HavenHorizontalAlignment.End);
        actions.Add(Ghost("Project.Settings.Cancel", "Cancel", string.Empty));
        actions.Add(new HavenButton { Name = "Project.Settings.Save", Content = "Save", Variant = ButtonVariant.Primary });
        card.Add(actions);
        return overlay;
    }

    private void SyncSearch(Input source, Input target)
    {
        if (_syncingSearch) return;
        _syncingSearch = true;
        try
        {
            _query = source.Text.Trim();
            if (target.Text != source.Text) target.Text = source.Text;
        }
        finally
        {
            _syncingSearch = false;
        }
        RenderRows();
    }

    private bool Match(string value) => _query.Length == 0 || value.Contains(_query, StringComparison.OrdinalIgnoreCase);

    private void ClearRows()
    {
        ExplorerChats.ClearItems();
        ExplorerFiles.ClearItems();
        ChatHistory.ClearItems();
        CompactFiles.ClearItems();
        _rowsSignature = string.Empty;
    }

    private static string RowsSignature(StudioSceneState state, string query) =>
        query + "|" + string.Join('|', state.Conversations.Select(x => x.Id + ":" + x.Title + ":" + x.UpdatedAt.UtcDateTime.Ticks)) +
        "|" + string.Join('|', state.Files.Select(x => x.RelativePath));

    private static string Relative(DateTimeOffset time)
    {
        var d = DateTimeOffset.UtcNow - time;
        return d.TotalMinutes < 1 ? "just now" : d.TotalHours < 1 ? $"{(int)d.TotalMinutes}m ago" : d.TotalDays < 1 ? $"{(int)d.TotalHours}h ago" : d.TotalDays < 7 ? $"{(int)d.TotalDays}d ago" : time.LocalDateTime.ToString("d MMM");
    }

    private static string Id(string value)
    {
        unchecked
        {
            var hash = 2166136261u;
            foreach (var c in value) { hash ^= c; hash *= 16777619u; }
            return hash.ToString("x8");
        }
    }

    private T Find<T>(string name) where T : HavenElement => Root.DescendantsAndSelf().OfType<T>().Single(x => x.Name == name);
    private void Wire(string name, Action action) => Wire(Find<HavenButton>(name), action);
    private static void Wire(HavenButton button, Action action) => button.Invoked += (_, _) => action();
    private static void Empty(HavenText text, bool visible) => text.SetValue(HavenProperties.Visibility, visible ? HavenVisibility.Visible : HavenVisibility.Collapsed);
    private static Container Grid(string name, string columns, string rows) { var c = new Container { Name = name, Layout = HavenLayout.Grid, Columns = columns, Rows = rows }; Full(c); c.SetValue(HavenProperties.Gap, HavenLength.Px(8)); return c; }
    private static Container Vertical(string name, double gap) { var c = new Container { Name = name, Layout = HavenLayout.Vertical }; c.SetValue(HavenProperties.Width, HavenLength.Percent(100)); c.SetValue(HavenProperties.Gap, HavenLength.Px(gap)); return c; }
    private static Container Wrap(string name, double gap) { var c = new Container { Name = name, Layout = HavenLayout.Wrap }; c.SetValue(HavenProperties.Width, HavenLength.Percent(100)); c.SetValue(HavenProperties.Gap, HavenLength.Px(gap)); return c; }
    private static Container Card(string name) { var c = Vertical(name, 8); c.SetValue(HavenProperties.Background, "Surface"); c.SetValue(HavenProperties.BorderColor, "Border"); c.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1)); c.SetValue(HavenProperties.Padding, HavenThickness.Uniform(HavenLength.Px(18))); c.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(16))); return c; }
    private static DynamicUIRuntime Runtime(string name) { var r = new DynamicUIRuntime { Name = name }; r.SetValue(HavenProperties.Width, HavenLength.Percent(100)); return r; }
    private static HavenText Label(string name, string content, TextLevel level = TextLevel.Paragraph) => new() { Name = name, Content = content, Level = level };
    private static HavenText Caption(string content) { var t = Label(string.Empty, content, TextLevel.Caption); t.SetValue(HavenProperties.Foreground, "TextSecondary"); return t; }
    private static HavenText Muted(string name, string content) { var t = Label(name, content, TextLevel.Caption); t.SetValue(HavenProperties.Foreground, "TextSecondary"); return t; }
    private static HavenText StatusValue(string name, string label) { var t = Label(name, label + " · not inspected", TextLevel.Caption); t.SetValue(HavenProperties.Foreground, "TextSecondary"); return t; }
    private static Input InputField(string name, string placeholder) { var input = new Input { Name = name, Placeholder = placeholder }; input.SetValue(HavenProperties.Width, HavenLength.Percent(100)); return input; }
    private static HavenButton Nav(string name, string content, string icon) { var b = new HavenButton { Name = name, Content = content, IconKey = icon, Variant = ButtonVariant.Navigation }; b.SetValue(HavenProperties.Width, HavenLength.Percent(100)); return b; }
    private static HavenButton Ghost(string name, string content, string icon) => new() { Name = name, Content = content, IconKey = icon, Variant = ButtonVariant.Ghost };
    private static HavenButton IconButton(string name, string icon, string accessible) { var b = new HavenButton { Name = name, IconKey = icon, Content = string.Empty, Variant = ButtonVariant.Icon }; b.Accessibility.AccessibleName = accessible; b.SetValue(HavenProperties.Width, HavenLength.Px(40)); b.SetValue(HavenProperties.Height, HavenLength.Px(40)); b.SetValue(HavenProperties.MinHeight, HavenLength.Px(40)); return b; }
    private static void Full(HavenElement element) { element.SetValue(HavenProperties.Width, HavenLength.Percent(100)); element.SetValue(HavenProperties.Height, HavenLength.Percent(100)); }

    public void Dispose() => ClearRows();
}
