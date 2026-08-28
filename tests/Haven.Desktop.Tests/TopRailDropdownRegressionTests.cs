using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Haven.Desktop.HavenUI.Backend;
using Haven.Desktop.Views.Shell.TopRail;
using Haven.UI;
using Haven.UI.Components;

namespace Haven.Desktop.Tests;

public sealed class TopRailDropdownRegressionTests
{
    [AvaloniaFact]
    public async Task Actions_footer_stays_inside_popup_and_long_catalogue_scrolls()
    {
        var scene = new ActionsFlyoutFinalScene();
        scene.SetActions(Enumerable.Range(0, 48)
            .Select(index => new DynamicActionToolbar.ToolbarAction(
                $"Action {index}", "bolt", () => { }, Category: "Tools", Description: $"Action {index}"))
            .ToArray());

        var host = new HavenSceneControl { Root = scene.Root };
        var window = new Window { Width = 690, Height = 650, Content = host };
        try
        {
            window.Show();
            await Dispatcher.UIThread.InvokeAsync(() => { });
            window.UpdateLayout();

            Assert.True(scene.EditButton.Bounds.Bottom <= scene.Root.Bounds.Bottom + 0.01d);
            var sections = Assert.IsType<Container>(scene.Root.DescendantsAndSelf()
                .Single(element => element.Name == "HeaderDropdown.Actions.Sections"));
            Assert.True(sections.MaxScrollY > 0);

            var router = new HavenInputRouter(scene.Root);
            var point = new HavenPoint(sections.Bounds.X + 12, sections.Bounds.Y + 12);
            Assert.True(router.Scroll(point, 0, 10_000));
            Assert.True(sections.ScrollY > 0);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Actions_footer_only_stays_inside_popup()
    {
        var scene = CreatePopulatedActionsScene();
        var host = new HavenSceneControl { Root = scene.Root };
        var window = new Window { Width = 690, Height = 650, Content = host };
        try
        {
            window.Show();
            await Dispatcher.UIThread.InvokeAsync(() => { });
            window.UpdateLayout();

            Assert.True(scene.EditButton.Bounds.Bottom <= scene.Root.Bounds.Bottom + 0.01d);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Actions_long_catalogue_reports_scroll_extent_only()
    {
        var scene = CreatePopulatedActionsScene();
        var host = new HavenSceneControl { Root = scene.Root };
        var window = new Window { Width = 690, Height = 650, Content = host };
        try
        {
            window.Show();
            await Dispatcher.UIThread.InvokeAsync(() => { });
            window.UpdateLayout();

            var sections = Assert.IsType<Container>(scene.Root.DescendantsAndSelf()
                .Single(element => element.Name == "HeaderDropdown.Actions.Sections"));
            Assert.True(sections.MaxScrollY > 0);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Actions_long_catalogue_accepts_scroll_input_only()
    {
        var scene = CreatePopulatedActionsScene();
        var host = new HavenSceneControl { Root = scene.Root };
        var window = new Window { Width = 690, Height = 650, Content = host };
        try
        {
            window.Show();
            await Dispatcher.UIThread.InvokeAsync(() => { });
            window.UpdateLayout();

            var sections = Assert.IsType<Container>(scene.Root.DescendantsAndSelf()
                .Single(element => element.Name == "HeaderDropdown.Actions.Sections"));
            var router = new HavenInputRouter(scene.Root);
            var point = new HavenPoint(sections.Bounds.X + 12, sections.Bounds.Y + 12);
            Assert.True(router.Scroll(point, 0, 10_000));
            Assert.True(sections.ScrollY > 0);
        }
        finally
        {
            window.Close();
        }
    }

    private static ActionsFlyoutFinalScene CreatePopulatedActionsScene()
    {
        var scene = new ActionsFlyoutFinalScene();
        scene.SetActions(Enumerable.Range(0, 48)
            .Select(index => new DynamicActionToolbar.ToolbarAction(
                $"Action {index}", "bolt", () => { }, Category: "Tools", Description: $"Action {index}"))
            .ToArray());
        return scene;
    }

    [AvaloniaFact]
    public async Task Apps_manage_footer_stays_inside_popup()
    {
        var scene = new AppLauncherFinalScene();
        var host = new HavenSceneControl { Root = scene.Root };
        var window = new Window { Width = 560, Height = 620, Content = host };
        try
        {
            window.Show();
            await Dispatcher.UIThread.InvokeAsync(() => { });
            window.UpdateLayout();

            Assert.True(scene.ManageButton.Bounds.Bottom <= scene.Root.Bounds.Bottom + 0.01d);
            var sections = Assert.IsType<Container>(scene.Root.DescendantsAndSelf()
                .Single(element => element.Name == "HeaderDropdown.Apps.Sections"));
            Assert.True(sections.Bounds.Bottom <= scene.ManageButton.Bounds.Y + 0.01d);
        }
        finally
        {
            window.Close();
        }
    }
}
