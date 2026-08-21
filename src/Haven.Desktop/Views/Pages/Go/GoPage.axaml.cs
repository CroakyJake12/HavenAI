using Avalonia.Controls;
using Haven.Core;
using Haven.Desktop.Events;
using Haven.Desktop.HavenUI.Backend;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Shell.TopRail;
using Haven.UI;

namespace Haven.Desktop.Views.Pages.Go;

/// <summary>
/// Product adapter for the Haven.UI Go scene. Existing services, navigation,
/// pending-task state and event contracts remain outside the UI framework.
/// </summary>
public sealed partial class GoPage : UserControl, IDisposable
{
    private readonly HavenEventBus _bus;
    private readonly GoHavenScene _route;
    private readonly TaskAttachmentContext _attachments = new();
    private readonly List<(HavenElement Element, EventHandler Handler)> _stateSubscriptions = [];
    private IReadOnlyList<ModeDefinition> _availableApps = [];
    private IReadOnlyList<GoSuggestion> _suggestions = GoSuggestionService.ImmediateDefaults;
    private bool _disposed;

    public GoPage(HavenEventBus bus)
    {
        _bus = bus;
        InitializeComponent();
        _route = new GoHavenScene();
        Scene.Root = _route.Root;
        WireEvents();
        SetSuggestions(_suggestions);
    }

    public event EventHandler<string>? SubmitRequested;
    public event EventHandler? RefreshSuggestionsRequested;
    public event EventHandler<AddMenu.AddMenuAction>? AddRequested;
    public event EventHandler<AddMenuSelection>? AddCatalogItemSelected;
    public event EventHandler? Disposed;

    internal HavenSceneControl SceneHost => Scene;
    internal GoHavenScene Route => _route;
    internal HavenElement SceneRoot => _route.Root;

    public void AttachFiles(IEnumerable<string> paths)
    {
        _attachments.AttachFiles(paths);
        RefreshAttachmentStatus();
    }

    public void AttachApp(ModeDefinition app)
    {
        _attachments.AttachApp(app);
        RefreshAttachmentStatus();
    }

    public bool IsCapabilityAttached(Guid capabilityId) => _attachments.IsCapabilityAttached(capabilityId);

    public void ToggleCapability(CapabilityDefinition capability)
    {
        if (_attachments.IsCapabilityAttached(capability.Id)) _attachments.RemoveCapability(capability.Id);
        else AttachCapability(capability);
        RefreshAttachmentStatus();
    }

    private void AttachCapability(CapabilityDefinition capability)
    {
        var owner = _availableApps.FirstOrDefault(app => app.Key.Equals(capability.OwnerAppKey, StringComparison.OrdinalIgnoreCase));
        _attachments.AttachCapability(capability, owner);
    }

    public TaskAttachmentSnapshot TakeAttachments()
    {
        var snapshot = _attachments.TakeSnapshot();
        RefreshAttachmentStatus();
        return snapshot;
    }

    public void RestorePendingTask(string instruction, TaskAttachmentSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _attachments.AttachSnapshot(snapshot);
        _route.Instruction.Text = instruction ?? string.Empty;
        _route.Instruction.PlaceCaretAtEnd();
        RefreshAttachmentStatus();
        FocusComposer();
    }

    public void FocusComposer() => Scene.FocusElement(_route.Instruction);

    public void SetSuggestions(IReadOnlyList<GoSuggestion> suggestions)
    {
        if (_disposed || suggestions.Count != 4) return;
        _suggestions = suggestions.ToArray();
        _route.SetSuggestions(_suggestions);
    }

    public void SetAddCatalogue(
        IReadOnlyList<AgentDefinition> agents,
        IReadOnlyList<CapabilityDefinition> capabilities,
        IReadOnlyList<PromptDefinition> instructions,
        IReadOnlyList<ModeDefinition> apps)
    {
        _availableApps = apps;
        _route.SetCatalogue(agents, capabilities, instructions, apps);
    }

    public void SetRefreshInProgress(bool inProgress)
    {
        if (!_disposed) _route.SetRefreshInProgress(inProgress);
    }

    private void WireEvents()
    {
        Register("Go.Suggestions.RecentChats", _route.SuggestionButtons(0));
        Register("Go.Suggestions.Study", _route.SuggestionButtons(1));
        Register("Go.Suggestions.Studio", _route.SuggestionButtons(2));
        Register("Go.Suggestions.Recap", _route.SuggestionButtons(3));
        Register("Go.Suggestions.LoadMore", [_route.LoadMoreButton]);
        Register("Go.Composer.Instruction", [_route.Instruction]);
        Register("Go.Composer.Send", [_route.SendButton]);
        Register("Go.Composer.Add", [_route.AddButton]);

        for (var index = 0; index < 4; index++)
        {
            var suggestionIndex = index;
            foreach (var button in _route.SuggestionButtons(index))
                button.Invoked += (_, _) => SubmitSuggestion(suggestionIndex);
        }

        _route.LoadMoreButton.Invoked += (_, _) =>
        {
            _bus.Fire("Go.Suggestions.LoadMore.Click");
            RefreshSuggestionsRequested?.Invoke(this, EventArgs.Empty);
        };
        _route.SendButton.Invoked += (_, _) => Submit();
        Scene.InputSubmitted += OnInputSubmitted;
        Scene.PointerPressedOutside += OnPointerPressedOutside;

        _route.AddActionSelected += (_, action) => AddRequested?.Invoke(this, action);
        _route.CatalogItemSelected += (_, selection) =>
        {
            if (selection.Item is ModeDefinition app) AttachApp(app);
            if (selection.Item is CapabilityDefinition capability) ToggleCapability(capability);
            AddCatalogItemSelected?.Invoke(this, selection);
        };
    }

    private void OnInputSubmitted(Haven.UI.Components.Input input)
    {
        if (ReferenceEquals(input, _route.Instruction)) Submit();
    }

    private void OnPointerPressedOutside() => _route.HideAddMenu();

    private void Register(string name, IEnumerable<HavenElement> elements)
    {
        _bus.RegisterElement(name, Scene);
        foreach (var element in elements)
        {
            var previous = element.State;
            EventHandler handler = (_, _) =>
            {
                var next = element.State;
                if (previous.HasFlag(HavenElementState.Hover) != next.HasFlag(HavenElementState.Hover))
                    _bus.Fire(name + (next.HasFlag(HavenElementState.Hover) ? ".Hover" : ".Leave"));
                if (previous.HasFlag(HavenElementState.Pressed) != next.HasFlag(HavenElementState.Pressed))
                    _bus.Fire(name + (next.HasFlag(HavenElementState.Pressed) ? ".Press" : ".Release"));
                previous = next;
            };
            element.Invalidated += handler;
            _stateSubscriptions.Add((element, handler));
        }
    }

    private void SubmitSuggestion(int index)
    {
        if (_disposed || index < 0 || index >= _suggestions.Count) return;
        _bus.Fire($"Go.Suggestions.Item{index}.Click");
        SubmitRequested?.Invoke(this, _suggestions[index].Instruction);
    }

    private void Submit()
    {
        var instruction = _route.Instruction.Text.Trim();
        if (string.IsNullOrWhiteSpace(instruction)) return;
        _route.Instruction.Text = string.Empty;
        _bus.Fire("Go.Composer.Send.Click");
        SubmitRequested?.Invoke(this, instruction);
    }

    private void RefreshAttachmentStatus()
    {
        if (_attachments.IsEmpty)
        {
            _route.SetAttachmentStatus(null);
            return;
        }

        var parts = new List<string>();
        if (_attachments.Apps.Count > 0) parts.Add("Apps: " + string.Join(", ", _attachments.Apps.Select(item => item.Name)));
        if (_attachments.Capabilities.Count > 0) parts.Add("Capabilities: " + string.Join(", ", _attachments.Capabilities.Select(item => item.Name)));
        if (_attachments.Files.Count > 0) parts.Add("Files: " + string.Join(", ", _attachments.Files.Select(Path.GetFileName)));
        _route.SetAttachmentStatus(string.Join("  •  ", parts));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Scene.InputSubmitted -= OnInputSubmitted;
        Scene.PointerPressedOutside -= OnPointerPressedOutside;
        foreach (var (element, handler) in _stateSubscriptions) element.Invalidated -= handler;
        _stateSubscriptions.Clear();
        _route.Dispose();
        Disposed?.Invoke(this, EventArgs.Empty);
    }
}
