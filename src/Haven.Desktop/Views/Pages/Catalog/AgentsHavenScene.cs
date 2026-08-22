using System.Collections.Specialized;
using System.ComponentModel;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.ViewModels;
using Haven.UI;
using Haven.UI.Components;
using Container = Haven.UI.Components.Container;
using HavenButton = Haven.UI.Components.Button;
using HavenText = Haven.UI.Components.Text;

namespace Haven.Desktop.Views.Pages.Catalog;

/// <summary>
/// Haven-native Agents management surface. Agent definitions and all persistence remain
/// owned by CatalogPageViewModel/ICatalogRepository; this scene projects that real state
/// through Haven.UI and uses DynamicUI for the runtime-owned repeated card list.
/// </summary>
internal sealed class AgentsHavenScene : IDisposable
{
    private readonly CatalogPageViewModel _viewModel;
    private readonly DynamicUI _dynamicUi;
    private readonly AgentTaskRuntimeService? _runtime;
    private AgentRun? _latestRun;
    private IReadOnlyList<AgentRun> _recentRuns = [];
    private Guid? _pendingDeleteId;
    private bool _disposed;

    public AgentsHavenScene(CatalogPageViewModel viewModel, AgentTaskRuntimeService? runtime = null)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _runtime = runtime;
        if (viewModel.Kind != CatalogPageKind.Agents)
            throw new ArgumentException("AgentsHavenScene requires the Agents catalogue view-model.", nameof(viewModel));

        Root = new Page
        {
            Name = "Agents.Root",
            Layout = HavenLayout.Grid,
            Columns = "1fr",
            Rows = "auto auto auto auto 1fr"
        };
        Set(Root, HavenProperties.Padding, HavenThickness.Parse("26px 30px"));
        Set(Root, HavenProperties.Gap, HavenLength.Px(14));
        Set(Root, HavenProperties.Background, "Transparent");

        var header = new Container { Name = "Agents.Header", Layout = HavenLayout.Grid, Columns = "1fr Auto Auto", Rows = "auto" };
        Set(header, HavenProperties.Gap, HavenLength.Px(8));
        var titles = new Container { Layout = HavenLayout.Vertical };
        Set(titles, HavenProperties.Gap, HavenLength.Px(4));
        titles.Add(new HavenText("Agents") { Name = "Agents.Title", Level = TextLevel.H1 });
        var subtitle = new HavenText("Create and manage saved agent definitions used by Chat and Go.") { Name = "Agents.Subtitle", Level = TextLevel.Paragraph };
        Set(subtitle, HavenProperties.Foreground, "TextSecondary");
        titles.Add(subtitle);
        header.Add(titles);

        RefreshButton = new HavenButton { Name = "Agents.Refresh", Content = "Refresh", Variant = ButtonVariant.Secondary };
        RefreshButton.Accessibility.AccessibleName = "Refresh agents";
        Set(RefreshButton, HavenProperties.Column, 1);
        header.Add(RefreshButton);
        CreateToggleButton = new HavenButton { Name = "Agents.Create.Toggle", Content = "Create agent", Variant = ButtonVariant.Primary };
        CreateToggleButton.Accessibility.AccessibleName = "Create agent";
        Set(CreateToggleButton, HavenProperties.Column, 2);
        header.Add(CreateToggleButton);
        Root.Add(header);

        var runtimeNotice = new Container { Name = "Agents.RuntimeNotice", Layout = HavenLayout.Vertical };
        Set(runtimeNotice, HavenProperties.Row, 1);
        Set(runtimeNotice, HavenProperties.Width, HavenLength.Percent(100));
        Set(runtimeNotice, HavenProperties.Background, "SurfaceRaised");
        Set(runtimeNotice, HavenProperties.BorderColor, "Border");
        Set(runtimeNotice, HavenProperties.BorderWidth, HavenLength.Px(1));
        Set(runtimeNotice, HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(14)));
        Set(runtimeNotice, HavenProperties.Padding, HavenThickness.Parse("12px 14px"));
        Set(runtimeNotice, HavenProperties.Gap, HavenLength.Px(3));
        var runtimeTitle = new HavenText("Agent runtime") { Name = "Agents.RuntimeNotice.Title", Level = TextLevel.H4 };
        runtimeNotice.Add(runtimeTitle);
        ExecutionStatusText = new HavenText(runtime is null
            ? "Agent runtime is unavailable in this host."
            : "Ready. Enter a task, then run it with any enabled Agent.") { Name = "Agents.Execution.Status", Level = TextLevel.Paragraph };
        ExecutionStatusText.Accessibility.AccessibleName = "Agent execution status";
        Set(ExecutionStatusText, HavenProperties.Foreground, "TextSecondary");
        runtimeNotice.Add(ExecutionStatusText);
        RunTaskInput = InputField("Agents.Execution.Task", "Task for this agent");
        RunTaskInput.Multiline = true;
        RunTaskInput.SubmitOnEnter = false;
        Set(RunTaskInput, HavenProperties.MinHeight, HavenLength.Px(72));
        runtimeNotice.Add(RunTaskInput);
        RunResourceInput = InputField("Agents.Execution.Resource", "Optional resource, file, URL, or Haven item reference");
        runtimeNotice.Add(RunResourceInput);
        var runActions = new Container { Layout = HavenLayout.Horizontal };
        Set(runActions, HavenProperties.Gap, HavenLength.Px(8));
        CancelLatestButton = new HavenButton { Name = "Agents.Execution.Cancel", Content = "Cancel active run", Variant = ButtonVariant.Secondary };
        RetryLatestButton = new HavenButton { Name = "Agents.Execution.Retry", Content = "Retry latest", Variant = ButtonVariant.Secondary };
        CancelLatestButton.Accessibility.AccessibleName = "Cancel active agent run";
        RetryLatestButton.Accessibility.AccessibleName = "Retry latest agent run";
        runActions.Add(CancelLatestButton);
        runActions.Add(RetryLatestButton);
        runtimeNotice.Add(runActions);
        var recentTitle = new HavenText("Recent runs") { Level = TextLevel.H4 };
        runtimeNotice.Add(recentTitle);
        RecentRunsText = new HavenText("No Agent runs yet.") { Name = "Agents.Execution.RecentRuns", Level = TextLevel.Caption };
        RecentRunsText.Accessibility.AccessibleName = "Recent Agent runs";
        Set(RecentRunsText, HavenProperties.Foreground, "TextSecondary");
        runtimeNotice.Add(RecentRunsText);
        Root.Add(runtimeNotice);

        Creator = BuildCreator();
        Set(Creator, HavenProperties.Row, 2);
        Root.Add(Creator);

        StatusText = new HavenText { Name = "Agents.Status", Level = TextLevel.Caption };
        Set(StatusText, HavenProperties.Row, 3);
        Set(StatusText, HavenProperties.Foreground, "TextSecondary");
        Root.Add(StatusText);

        AgentCards = new DynamicUIRuntime { Name = "AgentCards", Layout = HavenLayout.Vertical };
        Set(AgentCards, HavenProperties.Row, 4);
        Set(AgentCards, HavenProperties.Width, HavenLength.Percent(100));
        Set(AgentCards, HavenProperties.Height, HavenLength.Percent(100));
        Set(AgentCards, HavenProperties.Gap, HavenLength.Px(10));
        Set(AgentCards, HavenProperties.Overflow, HavenOverflow.Scroll);
        Root.Add(AgentCards);

        var templates = HavenDynamicUITemplateCatalog.FromAssembly(typeof(AgentsHavenScene).Assembly);
        _dynamicUi = new DynamicUI(Root, templates);

        RefreshButton.Invoked += OnRefreshInvoked;
        CreateToggleButton.Invoked += OnCreateToggleInvoked;
        BuildWithAiButton.Invoked += OnBuildWithAiInvoked;
        SaveButton.Invoked += OnSaveInvoked;
        BuilderPromptInput.Invalidated += OnDraftInvalidated;
        NameInput.Invalidated += OnDraftInvalidated;
        DescriptionInput.Invalidated += OnDraftInvalidated;
        InstructionsInput.Invalidated += OnDraftInvalidated;
        ModelInput.Invalidated += OnDraftInvalidated;
        CapabilitiesInput.Invalidated += OnDraftInvalidated;
        PermissionProfileInput.Invalidated += OnDraftInvalidated;
        SandboxProfileInput.Invalidated += OnDraftInvalidated;
        KnowledgeResourcesInput.Invalidated += OnDraftInvalidated;
        MemoryModeInput.Invalidated += OnDraftInvalidated;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.Items.CollectionChanged += OnItemsCollectionChanged;
        CancelLatestButton.Invoked += OnCancelLatestInvoked;
        RetryLatestButton.Invoked += OnRetryLatestInvoked;
        if (_runtime is not null) _runtime.RunChanged += OnRunChanged;

        SyncDraftFromViewModel();
        RefreshChrome();
        RefreshCards();
        _ = RefreshRunsAsync();
    }

    public Page Root { get; }
    public HavenButton RefreshButton { get; }
    public HavenButton CreateToggleButton { get; }
    public Container Creator { get; }
    public Input BuilderPromptInput { get; private set; } = null!;
    public HavenButton BuildWithAiButton { get; private set; } = null!;
    public Input NameInput { get; private set; } = null!;
    public Input DescriptionInput { get; private set; } = null!;
    public Input InstructionsInput { get; private set; } = null!;
    public Input ModelInput { get; private set; } = null!;
    public Input CapabilitiesInput { get; private set; } = null!;
    public Input PermissionProfileInput { get; private set; } = null!;
    public Input SandboxProfileInput { get; private set; } = null!;
    public Input KnowledgeResourcesInput { get; private set; } = null!;
    public Input MemoryModeInput { get; private set; } = null!;
    public HavenButton SaveButton { get; private set; } = null!;
    public HavenText StatusText { get; }
    public HavenText ExecutionStatusText { get; }
    public Input RunTaskInput { get; }
    public Input RunResourceInput { get; }
    public HavenButton CancelLatestButton { get; }
    public HavenButton RetryLatestButton { get; }
    public HavenText RecentRunsText { get; }
    public DynamicUIRuntime AgentCards { get; }

    private Container BuildCreator()
    {
        var panel = new Container { Name = "Agents.Creator", Layout = HavenLayout.Vertical };
        Set(panel, HavenProperties.Width, HavenLength.Percent(100));
        Set(panel, HavenProperties.Background, "SurfaceRaised");
        Set(panel, HavenProperties.BorderColor, "Border");
        Set(panel, HavenProperties.BorderWidth, HavenLength.Px(1));
        Set(panel, HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(18)));
        Set(panel, HavenProperties.Padding, HavenThickness.Parse("16px"));
        Set(panel, HavenProperties.Gap, HavenLength.Px(10));
        Set(panel, HavenProperties.Shadow, "Card");

        var builderRow = new Container { Layout = HavenLayout.Grid, Columns = "1fr Auto", Rows = "auto" };
        Set(builderRow, HavenProperties.Gap, HavenLength.Px(8));
        BuilderPromptInput = InputField("Agents.Creator.BuilderPrompt", "Describe the assistant you want Haven to create");
        builderRow.Add(BuilderPromptInput);
        BuildWithAiButton = new HavenButton { Name = "Agents.Creator.BuildWithAi", Content = "Build with AI", Variant = ButtonVariant.Secondary };
        BuildWithAiButton.Accessibility.AccessibleName = "Draft agent instructions with AI";
        Set(BuildWithAiButton, HavenProperties.Column, 1);
        builderRow.Add(BuildWithAiButton);
        panel.Add(builderRow);

        var identityRow = new Container { Layout = HavenLayout.Grid, Columns = "1fr 1.5fr", Rows = "auto" };
        Set(identityRow, HavenProperties.Gap, HavenLength.Px(8));
        NameInput = InputField("Agents.Creator.Name", "Name");
        identityRow.Add(NameInput);
        DescriptionInput = InputField("Agents.Creator.Description", "Short description");
        Set(DescriptionInput, HavenProperties.Column, 1);
        identityRow.Add(DescriptionInput);
        panel.Add(identityRow);

        InstructionsInput = InputField("Agents.Creator.Instructions", "System instructions");
        InstructionsInput.Multiline = true;
        InstructionsInput.SubmitOnEnter = false;
        Set(InstructionsInput, HavenProperties.MinHeight, HavenLength.Px(112));
        panel.Add(InstructionsInput);

        CapabilitiesInput = InputField("Agents.Creator.Capabilities", "Capability keys, comma-separated (for example web-search)");
        panel.Add(CapabilitiesInput);
        var permissionNote = new HavenText("Capability access is an allowlist. Haven's global sandbox and permission settings always remain authoritative.") { Level = TextLevel.Caption };
        Set(permissionNote, HavenProperties.Foreground, "TextSecondary");
        panel.Add(permissionNote);

        PermissionProfileInput = InputField("Agents.Creator.PermissionProfile", "Permission profile reference (optional)");
        SandboxProfileInput = InputField("Agents.Creator.SandboxProfile", "Sandbox profile reference (optional)");
        KnowledgeResourcesInput = InputField("Agents.Creator.KnowledgeResources", "Knowledge/resource references, comma-separated");
        MemoryModeInput = InputField("Agents.Creator.MemoryMode", "Memory default: session, persistent, or none");
        panel.Add(PermissionProfileInput); panel.Add(SandboxProfileInput); panel.Add(KnowledgeResourcesInput); panel.Add(MemoryModeInput);

        var footer = new Container { Layout = HavenLayout.Grid, Columns = "Auto 1fr Auto", Rows = "auto" };
        Set(footer, HavenProperties.Gap, HavenLength.Px(8));
        var modelLabel = new HavenText("Preferred model") { Level = TextLevel.Caption };
        Set(modelLabel, HavenProperties.Foreground, "TextSecondary");
        Set(modelLabel, HavenProperties.VerticalAlignment, HavenVerticalAlignment.Center);
        footer.Add(modelLabel);
        ModelInput = InputField("Agents.Creator.Model", "Use current/default model");
        Set(ModelInput, HavenProperties.Column, 1);
        footer.Add(ModelInput);
        SaveButton = new HavenButton { Name = "Agents.Creator.Save", Content = "Create agent", Variant = ButtonVariant.Primary };
        SaveButton.Accessibility.AccessibleName = "Create agent";
        Set(SaveButton, HavenProperties.Column, 2);
        footer.Add(SaveButton);
        panel.Add(footer);
        return panel;
    }

    internal async Task RefreshAsync()
    {
        await _viewModel.RefreshCommand.ExecuteAsync();
        RefreshChrome();
        RefreshCards();
    }

    internal async Task DraftAgentAsync()
    {
        SyncDraftToViewModel();
        await _viewModel.BuildWithAiCommand.ExecuteAsync();
        SyncDraftFromViewModel();
        RefreshChrome();
    }

    internal async Task CreateAgentAsync()
    {
        SyncDraftToViewModel();
        if (_viewModel.IsEditingAgent)
            await _viewModel.SaveAgentEditsAsync();
        else
            await _viewModel.CreateCommand.ExecuteAsync();
        SyncDraftFromViewModel();
        RefreshChrome();
        RefreshCards();
    }

    internal async Task<bool> EditAgentAsync(CatalogCardViewModel card)
    {
        _pendingDeleteId = null;
        UpdateDeleteLabels();
        var opened = await _viewModel.BeginAgentEditAsync(card);
        if (!opened) return false;
        SyncDraftFromViewModel();
        RefreshChrome();
        return true;
    }

    internal async Task DuplicateAgentAsync(CatalogCardViewModel card)
    {
        _pendingDeleteId = null;
        UpdateDeleteLabels();
        await _viewModel.DuplicateCommand.ExecuteAsync(card);
        RefreshCards();
    }

    internal async Task<bool> DeleteAgentAsync(CatalogCardViewModel card)
    {
        if (card.IsBuiltIn) return false;
        if (_pendingDeleteId != card.Id)
        {
            _pendingDeleteId = card.Id;
            UpdateDeleteLabels();
            return false;
        }

        _pendingDeleteId = null;
        await _viewModel.DeleteCommand.ExecuteAsync(card);
        RefreshCards();
        return true;
    }

    private void RefreshCards()
    {
        var expected = _viewModel.Items.Select(card => card.Id.ToString("N")).ToHashSet(StringComparer.Ordinal);
        foreach (var stale in AgentCards.Items.Where(item => !expected.Contains(item.InstanceID)).Select(item => item.InstanceID).ToArray())
            _dynamicUi.DeleteItem("AgentCards", stale);

        for (var index = 0; index < _viewModel.Items.Count; index++)
        {
            var card = _viewModel.Items[index];
            var id = card.Id.ToString("N");
            if (!_dynamicUi.TryGetItem("AgentCards", id, out var item))
            {
                item = _dynamicUi.CreateItem("AgentCatalogCard", "AgentCards", id, ValuesFor(card), index);
                WireCard(item, card);
            }
            else
            {
                item.SetVariables(ValuesFor(card));
                var currentIndex = AgentCards.Items.ToList().IndexOf(item);
                if (currentIndex != index) _dynamicUi.MoveItem("AgentCards", id, index);
            }

            UpdateCardAccessibility(item, card);
            Visible(item.GetComponent<HavenButton>("Edit"), !card.IsBuiltIn);
            Visible(item.GetComponent<HavenButton>("Delete"), !card.IsBuiltIn);
        }
        RefreshChrome();
    }

    private void WireCard(DynamicUIItem item, CatalogCardViewModel card)
    {
        var edit = item.GetComponent<HavenButton>("Edit");
        edit.Invoked += async (_, _) => await EditAgentAsync(card);

        var duplicate = item.GetComponent<HavenButton>("Duplicate");
        duplicate.Invoked += async (_, _) => await DuplicateAgentAsync(card);

        var run = item.GetComponent<HavenButton>("Run");
        run.Invoked += async (_, _) => await RunAgentAsync(card);
        var toggle = item.GetComponent<HavenButton>("Toggle");
        toggle.Invoked += async (_, _) => await ToggleAgentAsync(card);
        var delete = item.GetComponent<HavenButton>("Delete");
        delete.Invoked += async (_, _) => await DeleteAgentAsync(card);
    }

    private static void UpdateCardAccessibility(DynamicUIItem item, CatalogCardViewModel card)
    {
        item.GetComponent<HavenButton>("Run").Accessibility.AccessibleName = "Run " + card.Name;
        item.GetComponent<HavenButton>("Toggle").Accessibility.AccessibleName = (card.IsEnabled ? "Disable " : "Enable ") + card.Name;
        item.GetComponent<HavenButton>("Edit").Accessibility.AccessibleName = "Edit " + card.Name;
        item.GetComponent<HavenButton>("Duplicate").Accessibility.AccessibleName = "Duplicate " + card.Name;
        item.GetComponent<HavenButton>("Delete").Accessibility.AccessibleName = "Delete " + card.Name;
    }

    private Dictionary<string, object?> ValuesFor(CatalogCardViewModel card) => new(StringComparer.Ordinal)
    {
        ["NAME"] = card.Name,
        ["DESCRIPTION"] = card.Description,
        ["MODEL"] = string.IsNullOrWhiteSpace(card.Meta) ? "default" : card.Meta,
        ["BADGE"] = card.IsBuiltIn ? (card.IsEnabled ? "BUILT-IN" : "BUILT-IN · OFF") : (card.IsEnabled ? "CUSTOM" : "CUSTOM · OFF"),
        ["ENABLE_LABEL"] = card.IsEnabled ? "Disable" : "Enable",
        ["DELETE_LABEL"] = _pendingDeleteId == card.Id ? "Confirm delete" : "Delete"
    };

    internal async Task<AgentRun?> RunAgentAsync(CatalogCardViewModel card)
    {
        if (_runtime is null)
        {
            ExecutionStatusText.Content = "Agent runtime is unavailable in this host.";
            return null;
        }
        if (!card.IsEnabled)
        {
            ExecutionStatusText.Content = $"{card.Name} is disabled. Enable it before running.";
            return null;
        }
        if (string.IsNullOrWhiteSpace(RunTaskInput.Text))
        {
            ExecutionStatusText.Content = "Enter a task before starting the Agent.";
            return null;
        }

        ExecutionStatusText.Content = $"Starting {card.Name}…";
        var run = await _runtime.RunAsync(card.Id, RunTaskInput.Text.Trim(), CancellationToken.None, resourceReference: RunResourceInput.Text);
        _latestRun = run;
        _recentRuns = [run, .. _recentRuns.Where(item => item.Id != run.Id).Take(7)];
        RefreshExecutionStatus();
        return run;
    }

    internal async Task ToggleAgentAsync(CatalogCardViewModel card)
    {
        await _viewModel.SetAgentEnabledAsync(card, !card.IsEnabled);
        RefreshCards();
    }

    internal async Task RefreshRunsAsync()
    {
        if (_runtime is null)
        {
            RefreshExecutionStatus();
            return;
        }
        ExecutionStatusText.Content = "Loading Agent run history…";
        var recent = await _runtime.GetRecentAsync(8, CancellationToken.None);
        _recentRuns = recent;
        _latestRun = recent.FirstOrDefault();
        RefreshExecutionStatus();
    }

    private void RefreshExecutionStatus()
    {
        if (_runtime is null)
        {
            ExecutionStatusText.Content = "Agent runtime is unavailable in this host.";
            Enabled(CancelLatestButton, false);
            Enabled(RetryLatestButton, false);
            RefreshRecentRunsText();
            return;
        }

        if (_latestRun is null)
        {
            ExecutionStatusText.Content = "Ready. Enter a task, then run it with any enabled Agent.";
            Enabled(CancelLatestButton, false);
            Enabled(RetryLatestButton, false);
            RefreshRecentRunsText();
            return;
        }

        var detail = _latestRun.Status switch
        {
            AgentRunStatus.Completed => string.IsNullOrWhiteSpace(_latestRun.Result) ? "Completed." : _latestRun.Result,
            AgentRunStatus.Failed => "Failed · " + _latestRun.Error,
            AgentRunStatus.Cancelled => "Cancelled.",
            AgentRunStatus.Running => "Running…",
            _ => "Queued…"
        };
        var resource = string.IsNullOrWhiteSpace(_latestRun.ResourceReference) ? string.Empty : $" · {_latestRun.ResourceReference}";
        ExecutionStatusText.Content = $"{_latestRun.AgentName} · {_latestRun.Status} · {_latestRun.ProgressPercent}%{resource} · {detail}";
        Enabled(CancelLatestButton, _latestRun.Status is AgentRunStatus.Queued or AgentRunStatus.Running);
        Enabled(RetryLatestButton, _latestRun.Status is AgentRunStatus.Completed or AgentRunStatus.Failed or AgentRunStatus.Cancelled);
        RefreshRecentRunsText();
    }

    private void RefreshRecentRunsText()
    {
        RecentRunsText.Content = _recentRuns.Count == 0
            ? "No Agent runs yet."
            : string.Join(Environment.NewLine, _recentRuns.Take(6).Select(run =>
                $"{run.AgentName} · {run.Status} · {run.ProgressPercent}% · {Short(run.Task, 72)}"));
    }

    private static string Short(string value, int limit) => value.Length <= limit ? value : value[..(limit - 1)] + "…";

    private void OnRunChanged(AgentRun run)
    {
        _latestRun = run;
        RefreshExecutionStatus();
    }

    private void OnCancelLatestInvoked(object? sender, EventArgs e)
    {
        if (_runtime is not null && _latestRun is not null) _runtime.Cancel(_latestRun.Id);
    }

    private async void OnRetryLatestInvoked(object? sender, EventArgs e)
    {
        if (_runtime is null || _latestRun is null) return;
        _latestRun = await _runtime.RetryAsync(_latestRun.Id, CancellationToken.None);
        RefreshExecutionStatus();
    }

    private void UpdateDeleteLabels()
    {
        foreach (var card in _viewModel.Items)
            if (_dynamicUi.TryGetItem("AgentCards", card.Id.ToString("N"), out var item))
                item.SetVariable("DELETE_LABEL", _pendingDeleteId == card.Id ? "Confirm delete" : "Delete");
    }

    private void RefreshChrome()
    {
        StatusText.Content = _viewModel.Status;
        Visible(Creator, _viewModel.IsCreating);
        CreateToggleButton.Content = _viewModel.IsCreating
            ? _viewModel.IsEditingAgent ? "Cancel edit" : "Close creator"
            : "Create agent";
        SaveButton.Content = _viewModel.IsEditingAgent ? "Save changes" : "Create agent";
        SaveButton.Accessibility.AccessibleName = SaveButton.Content;
        Enabled(BuildWithAiButton, _viewModel.BuildWithAiCommand.CanExecute(null));
        Enabled(SaveButton, _viewModel.CreateCommand.CanExecute(null));
        StatusText.SetValue(HavenProperties.Visibility, string.IsNullOrWhiteSpace(_viewModel.Status) ? HavenVisibility.Collapsed : HavenVisibility.Visible);
    }

    private void SyncDraftToViewModel()
    {
        _viewModel.BuilderPrompt = BuilderPromptInput.Text;
        _viewModel.NewName = NameInput.Text;
        _viewModel.NewDescription = DescriptionInput.Text;
        _viewModel.NewInstructions = InstructionsInput.Text;
        _viewModel.NewModel = ModelInput.Text;
        _viewModel.NewCapabilities = CapabilitiesInput.Text;
        _viewModel.NewPermissionProfile = PermissionProfileInput.Text;
        _viewModel.NewSandboxProfile = SandboxProfileInput.Text;
        _viewModel.NewKnowledgeResources = KnowledgeResourcesInput.Text;
        _viewModel.NewMemoryMode = MemoryModeInput.Text;
    }

    private void SyncDraftFromViewModel()
    {
        BuilderPromptInput.Invalidated -= OnDraftInvalidated;
        NameInput.Invalidated -= OnDraftInvalidated;
        DescriptionInput.Invalidated -= OnDraftInvalidated;
        InstructionsInput.Invalidated -= OnDraftInvalidated;
        ModelInput.Invalidated -= OnDraftInvalidated;
        CapabilitiesInput.Invalidated -= OnDraftInvalidated;
        PermissionProfileInput.Invalidated -= OnDraftInvalidated; SandboxProfileInput.Invalidated -= OnDraftInvalidated; KnowledgeResourcesInput.Invalidated -= OnDraftInvalidated; MemoryModeInput.Invalidated -= OnDraftInvalidated;
        try
        {
            SyncText(BuilderPromptInput, _viewModel.BuilderPrompt);
            SyncText(NameInput, _viewModel.NewName);
            SyncText(DescriptionInput, _viewModel.NewDescription);
            SyncText(InstructionsInput, _viewModel.NewInstructions);
            SyncText(ModelInput, _viewModel.NewModel);
            SyncText(CapabilitiesInput, _viewModel.NewCapabilities);
            SyncText(PermissionProfileInput, _viewModel.NewPermissionProfile); SyncText(SandboxProfileInput, _viewModel.NewSandboxProfile); SyncText(KnowledgeResourcesInput, _viewModel.NewKnowledgeResources); SyncText(MemoryModeInput, _viewModel.NewMemoryMode);
        }
        finally
        {
            BuilderPromptInput.Invalidated += OnDraftInvalidated;
            NameInput.Invalidated += OnDraftInvalidated;
            DescriptionInput.Invalidated += OnDraftInvalidated;
            InstructionsInput.Invalidated += OnDraftInvalidated;
            ModelInput.Invalidated += OnDraftInvalidated;
            CapabilitiesInput.Invalidated += OnDraftInvalidated;
            PermissionProfileInput.Invalidated += OnDraftInvalidated; SandboxProfileInput.Invalidated += OnDraftInvalidated; KnowledgeResourcesInput.Invalidated += OnDraftInvalidated; MemoryModeInput.Invalidated += OnDraftInvalidated;
        }
    }

    private static void SyncText(Input input, string value)
    {
        if (!string.Equals(input.Text, value, StringComparison.Ordinal)) input.Text = value;
    }

    private void OnDraftInvalidated(object? sender, EventArgs e)
    {
        SyncDraftToViewModel();
        RefreshChrome();
    }

    private async void OnRefreshInvoked(object? sender, EventArgs e) => await RefreshAsync();
    private void OnCreateToggleInvoked(object? sender, EventArgs e)
    {
        if (_viewModel.IsEditingAgent)
        {
            _viewModel.CancelAgentEdit();
            SyncDraftFromViewModel();
            RefreshChrome();
            return;
        }

        _viewModel.ToggleCreateCommand.Execute(null);
    }
    private async void OnBuildWithAiInvoked(object? sender, EventArgs e) => await DraftAgentAsync();
    private async void OnSaveInvoked(object? sender, EventArgs e) => await CreateAgentAsync();
    private void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => RefreshCards();
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e) => RefreshChrome();

    private static Input InputField(string name, string placeholder)
    {
        var input = new Input { Name = name, Placeholder = placeholder, SubmitOnEnter = false };
        input.Accessibility.AccessibleName = placeholder;
        Set(input, HavenProperties.Width, HavenLength.Percent(100));
        return input;
    }

    private static void Visible(HavenElement element, bool visible) =>
        Set(element, HavenProperties.Visibility, visible ? HavenVisibility.Visible : HavenVisibility.Collapsed);

    private static void Enabled(HavenElement element, bool enabled) => Set(element, HavenProperties.Enabled, enabled);
    private static void Set<T>(HavenElement element, HavenProperty<T> property, T value) => element.SetValue(property, value);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel.Items.CollectionChanged -= OnItemsCollectionChanged;
        if (_runtime is not null) _runtime.RunChanged -= OnRunChanged;
        _dynamicUi.Clear("AgentCards");
    }
}
