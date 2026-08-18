using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Haven.Desktop.HavenUI.Backend;
using Haven.Desktop.Views.Shell.TopRail;
using Haven.UI;

namespace Haven.Desktop.Tests;

public sealed class ActionsFlyoutVisualPolicyTests
{
    [AvaloniaFact]
    public void Actions_flyout_is_Haven_owned_searchable_three_column_and_routes_actions_and_edit()
    {
        var actionRuns = 0;
        var editRuns = 0;
        var invoked = 0;
        var actions = new[]
        {
            new DynamicActionToolbar.ToolbarAction("Voice", "call", () => actionRuns++, Description: "Start voice.", IsFeatured: true),
            new DynamicActionToolbar.ToolbarAction("New chat", "plus", () => actionRuns++, Category: "Recommended", Description: "Start chat.", IsFeatured: true),
            new DynamicActionToolbar.ToolbarAction("Settings", "settings", () => actionRuns++, Category: "General", Description: "Open settings.", IsFeatured: true),
            new DynamicActionToolbar.ToolbarAction("Branch chat", "branch", () => actionRuns++, Category: "Chat", Description: "Branch chat."),
            new DynamicActionToolbar.ToolbarAction("Build project", "build", () => actionRuns++, Category: "Studio", Description: "Build project.", Shortcut: "Ctrl+B"),
            new DynamicActionToolbar.ToolbarAction("Today", "calendar", () => actionRuns++, Category: "Plan", Description: "Jump to today.")
        };

        var control = new ActionsFlyoutControl();
        control.ActionInvoked += (_, _) => invoked++;
        control.SetEditActionsHandler(() => editRuns++);
        control.SetActions(actions);
        var window = new Window { Width = 690, Height = 650, Content = control };
        try
        {
            window.Show();
            window.UpdateLayout();

            var scene = control.HavenScene;
            Assert.IsType<HavenSceneControl>(control.Content);
            Assert.Same(scene.Root, control.SceneHost.Root);
            var title = Assert.Single(scene.Root.DescendantsAndSelf(), item => item.Name == "HeaderDropdown.Actions.Title");
            var searchHost = Assert.Single(scene.Root.DescendantsAndSelf(), item => item.Name == "HeaderDropdown.Actions.SearchHost");
            var sections = Assert.Single(scene.Root.DescendantsAndSelf(), item => item.Name == "HeaderDropdown.Actions.Sections");
            Assert.Equal("Actions", Assert.IsType<Haven.UI.Components.Text>(title).Content);
            Assert.True(title.Bounds.Bottom + 8 <= searchHost.Bounds.Y);
            Assert.True(searchHost.Bounds.Bottom + 8 <= sections.Bounds.Y);
            Assert.True(sections.Bounds.Bottom + 8 <= scene.EditButton.Bounds.Y);
            Assert.Equal("Edit Actions", scene.EditButton.Content);
            Assert.Equal(6, scene.ActionButtons.Count);

            var firstThree = scene.ActionButtons.Take(3).ToArray();
            Assert.InRange(firstThree.Max(button => button.Bounds.Y) - firstThree.Min(button => button.Bounds.Y), 0, .5);
            Assert.All(firstThree, button => Assert.InRange(button.Bounds.Height, 71, 73));
            Assert.True(firstThree[0].Bounds.Right + 8 <= firstThree[1].Bounds.X);
            Assert.True(firstThree[1].Bounds.Right + 8 <= firstThree[2].Bounds.X);

            Assert.True(control.FocusSearch());
            Assert.True(scene.Search.State.HasFlag(HavenElementState.Focused));
            scene.Search.Text = "Build";
            window.UpdateLayout();
            var filtered = Assert.Single(scene.ActionButtons);
            Assert.Equal("Build project", filtered.Content);

            var router = new HavenInputRouter(scene.Root);
            var actionPoint = new HavenPoint(filtered.Bounds.X + filtered.Bounds.Width / 2, filtered.Bounds.Y + filtered.Bounds.Height / 2);
            router.PointerPressed(actionPoint);
            Assert.True(router.PointerReleased(actionPoint));
            Assert.Equal(1, actionRuns);
            Assert.Equal(1, invoked);

            var editPoint = new HavenPoint(scene.EditButton.Bounds.X + scene.EditButton.Bounds.Width / 2, scene.EditButton.Bounds.Y + scene.EditButton.Bounds.Height / 2);
            router.PointerPressed(editPoint);
            Assert.True(router.PointerReleased(editPoint));
            Assert.Equal(1, editRuns);
            Assert.Equal(2, invoked);
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }
}
