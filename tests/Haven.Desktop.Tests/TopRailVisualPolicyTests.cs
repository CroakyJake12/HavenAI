using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Shell.TopRail;
using Haven.UI;

namespace Haven.Desktop.Tests;

public sealed class TopRailVisualPolicyTests
{
    [Fact]
    public void Model_effort_runs_from_yellow_at_twenty_to_orange_at_one_hundred()
    {
        Assert.Equal(Color.Parse("#FFFBC02D"), TopRail.EffortColour(20));
        Assert.Equal(Color.Parse("#FFFF6D00"), TopRail.EffortColour(100));
        Assert.Equal(TopRail.EffortColour(20), TopRail.EffortColour(0));
        Assert.Equal(TopRail.EffortColour(100), TopRail.EffortColour(150));
    }

    [Fact]
    public void Notification_urgency_runs_from_yellow_to_bright_red_and_caps_at_thirty()
    {
        Assert.Equal(Color.Parse("#FFFFD54F"), TopRail.NotificationUrgencyColour(0));
        Assert.Equal(Color.Parse("#FFFF1744"), TopRail.NotificationUrgencyColour(30));
        Assert.Equal(TopRail.NotificationUrgencyColour(30), TopRail.NotificationUrgencyColour(99));
    }

    [Fact]
    public void Tab_scroll_arrows_only_appear_for_available_directions()
    {
        Assert.Equal((false, true), TopRail.GetTabScrollAvailability(0, 900, 300));
        Assert.Equal((true, true), TopRail.GetTabScrollAvailability(250, 900, 300));
        Assert.Equal((true, false), TopRail.GetTabScrollAvailability(600, 900, 300));
        Assert.Equal((false, false), TopRail.GetTabScrollAvailability(0, 280, 300));
    }

    [AvaloniaFact]
    public async Task Visible_header_is_haven_owned_and_native_anchors_are_noninteractive()
    {
        using var rail = new TopRail();
        var window = new Window { Width = 1440, Height = 120, Content = rail };
        try
        {
            window.Show();
            await Dispatcher.UIThread.InvokeAsync(() => { });
            Assert.NotNull(rail.HavenOwnedScene);
            Assert.Same(rail.HavenOwnedScene!.Root, rail.SceneHost.Root);
            var anchors = rail.FindControl<Grid>("LegacyAnchorLayer");
            Assert.NotNull(anchors);
            Assert.Equal(0d, anchors.Opacity);
            Assert.False(anchors.IsHitTestVisible);
            var names = rail.HavenOwnedScene.Root.DescendantsAndSelf().Select(element => element.Name).ToHashSet();
            Assert.Contains("TopRail.Logo", names);
            Assert.Contains("TopRail.Actions.Apps", names);
            Assert.Contains("TopRail.Actions.Capabilities", names);
            Assert.Contains("TopRail.Actions.Model", names);
            Assert.Contains("TopRail.Actions.Notifications", names);
            Assert.Contains("TopRail.Actions.Search", names);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Haven_header_keeps_tabs_compact_right_chrome_stable_and_routes_existing_actions()
    {
        using var rail = new TopRail();
        var window = new Window { Width = 1440, Height = 120, Content = rail };
        try
        {
            var appsRequested = 0;
            rail.AppsRequested += (_, _) => appsRequested++;
            window.Show();
            rail.SetTabs([new TopRailTab("go", "Go", "sparkles", true, false), new TopRailTab("research", "Research workspace", "search", false, true)]);
            rail.SetNavigationAvailability(true, false);
            rail.SetModelSummary("qwen3.5-coder", 72);
            await Dispatcher.UIThread.InvokeAsync(() => { });
            window.UpdateLayout();
            var scene = Assert.IsType<TopRailFinalScene>(rail.HavenOwnedScene);
            Assert.Equal(2, scene.Tabs.Count);
            Assert.Equal(HavenVisibility.Visible, scene.BackButton.GetValue(HavenProperties.Visibility));
            Assert.Equal(HavenVisibility.Visible, scene.ForwardButton.GetValue(HavenProperties.Visibility));
            Assert.False(scene.ForwardButton.GetValue(HavenProperties.Enabled));
            Assert.True(scene.TabStrip.Bounds.Width <= 460.01d);
            Assert.InRange(scene.TabActionsHost.Bounds.X - scene.TabStrip.Bounds.Right, 0d, 8.01d);
            Assert.True(scene.Spacer.Bounds.Width > 0d);
            Assert.True(scene.AppsHost.Bounds.X > scene.TabActionsHost.Bounds.Right);
            Assert.Contains("72%", scene.ModelButton.Content);
            var point = new HavenPoint(scene.AppsButton.Bounds.X + 10, scene.AppsButton.Bounds.Y + 10);
            var router = new HavenInputRouter(scene.Root);
            Assert.Same(scene.AppsButton, router.HitTest(point));
            router.PointerPressed(point);
            Assert.True(router.PointerReleased(point));
            Assert.Equal(1, appsRequested);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Tab_context_menu_is_haven_owned_uses_the_real_tab_anchor_and_preserves_actions()
    {
        using var rail = new TopRail();
        var window = new Window { Width = 1440, Height = 120, Content = rail };
        try
        {
            string? closed = null;
            rail.TabCloseRequested += (_, key) => closed = key;
            window.Show();
            rail.SetTabs([new TopRailTab("home", "Home", "home", true, false), new TopRailTab("notes", "Notes", "edit", false, true)]);
            await Dispatcher.UIThread.InvokeAsync(() => { });
            window.UpdateLayout();
            var scene = Assert.IsType<TopRailFinalScene>(rail.HavenOwnedScene);
            TopRailTab? renameRequested = null;
            scene.TabRenameRequested += (_, tab) => renameRequested = tab;
            var router = new HavenInputRouter(scene.Root);

            SecondaryInvoke(router, scene.TabStrip.ItemButtons[0]);
            var homeMenu = Assert.IsType<Haven.UI.Components.PopupMenu>(scene.ActiveTabMenu);
            var homeClose = Assert.IsType<Haven.UI.Components.Button>(homeMenu.Card.Children[1]);
            Assert.False(homeClose.GetValue(HavenProperties.Enabled));

            SecondaryInvoke(router, scene.TabStrip.ItemButtons[1]);
            var notesMenu = Assert.IsType<Haven.UI.Components.PopupMenu>(scene.ActiveTabMenu);
            Assert.DoesNotContain(homeMenu, scene.Root.Children);
            var rename = Assert.IsType<Haven.UI.Components.Button>(notesMenu.Card.Children[0]);
            await Dispatcher.UIThread.InvokeAsync(() => { });
            window.UpdateLayout();
            PrimaryInvoke(new HavenInputRouter(scene.Root), rename);
            Assert.Null(scene.ActiveTabMenu);
            Assert.NotNull(renameRequested);
            Assert.Equal("notes", renameRequested!.Key);

            SecondaryInvoke(router, scene.TabStrip.ItemButtons[1]);
            var reopened = Assert.IsType<Haven.UI.Components.PopupMenu>(scene.ActiveTabMenu);
            var close = Assert.IsType<Haven.UI.Components.Button>(reopened.Card.Children[1]);
            await Dispatcher.UIThread.InvokeAsync(() => { });
            window.UpdateLayout();
            PrimaryInvoke(new HavenInputRouter(scene.Root), close);
            Assert.Equal("notes", closed);
            Assert.Null(scene.ActiveTabMenu);
        }
        finally { window.Close(); }

        static void SecondaryInvoke(HavenInputRouter router, Haven.UI.Components.Button button)
        {
            var point = new HavenPoint(button.Bounds.X + button.Bounds.Width / 2, button.Bounds.Y + button.Bounds.Height / 2);
            router.PointerPressed(point, pointerButton: HavenPointerButton.Secondary);
            Assert.True(router.PointerReleased(point));
        }

        static void PrimaryInvoke(HavenInputRouter router, Haven.UI.Components.Button button)
        {
            var point = new HavenPoint(button.Bounds.X + button.Bounds.Width / 2, button.Bounds.Y + button.Bounds.Height / 2);
            router.PointerPressed(point);
            Assert.True(router.PointerReleased(point));
        }
    }

    [AvaloniaFact]
    public async Task Header_badge_tracks_the_real_notification_collection_in_haven_scene()
    {
        using var service = new NotificationService();
        using var rail = new TopRail();
        var window = new Window { Width = 1440, Height = 120, Content = rail };
        try
        {
            window.Show();
            rail.AttachNotifications(service);
            service.Show("Build finished", "All checks passed.", ToastKind.Success, TimeSpan.FromMinutes(1));
            await Dispatcher.UIThread.InvokeAsync(() => { });
            var scene = Assert.IsType<TopRailFinalScene>(rail.HavenOwnedScene);
            Assert.Equal(HavenVisibility.Visible, scene.NotificationBadge.GetValue(HavenProperties.Visibility));
            Assert.Equal("1", scene.NotificationBadgeText.Content);
            service.Dismiss(service.Notifications[0].Id);
            await Dispatcher.UIThread.InvokeAsync(() => { });
            Assert.Equal(HavenVisibility.Collapsed, scene.NotificationBadge.GetValue(HavenProperties.Visibility));
        }
        finally { window.Close(); }
    }
}
