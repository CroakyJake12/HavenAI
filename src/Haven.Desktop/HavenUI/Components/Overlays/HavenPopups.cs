using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace Haven.Desktop.HavenUI.Components;

/// <summary>
/// Canonical light-dismiss bridge for older code-built flyouts whose semantic
/// role is not yet specific enough for HavenPopup or HavenDropdown.
/// </summary>
public sealed class HavenAdaptivePopup : Flyout
{
    public HavenAdaptivePopup()
    {
        ShowMode = FlyoutShowMode.Transient;
        FlyoutPresenterClasses.Add("havenAdaptivePopupPresenter");
    }
}

/// <summary>
/// Canonical light-dismiss desktop popup. Content should be a HavenPopupCard;
/// clicking outside closes it as required by slide 6.
/// </summary>
public class HavenPopup : Flyout
{
    public HavenPopup()
    {
        Placement = PlacementMode.AnchorAndGravity;
        PlacementAnchor = Avalonia.Controls.Primitives.PopupPositioning.PopupAnchor.Bottom;
        PlacementGravity = Avalonia.Controls.Primitives.PopupPositioning.PopupGravity.Bottom;
        ShowMode = FlyoutShowMode.Transient;
        FlyoutPresenterClasses.Add("havenPopupPresenter");
    }
}

/// <summary>Bottom-edge, touch-friendly light-dismiss popup for compact layouts.</summary>
public sealed class HavenMobilePopup : Flyout
{
    public HavenMobilePopup()
    {
        Placement = PlacementMode.BottomEdgeAlignedLeft;
        ShowMode = FlyoutShowMode.Transient;
        FlyoutPresenterClasses.Add("havenMobilePopupPresenter");
    }
}

/// <summary>Blurred menu flyout using canonical dropdown presenter styling.</summary>
public sealed class HavenDropdown : Flyout
{
    public HavenDropdown()
    {
        Placement = PlacementMode.BottomEdgeAlignedLeft;
        ShowMode = FlyoutShowMode.Transient;
        FlyoutPresenterClasses.Add("havenDropdownPresenter");
    }
}
