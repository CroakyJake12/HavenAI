using Avalonia;
using Avalonia.Controls;

namespace Haven.Desktop.HavenUI.Components;

public enum HavenDropdownItemRole
{
    Main,
    Important,
    Negative,
    Invisible
}

/// <summary>A typed drop-down row with the four roles specified on slide 5.</summary>
public sealed class HavenDropdownItemButton : HavenButtonBase
{
    public static readonly StyledProperty<HavenDropdownItemRole> RoleProperty =
        AvaloniaProperty.Register<HavenDropdownItemButton, HavenDropdownItemRole>(
            nameof(Role), HavenDropdownItemRole.Main);

    public static readonly StyledProperty<bool> OpensNestedSurfaceProperty =
        AvaloniaProperty.Register<HavenDropdownItemButton, bool>(nameof(OpensNestedSurface));

    public HavenDropdownItemButton() : base("havenDropdownItem")
    {
        ApplyRole(Role);
    }

    public HavenDropdownItemRole Role
    {
        get => GetValue(RoleProperty);
        set => SetValue(RoleProperty, value);
    }

    public bool OpensNestedSurface
    {
        get => GetValue(OpensNestedSurfaceProperty);
        set => SetValue(OpensNestedSurfaceProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == RoleProperty)
            ApplyRole(change.GetNewValue<HavenDropdownItemRole>());
        if (change.Property == OpensNestedSurfaceProperty)
            Classes.Set("nested", change.GetNewValue<bool>());
    }

    private void ApplyRole(HavenDropdownItemRole role)
    {
        Classes.Set("important", role == HavenDropdownItemRole.Important);
        Classes.Set("negative", role == HavenDropdownItemRole.Negative);
        Classes.Set("invisible", role == HavenDropdownItemRole.Invisible);
        Classes.Set("main", role == HavenDropdownItemRole.Main);
    }
}

/// <summary>A sticky search/header row that remains visible in long menus.</summary>
public sealed class HavenStickyDropdownHeader : ContentControl
{
    public HavenStickyDropdownHeader() => Classes.Add("havenStickyDropdownHeader");
}

/// <summary>Canonical desktop context menu; touch hosts present the same actions in a sheet.</summary>
public sealed class HavenContextMenu : ContextMenu
{
    public HavenContextMenu()
    {
        Theme = HavenControlThemeResolver.For(typeof(ContextMenu));
        Classes.Add("havenContextMenu");
    }
}

/// <summary>Canonical semantic row shared by context, overflow and popup menus.</summary>
public sealed class HavenMenuItem : MenuItem
{
    public HavenMenuItem()
    {
        Theme = HavenControlThemeResolver.For(typeof(MenuItem));
        Classes.Add("havenMenuItem");
    }
}

/// <summary>Canonical selectable collection surface for settings and utility pages.</summary>
public sealed class HavenListBox : ListBox
{
    public HavenListBox()
    {
        Theme = HavenControlThemeResolver.For(typeof(ListBox));
        Classes.Add("havenListBox");
    }
}

/// <summary>Canonical selectable row used by custom list item templates.</summary>
public sealed class HavenListBoxItem : ListBoxItem
{
    public HavenListBoxItem()
    {
        Theme = HavenControlThemeResolver.For(typeof(ListBoxItem));
        Classes.Add("havenListBoxItem");
    }
}
