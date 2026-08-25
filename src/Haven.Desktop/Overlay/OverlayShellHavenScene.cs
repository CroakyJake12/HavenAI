#if !ANDROID
using Haven.Core;
using Haven.UI;
using Haven.UI.Components;
using Container = Haven.UI.Components.Container;
using HavenButton = Haven.UI.Components.Button;
using HavenText = Haven.UI.Components.Text;

namespace Haven.Desktop.Overlay;

internal sealed class OverlayShellHavenScene : IDisposable
{
    private readonly DynamicUI _dynamicUi;
    private readonly HavenButton[] _suggestionButtons;
    private readonly string[] _suggestionPrompts =
    [
        "Continue Code Refactor",
        "Review today's tasks",
        "Summarise this file",
        "Research a question"
    ];
    private bool _disposed;

    public OverlayShellHavenScene()
    {
        Root = new Page { Name = "Overlay.Root", Layout = HavenLayout.Vertical };
        Set(Root, HavenProperties.Width, HavenLength.Percent(100));
        Set(Root, HavenProperties.Background, "Transparent");

        CollapsedPromptButton = Action("Overlay.CollapsedPrompt", "Ask Haven about your Screen", ButtonVariant.Primary);
        Set(CollapsedPromptButton, HavenProperties.Width, HavenLength.Percent(100));
        Set(CollapsedPromptButton, HavenProperties.MinHeight, HavenLength.Px(56));
        CollapsedPromptButton.Accessibility.AccessibleName = "Ask Haven about your Screen";
        Root.Add(CollapsedPromptButton);

        ExpandedPanel = new Container { Name = "Overlay.Expanded", Layout = HavenLayout.Vertical };
        Set(ExpandedPanel, HavenProperties.Width, HavenLength.Percent(100));
        Set(ExpandedPanel, HavenProperties.Padding, HavenThickness.Parse("16px"));
        Set(ExpandedPanel, HavenProperties.Gap, HavenLength.Px(12));
        Set(ExpandedPanel, HavenProperties.Background, "Surface");
        Set(ExpandedPanel, HavenProperties.BorderColor, "Border");
        Set(ExpandedPanel, HavenProperties.BorderWidth, HavenLength.Px(1));
        Set(ExpandedPanel, HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(22)));
        Root.Add(ExpandedPanel);

        var header = new OverlayDragHandle
        {
            Name = "Overlay.Header",
            Layout = HavenLayout.Grid,
            Columns = "Auto 1fr Auto Auto Auto Auto"
        };
        Set(header, HavenProperties.Width, HavenLength.Percent(100));
        Set(header, HavenProperties.Gap, HavenLength.Px(6));
        header.DragDelta += delta => DragDelta?.Invoke(delta);

        ModeButton = Action("Overlay.Mode", "Go", ButtonVariant.Tertiary);
        header.Add(ModeButton);

        var identity = new Container { Name = "Overlay.Identity", Layout = HavenLayout.Vertical };
        Set(identity, HavenProperties.Column, 1);
        TitleText = new HavenText("Overlay") { Name = "Overlay.Title", Level = TextLevel.Caption };
        SourceText = Secondary("Independent Haven workspace", "Overlay.Source");
        Set(SourceText, HavenProperties.Visibility, HavenVisibility.Collapsed);
        identity.Add(TitleText);
        identity.Add(SourceText);
        header.Add(identity);

        CaptureButton = Action("Overlay.Capture", "AI Select", ButtonVariant.Secondary);
        NewChatButton = Action("Overlay.NewChat", "New chat", ButtonVariant.Ghost);
        PinButton = Action("Overlay.Pin", "Pin", ButtonVariant.Ghost);
        CollapseButton = Action("Overlay.Collapse", "Collapse", ButtonVariant.Ghost);
        CloseButton = Action("Overlay.Close", "Close", ButtonVariant.Ghost);
        Set(CaptureButton, HavenProperties.Column, 2);
        Set(PinButton, HavenProperties.Column, 3);
        Set(CollapseButton, HavenProperties.Column, 4);
        Set(CloseButton, HavenProperties.Column, 5);
        Set(NewChatButton, HavenProperties.Visibility, HavenVisibility.Collapsed);
        header.Add(CaptureButton);
        header.Add(PinButton);
        header.Add(CollapseButton);
        header.Add(CloseButton);
        header.Add(NewChatButton);
        ExpandedPanel.Add(header);

        Heading = new HavenText("Ask Haven about your Screen") { Name = "Overlay.Heading", Level = TextLevel.H2 };
        ExpandedPanel.Add(Heading);

        SuggestionsPanel = new Container
        {
            Name = "Overlay.Suggestions",
            Layout = HavenLayout.Grid,
            Columns = "1fr 1fr",
            Rows = "Auto Auto"
        };
        Set(SuggestionsPanel, HavenProperties.Width, HavenLength.Percent(100));
        Set(SuggestionsPanel, HavenProperties.Gap, HavenLength.Px(8));
        _suggestionButtons = new HavenButton[4];
        for (var index = 0; index < _suggestionButtons.Length; index++)
        {
            var button = Action($"Overlay.Suggestion.{index}", _suggestionPrompts[index], ButtonVariant.Secondary);
            Set(button, HavenProperties.Row, index / 2);
            Set(button, HavenProperties.Column, index % 2);
            var captured = index;
            button.Invoked += (_, _) => SubmitRequested?.Invoke(this, _suggestionPrompts[captured]);
            SuggestionsPanel.Add(button);
            _suggestionButtons[index] = button;
        }
        ExpandedPanel.Add(SuggestionsPanel);

        SessionTabs = new DynamicUIRuntime { Name = "Overlay.SessionTabs", Layout = HavenLayout.Horizontal };
        Set(SessionTabs, HavenProperties.Width, HavenLength.Percent(100));
        Set(SessionTabs, HavenProperties.Gap, HavenLength.Px(6));
        Set(SessionTabs, HavenProperties.Overflow, HavenOverflow.Scroll);
        Set(SessionTabs, HavenProperties.Visibility, HavenVisibility.Collapsed);
        ExpandedPanel.Add(SessionTabs);

        ContextPanel = new Container { Name = "Overlay.Context", Layout = HavenLayout.Vertical };
        Set(ContextPanel, HavenProperties.Width, HavenLength.Percent(100));
        Set(ContextPanel, HavenProperties.Padding, HavenThickness.Parse("8px 10px"));
        Set(ContextPanel, HavenProperties.Background, "SurfaceRaised");
        Set(ContextPanel, HavenProperties.BorderColor, "Border");
        Set(ContextPanel, HavenProperties.BorderWidth, HavenLength.Px(1));
        Set(ContextPanel, HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(12)));
        ContextSummary = new HavenText("No selected context.") { Name = "Overlay.Context.Summary", Level = TextLevel.Caption };
        PermissionText = Secondary("Capture inactive", "Overlay.Context.Permission");
        ContextPanel.Add(ContextSummary);
        ContextPanel.Add(PermissionText);
        Set(ContextPanel, HavenProperties.Visibility, HavenVisibility.Collapsed);
        ExpandedPanel.Add(ContextPanel);

        Actions = new DynamicUIRuntime { Name = "Overlay.Actions", Layout = HavenLayout.Horizontal };
        Set(Actions, HavenProperties.Width, HavenLength.Percent(100));
        Set(Actions, HavenProperties.Gap, HavenLength.Px(6));
        Set(Actions, HavenProperties.Overflow, HavenOverflow.Scroll);
        ExpandedPanel.Add(Actions);

        ProgressPanel = new Container { Name = "Overlay.Progress", Layout = HavenLayout.Vertical };
        Set(ProgressPanel, HavenProperties.Width, HavenLength.Percent(100));
        Set(ProgressPanel, HavenProperties.Padding, HavenThickness.Parse("8px 10px"));
        Set(ProgressPanel, HavenProperties.Background, "AccentSubtle");
        Set(ProgressPanel, HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(12)));
        ProgressText = new HavenText("Action 1/1") { Name = "Overlay.Progress.Text", Level = TextLevel.Caption };
        ProgressPanel.Add(ProgressText);
        Set(ProgressPanel, HavenProperties.Visibility, HavenVisibility.Collapsed);
        ExpandedPanel.Add(ProgressPanel);

        ResultPanel = new Container { Name = "Overlay.Result", Layout = HavenLayout.Vertical };
        Set(ResultPanel, HavenProperties.Width, HavenLength.Percent(100));
        Set(ResultPanel, HavenProperties.Padding, HavenThickness.Parse("8px 10px"));
        Set(ResultPanel, HavenProperties.Background, "SurfaceRaised");
        Set(ResultPanel, HavenProperties.BorderColor, "Border");
        Set(ResultPanel, HavenProperties.BorderWidth, HavenLength.Px(1));
        Set(ResultPanel, HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(12)));
        ResultText = new HavenText(string.Empty) { Name = "Overlay.Result.Text", Level = TextLevel.Caption };
        ResultPanel.Add(ResultText);
        Set(ResultPanel, HavenProperties.Visibility, HavenVisibility.Collapsed);
        ExpandedPanel.Add(ResultPanel);

        var composerRow = new Container
        {
            Name = "Overlay.Composer",
            Layout = HavenLayout.Grid,
            Columns = "Auto 1fr Auto"
        };
        Set(composerRow, HavenProperties.Width, HavenLength.Percent(100));
        Set(composerRow, HavenProperties.Gap, HavenLength.Px(8));
        Set(composerRow, HavenProperties.Padding, HavenThickness.Parse("8px"));
        Set(composerRow, HavenProperties.Background, "SurfaceRaised");
        Set(composerRow, HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(18)));

        AddButton = Action("Overlay.Composer.Add", "+", ButtonVariant.Ghost);
        ComposerInput = new Input
        {
            Name = "Overlay.Composer.Input",
            Placeholder = "Ask Haven anything",
            SubmitOnEnter = true
        };
        Set(ComposerInput, HavenProperties.Column, 1);
        Set(ComposerInput, HavenProperties.Width, HavenLength.Percent(100));
        SendButton = Action("Overlay.Composer.Send", "Ã¢â€ â€˜", ButtonVariant.Primary);
        Set(SendButton, HavenProperties.Column, 2);
        composerRow.Add(AddButton);
        composerRow.Add(ComposerInput);
        composerRow.Add(SendButton);
        ExpandedPanel.Add(composerRow);

        _dynamicUi = new DynamicUI(Root, HavenDynamicUITemplateCatalog.FromAssembly(typeof(OverlayShellHavenScene).Assembly));

        CollapsedPromptButton.Invoked += (_, _) => CollapseToggleRequested?.Invoke(this, EventArgs.Empty);
        CaptureButton.Invoked += (_, _) => CaptureRequested?.Invoke(this, EventArgs.Empty);
        NewChatButton.Invoked += (_, _) => NewChatRequested?.Invoke(this, EventArgs.Empty);
        PinButton.Invoked += (_, _) => PinToggleRequested?.Invoke(this, EventArgs.Empty);
        CollapseButton.Invoked += (_, _) => CollapseToggleRequested?.Invoke(this, EventArgs.Empty);
        CloseButton.Invoked += (_, _) => CloseRequested?.Invoke(this, EventArgs.Empty);
        AddButton.Invoked += (_, _) => AddRequested?.Invoke(this, EventArgs.Empty);
        SendButton.Invoked += (_, _) => SubmitComposer();

        SetCollapsed(false);
    }

    public event EventHandler? CaptureRequested;
    public event EventHandler? NewChatRequested;
    public event EventHandler? PinToggleRequested;
    public event EventHandler? CollapseToggleRequested;
    public event EventHandler? CloseRequested;
    public event EventHandler? AddRequested;
    public event EventHandler<string>? SubmitRequested;
    public event EventHandler<Guid>? SessionActivated;
    public event EventHandler<OverlayContextActionDescriptor>? ActionRequested;
    public event Action<HavenPoint>? DragDelta;

    public Page Root { get; }
    public Container ExpandedPanel { get; }
    public HavenButton CollapsedPromptButton { get; }
    public HavenButton ModeButton { get; }
    public HavenText Heading { get; }
    public HavenText TitleText { get; }
    public HavenText SourceText { get; }
    public HavenButton CaptureButton { get; }
    public HavenButton NewChatButton { get; }
    public HavenButton PinButton { get; }
    public HavenButton CollapseButton { get; }
    public HavenButton CloseButton { get; }
    public Container SuggestionsPanel { get; }
    public DynamicUIRuntime SessionTabs { get; }
    public Container ContextPanel { get; }
    public HavenText ContextSummary { get; }
    public HavenText PermissionText { get; }
    public DynamicUIRuntime Actions { get; }
    public Container ProgressPanel { get; }
    public HavenText ProgressText { get; }
    public Container ResultPanel { get; }
    public HavenText ResultText { get; }
    public HavenButton AddButton { get; }
    public Input ComposerInput { get; }
    public HavenButton SendButton { get; }

    public void ApplySnapshot(OverlayWorkspaceSnapshot snapshot, Guid windowSessionId)
    {
        var current = snapshot.Sessions.FirstOrDefault(session => session.Id == windowSessionId);
        if (current is null) return;

        TitleText.Content = current.Title;
        SourceText.Content = SourceLabel(current);
        ModeButton.Content = current.AppKey.Equals("chat", StringComparison.OrdinalIgnoreCase) ? "Chat" : "Go";
        PinButton.Content = current.IsPinned ? "Unpin" : "Pin";
        PinButton.Variant = current.IsPinned ? ButtonVariant.Primary : ButtonVariant.Ghost;
        CollapseButton.Content = current.IsCollapsed ? "Expand" : "Collapse";
        CollapseButton.Variant = current.IsCollapsed ? ButtonVariant.Secondary : ButtonVariant.Ghost;
        ContextSummary.Content = current.Context is null ? "No selected context." : ContextLabel(current.Context);
        PermissionText.Content = current.Context is null ? "Capture inactive" : PermissionLabel(current.Context.Provenance);
        Set(ContextPanel, HavenProperties.Visibility, current.Context is null ? HavenVisibility.Collapsed : HavenVisibility.Visible);
        RebuildSessions(snapshot, windowSessionId);
        SetCollapsed(current.IsCollapsed);
    }

    public void SetSuggestions(IReadOnlyList<string> labels)
    {
        for (var index = 0; index < _suggestionButtons.Length; index++)
        {
            var label = index < labels.Count && !string.IsNullOrWhiteSpace(labels[index])
                ? labels[index].Trim()
                : _suggestionPrompts[index];
            _suggestionPrompts[index] = label;
            _suggestionButtons[index].Content = label;
            _suggestionButtons[index].Accessibility.AccessibleName = label;
        }
    }

    public void SubmitComposer()
    {
        var text = ComposerInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(text)) return;
        ComposerInput.Text = string.Empty;
        SubmitRequested?.Invoke(this, text);
    }

    public void SetCollapsed(bool collapsed)
    {
        Set(CollapsedPromptButton, HavenProperties.Visibility, collapsed ? HavenVisibility.Visible : HavenVisibility.Collapsed);
        Set(ExpandedPanel, HavenProperties.Visibility, collapsed ? HavenVisibility.Collapsed : HavenVisibility.Visible);
    }

    public void SetActionProgress(string label, int current = 1, int total = 1)
    {
        total = Math.Max(1, total);
        current = Math.Clamp(current, 1, total);
        ProgressText.Content = $"Action {current}/{total} · {label}";
        Set(ResultPanel, HavenProperties.Visibility, HavenVisibility.Collapsed);
        Set(ProgressPanel, HavenProperties.Visibility, HavenVisibility.Visible);
    }

    public void SetActionResult(string message, bool success = true)
    {
        ResultText.Content = success ? message : "Could not complete: " + message;
        Set(ProgressPanel, HavenProperties.Visibility, HavenVisibility.Collapsed);
        Set(ResultPanel, HavenProperties.Visibility, HavenVisibility.Visible);
    }

    public void ClearActionFeedback()
    {
        Set(ProgressPanel, HavenProperties.Visibility, HavenVisibility.Collapsed);
        Set(ResultPanel, HavenProperties.Visibility, HavenVisibility.Collapsed);
    }

    public void SetActions(IReadOnlyList<OverlayContextActionDescriptor> actions)
    {
        _dynamicUi.Clear("Overlay.Actions");
        Set(Actions, HavenProperties.Visibility, actions.Count == 0 ? HavenVisibility.Collapsed : HavenVisibility.Visible);
        for (var index = 0; index < actions.Count; index++)
        {
            var descriptor = actions[index];
            var item = _dynamicUi.CreateItem(
                "OverlayActionChip", "Overlay.Actions", ActionId(descriptor.Id, index),
                new Dictionary<string, object?> { ["LABEL"] = ActionLabel(descriptor) }, index);
            var button = item.GetComponent<HavenButton>("Invoke");
            button.Variant = descriptor.Id.Equals("ask-haven", StringComparison.OrdinalIgnoreCase)
                ? ButtonVariant.Primary
                : descriptor.IsGenerated ? ButtonVariant.Secondary : ButtonVariant.Ghost;
            button.Accessibility.AccessibleName = descriptor.Label;
            button.Invoked += (_, _) => ActionRequested?.Invoke(this, descriptor);
        }
    }

    private void RebuildSessions(OverlayWorkspaceSnapshot snapshot, Guid windowSessionId)
    {
        _dynamicUi.Clear("Overlay.SessionTabs");
        for (var index = 0; index < snapshot.Sessions.Count; index++)
        {
            var session = snapshot.Sessions[index];
            var item = _dynamicUi.CreateItem(
                "OverlaySessionTab", "Overlay.SessionTabs", session.Id.ToString("N"),
                new Dictionary<string, object?> { ["LABEL"] = session.Title + (session.IsPinned ? " Ã‚Â· pinned" : "") }, index);
            var button = item.GetComponent<HavenButton>("Activate");
            button.Variant = session.Id == windowSessionId ? ButtonVariant.Primary : ButtonVariant.Secondary;
            var id = session.Id;
            button.Invoked += (_, _) => SessionActivated?.Invoke(this, id);
        }
    }

    private static string SourceLabel(OverlaySessionState session)
    {
        var provenance = session.Context?.Provenance;
        var parts = new[] { provenance?.SourceApplication, provenance?.SourceWindow }
            .Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!);
        var label = string.Join(" · ", parts);
        return string.IsNullOrWhiteSpace(label)
            ? session.SourceAssociation
              ?? (session.AppKey.Equals("chat", StringComparison.OrdinalIgnoreCase) ? "Haven Chat" : "Haven Go")
            : label;
    }

    private static string ContextLabel(OverlayContextEnvelope context) =>
        OverlaySelectionPresentation.ContextLabel(context);
    private static string PermissionLabel(OverlayContextProvenance provenance)
    {
        var state = provenance.PermissionState switch
        {
            OverlayContextPermissionState.Granted => "Capture allowed",
            OverlayContextPermissionState.Denied => "Capture denied",
            OverlayContextPermissionState.Unavailable => "Capture unavailable",
            _ => "No capture permission required"
        };
        return string.IsNullOrWhiteSpace(provenance.PermissionDescription)
            ? state
            : state + " · " + provenance.PermissionDescription;
    }
    private static string ActionLabel(OverlayContextActionDescriptor descriptor) =>
        descriptor.IsGenerated && descriptor.Availability == CapabilityAvailability.PermissionRequired
            ? descriptor.Label + " · asks"
            : descriptor.Label;
    private static string ActionId(string value, int index) =>
        new string(value.Select(character => char.IsLetterOrDigit(character) ? character : '-').ToArray()) + "-" + index;

    private static HavenText Secondary(string content, string name)
    {
        var text = new HavenText(content) { Name = name, Level = TextLevel.Caption };
        Set(text, HavenProperties.Foreground, "TextSecondary");
        return text;
    }

    private static HavenButton Action(string name, string content, ButtonVariant variant) =>
        new() { Name = name, Content = content, Variant = variant };

    private static void Set<T>(HavenElement element, HavenProperty<T> property, T value) =>
        element.SetValue(property, value);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _dynamicUi.Clear("Overlay.SessionTabs");
        _dynamicUi.Clear("Overlay.Actions");
    }
}
#endif
