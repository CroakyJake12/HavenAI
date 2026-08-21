/*
 * FILE DOCUMENTATION
 * Where: tests/Haven.Desktop.Tests/VisualSystemHeadlessTests.cs, in the automated test suite, where executable examples protect behavior against regressions.
 * What: This file owns VisualSystemHeadlessTests. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The test is intentionally close to the public behavior it protects, making failures describe a user-visible or architectural contract.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using Haven.Core;
using Haven.Desktop.Controls;
using Haven.Desktop.Converters;
using Haven.Desktop.ViewModels;
using Haven.Desktop.Views;
using Xunit;

namespace Haven.Desktop.Tests;

/// <summary>
/// Represents visual system headless tests and keeps its related state and behavior together.
/// </summary>
public sealed class VisualSystemHeadlessTests
{
    /// <summary>
    /// Performs the global button and acrylic themes apply from the application step owned by this component.
    /// </summary>
    [AvaloniaFact]
    public void GlobalButtonAndAcrylicThemesApplyFromTheApplication()
    {
        var button = new Button { Content = "Primary" };
        button.Classes.Add("primary");
        var acrylic = new AcrylicSurface { Content = button };
        var window = new Window { Width = 320, Height = 180, Content = acrylic };

        window.Show();
        acrylic.ApplyTemplate();

        Assert.Equal(36, button.MinHeight);
        Assert.Equal(new CornerRadius(10), button.CornerRadius);
        Assert.Equal(FontWeight.Bold, button.FontWeight);
        Assert.Contains("Montserrat", button.FontFamily?.Name ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(new CornerRadius(16), acrylic.CornerRadius);
        Assert.NotEqual(0, acrylic.FallbackColor.A);

        var backdrop = acrylic.GetVisualDescendants().OfType<ExperimentalAcrylicBorder>().Single();
        Assert.NotNull(backdrop.Material);
        Assert.Equal(AcrylicBackgroundSource.Digger, backdrop.Material.BackgroundSource);
        window.Close();
    }

    [AvaloniaFact]
    public void HavenChromeUsesBundledMontserratWithThickDefaultWeights()
    {
        var label = new TextBlock { Text = "Haven" };
        var input = new TextBox { Text = "Settings" };
        var button = new Button { Content = "Open" };
        var window = new Window
        {
            Width = 360,
            Height = 180,
            Content = new StackPanel { Children = { label, input, button } }
        };

        window.Show();

        Assert.Contains("Montserrat", label.FontFamily?.Name ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Montserrat", input.FontFamily?.Name ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Montserrat", button.FontFamily?.Name ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(FontWeight.SemiBold, label.FontWeight);
        Assert.Equal(FontWeight.SemiBold, input.FontWeight);
        Assert.Equal(FontWeight.Bold, button.FontWeight);

        window.Close();
    }

    /// <summary>
    /// Performs the product icons resolve to closed visible geometry step owned by this component.
    /// </summary>
    [AvaloniaTheory]
    [InlineData("home")]
    [InlineData("teach")]
    [InlineData("call")]
    [InlineData("mic")]
    [InlineData("mute")]
    [InlineData("screen-share")]
    [InlineData("hang-up")]
    [InlineData("plan")]
    public void ProductIconsResolveToClosedVisibleGeometry(string key)
    {
        var icon = new HavenIcon { IconKey = key };

        Assert.True(HavenIcon.IsKnown(key));
        Assert.NotNull(icon.Data);
        Assert.True(icon.Data.Bounds.Width > 0);
        Assert.True(icon.Data.Bounds.Height > 0);
    }

    /// <summary>
    /// Performs the haven icon uses the path icon theme and renders a visual step owned by this component.
    /// </summary>
    [AvaloniaFact]
    public void HavenIconUsesThePathIconThemeAndRendersAVisual()
    {
        var icon = new HavenIcon { IconKey = "plan", Width = 24, Height = 24 };
        var window = new Window { Width = 100, Height = 100, Content = icon };

        window.Show();
        icon.ApplyTemplate();

        Assert.NotEmpty(icon.GetVisualDescendants());
        Assert.NotNull(icon.Foreground);
        window.Close();
    }

    /// <summary>
    /// Performs the planner date converter round trips the local calendar day step owned by this component.
    /// </summary>
    [Fact]
    public void PlannerDateConverterRoundTripsTheLocalCalendarDay()
    {
        var converter = new DateTimeOffsetDateConverter();
        var source = new DateTimeOffset(2026, 10, 25, 0, 0, 0, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 10, 25)));

        var date = Assert.IsType<DateTime>(converter.Convert(source, typeof(DateTime?), null, System.Globalization.CultureInfo.InvariantCulture));
        var restored = Assert.IsType<DateTimeOffset>(converter.ConvertBack(date, typeof(DateTimeOffset?), null, System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal(source.Date, restored.Date);
        Assert.Equal(TimeZoneInfo.Local.GetUtcOffset(date), restored.Offset);
    }

    /// <summary>
    /// Performs the unknown icon keys use a visible fallback step owned by this component.
    /// </summary>
    [AvaloniaFact]
    public void UnknownIconKeysUseAVisibleFallback()
    {
        var icon = new HavenIcon { IconKey = "not-a-real-persisted-key" };

        Assert.NotNull(icon.Data);
        Assert.True(icon.Data.Bounds.Width > 0);
        Assert.True(icon.Data.Bounds.Height > 0);
    }

    /// <summary>
    /// Planner keeps its view navigation on the canonical Haven tabber and its scheduled-task entry in the header host.
    /// </summary>
    [AvaloniaFact]
    public void PlanViewUsesCanonicalTabberAndScheduledTaskHost()
    {
        var view = new PlanView();
        var window = new Window { Width = 1280, Height = 800, Content = view };

        window.Show();
        window.UpdateLayout();

        var tabs = Assert.Single(view.GetVisualDescendants().OfType<Haven.Desktop.HavenUI.Components.HavenTabView>());
        Assert.Equal(9, tabs.Items.Count);
        Assert.NotNull(view.FindControl<Grid>("ScheduledTaskHost"));
        var sidebar = Assert.IsAssignableFrom<Control>(view.FindControl<Control>("CollectionSidebar"));
        var compactPicker = Assert.IsAssignableFrom<Control>(view.FindControl<Control>("CompactCollectionPicker"));
        var inspector = Assert.IsAssignableFrom<Control>(view.FindControl<Control>("PlannerInspector"));
        Assert.True(sidebar.IsVisible);
        Assert.False(compactPicker.IsVisible);

        window.Width = 760;
        view.Width = 760;
        window.UpdateLayout();

        Assert.False(sidebar.IsVisible);
        Assert.True(compactPicker.IsVisible);
        Assert.True(double.IsNaN(inspector.Width));
        Assert.Equal(Avalonia.Layout.HorizontalAlignment.Stretch, inspector.HorizontalAlignment);
        window.Close();
    }

    /// <summary>
    /// Performs the first class surface views construct under headless avalonia step owned by this component.
    /// </summary>
    [AvaloniaFact]
    public void FirstClassSurfaceViewsConstructUnderHeadlessAvalonia()
    {
        Control[] pages = [new HomeView(), new CallView(), new PlanView(), new ChatGroupView()];
        var window = new Window { Width = 1280, Height = 800, Content = new StackPanel { Children = { pages[0], pages[1], pages[2], pages[3] } } };

        window.Show();

        Assert.All(pages, page => Assert.NotNull(page));
        var tab = new WorkspaceTabViewModel("home", "Home", pages[0], false, HavenSurface.Home);
        Assert.Equal(HavenSurface.Home, tab.Surface);
        tab.SetSurface(HavenSurface.Plan);
        Assert.Equal(HavenSurface.Plan, tab.Surface);
        window.Close();
    }
}
