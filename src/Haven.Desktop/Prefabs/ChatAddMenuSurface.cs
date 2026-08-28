using Haven.Core;
using Haven.Desktop.Views.Shell.TopRail;
using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;
using HavenText = Haven.UI.Components.Text;

namespace Haven.Desktop.Prefabs;

/// <summary>
/// Shared behavior for the Chatbox Add popup. The visual tree is owned by the
/// ChatAddMenu prefab so Go and Chat cannot drift into separate popup designs.
/// </summary>
internal sealed class ChatAddMenuSurface : IDisposable
{
    private readonly List<(HavenButton Button, AddMenu.AddMenuAction Action, object Item)> _catalogRows = [];
    private readonly bool _composerOwnsSearch;
    private readonly bool _showThreadSettings;
    private readonly bool _showMultipleResponses;
    private IReadOnlyList<AgentDefinition> _agents = [];
    private IReadOnlyList<CapabilityDefinition> _capabilities = [];
    private IReadOnlyList<PromptDefinition> _instructions = [];
    private IReadOnlyList<ModeDefinition> _apps = [];
    private AddMenu.AddMenuAction? _catalogAction;
    private string _lastSearch = string.Empty;
    private string _externalQuery = string.Empty;
    private bool _searchAll;
    private string _currentAgentName = "No Agent (Default)";
    private IReadOnlyList<string> _multipleResponseModelKeys = [];
    private HashSet<string> _selectedMultipleResponseModels = new(StringComparer.OrdinalIgnoreCase);
    private ChatActionMode _currentActionMode = ChatActionMode.AllowBasicActions;
    private GenerativeUiResponseMode _currentVisualMode = GenerativeUiResponseMode.Auto;
    private bool _disposed;

    public ChatAddMenuSurface(
        Prefab prefab,
        bool composerOwnsSearch = false,
        bool showThreadSettings = true,
        bool showMultipleResponses = false)
    {
        Prefab = prefab ?? throw new ArgumentNullException(nameof(prefab));
        _composerOwnsSearch = composerOwnsSearch;
        _showThreadSettings = showThreadSettings;
        _showMultipleResponses = showMultipleResponses;
        Overlay = prefab.GetComponent<Container>("AddOverlay");
        DismissButton = prefab.GetComponent<HavenButton>("Dismiss");
        MainMenu = prefab.GetComponent<Container>("MainMenu");
        CatalogPanel = prefab.GetComponent<Container>("CatalogPanel");
        CatalogSearch = prefab.GetComponent<Input>("CatalogSearch");
        CatalogResults = prefab.GetComponent<Container>("CatalogResults");
        MenuTitle = prefab.GetComponent<HavenText>("MenuTitle");
        ToolsHeading = prefab.GetComponent<HavenText>("ToolsHeading");
        OptionsHeading = prefab.GetComponent<HavenText>("OptionsHeading");
        AttachFilesButton = prefab.GetComponent<HavenButton>("AttachFiles");
        AgentsButton = prefab.GetComponent<HavenButton>("Agents");
        InstructionsButton = prefab.GetComponent<HavenButton>("Instructions");
        CapabilitiesButton = prefab.GetComponent<HavenButton>("Capabilities");
        AppsButton = prefab.GetComponent<HavenButton>("Apps");
        AllowActionsButton = prefab.GetComponent<HavenButton>("AllowActions");
        VisualResponsesButton = prefab.GetComponent<HavenButton>("VisualResponses");
        MultipleResponsesButton = prefab.GetComponent<HavenButton>("MultipleResponses");
        CurrentResponseState = prefab.GetComponent<HavenText>("CurrentResponseState");

        if (_composerOwnsSearch)
        {
            CatalogSearch.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
            MenuTitle.Content = "Attach to Chat";
            ToolsHeading.Content = "Browse";
            InstructionsButton.Content = "Skills";
            CapabilitiesButton.Content = "Capabilities / Plugins";
            AttachFilesButton.Content = "Files";

            var tools = prefab.GetComponent<Container>("Tools");
            foreach (var button in new[] { AgentsButton, InstructionsButton, CapabilitiesButton, AppsButton })
                tools.Remove(button);
            MainMenu.Remove(AttachFilesButton);
            tools.Add(AttachFilesButton);
            tools.Add(AppsButton);
            tools.Add(CapabilitiesButton);
            tools.Add(InstructionsButton);
            tools.Add(AgentsButton);
        }
        MultipleResponsesButton.SetValue(
            HavenProperties.Visibility,
            _showMultipleResponses ? HavenVisibility.Visible : HavenVisibility.Collapsed);
        if (!_showThreadSettings)
        {
            OptionsHeading.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
            AllowActionsButton.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
            VisualResponsesButton.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
            CurrentResponseState.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
        }

        DismissButton.Invoked += OnDismissInvoked;
        AttachFilesButton.Invoked += OnAttachFilesInvoked;
        AgentsButton.Invoked += OnAgentsInvoked;
        InstructionsButton.Invoked += OnInstructionsInvoked;
        CapabilitiesButton.Invoked += OnCapabilitiesInvoked;
        AppsButton.Invoked += OnAppsInvoked;
        AllowActionsButton.Invoked += OnAllowActionsInvoked;
        VisualResponsesButton.Invoked += OnVisualResponsesInvoked;
        MultipleResponsesButton.Invoked += OnMultipleResponsesInvoked;
        if (!_composerOwnsSearch) CatalogSearch.Invalidated += OnCatalogSearchInvalidated;
        Hide();
    }

    public Prefab Prefab { get; }
    public Container Overlay { get; }
    public HavenButton DismissButton { get; }
    public Container MainMenu { get; }
    public Container CatalogPanel { get; }
    public Input CatalogSearch { get; }
    public Container CatalogResults { get; }
    public HavenText MenuTitle { get; }
    public HavenText ToolsHeading { get; }
    public HavenText OptionsHeading { get; }
    public HavenButton AttachFilesButton { get; }
    public HavenButton AgentsButton { get; }
    public HavenButton InstructionsButton { get; }
    public HavenButton CapabilitiesButton { get; }
    public HavenButton AppsButton { get; }
    public HavenButton AllowActionsButton { get; }
    public HavenButton VisualResponsesButton { get; }
    public HavenButton MultipleResponsesButton { get; }
    public HavenText CurrentResponseState { get; }

    public event EventHandler<AddMenu.AddMenuAction>? AddActionSelected;
    public event EventHandler<AddMenuSelection>? CatalogItemSelected;
    public event EventHandler? MultipleResponsesRequested;
    public event EventHandler<string>? MultipleResponseModelToggled;

    public void ShowMultipleResponseModels(
        IReadOnlyList<string> modelKeys,
        IReadOnlyCollection<string> selectedModelKeys)
    {
        ArgumentNullException.ThrowIfNull(modelKeys);
        ArgumentNullException.ThrowIfNull(selectedModelKeys);
        _catalogAction = null;
        _searchAll = false;
        _externalQuery = string.Empty;
        _multipleResponseModelKeys = modelKeys
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        _selectedMultipleResponseModels = new HashSet<string>(selectedModelKeys, StringComparer.OrdinalIgnoreCase);
        CatalogSearch.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
        CatalogPanel.SetValue(HavenProperties.Visibility, HavenVisibility.Visible);
        Overlay.SetValue(HavenProperties.Visibility, HavenVisibility.Visible);
        RebuildMultipleResponseModels();
    }

    public void SetCatalogue(
        IReadOnlyList<AgentDefinition> agents,
        IReadOnlyList<CapabilityDefinition> capabilities,
        IReadOnlyList<PromptDefinition> instructions,
        IReadOnlyList<ModeDefinition> apps)
    {
        _agents = (agents ?? []).Where(item => item.IsEnabled).OrderBy(item => item.Name).ToArray();
        _capabilities = (capabilities ?? []).Where(item => item.IsEnabled && item.IsAttachable).OrderBy(item => item.Name).ToArray();
        _instructions = (instructions ?? []).Where(item => item.IsEnabled).OrderBy(item => item.Name).ToArray();
        _apps = (apps ?? []).Where(item => item.IsEnabled).OrderBy(item => item.Name).ToArray();
        if (_catalogAction is not null || _searchAll) RebuildCatalogue();
    }

    public void SetResponseState(string agentName, ChatActionMode actionMode, GenerativeUiResponseMode visualMode)
    {
        _currentAgentName = string.IsNullOrWhiteSpace(agentName) ? "No Agent (Default)" : agentName.Trim();
        _currentActionMode = actionMode;
        _currentVisualMode = visualMode;
        var agent = _currentAgentName.Equals("No Agent (Default)", StringComparison.OrdinalIgnoreCase)
            ? "Default agent"
            : _currentAgentName;
        CurrentResponseState.Content = $"{agent} · {ActionModeSummary(_currentActionMode)} · {VisualModeSummary(_currentVisualMode)}";
        AgentsButton.Accessibility.Description = "Current agent: " + _currentAgentName;
        AllowActionsButton.Accessibility.Description = "Current setting: " + ActionModeSummary(_currentActionMode);
        VisualResponsesButton.Accessibility.Description = "Current setting: " + VisualModeSummary(_currentVisualMode);
        if (_catalogAction is not null || _searchAll) RebuildCatalogue();
    }

    public void Show()
    {
        _catalogAction = null;
        _searchAll = false;
        _externalQuery = string.Empty;
        CatalogPanel.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
        Overlay.SetValue(HavenProperties.Visibility, HavenVisibility.Visible);
    }

    public void Hide()
    {
        _catalogAction = null;
        _searchAll = false;
        _lastSearch = string.Empty;
        _externalQuery = string.Empty;
        CatalogSearch.Text = string.Empty;
        CatalogPanel.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
        Overlay.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
    }

    /// <summary>Shows the Attach catalogue filtered by the inline Chat composer @ query.</summary>
    public void ShowMentionSearch(string? query)
    {
        if (!_composerOwnsSearch)
        {
            Show();
            FilterCatalogue(query);
            return;
        }

        _catalogAction = null;
        _searchAll = true;
        _externalQuery = query?.Trim() ?? string.Empty;
        CatalogSearch.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
        CatalogPanel.SetValue(HavenProperties.Visibility, HavenVisibility.Visible);
        Overlay.SetValue(HavenProperties.Visibility, HavenVisibility.Visible);
        RebuildCatalogue();
    }

    public void FilterCatalogue(string? query)
    {
        if (_composerOwnsSearch)
        {
            ShowMentionSearch(query);
            return;
        }
        CatalogSearch.Text = query ?? string.Empty;
        RebuildCatalogue();
    }

    private void OpenCatalogue(AddMenu.AddMenuAction action)
    {
        _catalogAction = action;
        _searchAll = false;
        _externalQuery = string.Empty;
        _lastSearch = string.Empty;
        CatalogSearch.Text = string.Empty;
        CatalogSearch.Placeholder = action == AddMenu.AddMenuAction.Agent ? "Search Agents" : "Search";
        CatalogSearch.SetValue(
            HavenProperties.Visibility,
            !_composerOwnsSearch && action is not (AddMenu.AddMenuAction.AllowActions or AddMenu.AddMenuAction.VisualResponses)
                ? HavenVisibility.Visible
                : HavenVisibility.Collapsed);
        CatalogPanel.SetValue(HavenProperties.Visibility, HavenVisibility.Visible);
        RebuildCatalogue();
    }

    private void RebuildCatalogue()
    {
        foreach (var child in CatalogResults.Children.ToArray()) CatalogResults.Remove(child);
        _catalogRows.Clear();
        var query = (_composerOwnsSearch ? _externalQuery : CatalogSearch.Text).Trim();
        if (_searchAll)
        {
            RebuildMentionSearch(query);
            return;
        }
        if (_catalogAction is null) return;
        switch (_catalogAction.Value)
        {
            case AddMenu.AddMenuAction.Agent:
                AddCatalogHeading("Current: " + _currentAgentName);
                AddCatalogRows(AddMenu.AddMenuAction.Agent, _agents.Where(item => Matches(item.Name, item.Description, query)), item => item.Name, item => item.Description);
                break;
            case AddMenu.AddMenuAction.Capability:
                AddCatalogRows(AddMenu.AddMenuAction.Capability, _capabilities.Where(item => Matches(item.Name, item.Description, query)), item => item.Name, item => item.Description);
                break;
            case AddMenu.AddMenuAction.Instruction:
                AddCatalogRows(AddMenu.AddMenuAction.Instruction, _instructions.Where(item => Matches(item.Name, item.Description, query)), item => item.Name, item => item.Description);
                break;
            case AddMenu.AddMenuAction.App:
                AddCatalogRows(AddMenu.AddMenuAction.App, _apps.Where(item => Matches(item.Name, item.Description, query)), item => item.Name, item => item.Description);
                break;
            case AddMenu.AddMenuAction.AllowActions:
                AddModeRow("Allow All Actions", AddMenu.AddMenuAction.AllowActions, ChatActionMode.AllowAllActions);
                AddModeRow("Allow Basic Actions (Default)", AddMenu.AddMenuAction.AllowActions, ChatActionMode.AllowBasicActions);
                AddModeRow("Just Chat", AddMenu.AddMenuAction.AllowActions, ChatActionMode.JustChat);
                break;
            case AddMenu.AddMenuAction.VisualResponses:
                AddModeRow("Always Visual", AddMenu.AddMenuAction.VisualResponses, GenerativeUiResponseMode.AlwaysVisual);
                AddModeRow("Prefer Visual", AddMenu.AddMenuAction.VisualResponses, GenerativeUiResponseMode.PreferVisual);
                AddModeRow("Auto (Default)", AddMenu.AddMenuAction.VisualResponses, GenerativeUiResponseMode.Auto);
                AddModeRow("Prefer Text", AddMenu.AddMenuAction.VisualResponses, GenerativeUiResponseMode.PreferText);
                AddModeRow("Always Text", AddMenu.AddMenuAction.VisualResponses, GenerativeUiResponseMode.AlwaysText);
                break;
        }
    }

    private void AddCatalogRows<T>(
        AddMenu.AddMenuAction action,
        IEnumerable<T> items,
        Func<T, string> label,
        Func<T, string> description)
        where T : notnull
    {
        foreach (var item in items)
        {
            var button = MenuButton(label(item), 284);
            button.Accessibility.Description = description(item);
            var boxed = (object)item;
            button.Invoked += (_, _) => SelectCatalogue(new AddMenuSelection(action, boxed));
            _catalogRows.Add((button, action, boxed));
            CatalogResults.Add(button);
        }
    }

    private void AddModeRow(string label, AddMenu.AddMenuAction action, object item)
    {
        var button = MenuButton(label, 284);
        button.Invoked += (_, _) => SelectCatalogue(new AddMenuSelection(action, item));
        _catalogRows.Add((button, action, item));
        CatalogResults.Add(button);
    }

    private void AddCatalogHeading(string text)
    {
        var heading = new HavenText(text) { Level = TextLevel.Caption };
        heading.SetValue(HavenProperties.FontWeight, 700);
        heading.SetValue(HavenProperties.Foreground, "TextSecondary");
        CatalogResults.Add(heading);
    }

    private void RebuildMultipleResponseModels()
    {
        foreach (var child in CatalogResults.Children.ToArray()) CatalogResults.Remove(child);
        _catalogRows.Clear();
        AddCatalogHeading($"Select 2 or more models · {_selectedMultipleResponseModels.Count} selected");
        foreach (var modelKey in _multipleResponseModelKeys)
        {
            var selected = _selectedMultipleResponseModels.Contains(modelKey);
            var button = MenuButton((selected ? "✓ " : string.Empty) + modelKey, 284);
            button.Accessibility.AccessibleName = selected
                ? $"{modelKey} selected for Multiple Responses. Remove"
                : $"Add {modelKey} to Multiple Responses";
            button.Invoked += (_, _) => MultipleResponseModelToggled?.Invoke(this, modelKey);
            CatalogResults.Add(button);
        }
        if (_multipleResponseModelKeys.Count == 0)
            AddCatalogHeading("No installed models are available.");
    }

    private void RebuildMentionSearch(string query)
    {
        var candidates = new List<(AddMenu.AddMenuAction Action, object Item, string Name, string Description, int Score)>();

        void AddCandidates<T>(AddMenu.AddMenuAction action, IEnumerable<T> items, Func<T, string> name, Func<T, string> description)
            where T : notnull
        {
            foreach (var item in items)
            {
                var label = name(item);
                var detail = description(item);
                var score = SearchScore(label, detail, query);
                if (score >= 0) candidates.Add((action, item, label, detail, score));
            }
        }

        AddCandidates(AddMenu.AddMenuAction.Agent, _agents, item => item.Name, item => item.Description);
        AddCandidates(AddMenu.AddMenuAction.Capability, _capabilities, item => item.Name, item => item.Description);
        AddCandidates(AddMenu.AddMenuAction.Instruction, _instructions, item => item.Name, item => item.Description);
        AddCandidates(AddMenu.AddMenuAction.App, _apps, item => item.Name, item => item.Description);

        if (_showMultipleResponses && SearchScore("Multiple Responses", "Run the prompt with two or more models", query) >= 0)
        {
            var multipleResponses = MenuButton("Multiple Responses", 284);
            multipleResponses.Accessibility.Description = "Run the prompt with two or more models";
            multipleResponses.Invoked += (_, _) =>
            {
                Hide();
                MultipleResponsesRequested?.Invoke(this, EventArgs.Empty);
            };
            CatalogResults.Add(multipleResponses);
        }

        var fileScore = SearchScore("Files", "Attach files from this device", query);
        if (fileScore >= 0)
        {
            var files = MenuButton("Files", 284);
            files.Accessibility.Description = "Attach files from this device";
            files.Invoked += OnAttachFilesInvoked;
            CatalogResults.Add(files);
        }

        foreach (var candidate in candidates
                     .OrderByDescending(item => item.Score)
                     .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                     .Take(30))
        {
            var button = MenuButton(candidate.Name, 284);
            button.Accessibility.Description = candidate.Description;
            var selection = new AddMenuSelection(candidate.Action, candidate.Item);
            button.Invoked += (_, _) => SelectCatalogue(selection);
            _catalogRows.Add((button, candidate.Action, candidate.Item));
            CatalogResults.Add(button);
        }

        if (CatalogResults.Children.Count == 0)
            AddCatalogHeading("No matching attachments");
    }

    internal static int SearchScore(string name, string description, string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return 100;
        var q = NormalizeSearchText(query);
        var n = NormalizeSearchText(name);
        var d = NormalizeSearchText(description);
        if (q.Length == 0) return 100;
        if (n.Equals(q, StringComparison.Ordinal)) return 1000;
        if (n.StartsWith(q, StringComparison.Ordinal)) return 850 - Math.Min(100, n.Length - q.Length);
        if (n.Contains(q, StringComparison.Ordinal)) return 700;
        if (d.Contains(q, StringComparison.Ordinal)) return 520;

        foreach (var word in (name + " " + description).Split([' ', '-', '_', '/', '.'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var normalizedWord = NormalizeSearchText(word);
            if (normalizedWord.StartsWith(q, StringComparison.Ordinal)) return 650;
            var wordDistance = LevenshteinDistance(q, normalizedWord);
            var wordThreshold = q.Length <= 3 ? 1 : Math.Max(2, q.Length / 3);
            if (wordDistance <= wordThreshold) return 430 - wordDistance * 30;
        }

        var prefixLength = Math.Min(n.Length, Math.Max(q.Length, Math.Min(n.Length, q.Length + 2)));
        var prefix = n[..prefixLength];
        var distance = LevenshteinDistance(q, prefix);
        var threshold = q.Length <= 3 ? 1 : Math.Max(2, q.Length / 3);
        return distance <= threshold ? 400 - distance * 30 : -1;
    }

    private static string NormalizeSearchText(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static int LevenshteinDistance(string left, string right)
    {
        if (left.Length == 0) return right.Length;
        if (right.Length == 0) return left.Length;
        var previous = Enumerable.Range(0, right.Length + 1).ToArray();
        var current = new int[right.Length + 1];
        for (var i = 1; i <= left.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= right.Length; j++)
            {
                var cost = left[i - 1] == right[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }
            (previous, current) = (current, previous);
        }
        return previous[right.Length];
    }

    private void SelectCatalogue(AddMenuSelection selection)
    {
        Hide();
        CatalogItemSelected?.Invoke(this, selection);
    }

    private static HavenButton MenuButton(string label, double width)
    {
        var button = new HavenButton { Content = label, Variant = ButtonVariant.Tertiary };
        button.Accessibility.AccessibleName = label;
        button.SetValue(HavenProperties.Width, HavenLength.Px(width));
        button.SetValue(HavenProperties.MinHeight, HavenLength.Px(44));
        button.SetValue(HavenProperties.Padding, HavenThickness.Parse("10px 12px"));
        button.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(14)));
        button.SetValue(HavenProperties.FontSize, 12d);
        button.SetValue(HavenProperties.FontWeight, 700);
        return button;
    }

    private static string ActionModeSummary(ChatActionMode mode) => mode switch
    {
        ChatActionMode.AllowAllActions => "All actions",
        ChatActionMode.JustChat => "Just chat",
        _ => "Basic actions"
    };

    private static string VisualModeSummary(GenerativeUiResponseMode mode) => mode switch
    {
        GenerativeUiResponseMode.AlwaysVisual => "Always visual",
        GenerativeUiResponseMode.PreferVisual => "Prefer visual",
        GenerativeUiResponseMode.PreferText => "Prefer text",
        GenerativeUiResponseMode.AlwaysText => "Always text",
        _ => "Auto"
    };

    private static bool Matches(string name, string description, string query) =>
        string.IsNullOrWhiteSpace(query)
        || name.Contains(query, StringComparison.OrdinalIgnoreCase)
        || description.Contains(query, StringComparison.OrdinalIgnoreCase);

    private void OnDismissInvoked(object? sender, EventArgs e) => Hide();
    private void OnAttachFilesInvoked(object? sender, EventArgs e)
    {
        Hide();
        AddActionSelected?.Invoke(this, AddMenu.AddMenuAction.File);
    }
    private void OnAgentsInvoked(object? sender, EventArgs e) => OpenCatalogue(AddMenu.AddMenuAction.Agent);
    private void OnInstructionsInvoked(object? sender, EventArgs e) => OpenCatalogue(AddMenu.AddMenuAction.Instruction);
    private void OnCapabilitiesInvoked(object? sender, EventArgs e) => OpenCatalogue(AddMenu.AddMenuAction.Capability);
    private void OnAppsInvoked(object? sender, EventArgs e) => OpenCatalogue(AddMenu.AddMenuAction.App);
    private void OnAllowActionsInvoked(object? sender, EventArgs e) => OpenCatalogue(AddMenu.AddMenuAction.AllowActions);
    private void OnVisualResponsesInvoked(object? sender, EventArgs e) => OpenCatalogue(AddMenu.AddMenuAction.VisualResponses);
    private void OnMultipleResponsesInvoked(object? sender, EventArgs e) =>
        MultipleResponsesRequested?.Invoke(this, EventArgs.Empty);

    private void OnCatalogSearchInvalidated(object? sender, EventArgs e)
    {
        var next = CatalogSearch.Text;
        if (next == _lastSearch) return;
        _lastSearch = next;
        if (_catalogAction is not null || _searchAll) RebuildCatalogue();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DismissButton.Invoked -= OnDismissInvoked;
        AttachFilesButton.Invoked -= OnAttachFilesInvoked;
        AgentsButton.Invoked -= OnAgentsInvoked;
        InstructionsButton.Invoked -= OnInstructionsInvoked;
        CapabilitiesButton.Invoked -= OnCapabilitiesInvoked;
        AppsButton.Invoked -= OnAppsInvoked;
        AllowActionsButton.Invoked -= OnAllowActionsInvoked;
        VisualResponsesButton.Invoked -= OnVisualResponsesInvoked;
        MultipleResponsesButton.Invoked -= OnMultipleResponsesInvoked;
        CatalogSearch.Invalidated -= OnCatalogSearchInvalidated;
        _catalogRows.Clear();
    }
}
