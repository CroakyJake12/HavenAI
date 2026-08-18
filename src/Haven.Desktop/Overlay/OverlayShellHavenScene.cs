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
    private bool _disposed;

    public OverlayShellHavenScene()
    {
        Root = new Page { Name = "Overlay.Root", Layout = HavenLayout.Vertical };
        Set(Root, HavenProperties.Width, HavenLength.Percent(100));
        Set(Root, HavenProperties.Background, "Transparent");
        Set(Root, HavenProperties.Gap, HavenLength.Px(8));

        var header = new OverlayDragHandle { Name = "Overlay.Header", Layout = HavenLayout.Grid, Columns = "1fr Auto Auto Auto Auto" };
        Set(header, HavenProperties.Width, HavenLength.Percent(100));
        Set(header, HavenProperties.Gap, HavenLength.Px(6));
        header.DragDelta += delta => DragDelta?.Invoke(delta);

        var identity = new Container { Layout = HavenLayout.Vertical };
        TitleText = new HavenText("Overlay") { Name = "Overlay.Title", Level = TextLevel.H4 };
        SourceText = Secondary("Independent Haven workspace", "Overlay.Source");
        identity.Add(TitleText);
        identity.Add(SourceText);
        header.Add(identity);

        NewChatButton = Action("Overlay.NewChat", "New chat", ButtonVariant.Secondary);
        PinButton = Action("Overlay.Pin", "Pin", ButtonVariant.Ghost);
        CollapseButton = Action("Overlay.Collapse", "Collapse", ButtonVariant.Ghost);
        CloseButton = Action("Overlay.Close", "Close", ButtonVariant.Ghost);
        Set(NewChatButton, HavenProperties.Column, 1);
        Set(PinButton, HavenProperties.Column, 2);
        Set(CollapseButton, HavenProperties.Column, 3);
        Set(CloseButton, HavenProperties.Column, 4);
        header.Add(NewChatButton);
        header.Add(PinButton);
        header.Add(CollapseButton);
        header.Add(CloseButton);
        Root.Add(header);

        SessionTabs = new DynamicUIRuntime { Name = "Overlay.SessionTabs", Layout = HavenLayout.Horizontal };
        Set(SessionTabs, HavenProperties.Width, HavenLength.Percent(100));
        Set(SessionTabs, HavenProperties.Gap, HavenLength.Px(6));
        Set(SessionTabs, HavenProperties.Overflow, HavenOverflow.Scroll);
        Root.Add(SessionTabs);

        ContextPanel = new Container { Name = "Overlay.Context", Layout = HavenLayout.Vertical };
        Set(ContextPanel, HavenProperties.Width, HavenLength.Percent(100));
        Set(ContextPanel, HavenProperties.Padding, HavenThickness.Parse("10px 12px"));
        Set(ContextPanel, HavenProperties.Background, "SurfaceRaised");
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

        _dynamicUi = new DynamicUI(Root, HavenDynamicUITemplateCatalog.FromAssembly(typeof(OverlayShellHavenScene).Assembly));
        NewChatButton.Invoked += (_, _) => NewChatRequested?.Invoke(this, EventArgs.Empty);
        PinButton.Invoked += (_, _) => PinToggleRequested?.Invoke(this, EventArgs.Empty);
        CollapseButton.Invoked += (_, _) => CollapseToggleRequested?.Invoke(this, EventArgs.Empty);
        CloseButton.Invoked += (_, _) => CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? NewChatRequested;
    public event EventHandler? PinToggleRequested;
    public event EventHandler? CollapseToggleRequested;
    public event EventHandler? CloseRequested;
    public event EventHandler<Guid>? SessionActivated;
    public event EventHandler<OverlayContextActionDescriptor>? ActionRequested;
    public event Action<HavenPoint>? DragDelta;

    public Page Root { get; }
    public HavenText TitleText { get; }
    public HavenText SourceText { get; }
    public HavenButton NewChatButton { get; }
    public HavenButton PinButton { get; }
    public HavenButton CollapseButton { get; }
    public HavenButton CloseButton { get; }
    public DynamicUIRuntime SessionTabs { get; }
    public Container ContextPanel { get; }
    public HavenText ContextSummary { get; }
    public HavenText PermissionText { get; }
    public DynamicUIRuntime Actions { get; }

    public void ApplySnapshot(OverlayWorkspaceSnapshot snapshot, Guid windowSessionId)
    {
        var current = snapshot.Sessions.FirstOrDefault(session => session.Id == windowSessionId);
        if (current is null) return;

        TitleText.Content = current.Title;
        SourceText.Content = SourceLabel(current);
        PinButton.Content = current.IsPinned ? "Unpin" : "Pin";
        PinButton.Variant = current.IsPinned ? ButtonVariant.Primary : ButtonVariant.Ghost;
        CollapseButton.Content = current.IsCollapsed ? "Expand" : "Collapse";
        CollapseButton.Variant = current.IsCollapsed ? ButtonVariant.Secondary : ButtonVariant.Ghost;
        ContextSummary.Content = current.Context is null ? "No selected context." : ContextLabel(current.Context);
        PermissionText.Content = current.Context is null ? "Capture inactive" : PermissionLabel(current.Context.Provenance);
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

    private static string SourceLabel(OverlaySessionState session)
    {
        var provenance = session.Context?.Provenance;
        var parts = new[] { provenance?.SourceApplication, provenance?.SourceWindow }
            .Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!);
        var label = string.Join(" · ", parts);
        return string.IsNullOrWhiteSpace(label)
            ? session.SourceAssociation ?? "Independent Haven workspace"
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

    private static void Set<T>(HavenElement element, HavenProperty<T> property, T value) => element.SetValue(property, value);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _dynamicUi.Clear("Overlay.SessionTabs");
        _dynamicUi.Clear("Overlay.Actions");
    }
}
#endif
