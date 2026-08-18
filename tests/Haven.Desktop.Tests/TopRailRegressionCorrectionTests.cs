using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Haven.Desktop.Views.Shell.TopRail;
using Haven.UI;
using Haven.UI.Components;

namespace Haven.Desktop.Tests;

public sealed class TopRailRegressionCorrectionTests
{
    [AvaloniaFact]
    public async Task Final_rail_restores_compact_tabs_chevrons_navigation_effort_palette_and_text_tabs()
    {
        using var rail = new TopRail(); var window = new Window { Width = 1440, Height = 120, Content = rail };
        try
        {
            window.Show(); rail.SetTabs([new TopRailTab("go", "Go", "sparkles", true, false)]); rail.SetNavigationAvailability(true, true); rail.SetModelSummary("mistral small3.1", 60); await Dispatcher.UIThread.InvokeAsync(() => { }); window.UpdateLayout();
            var scene = Assert.IsType<TopRailFinalScene>(rail.HavenOwnedScene);
            Assert.Equal(HavenVisibility.Visible, scene.AddTabButton.GetValue(HavenProperties.Visibility)); Assert.Equal(HavenVisibility.Visible, scene.TabOverviewButton.GetValue(HavenProperties.Visibility)); Assert.Equal(HavenVisibility.Visible, scene.BackButton.GetValue(HavenProperties.Visibility)); Assert.Equal(HavenVisibility.Visible, scene.ForwardButton.GetValue(HavenProperties.Visibility));
            Assert.InRange(scene.TabStrip.Bounds.Width, 71.9d, 72.1d); Assert.InRange(scene.TabActionsHost.Bounds.X - scene.TabStrip.Bounds.Right, 0d, 8.01d); Assert.True(scene.Spacer.Bounds.Width > 0d);
            Assert.Equal("Transparent", scene.TabStrip.ItemButtons[0].GetValue(HavenProperties.Background)); Assert.Equal("Accent", scene.TabStrip.SelectionIndicators[0].GetValue(HavenProperties.Background));
            Assert.Contains(scene.AppsHost.DescendantsAndSelf().OfType<Icon>(), x => x.Key == "chevron-down"); Assert.Contains(scene.ActionsHost.DescendantsAndSelf().OfType<Icon>(), x => x.Key == "chevron-down"); Assert.Contains(scene.ModelHost.DescendantsAndSelf().OfType<Icon>(), x => x.Key == "chevron-down");
            Assert.True(scene.AppsButton.Bounds.Width > 110d); Assert.True(scene.ActionsButton.Bounds.Width >= 154d); Assert.True(scene.ModelButton.Bounds.Width > 236d);
            Assert.Equal("ButtonTextPrimary", scene.AppsButton.GetValue(HavenProperties.Foreground)); Assert.Equal("ButtonTextPrimary", scene.ActionsButton.GetValue(HavenProperties.Foreground)); Assert.Equal("TextOnAccent", scene.ModelButton.GetValue(HavenProperties.Foreground));
            Assert.Equal("HavenModelReasoningBalancedBrush", scene.ModelButton.GetValue(HavenProperties.Background)); rail.SetModelSummary("mistral-small", 20); Assert.Equal("HavenModelReasoningLowBrush", scene.ModelButton.GetValue(HavenProperties.Background)); rail.SetModelSummary("mistral-small", 80); Assert.Equal("HavenModelReasoningHighBrush", scene.ModelButton.GetValue(HavenProperties.Background)); rail.SetModelSummary("mistral-small", 100); Assert.Equal("HavenModelReasoningMaxBrush", scene.ModelButton.GetValue(HavenProperties.Background));
            rail.SetNavigationAvailability(false, false); Assert.Equal(HavenVisibility.Visible, scene.BackButton.GetValue(HavenProperties.Visibility)); Assert.Equal(HavenVisibility.Visible, scene.ForwardButton.GetValue(HavenProperties.Visibility)); Assert.False(scene.BackButton.GetValue(HavenProperties.Enabled)); Assert.False(scene.ForwardButton.GetValue(HavenProperties.Enabled));
        }
        finally { window.Close(); }
    }
}
