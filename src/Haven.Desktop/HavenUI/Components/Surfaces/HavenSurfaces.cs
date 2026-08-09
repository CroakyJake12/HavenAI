using Avalonia;
using Avalonia.Controls;

namespace Haven.Desktop.HavenUI.Components;

/// <summary>
/// Canonical adaptive surface for older composed layouts. A plain instance is
/// structurally transparent; semantic classes are resolved by the central
/// theme. Rebuilt screens should prefer the specific surface classes below.
/// </summary>
public sealed class HavenAdaptiveSurface : Border
{
    public HavenAdaptiveSurface() => Classes.Add("havenAdaptiveSurface");
}

/// <summary>Standard content card used across all Haven screens.</summary>
public class HavenCard : Border
{
    public HavenCard() => Classes.Add("havenCard");
}

/// <summary>Blurred menu/dropdown container from slide 5.</summary>
public sealed class HavenDropdownCard : HavenCard
{
    public HavenDropdownCard() => Classes.Add("havenDropdownCard");
}

/// <summary>Large dismissible dialog surface from slide 6.</summary>
public sealed class HavenPopupCard : HavenCard
{
    public HavenPopupCard() => Classes.Add("havenPopupCard");
}

/// <summary>
/// Bottom-anchored compact popup. Desktop popups and mobile sheets share
/// typography, action rows and glow; only placement and corner geometry differ.
/// </summary>
public sealed class HavenMobileSheet : HavenCard
{
    public HavenMobileSheet() => Classes.Add("havenMobileSheet");
}

/// <summary>Compact rounded semantic label or status surface.</summary>
public sealed class HavenPill : Border
{
    public HavenPill() => Classes.Add("havenPill");
}

/// <summary>Compact swipe affordance shared by every mobile sheet and drawer.</summary>
public sealed class HavenDragHandle : Border
{
    public HavenDragHandle() => Classes.Add("havenDragHandle");
}

/// <summary>Two-pixel live-gradient marker used by selected navigation tabs.</summary>
public sealed class HavenSelectionIndicator : Border
{
    public HavenSelectionIndicator() => Classes.Add("havenSelectionIndicator");
}

/// <summary>The floating message/task composer shell used on every mode.</summary>
public sealed class HavenComposerShell : Border
{
    public HavenComposerShell() => Classes.Add("havenComposer");
}

/// <summary>The dark grouped navigation surface used by sidebars.</summary>
public sealed class HavenSidebarSurface : Border
{
    public HavenSidebarSurface() => Classes.Add("havenSidebar");
}

/// <summary>Reusable centred empty-state container.</summary>
public sealed class HavenEmptyState : ContentControl
{
    public HavenEmptyState() => Classes.Add("havenEmptyState");
}

/// <summary>Responsive page host with canonical safe padding and max width.</summary>
public sealed class HavenPageSurface : ContentControl
{
    public static readonly StyledProperty<double> ContentMaxWidthProperty =
        AvaloniaProperty.Register<HavenPageSurface, double>(nameof(ContentMaxWidth), 1120d);

    public HavenPageSurface() => Classes.Add("havenPage");

    public double ContentMaxWidth
    {
        get => GetValue(ContentMaxWidthProperty);
        set => SetValue(ContentMaxWidthProperty, value);
    }
}

/// <summary>Low-elevation grouped content panel.</summary>
public sealed class HavenPanel : Border
{
    public HavenPanel() => Classes.Add("havenPanel");
}

/// <summary>Canonical horizontal command/action surface.</summary>
public sealed class HavenToolbar : Border
{
    public HavenToolbar() => Classes.Add("havenToolbar");
}

/// <summary>Canonical application navigation rail surface.</summary>
public sealed class HavenNavigationRail : Border
{
    public HavenNavigationRail() => Classes.Add("havenNavigationRail");
}

/// <summary>Canonical page header surface.</summary>
public sealed class HavenHeader : Border
{
    public HavenHeader() => Classes.Add("havenHeader");
}

/// <summary>Canonical page footer/action surface.</summary>
public sealed class HavenFooter : Border
{
    public HavenFooter() => Classes.Add("havenFooter");
}

/// <summary>Canonical full-surface dim/blur overlay host.</summary>
public sealed class HavenOverlay : Border
{
    public HavenOverlay() => Classes.Add("havenOverlay");
}
