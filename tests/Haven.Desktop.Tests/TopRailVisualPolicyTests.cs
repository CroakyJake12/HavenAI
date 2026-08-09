using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Shell.TopRail;

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
    public async Task Header_badge_tracks_the_real_notification_collection()
    {
        using var service = new NotificationService();
        using var rail = new TopRail();
        var window = new Window { Content = rail };
        try
        {
            window.Show();
            rail.AttachNotifications(service);
            service.Show("Build finished", "All checks passed.", ToastKind.Success, TimeSpan.FromMinutes(1));
            await Dispatcher.UIThread.InvokeAsync(() => { });

            var badge = rail.FindControl<Border>("NotificationBadge");
            var text = rail.FindControl<TextBlock>("NotificationBadgeText");
            Assert.NotNull(badge);
            Assert.NotNull(text);
            Assert.True(badge.IsVisible);
            Assert.Equal("1", text.Text);

            service.Dismiss(service.Notifications[0].Id);
            await Dispatcher.UIThread.InvokeAsync(() => { });
            Assert.False(badge.IsVisible);
        }
        finally
        {
            window.Close();
        }
    }
}
