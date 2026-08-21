using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Haven.Core;
using Haven.Desktop.HavenUI.Backend;
using Haven.Desktop.Views.Pages.Write;
using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;

namespace Haven.Desktop.Tests;

public sealed class WriteRibbonLayoutTests
{
    [AvaloniaFact]
    public void Write_ribbon_uses_named_tool_groups_and_keeps_the_page_canvas_dominant()
    {
        using var scene = new WordWriteHavenScene();
        scene.SetDocument(NotesDocument.Create("Structured document"), 0, 1);
        var host = new HavenSceneControl { Root = scene.Root };
        var window = new Window { Width = 1200, Height = 850, Content = host };
        try
        {
            window.Show();
            window.UpdateLayout();

            var groups = scene.RibbonContent.Children.OfType<Container>().ToArray();
            Assert.True(groups.Length >= 3);
            Assert.All(groups, group => Assert.StartsWith("Write.Ribbon.Group.", group.Name, StringComparison.Ordinal));
            Assert.DoesNotContain(scene.RibbonContent.Children, child => child is HavenButton);
            Assert.Contains(groups, group => group.Name == "Write.Ribbon.Group.Font");
            Assert.Contains(groups, group => group.Name == "Write.Ribbon.Group.Formatting");
            Assert.Contains(groups, group => group.Name == "Write.Ribbon.Group.Paragraph");
            Assert.DoesNotContain(scene.RibbonContent.DescendantsAndSelf().OfType<HavenButton>(), button => button.Variant == ButtonVariant.Tertiary);
            Assert.Equal(new HavenElement[] { scene.Header, scene.QuickBar, scene.Ribbon, scene.Ruler }, scene.Chrome.Children);
            Assert.Equal(new HavenElement[] { scene.Chrome, scene.Scroller, scene.StatusBar }, scene.Root.Children);
            Assert.True(scene.Header.Bounds.Bottom <= scene.QuickBar.Bounds.Y + 0.1);
            Assert.True(scene.QuickBar.Bounds.Bottom <= scene.Ribbon.Bounds.Y + 0.1);
            Assert.True(scene.Ribbon.Bounds.Bottom <= scene.Ruler.Bounds.Y + 0.1);
            Assert.True(scene.Ruler.Bounds.Bottom <= scene.Scroller.Bounds.Y + 0.1);
            Assert.True(scene.DocumentSurface.Bounds.Width > 760);
            Assert.True(scene.DocumentSurface.Bounds.Height > scene.Ribbon.Bounds.Height * 2);
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Write_navigation_tabs_are_compact_text_tabs_not_filled_pills()
    {
        using var scene = new WordWriteHavenScene();
        scene.SetDocument(NotesDocument.Create("Tabs"), 0, 1);

        foreach (var tab in new[] { scene.HomeTab, scene.InsertTab, scene.LayoutTab, scene.ReviewTab })
        {
            Assert.Equal(ButtonVariant.Text, tab.Variant);
            Assert.Equal(tab == scene.HomeTab ? "ButtonTextPrimary" : "ButtonTextSecondary", tab.GetValue(HavenProperties.Foreground));
            Assert.Equal(HavenLength.Px(30), tab.GetValue(HavenProperties.MinHeight));
            Assert.Equal(HavenLength.Px(6), tab.GetValue(HavenProperties.Radius).TopLeft);
        }
    }
}
