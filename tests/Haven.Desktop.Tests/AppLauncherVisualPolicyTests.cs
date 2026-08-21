using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Haven.Application;
using Haven.Desktop.Views.Shell.TopRail;
using Haven.UI;
using Haven.UI.Components;

namespace Haven.Desktop.Tests;

public sealed class AppLauncherVisualPolicyTests
{
    [AvaloniaFact]
    public void Apps_launcher_keeps_title_search_scroll_region_and_footer_separated_with_two_column_rows()
    {
        var apps = BuiltInModeSeed.Modes.ToArray();
        var launcher = new AppLauncherControl();
        var window = new Window { Width = 560, Height = 620, Content = launcher };
        try
        {
            launcher.Configure(apps, apps.Take(4).Select(item => item.Id).ToHashSet(), false, (_, _) => { }, () => { });
            window.Show();
            window.UpdateLayout();

            var scene = launcher.HavenScene;
            var title = Assert.Single(scene.Root.DescendantsAndSelf(), item => item.Name == "HeaderDropdown.Apps.Title");
            var searchHost = Assert.Single(scene.Root.DescendantsAndSelf(), item => item.Name == "HeaderDropdown.Apps.SearchHost");
            var sections = Assert.Single(scene.Root.DescendantsAndSelf(), item => item.Name == "HeaderDropdown.Apps.Sections");
            var searchIcon = Assert.Single(scene.Root.DescendantsAndSelf(), item => item.Name == "HeaderDropdown.Apps.SearchIcon");

            Assert.True(title.Bounds.Bottom + 8 <= searchHost.Bounds.Y);
            Assert.True(searchHost.Bounds.Bottom + 8 <= sections.Bounds.Y);
            Assert.True(sections.Bounds.Bottom + 8 <= scene.ManageButton.Bounds.Y);
            Assert.Equal("AccentSecondary", searchIcon.GetValue(HavenProperties.Foreground));
            Assert.Equal(ButtonVariant.Primary, scene.ManageButton.Variant);
            Assert.Equal(HavenHorizontalAlignment.Center, scene.ManageButton.GetValue(HavenProperties.HorizontalAlignment));
            Assert.InRange(scene.ManageButton.Bounds.Width, 229.5, 230.5);

            var firstPair = scene.AppButtons.Take(2).ToArray();
            Assert.Equal(2, firstPair.Length);
            Assert.InRange(Math.Abs(firstPair[0].Bounds.Y - firstPair[1].Bounds.Y), 0, .5);
            Assert.True(firstPair[0].Bounds.Right + 8 <= firstPair[1].Bounds.X);
            Assert.InRange(firstPair[0].Bounds.Height, 63, 65);
            Assert.InRange(firstPair[1].Bounds.Height, 63, 65);
            Assert.True(firstPair.All(button => button.Bounds.Width >= 220));

            scene.Search.Text = "Imagine";
            window.UpdateLayout();
            var filtered = Assert.Single(scene.AppButtons);
            Assert.Equal("Imagine", filtered.Content);
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }
}
