namespace Haven.UI.Components;

public sealed record PopupMenuItem(
    string Label,
    Action Action,
    bool Destructive = false,
    string IconKey = "",
    bool Enabled = true);

/// <summary>
/// Haven-owned detached popup menu. The full-scene layer provides light-dismiss semantics while
/// the menu card is canvas-positioned beside its anchor, so opening it never participates in the
/// anchor row/message layout.
/// </summary>
public sealed class PopupMenu : Container
{
    private const double Edge = 8d;
    private const double AnchorGap = 4d;

    public PopupMenu(HavenElement anchor, HavenElement sceneRoot, IReadOnlyList<PopupMenuItem> items, double menuWidth = 220d, string accessibleName = "Actions menu")
    {
        ArgumentNullException.ThrowIfNull(anchor);
        ArgumentNullException.ThrowIfNull(sceneRoot);
        ArgumentNullException.ThrowIfNull(items);

        Layout = HavenLayout.Canvas;
        Name = "PopupMenuOverlay";
        SetValue(HavenProperties.Width, HavenLength.Percent(100));
        SetValue(HavenProperties.Height, HavenLength.Percent(100));
        SetValue(HavenProperties.LayoutParticipation, HavenLayoutParticipation.Overlay);
        SetValue(HavenProperties.ZIndex, 500);
        SetValue(HavenProperties.PointerEvents, HavenPointerEvents.ChildrenOnly);

        var dismiss = new Button { Variant = ButtonVariant.Text, Content = string.Empty, Name = "PopupDismiss" };
        dismiss.Accessibility.AccessibleName = "Dismiss menu";
        dismiss.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        dismiss.SetValue(HavenProperties.Height, HavenLength.Percent(100));
        dismiss.SetValue(HavenProperties.MinHeight, HavenLength.Px(0));
        dismiss.SetValue(HavenProperties.Padding, HavenThickness.Zero);
        dismiss.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(0)));
        dismiss.SetValue(HavenProperties.Background, "Transparent");
        dismiss.SetValue(HavenProperties.ZIndex, 0);
        dismiss.Invoked += (_, _) => Dismiss();
        Add(dismiss);

        Card = new Container { Layout = HavenLayout.Vertical, Name = "PopupMenuCard" };
        Card.Accessibility.AccessibleName = accessibleName;
        Card.SetValue(HavenProperties.Width, HavenLength.Px(menuWidth));
        Card.SetValue(HavenProperties.MaxWidth, HavenLength.Percent(92));
        Card.SetValue(HavenProperties.Background, "SurfaceRaised");
        Card.SetValue(HavenProperties.BorderColor, "Border");
        Card.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
        Card.SetValue(HavenProperties.Padding, HavenThickness.Uniform(HavenLength.Px(6)));
        Card.SetValue(HavenProperties.Gap, HavenLength.Px(2));
        Card.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(14)));
        Card.SetValue(HavenProperties.Shadow, "Card");
        Card.SetValue(HavenProperties.ZIndex, 1);

        foreach (var item in items)
        {
            var menuItem = new Button
            {
                Content = item.Label,
                IconKey = item.IconKey,
                Variant = item.Destructive ? ButtonVariant.Danger : ButtonVariant.Navigation
            };
            menuItem.SetValue(HavenProperties.Width, HavenLength.Percent(100));
            menuItem.SetValue(HavenProperties.MinHeight, HavenLength.Px(34));
            menuItem.SetValue(HavenProperties.Padding, HavenThickness.Parse("7px 10px"));
            menuItem.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(10)));
            menuItem.SetValue(HavenProperties.FontSize, 13d);
            menuItem.SetValue(HavenProperties.Enabled, item.Enabled);
            menuItem.SetState(HavenElementState.Disabled, !item.Enabled);
            menuItem.Invoked += (_, _) =>
            {
                if (!item.Enabled) return;
                Dismiss();
                item.Action();
            };
            Card.Add(menuItem);
        }

        Add(Card);
        Position(anchor, sceneRoot, menuWidth, items.Count);
    }

    public Container Card { get; }
    public event EventHandler? Dismissed;

    public void Dismiss()
    {
        var parent = Parent;
        if (parent is not null) parent.Remove(this);
        Dismissed?.Invoke(this, EventArgs.Empty);
    }

    private void Position(HavenElement anchor, HavenElement sceneRoot, double menuWidth, int itemCount)
    {
        var rootWidth = Math.Max(sceneRoot.Bounds.Width, menuWidth + Edge * 2);
        var rootHeight = Math.Max(sceneRoot.Bounds.Height, 120d);
        var anchorTop = anchor.Bounds.Y - sceneRoot.Bounds.Y;
        var anchorRight = anchor.Bounds.Right - sceneRoot.Bounds.X;
        var anchorBottom = anchor.Bounds.Bottom - sceneRoot.Bounds.Y;
        var estimatedHeight = 12d + Math.Max(1, itemCount) * 36d;

        var left = Math.Clamp(anchorRight - menuWidth, Edge, Math.Max(Edge, rootWidth - menuWidth - Edge));
        var top = anchorBottom + AnchorGap;
        if (top + estimatedHeight > rootHeight - Edge)
            top = Math.Max(Edge, anchorTop - estimatedHeight - AnchorGap);

        Card.SetValue(HavenProperties.Left, HavenLength.Px(left));
        Card.SetValue(HavenProperties.Top, HavenLength.Px(top));
        Card.SetValue(HavenProperties.MaxHeight, HavenLength.Px(Math.Max(80d, rootHeight - Edge * 2)));
        Card.SetValue(HavenProperties.Overflow, HavenOverflow.Scroll);
    }
}
