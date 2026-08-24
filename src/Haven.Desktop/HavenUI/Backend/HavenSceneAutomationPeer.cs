using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.VisualTree;
using Haven.UI;
using Haven.UI.Components;

namespace Haven.Desktop.HavenUI.Backend;

/// <summary>Projects the backend-neutral Haven scene tree into Avalonia automation/UIA.</summary>
internal sealed class HavenSceneAutomationPeer : ControlAutomationPeer
{
    private readonly HavenSceneControl _owner;
    private readonly Dictionary<HavenElement, HavenElementAutomationPeer> _elementPeers = [];

    public HavenSceneAutomationPeer(HavenSceneControl owner) : base(owner)
    {
        _owner = owner;
    }

    protected override AutomationControlType GetAutomationControlTypeCore() =>
        _owner.Root is { } root ? MapRole(root.Accessibility.Role) : AutomationControlType.Pane;

    protected override string GetClassNameCore() => nameof(HavenSceneControl);
    protected override string? GetAutomationIdCore() => _owner.Root?.Name;

    protected override string? GetNameCore()
    {
        var root = _owner.Root;
        if (root is null) return "Haven";
        if (!string.IsNullOrWhiteSpace(root.Accessibility.AccessibleName)) return root.Accessibility.AccessibleName;
        if (!string.IsNullOrWhiteSpace(root.Name)) return root.Name;
        return root.Metadata.ComponentName;
    }

    protected override string? GetHelpTextCore() => _owner.Root?.Accessibility.Description;

    protected override IReadOnlyList<AutomationPeer> GetOrCreateChildrenCore() =>
        _owner.Root is { } root ? GetChildrenFor(root, this) : [];

    internal IReadOnlyList<AutomationPeer> GetChildrenFor(HavenElement parent, AutomationPeer parentPeer)
    {
        var result = new List<AutomationPeer>();
        foreach (var child in parent.Children) AddSemanticChild(child, parentPeer, result);
        return result;
    }

    internal bool IsExposed(HavenElement element)
    {
        for (HavenElement? current = element; current is not null; current = current.Parent)
        {
            if (!current.IsIncluded || current.GetValue(HavenProperties.Visibility) != HavenVisibility.Visible) return false;
            if (ReferenceEquals(current, _owner.Root)) return true;
        }
        return false;
    }

    internal Rect BoundsInTopLevel(HavenElement element) => BoundsInTopLevel(element.Bounds);

    internal Rect BoundsInTopLevel(HavenRect bounds)
    {
        var fallback = new Rect(bounds.X, bounds.Y, Math.Max(0d, bounds.Width), Math.Max(0d, bounds.Height));
        var topLevel = Avalonia.Controls.TopLevel.GetTopLevel(_owner);
        if (topLevel is null) return fallback;
        var translated = _owner.TranslatePoint(new Point(bounds.X, bounds.Y), topLevel);
        return translated is { } origin
            ? new Rect(origin.X, origin.Y, fallback.Width, fallback.Height)
            : fallback;
    }

    internal bool IsOffscreen(HavenElement element)
    {
        if (!IsExposed(element)) return true;
        var bounds = element.Bounds;
        if (bounds.Width <= 0d || bounds.Height <= 0d || _owner.Root is not { } root) return true;
        var viewport = root.Bounds;
        return bounds.Right <= viewport.X || bounds.Bottom <= viewport.Y || bounds.X >= viewport.Right || bounds.Y >= viewport.Bottom;
    }

    private void AddSemanticChild(HavenElement element, AutomationPeer parentPeer, List<AutomationPeer> result)
    {
        if (!IsExposed(element)) return;
        if (element.Accessibility.Role == HavenAccessibleRole.None)
        {
            foreach (var child in element.Children) AddSemanticChild(child, parentPeer, result);
            return;
        }

        var peer = GetOrCreateElementPeer(element);
        peer.AttachTo(parentPeer);
        result.Add(peer);
    }

    internal HavenElementAutomationPeer GetOrCreateElementPeer(HavenElement element)
    {
        if (_elementPeers.TryGetValue(element, out var peer)) return peer;
        peer = CreateElementPeer(element);
        _elementPeers[element] = peer;
        return peer;
    }

    private HavenElementAutomationPeer CreateElementPeer(HavenElement element) => element switch
    {
        Haven.UI.Components.TabStrip tabs => new HavenTabAutomationPeer(_owner, this, tabs),
        Button button when button.Accessibility.Role == HavenAccessibleRole.TabItem => new HavenTabItemAutomationPeer(_owner, this, button),
        Button button => new HavenButtonAutomationPeer(_owner, this, button),
        Toggle toggle => new HavenToggleAutomationPeer(_owner, this, toggle),
        Input input => new HavenInputAutomationPeer(_owner, this, input),
        Slider slider => new HavenSliderAutomationPeer(_owner, this, slider),
        Select select => new HavenSelectAutomationPeer(_owner, this, select),
        _ => new HavenElementAutomationPeer(_owner, this, element)
    };

    internal static AutomationControlType MapRole(HavenAccessibleRole role) => role switch
    {
        HavenAccessibleRole.Window => AutomationControlType.Window,
        HavenAccessibleRole.Group => AutomationControlType.Group,
        HavenAccessibleRole.Text => AutomationControlType.Text,
        HavenAccessibleRole.Button => AutomationControlType.Button,
        HavenAccessibleRole.Input => AutomationControlType.Edit,
        HavenAccessibleRole.CheckBox => AutomationControlType.CheckBox,
        HavenAccessibleRole.Slider => AutomationControlType.Slider,
        HavenAccessibleRole.List => AutomationControlType.List,
        HavenAccessibleRole.ListItem => AutomationControlType.ListItem,
        HavenAccessibleRole.Tab => AutomationControlType.Tab,
        HavenAccessibleRole.TabItem => AutomationControlType.TabItem,
        HavenAccessibleRole.Image => AutomationControlType.Image,
        HavenAccessibleRole.Link => AutomationControlType.Hyperlink,
        HavenAccessibleRole.Menu => AutomationControlType.Menu,
        HavenAccessibleRole.MenuItem => AutomationControlType.MenuItem,
        HavenAccessibleRole.Dialog => AutomationControlType.Window,
        _ => AutomationControlType.Custom
    };
}

internal class HavenElementAutomationPeer : ControlAutomationPeer
{
    private readonly HavenSceneControl _owner;
    private readonly HavenSceneAutomationPeer _rootPeer;
    private readonly HavenElement _element;
    private AutomationPeer? _parent;

    public HavenElementAutomationPeer(HavenSceneControl owner, HavenSceneAutomationPeer rootPeer, HavenElement element)
        : base(owner)
    {
        _owner = owner;
        _rootPeer = rootPeer;
        _element = element;
        _element.Invalidated += OnElementInvalidated;
    }

    protected HavenSceneControl SceneOwner => _owner;
    protected HavenSceneAutomationPeer RootPeer => _rootPeer;
    protected HavenElement Element => _element;
    protected bool CanInteract => IsEnabledCore();

    internal void AttachTo(AutomationPeer parent) => _parent = parent;

    protected virtual void OnSemanticStateInvalidated() { }

    private void OnElementInvalidated(object? sender, EventArgs e) => OnSemanticStateInvalidated();

    protected override AutomationControlType GetAutomationControlTypeCore() => HavenSceneAutomationPeer.MapRole(_element.Accessibility.Role);
    protected override string GetClassNameCore() => _element.GetType().Name;
    protected override string? GetAutomationIdCore() => _element.Name;
    protected override Rect GetBoundingRectangleCore() => _rootPeer.BoundsInTopLevel(_element);
    protected override IReadOnlyList<AutomationPeer> GetOrCreateChildrenCore() => _rootPeer.GetChildrenFor(_element, this);
    protected override AutomationPeer? GetParentCore() => _parent;
    protected override string? GetHelpTextCore() => _element.Accessibility.Description;
    protected override string? GetPlaceholderTextCore() => _element is Input input ? input.Placeholder : null;

    protected override string? GetNameCore()
    {
        if (!string.IsNullOrWhiteSpace(_element.Accessibility.AccessibleName)) return _element.Accessibility.AccessibleName;
        return _element switch
        {
            Text text when !string.IsNullOrWhiteSpace(text.Content) => text.Content,
            Button button when !string.IsNullOrWhiteSpace(button.Content) => button.Content,
            Input input when !string.IsNullOrWhiteSpace(input.Placeholder) => input.Placeholder,
            Select select when !string.IsNullOrWhiteSpace(select.SelectedItem) => select.SelectedItem,
            _ when !string.IsNullOrWhiteSpace(_element.Name) => _element.Name,
            _ => _element.Metadata.ComponentName
        };
    }

    protected override bool HasKeyboardFocusCore() => _element.State.HasFlag(HavenElementState.Focused);
    protected override bool IsEnabledCore() => _element.Accessibility.Enabled && _element.GetValue(HavenProperties.Enabled) && !_element.State.HasFlag(HavenElementState.Disabled);
    protected override bool IsKeyboardFocusableCore() => _element.Accessibility.Focusable && IsEnabledCore();
    protected override bool IsControlElementCore() => _element.Accessibility.Role != HavenAccessibleRole.None;
    protected override bool IsContentElementCore() => _element.Accessibility.Role is not HavenAccessibleRole.None and not HavenAccessibleRole.Group and not HavenAccessibleRole.Window and not HavenAccessibleRole.Dialog;
    protected override bool IsOffscreenCore() => _rootPeer.IsOffscreen(_element);

    protected override void SetFocusCore()
    {
        if (IsKeyboardFocusableCore()) _owner.FocusElement(_element);
    }
}
