using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace Haven.Desktop.HavenUI.Components;

/// <summary>Canonical stateful push button for reveal and selection actions.</summary>
public sealed class HavenToggleButton : ToggleButton
{
    public HavenToggleButton()
    {
        Theme = HavenControlThemeResolver.For(typeof(ToggleButton));
        Classes.Add("havenToggleButton");
    }
}

/// <summary>Canonical disclosure surface for advanced or secondary controls.</summary>
public sealed class HavenExpander : Expander
{
    public HavenExpander()
    {
        Theme = HavenControlThemeResolver.For(typeof(Expander));
        Classes.Add("havenExpander");
    }
}

/// <summary>Canonical menu flyout for declarative button menus.</summary>
public sealed class HavenMenuFlyout : MenuFlyout
{
    public HavenMenuFlyout() => FlyoutPresenterClasses.Add("havenMenuFlyoutPresenter");
}
