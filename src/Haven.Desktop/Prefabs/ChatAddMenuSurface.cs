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
    private IReadOnlyList<AgentDefinition> _agents = [];
    private IReadOnlyList<CapabilityDefinition> _capabilities = [];
    private IReadOnlyList<PromptDefinition> _instructions = [];
    private IReadOnlyList<ModeDefinition> _apps = [];
    private AddMenu.AddMenuAction? _catalogAction;
    private string _lastSearch = string.Empty;
    private string _currentAgentName = "No Agent (Default)";
    private ChatActionMode _currentActionMode = ChatActionMode.AllowBasicActions;
    private GenerativeUiResponseMode _currentVisualMode = GenerativeUiResponseMode.Auto;
    private bool _embeddedSearchVisible = true;
    private bool _unifiedSearch;
    private bool _disposed;

    public ChatAddMenuSurface(Prefab prefab)
    {
        Prefab = prefab ?? throw new ArgumentNullException(nameof(prefab));
        Overlay = prefab.GetComponent<Container>("AddOverlay");
        DismissButton = prefab.GetComponent<HavenButton>("Dismiss");
        MainMenu = prefab.GetComponent<Container>("MainMenu");
        CatalogPanel = prefab.GetComponent<Container>("CatalogPanel");
        CatalogSearch = prefab.GetComponent<Input>("CatalogSearch");
        CatalogResults = prefab.GetComponent<Container>("CatalogResults");
        AttachFilesButton = prefab.GetComponent<HavenButton>("AttachFiles");
        AgentsButton = prefab.GetComponent<HavenButton>("Agents");
        InstructionsButton = prefab.GetComponent<HavenButton>("Instructions");
        CapabilitiesButton = prefab.GetComponent<HavenButton>("Capabilities");
        AppsButton = prefab.GetComponent<HavenButton>("Apps");
        MultipleResponsesButton = prefab.GetComponent<HavenButton>("MultipleResponses");
        AllowActionsButton = prefab.GetComponent<HavenButton>("AllowActions");
        VisualResponsesButton = prefab.GetComponent<HavenButton>("VisualResponses");
        CurrentResponseState = prefab.GetComponent<HavenText>("CurrentResponseState");

        DismissButton.Invoked += OnDismissInvoked;
        AttachFilesButton.Invoked += OnAttachFilesInvoked;
        AgentsButton.Invoked += OnAgentsInvoked;
        InstructionsButton.Invoked += OnInstructionsInvoked;
        CapabilitiesButton.Invoked += OnCapabilitiesInvoked;
        AppsButton.Invoked += OnAppsInvoked;
        MultipleResponsesButton.Invoked += OnMultipleResponsesInvoked;
        AllowActionsButton.Invoked += OnAllowActionsInvoked;
        VisualResponsesButton.Invoked += OnVisualResponsesInvoked;
        CatalogSearch.Invalidated += OnCatalogSearchInvalidated;
        Hide();
    }

    public Prefab Prefab { get; }
    public Container Overlay { get; }
    public HavenButton DismissButton { get; }
    public Container MainMenu { get; }
    public Container CatalogPanel { get; }
    public Input CatalogSearch { get; }
    public Container CatalogResults { get; }
    public HavenButton AttachFilesButton { get; }
    public HavenButton AgentsButton { get; }
    public HavenButton InstructionsButton { get; }
    public HavenButton CapabilitiesButton { get; }
    public HavenButton AppsButton { get; }
    public HavenButton MultipleResponsesButton { get; }
    public HavenButton AllowActionsButton { get; }
    public HavenButton VisualResponsesButton { get; }
    public HavenText CurrentResponseState { get; }

    public event EventHandler<AddMenu.AddMenuAction>? AddActionSelected;
    public event EventHandler<AddMenuSelection>? CatalogItemSelected;

    public void SetEmbeddedSearchVisible(bool visible)
    {
        _embeddedSearchVisible = visible;
        CatalogSearch.SetValue(HavenProperties.Visibility, visible ? HavenVisibility.Visible : HavenVisibility.Collapsed);
    }

    public void SetThreadSettingsVisible(bool visible)
    {
        var state = visible ? HavenVisibility.Visible : HavenVisibility.Collapsed;
        CurrentResponseState.SetValue(HavenProperties.Visibility, state);
        AllowActionsButton.SetValue(HavenProperties.Visibility, state);
        VisualResponsesButton.SetValue(HavenProperties.Visibility, state);
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
        if (_catalogAction is not null) RebuildCatalogue();
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
        if (_catalogAction is not null) RebuildCatalogue();
    }

    public void Show()
    {
        _unifiedSearch = false;
        _catalogAction = null;
        _lastSearch = string.Empty;
        MainMenu.SetValue(HavenProperties.Visibility, HavenVisibility.Visible);
        CatalogPanel.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
        Overlay.SetValue(HavenProperties.Visibility, HavenVisibility.Visible);
    }

    public void Hide()
    {
        _catalogAction = null;
        _unifiedSearch = false;
        _lastSearch = string.Empty;
        CatalogSearch.Text = string.Empty;
        MainMenu.SetValue(HavenProperties.Visibility, HavenVisibility.Visible);
        CatalogPanel.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
        Overlay.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
    }

    public void FilterCatalogue(string? query)
    {
        _lastSearch = query ?? string.Empty;
        if (_embeddedSearchVisible) CatalogSearch.Text = _lastSearch;
        RebuildCatalogue();
    }

    public void ShowUnifiedSearch(string? query)
    {
        _catalogAction = null;
        _unifiedSearch = true;
        _lastSearch = query?.Trim() ?? string.Empty;
        MainMenu.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
        CatalogSearch.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
        CatalogPanel.SetValue(HavenProperties.Visibility, HavenVisibility.Visible);
        Overlay.SetValue(HavenProperties.Visibility, HavenVisibility.Visible);
        RebuildCatalogue();
    }

    private void OpenCatalogue(AddMenu.AddMenuAction action)
    {
        _unifiedSearch = false;
        _catalogAction = action;
        _lastSearch = string.Empty;
        CatalogSearch.Text = string.Empty;
        CatalogSearch.Placeholder = action == AddMenu.AddMenuAction.Agent ? "Search Agents" : "Search";
        CatalogSearch.SetValue(
            HavenProperties.Visibility,
            !_embeddedSearchVisible || action is AddMenu.AddMenuAction.AllowActions or AddMenu.AddMenuAction.VisualResponses
                ? HavenVisibility.Collapsed
                : HavenVisibility.Visible);
        CatalogPanel.SetValue(HavenProperties.Visibility, HavenVisibility.Visible);
        RebuildCatalogue();
    }

    private void RebuildCatalogue()
    {
        foreach (var child in CatalogResults.Children.ToArray()) CatalogResults.Remove(child);
        _catalogRows.Clear();
        if (_unifiedSearch)
        {
            RebuildUnifiedCatalogue();
            return;
        }
        if (_catalogAction is null) return;
        var query = _lastSearch.Trim();
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

    private void RebuildUnifiedCatalogue()
    {
        var query = _lastSearch.Trim();
        var ranked = new List<(int Score, AddMenu.AddMenuAction Action, object Item, string Label, string Description)>();
        void Add(AddMenu.AddMenuAction action, object item, string label, string description)
        {
            var score = SearchScore(label, description, query);
            if (score < int.MaxValue) ranked.Add((score, action, item, label, description));
        }
        Add(AddMenu.AddMenuAction.File, "upload", "Upload Files", "Attach one or more local files");
        Add(AddMenu.AddMenuAction.MultipleResponses, "multiple-responses", "Multiple Responses", "Ask several selected models and compare their responses");
        foreach (var item in _instructions) Add(AddMenu.AddMenuAction.Instruction, item, item.Name, item.Description);
        foreach (var item in _capabilities) Add(AddMenu.AddMenuAction.Capability, item, item.Name, item.Description);
        foreach (var item in _agents) Add(AddMenu.AddMenuAction.Agent, item, item.Name, item.Description);
        foreach (var item in _apps) Add(AddMenu.AddMenuAction.App, item, item.Name, item.Description);
        foreach (var result in ranked.OrderBy(item => item.Score).ThenBy(item => item.Label, StringComparer.OrdinalIgnoreCase).Take(24))
        {
            var button = MenuButton(result.Label, 284);
            button.Accessibility.Description = result.Description;
            if (result.Action is AddMenu.AddMenuAction.File or AddMenu.AddMenuAction.MultipleResponses)
                button.Invoked += (_, _) => { Hide(); AddActionSelected?.Invoke(this, result.Action); };
            else
            {
                var selection = new AddMenuSelection(result.Action, result.Item);
                button.Invoked += (_, _) => SelectCatalogue(selection);
            }
            CatalogResults.Add(button);
        }
        if (ranked.Count == 0)
        {
            var empty = new HavenText("No close matches. Keep typing or try a shorter name.") { Level = TextLevel.Caption };
            empty.SetValue(HavenProperties.Foreground, "TextSecondary");
            CatalogResults.Add(empty);
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

    private static bool Matches(string name, string description, string query) => SearchScore(name, description, query) < int.MaxValue;

    internal static int SearchScore(string name, string description, string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return 0;
        var needle = query.Trim().ToLowerInvariant();
        var title = (name ?? string.Empty).ToLowerInvariant();
        var detail = (description ?? string.Empty).ToLowerInvariant();
        if (title == needle) return 0;
        if (title.StartsWith(needle, StringComparison.Ordinal)) return 1;
        if (title.Contains(needle, StringComparison.Ordinal)) return 2;
        if (detail.Contains(needle, StringComparison.Ordinal)) return 5;
        var words = title.Split(new[] { ' ', '-', '_', '/', '.', '(', ')' }, StringSplitOptions.RemoveEmptyEntries);
        var best = words.Select(word => EditDistance(word, needle)).Append(EditDistance(title, needle)).Min();
        var tolerance = needle.Length <= 4 ? 1 : needle.Length <= 8 ? 2 : 3;
        return best <= tolerance ? 10 + best : int.MaxValue;
    }

    private static int EditDistance(string left, string right)
    {
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
    private void OnMultipleResponsesInvoked(object? sender, EventArgs e)
    {
        Hide();
        AddActionSelected?.Invoke(this, AddMenu.AddMenuAction.MultipleResponses);
    }
    private void OnAllowActionsInvoked(object? sender, EventArgs e) => OpenCatalogue(AddMenu.AddMenuAction.AllowActions);
    private void OnVisualResponsesInvoked(object? sender, EventArgs e) => OpenCatalogue(AddMenu.AddMenuAction.VisualResponses);

    private void OnCatalogSearchInvalidated(object? sender, EventArgs e)
    {
        var next = CatalogSearch.Text;
        if (next == _lastSearch) return;
        _lastSearch = next;
        if (_catalogAction is not null) RebuildCatalogue();
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
        MultipleResponsesButton.Invoked -= OnMultipleResponsesInvoked;
        AllowActionsButton.Invoked -= OnAllowActionsInvoked;
        VisualResponsesButton.Invoked -= OnVisualResponsesInvoked;
        CatalogSearch.Invalidated -= OnCatalogSearchInvalidated;
        _catalogRows.Clear();
    }
}
