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
        AllowActionsButton = prefab.GetComponent<HavenButton>("AllowActions");
        VisualResponsesButton = prefab.GetComponent<HavenButton>("VisualResponses");

        DismissButton.Invoked += OnDismissInvoked;
        AttachFilesButton.Invoked += OnAttachFilesInvoked;
        AgentsButton.Invoked += OnAgentsInvoked;
        InstructionsButton.Invoked += OnInstructionsInvoked;
        CapabilitiesButton.Invoked += OnCapabilitiesInvoked;
        AppsButton.Invoked += OnAppsInvoked;
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
    public HavenButton AllowActionsButton { get; }
    public HavenButton VisualResponsesButton { get; }

    public event EventHandler<AddMenu.AddMenuAction>? AddActionSelected;
    public event EventHandler<AddMenuSelection>? CatalogItemSelected;

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

    public void Show()
    {
        _catalogAction = null;
        CatalogPanel.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
        Overlay.SetValue(HavenProperties.Visibility, HavenVisibility.Visible);
    }

    public void Hide()
    {
        _catalogAction = null;
        _lastSearch = string.Empty;
        CatalogSearch.Text = string.Empty;
        CatalogPanel.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
        Overlay.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
    }

    public void FilterCatalogue(string? query)
    {
        CatalogSearch.Text = query ?? string.Empty;
        RebuildCatalogue();
    }

    private void OpenCatalogue(AddMenu.AddMenuAction action)
    {
        _catalogAction = action;
        _lastSearch = string.Empty;
        CatalogSearch.Text = string.Empty;
        CatalogSearch.Placeholder = action == AddMenu.AddMenuAction.Agent ? "Search Agents" : "Search";
        CatalogSearch.SetValue(
            HavenProperties.Visibility,
            action is AddMenu.AddMenuAction.AllowActions or AddMenu.AddMenuAction.VisualResponses
                ? HavenVisibility.Collapsed
                : HavenVisibility.Visible);
        CatalogPanel.SetValue(HavenProperties.Visibility, HavenVisibility.Visible);
        RebuildCatalogue();
    }

    private void RebuildCatalogue()
    {
        foreach (var child in CatalogResults.Children.ToArray()) CatalogResults.Remove(child);
        _catalogRows.Clear();
        if (_catalogAction is null) return;
        var query = CatalogSearch.Text.Trim();
        switch (_catalogAction.Value)
        {
            case AddMenu.AddMenuAction.Agent:
                AddCatalogHeading("Current: No Agent (Default)");
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
        AllowActionsButton.Invoked -= OnAllowActionsInvoked;
        VisualResponsesButton.Invoked -= OnVisualResponsesInvoked;
        CatalogSearch.Invalidated -= OnCatalogSearchInvalidated;
        _catalogRows.Clear();
    }
}
