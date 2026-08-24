using Avalonia.Controls;

namespace Haven.Desktop.HavenUI.Components;

/// <summary>Canonical content tabber for page-level views.</summary>
public sealed class HavenTabView : TabControl
{
    public HavenTabView()
    {
        Theme = HavenControlThemeResolver.For(typeof(TabControl));
        Classes.Add("havenTabView");
    }
}

/// <summary>Canonical item used by HavenTabView.</summary>
public sealed class HavenTabItem : TabItem
{
    public HavenTabItem()
    {
        Theme = HavenControlThemeResolver.For(typeof(TabItem));
        Classes.Add("havenTabItem");
    }
}
