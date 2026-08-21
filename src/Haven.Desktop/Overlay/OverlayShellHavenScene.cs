#if !ANDROID
using Haven.Core;
using Haven.UI;
using Haven.UI.Components;
using Container = Haven.UI.Components.Container;
using HavenButton = Haven.UI.Components.Button;
using HavenInput = Haven.UI.Components.Input;
using HavenText = Haven.UI.Components.Text;

namespace Haven.Desktop.Overlay;

internal sealed record OverlayCompactSuggestion(string Label, string Instruction);

internal sealed class OverlayShellHavenScene : IDisposable
{
    private readonly DynamicUI _dynamicUi;
    private bool _collapsed;
    private bool _disposed;

    public OverlayShellHavenScene()
    {
        Root = new Page { Name = "Overlay.Root", Layout = HavenLayout.Vertical };
        Set(Root, HavenProperties.Width, HavenLength.Percent(100));
        Set(Root, HavenProperties.Background, "SurfaceRaised");
        Set(Root, HavenProperties.BorderColor, "Border");
        Set(Root, HavenProperties.BorderWidth, HavenLength.Px(1));
        Set(Root, HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(24)));
        Set(Root, HavenProperties.Shadow, "Card");
        Set(Root, HavenProperties.Padding, HavenThickness.Parse("10px 12px 12px 12px"));
        Set(Root, HavenProperties.Gap, HavenLength.Px(9));
        Set(Root, HavenProperties.Clip, true);

        var header = new OverlayDragHandle { Name = "Overlay.Header", Layout = HavenLayout.Grid, Columns = "1fr Auto Auto Auto Auto" };
        Set(header, HavenProperties.Width, HavenLength.Percent(100));
        Set(header, HavenProperties.Gap, HavenLength.Px(5));
        header.DragDelta += delta => DragDelta?.Invoke(delta);

        var identity = new Container { Name = "Overlay.Identity", Layout = HavenLayout.Vertical };
        TitleText = new HavenText("Go") { Name = "Overlay.Title", Level = TextLevel.H4 };
        SourceText = Secondary("Screen context", "Overlay.Source");
        identity.Add(TitleText);
        identity.Add(SourceText);
        header.Add(identity);

        CaptureButton = Action("Overlay.Capture", "Capture", ButtonVariant.Secondary);
        PinButton = Action("Overlay.Pin", "Pin", ButtonVariant.Ghost);
        CollapseButton = Action("Overlay.Collapse", "Collapse", ButtonVariant.Ghost);
        CloseButton = Action("Overlay.Close", "Close", ButtonVariant.Ghost);
        CaptureButton.Accessibility.AccessibleName = "Capture screen context";
        PinButton.Accessibility.AccessibleName = "Pin Haven Overlay";
        CollapseButton.Accessibility.AccessibleName = "Collapse Haven Overlay";
        CloseButton.Accessibility.AccessibleName = "Close Haven Overlay";
        Set(CaptureButton, HavenProperties.Column, 1);
        Set(PinButton, HavenProperties.Column, 2);
        Set(CollapseButton, HavenProperties.Column, 3);
        Set(CloseButton, HavenProperties.Column, 4);
        header.Add(CaptureButton);
        header.Add(PinButton);
        header.Add(CollapseButton);
        header.Add(CloseButton);
        Root.Add(header);

        PromptText = new HavenText("How can I help?") { Name = "Overlay.Prompt", Level = TextLevel.H2 };
        Set(PromptText, HavenProperties.FontWeight, 800);
        Root.Add(PromptText);

        SuggestedActions = new DynamicUIRuntime { Name = "Overlay.Suggestions", Layout = HavenLayout.Horizontal };
        Set(SuggestedActions, HavenProperties.Width, HavenLength.Percent(100));
        Set(SuggestedActions, HavenProperties.Gap, HavenLength.Px(6));
        Set(SuggestedActions, HavenProperties.Overflow, HavenOverflow.Scroll);
        Root.Add(SuggestedActions);

        ContextPanel = new Container { Name = "Overlay.Context", Layout = HavenLayout.Vertical };
        Set(ContextPanel, HavenProperties.Width, HavenLength.Percent(100));
        Set(ContextPanel, HavenProperties.Padding, HavenThickness.Parse("8px 10px"));
        Set(ContextPanel, HavenProperties.Background, "Surface");
        Set(ContextPanel, HavenProperties.BorderColor, "Border");
        Set(ContextPanel, HavenProperties.BorderWidth, HavenLength.Px(1));
        Set(ContextPanel, HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(14)));
        ContextSummary = new HavenText("No selected context.") { Name = "Overlay.Context.Summary", Level = TextLevel.Caption };
        PermissionText = Secondary("Capture inactive", "Overlay.Context.Permission");
        ContextPanel.Add(ContextSummary);
        ContextPanel.Add(PermissionText);
        Root.Add(ContextPanel);

        Actions = new DynamicUIRuntime { Name = "Overlay.Actions", Layout = HavenLayout.Horizontal };
        Set(Actions, HavenProperties.Width, HavenLength.Percent(100));
        Set(Actions, HavenProperties.Gap, HavenLength.Px(6));
        Set(Actions, HavenProperties.Overflow, HavenOverflow.Scroll);
        Root.Add(Actions);

        var composer = new Container { Name = "Overlay.Composer", Layout = HavenLayout.Grid, Columns = "Auto 1fr Auto" };
        Set(composer, HavenProperties.Width, HavenLength.Percent(100));
        Set(composer, HavenProperties.Gap, HavenLength.Px(7));
        Set(composer, HavenProperties.Padding, HavenThickness.Parse("6px"));
        Set(composer, HavenProperties.Background, "Surface");
        Set(composer, HavenProperties.BorderColor, "Border");
        Set(composer, HavenProperties.BorderWidth, HavenLength.Px(1));
        Set(composer, HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(20)));

        AddButton = Action("Overlay.Add", "+", ButtonVariant.Ghost);
        AddButton.Accessibility.AccessibleName = "Add files or context";
        Set(AddButton, HavenProperties.Column, 0);
        composer.Add(AddButton);

        ComposerInput = new HavenInput
        {
            Name = "Overlay.Composer.Input",
            Placeholder = "Ask Haven about your screen",
            Multiline = true,
            SubmitOnEnter = true
        };
        ComposerInput.Accessibility.AccessibleName = "Ask Haven about your screen";
        Set(ComposerInput, HavenProperties.Column, 1);
        Set(ComposerInput, HavenProperties.Width, HavenLength.Percent(100));
        composer.Add(ComposerInput);

        SendButton = Action("Overlay.Send", "Send", ButtonVariant.Primary);
        SendButton.Accessibility.AccessibleName = "Send to Haven";
        Set(SendButton, HavenProperties.Column, 2);
        composer.Add(SendButton);
        Composer = composer;
        Root.Add(composer);

        StatusText = Secondary(string.Empty, "Overlay.Status");
        Set(StatusText, HavenProperties.Visibility, HavenVisibility.Collapsed);
        Root.Add(StatusText);

        // Preserve the existing multi-session projection as state, but do not expose a tab strip in the compact surface.
        SessionTabs = new DynamicUIRuntime { Name = "Overlay.SessionTabs", Layout = HavenLayout.Horizontal };
        NewChatButton = Action("Overlay.NewChat", "New chat", ButtonVariant.Secondary);

        _dynamicUi = new DynamicUI(Root, HavenDynamicUITemplateCatalog.FromAssembly(typeof(OverlayShellHavenScene).Assembly));
        CaptureButton.Invoked += (_, _) => CaptureRequested?.Invoke(this, EventArgs.Empty);
        NewChatButton.Invoked += (_, _) => NewChatRequested?.Invoke(this, EventArgs.Empty);
        PinButton.Invoked += (_, _) => PinToggleRequested?.Invoke(this, EventArgs.Empty);
        CollapseButton.Invoked += (_, _) => CollapseToggleRequested?.Invoke(this, EventArgs.Empty);
        CloseButton.Invoked += (_, _) => CloseRequested?.Invoke(this, EventArgs.Empty);
        AddButton.Invoked += (_, _) => AddRequested?.Invoke(this, EventArgs.Empty);
        SendButton.Invoked += (_, _) => SubmitComposer();
    }

    public event EventHandler? CaptureRequested;
    public event EventHandler? NewChatRequested;
    public event EventHandler? PinToggleRequested;
    public event EventHandler? CollapseToggleRequested;
    public event EventHandler? CloseRequested;
    public event EventHandler? AddRequested;
    public event EventHandler<string>? SubmitRequested;
    public event EventHandler<int>? SuggestionRequested;
    public event EventHandler<Guid>? SessionActivated;
    public event EventHandler<OverlayContextActionDescriptor>? ActionRequested;
    public event Action<HavenPoint>? DragDelta;

    public Page Root { get; }
    public HavenText TitleText { get; }
    public HavenText SourceText { get; }
    public HavenText PromptText { get; }
    public HavenButton CaptureButton { get; }
    public HavenButton NewChatButton { get; }
    public HavenButton PinButton { get; }
    public HavenButton CollapseButton { get; }
    public HavenButton CloseButton { get; }
    public HavenButton AddButton { get; }
    public HavenButton SendButton { get; }
    public HavenInput ComposerInput { get; }
    public Container Composer { get; }
    public DynamicUIRuntime SuggestedActions { get; }
    public DynamicUIRuntime SessionTabs { get; }
    public Container ContextPanel { get; }
    public HavenText ContextSummary { get; }
    public HavenText PermissionText { get; }
    public DynamicUIRuntime Actions { get; }
    public HavenText StatusText { get; }

    public void ApplySnapshot(OverlayWorkspaceSnapshot snapshot, Guid windowSessionId)
    {
        var current = snapshot.Sessions.FirstOrDefault(session => session.Id == windowSessionId);
        if (current is null) return;

        _collapsed = current.IsCollapsed;
        TitleText.Content = _collapsed ? "Ask Haven about your Screen" : ContextIdentity(current);
        SourceText.Content = SourceLabel(current);
        PinButton.Content = current.IsPinned ? "Unpin" : "Pin";
        PinButton.Variant = current.IsPinned ? ButtonVariant.Primary : ButtonVariant.Ghost;
        CollapseButton.Content = _collapsed ? "Expand" : "Collapse";
        CollapseButton.Variant = _collapsed ? ButtonVariant.Secondary : ButtonVariant.Ghost;
        CollapseButton.Accessibility.AccessibleName = _collapsed ? "Expand Haven Overlay" : "Collapse Haven Overlay";
        ContextSummary.Content = current.Context is null ? "No selected context." : ContextLabel(current.Context);
        PermissionText.Content = current.Context is null ? "Capture inactive" : PermissionLabel(current.Context.Provenance);

        SetExpandedVisibility(SourceText, !_collapsed);
        SetExpandedVisibility(CaptureButton, !_collapsed);
        SetExpandedVisibility(PromptText, !_collapsed);
        SetExpandedVisibility(ContextPanel, !_collapsed);
        SetExpandedVisibility(Composer, !_collapsed);
        SetExpandedVisibility(StatusText, !_collapsed && !string.IsNullOrWhiteSpace(StatusText.Content));
        SetExpandedVisibility(SuggestedActions, !_collapsed && SuggestedActions.Items.Count > 0);
        SetExpandedVisibility(Actions, !_collapsed && Actions.Items.Count > 0);
        RebuildSessions(snapshot, windowSessionId);
    }

    public void SetActions(IReadOnlyList<OverlayContextActionDescriptor> actions)
    {
        _dynamicUi.Clear("Overlay.Actions");
        for (var index = 0; index < actions.Count; index++)
        {
            var descriptor = actions[index];
            var item = _dynamicUi.CreateItem(
                "OverlayActionChip", "Overlay.Actions", ActionId(descriptor.Id, index),
                new Dictionary<string, object?> { ["LABEL"] = ActionLabel(descriptor) }, index);
            var button = item.GetComponent<HavenButton>("Invoke");
            button.Variant = descriptor.Id.Equals("ask-haven", StringComparison.OrdinalIgnoreCase)
                ? ButtonVariant.Primary : descriptor.IsGenerated ? ButtonVariant.Secondary : ButtonVariant.Ghost;
            button.Accessibility.AccessibleName = descriptor.Label;
            button.Invoked += (_, _) => ActionRequested?.Invoke(this, descriptor);
        }
        SetExpandedVisibility(Actions, !_collapsed && actions.Count > 0);
    }

    public void SetSuggestions(IReadOnlyList<OverlayCompactSuggestion> suggestions)
    {
        _dynamicUi.Clear("Overlay.Suggestions");
        for (var index = 0; index < suggestions.Count; index++)
        {
            var suggestion = suggestions[index];
            var item = _dynamicUi.CreateItem(
                "OverlayActionChip", "Overlay.Suggestions", "suggestion-" + index,
                new Dictionary<string, object?> { ["LABEL"] = suggestion.Label }, index);
            var button = item.GetComponent<HavenButton>("Invoke");
            button.Variant = index == 0 ? ButtonVariant.Secondary : ButtonVariant.Ghost;
            button.Accessibility.AccessibleName = suggestion.Label;
            button.Accessibility.Description = suggestion.Instruction;
            var requestedIndex = index;
            button.Invoked += (_, _) => SuggestionRequested?.Invoke(this, requestedIndex);
        }
        SetExpandedVisibility(SuggestedActions, !_collapsed && suggestions.Count > 0);
    }

    public void SetDraft(string? text)
    {
        ComposerInput.Text = text ?? string.Empty;
        ComposerInput.PlaceCaretAtEnd();
    }

    public void ClearDraft() => ComposerInput.Text = string.Empty;

    public void SetStatus(string? text)
    {
        StatusText.Content = text ?? string.Empty;
        SetExpandedVisibility(StatusText, !_collapsed && !string.IsNullOrWhiteSpace(text));
    }

    internal void SubmitComposer()
    {
        var text = ComposerInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(text)) return;
        SubmitRequested?.Invoke(this, text);
    }

    private void RebuildSessions(OverlayWorkspaceSnapshot snapshot, Guid windowSessionId)
    {
        _dynamicUi.Clear("Overlay.SessionTabs");
        for (var index = 0; index < snapshot.Sessions.Count; index++)
        {
            var session = snapshot.Sessions[index];
            var item = _dynamicUi.CreateItem(
                "OverlaySessionTab", "Overlay.SessionTabs", session.Id.ToString("N"),
                new Dictionary<string, object?> { ["LABEL"] = session.Title + (session.IsPinned ? " · pinned" : "") }, index);
            var button = item.GetComponent<HavenButton>("Activate");
            button.Variant = session.Id == windowSessionId ? ButtonVariant.Primary : ButtonVariant.Secondary;
            var id = session.Id;
            button.Invoked += (_, _) => SessionActivated?.Invoke(this, id);
        }
    }

    private static string ContextIdentity(OverlaySessionState session)
    {
        var app = session.AppKey.Equals("go", StringComparison.OrdinalIgnoreCase) ? "Go" : "Chat";
        return string.IsNullOrWhiteSpace(session.SourceAssociation) ? app : app + " · " + session.SourceAssociation;
    }

    private static string SourceLabel(OverlaySessionState session)
    {
        var provenance = session.Context?.Provenance;
        var parts = new[] { provenance?.SourceApplication, provenance?.SourceWindow }
            .Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!);
        var label = string.Join(" · ", parts);
        return string.IsNullOrWhiteSpace(label)
            ? "Screen context"
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
        return string.IsNullOrWhiteSpace(provenance.PermissionDescription) ? state : state + " · " + provenance.PermissionDescription;
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

    private static void SetExpandedVisibility(HavenElement element, bool visible) =>
        Set(element, HavenProperties.Visibility, visible ? HavenVisibility.Visible : HavenVisibility.Collapsed);

    private static void Set<T>(HavenElement element, HavenProperty<T> property, T value) => element.SetValue(property, value);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _dynamicUi.Clear("Overlay.SessionTabs");
        _dynamicUi.Clear("Overlay.Actions");
        _dynamicUi.Clear("Overlay.Suggestions");
    }
}
#endif
