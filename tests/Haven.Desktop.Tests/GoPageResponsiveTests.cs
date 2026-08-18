using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Haven.Desktop;
using Haven.Desktop.Events;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Pages.Go;
using Haven.Application;
using Haven.Core;
using Haven.UI;
using Haven.UI.Components;

namespace Haven.Desktop.Tests;

public sealed class GoPageResponsiveTests
{
    [AvaloniaFact]
    public void Main_window_centres_and_allows_high_dpi_compact_desktop_layouts()
    {
        var window = new MainWindow();
        try
        {
            Assert.Equal(WindowStartupLocation.CenterScreen, window.WindowStartupLocation);
            Assert.Equal(720, window.MinWidth);
            Assert.Equal(520, window.MinHeight);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Composer_and_primary_prompt_stay_inside_compact_desktop_viewport()
    {
        using var page = new GoPage(new HavenEventBus());
        var window = new Window { Width = 620, Height = 760, Content = page };
        try
        {
            window.Show();
            window.UpdateLayout();

            Assert.True(page.Route.CompactHero.IsIncluded);
            Assert.False(page.Route.WideHero.IsIncluded);
            AssertInside(page.SceneHost.SurfaceMetrics.Viewport, page.Route.CompactTitle);
            AssertInside(page.SceneHost.SurfaceMetrics.Viewport, page.Route.CompactSuggestions);
            AssertInside(page.SceneHost.SurfaceMetrics.Viewport, page.Route.Instruction);
            AssertInside(page.SceneHost.SurfaceMetrics.Viewport, page.Route.SendButton);
            Assert.True(page.Route.Instruction.Bounds.Width >= 280);
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Wide_desktop_layout_keeps_primary_go_surfaces_centered()
    {
        using var page = new GoPage(new HavenEventBus());
        var window = new Window { Width = 1728, Height = 1000, Content = page };
        try
        {
            window.Show();
            window.UpdateLayout();

            Assert.True(page.Route.WideHero.IsIncluded);
            Assert.False(page.Route.CompactHero.IsIncluded);
            AssertInside(page.SceneHost.SurfaceMetrics.Viewport, page.Route.WideTitle);
            AssertInside(page.SceneHost.SurfaceMetrics.Viewport, page.Route.WideSuggestions);
            AssertInside(page.SceneHost.SurfaceMetrics.Viewport, page.Route.Instruction);
            AssertCentered(page.SceneHost.SurfaceMetrics.Viewport, page.Route.WideTitle.Bounds, 1.0);
            AssertCentered(page.SceneHost.SurfaceMetrics.Viewport, page.Route.WideSuggestions.Bounds, 1.0);
            AssertCentered(page.SceneHost.SurfaceMetrics.Viewport, page.Route.Instruction.Bounds, 55.0);
            Assert.Equal("1fr Auto Auto Auto", page.Route.Root.Rows);
            Assert.Equal(2, page.Route.AttachmentHost.GetValue(HavenProperties.Row));
            Assert.Equal(3, page.Route.Chatbox.GetValue(HavenProperties.Row));
            Assert.InRange(page.Route.WideSuggestions.Bounds.Width, 799.5, 800.5);
            Assert.InRange(page.Route.Composer.Bounds.Width, 899.5, 900.5);
            Assert.InRange(page.Route.Instruction.Bounds.Width, 781.5, 782.5);
            Assert.True(page.Route.AddButton.Bounds.Right <= page.Route.Instruction.Bounds.X + 0.1);
            Assert.True(page.Route.Instruction.Bounds.Right <= page.Route.SendButton.Bounds.X + 0.1);
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Haven_scene_suggestion_and_composer_preserve_go_submission_flow()
    {
        using var page = new GoPage(new HavenEventBus());
        var window = new Window { Width = 1000, Height = 760, Content = page };
        try
        {
            string? submitted = null;
            page.SubmitRequested += (_, instruction) => submitted = instruction;
            window.Show();
            window.UpdateLayout();

            var suggestion = Assert.Single(page.Route.SuggestionButtons(0), button => button.IsIncluded);
            var router = new HavenInputRouter(page.SceneRoot);
            Click(router, suggestion);
            Assert.Equal(GoSuggestionService.ImmediateDefaults[0].Instruction, submitted);

            submitted = null;
            page.Route.Instruction.Text = "  open my project  ";
            Click(router, page.Route.SendButton);
            Assert.Equal("open my project", submitted);
            Assert.Equal(string.Empty, page.Route.Instruction.Text);
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Add_menu_is_haven_owned_and_modal_overlay_closes_from_background()
    {
        using var page = new GoPage(new HavenEventBus());
        var window = new Window { Width = 1000, Height = 760, Content = page };
        try
        {
            window.Show();
            window.UpdateLayout();
            var router = new HavenInputRouter(page.SceneRoot);
            var desiredBeforeOpen = page.SceneRoot.DesiredSize;
            var composerBeforeOpen = page.Route.Composer.Bounds;

            Click(router, page.Route.AddButton);
            window.UpdateLayout();
            Assert.Equal(HavenVisibility.Visible, page.Route.AddOverlay.GetValue(HavenProperties.Visibility));
            Assert.Equal(HavenLayoutParticipation.Overlay, page.Route.AddOverlay.GetValue(HavenProperties.LayoutParticipation));
            Assert.Equal(desiredBeforeOpen, page.SceneRoot.DesiredSize);
            Assert.Equal(composerBeforeOpen, page.Route.Composer.Bounds);
            Assert.True(page.Route.AddOverlay.Bounds.Bottom <= page.SceneHost.SurfaceMetrics.Viewport.Height + 0.5);
            Assert.True(page.Route.MainMenu.IsIncluded);
            Assert.InRange(Math.Abs(page.Route.MainMenu.Bounds.X - page.Route.Composer.Bounds.X), 0, 1.0);
            Assert.True(page.Route.MainMenu.Bounds.Bottom <= page.Route.Composer.Bounds.Y - 4);
            var addCommands = new HavenSceneRenderer().Render(page.SceneRoot);
            Assert.Contains(addCommands, command => command is HavenIconCommand { Key: "agents" });
            Assert.Contains(addCommands, command => command is HavenIconCommand { Key: "prompt" });
            Assert.Contains(addCommands, command => command is HavenIconCommand { Key: "bolt" });
            Assert.Contains(addCommands, command => command is HavenIconCommand { Key: "rocket" });
            Assert.Contains(addCommands, command => command is HavenIconCommand { Key: "file" });

            ClickAt(router, new HavenPoint(
                page.Route.MainMenu.Bounds.Right + 20,
                page.Route.MainMenu.Bounds.Y + 10));
            Assert.Equal(HavenVisibility.Collapsed, page.Route.AddOverlay.GetValue(HavenProperties.Visibility));

            page.Route.ShowAddMenu();
            Assert.Equal(HavenVisibility.Visible, page.Route.AddOverlay.GetValue(HavenProperties.Visibility));
            page.SceneHost.NotifyPointerPressedOutside();
            Assert.Equal(HavenVisibility.Collapsed, page.Route.AddOverlay.GetValue(HavenProperties.Visibility));
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Go_route_uses_haven_owned_text_inputs_and_haven_caret_rendering()
    {
        using var page = new GoPage(new HavenEventBus());
        var window = new Window { Width = 1000, Height = 760, Content = page };
        try
        {
            window.Show();
            window.UpdateLayout();

            Assert.Same(page.SceneRoot, page.SceneHost.Root);
            Assert.Single(page.SceneHost.Children);
            Assert.Contains(page.Route.Root.DescendantsAndSelf().OfType<Input>(), input => ReferenceEquals(input, page.Route.Instruction));
            Assert.Contains(page.Route.Root.DescendantsAndSelf().OfType<Input>(), input => ReferenceEquals(input, page.Route.CatalogSearch));
            Assert.DoesNotContain(page.Route.Root.DescendantsAndSelf(), element => element is Video or Web);
            Assert.True(page.Route.Instruction.Accessibility.Focusable);
            Assert.Equal(HavenAccessibleRole.Input, page.Route.Instruction.Accessibility.Role);
            Assert.Equal("Ask Haven anything", page.Route.Instruction.Accessibility.AccessibleName);
            Assert.Equal(InputDefaults.FocusTransition, page.Route.Instruction.GetValue(HavenProperties.Transition));

            page.Route.Instruction.Text = "hello";
            Assert.True(page.SceneHost.FocusElement(page.Route.Instruction));
            Assert.True(page.Route.Instruction.State.HasFlag(HavenElementState.Focused));
            Assert.Equal(page.Route.Instruction.Text.Length, page.Route.Instruction.CaretIndex);
            Assert.Contains(new HavenSceneRenderer().Render(page.SceneRoot), command => command is HavenCaretCommand);
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Suggestion_hover_scales_the_entire_visual_host()
    {
        using var page = new GoPage(new HavenEventBus());
        var window = new Window { Width = 1000, Height = 760, Content = page };
        try
        {
            window.Show();
            window.UpdateLayout();
            var button = Assert.Single(page.Route.SuggestionButtons(0), item => item.IsIncluded);
            var pill = Assert.Single(
                page.Route.Root.DescendantsAndSelf().OfType<Container>(),
                item => item.Name == "Go.Suggestions.Item0.IconPill.Wide");
            var host = Assert.IsType<Container>(button.Parent);
            var router = new HavenInputRouter(page.SceneRoot);
            var point = new HavenPoint(button.Bounds.X + button.Bounds.Width / 2, button.Bounds.Y + button.Bounds.Height / 2);

            router.PointerMoved(point);

            Assert.True(button.State.HasFlag(HavenElementState.Hover));
            Assert.Equal(1.018d, host.GetValue(HavenProperties.Scale), 3);
            Assert.Equal(1d, button.GetValue(HavenProperties.Scale), 3);
            Assert.Equal(1d, pill.GetValue(HavenProperties.Scale), 3);
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Add_catalogues_render_rows_for_agents_capabilities_instructions_and_apps()
    {
        using var page = new GoPage(new HavenEventBus());
        var window = new Window { Width = 1000, Height = 760, Content = page };
        var now = DateTimeOffset.UtcNow;
        var agent = new AgentDefinition(Guid.NewGuid(), "Agent One", "Agent description", "Use agent", "agents", string.Empty, null, "[]", "{}", true, true, now);
        var capability = new CapabilityDefinition(Guid.NewGuid(), "cap.one", "Capability One", "Capability description", "go", "bolt", "Use capability", "test", "[]", CapabilityPlatform.Windows, CapabilityRiskClass.ReadOnly, CapabilityAvailability.Available, "[]", "test", true, true, true, true, now);
        var instruction = new PromptDefinition(Guid.NewGuid(), "Instruction One", "Instruction description", "prompt", "Use instruction", false, true, true, now);
        try
        {
            window.Show();
            window.UpdateLayout();
            page.SetAddCatalogue([agent], [capability], [instruction], BuiltInModeSeed.Modes);
            var router = new HavenInputRouter(page.SceneRoot);

            AssertCatalogueContains(page, window, router, page.Route.AgentsButton, "Agent One");
            AssertCatalogueContains(page, window, router, page.Route.CapabilitiesButton, "Capability One");
            AssertCatalogueContains(page, window, router, page.Route.InstructionsButton, "Instruction One");
            AssertCatalogueContains(page, window, router, page.Route.AppsButton, "Chat", "Study", "Studio");
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    private static void AssertCatalogueContains(GoPage page, Window window, HavenInputRouter router, HavenElement button, params string[] expectedLabels)
    {
        page.Route.ShowAddMenu();
        window.UpdateLayout();
        Click(router, button);
        window.UpdateLayout();
        var labels = new HavenSceneRenderer()
            .Render(page.SceneRoot)
            .OfType<HavenTextCommand>()
            .Select(command => command.Layout.Text)
            .ToArray();
        foreach (var label in expectedLabels) Assert.Contains(label, labels);
        page.Route.HideAddMenu();
        window.UpdateLayout();
    }

    private static void Click(HavenInputRouter router, HavenElement element)
    {
        var point = new HavenPoint(element.Bounds.X + element.Bounds.Width / 2, element.Bounds.Y + element.Bounds.Height / 2);
        ClickAt(router, point);
    }

    private static void ClickAt(HavenInputRouter router, HavenPoint point)
    {
        router.PointerPressed(point);
        Assert.True(router.PointerReleased(point));
    }

    private static void AssertCentered(HavenSize viewport, HavenRect child, double tolerance)
    {
        var childCenter = child.X + child.Width / 2;
        Assert.InRange(Math.Abs(childCenter - viewport.Width / 2), 0, tolerance);
    }

    private static void AssertInside(HavenSize viewport, HavenElement child)
    {
        Assert.True(child.IsIncluded);
        Assert.InRange(child.Bounds.X, 0, viewport.Width);
        Assert.InRange(child.Bounds.Y, 0, viewport.Height);
        Assert.True(child.Bounds.Right <= viewport.Width + 0.5);
        Assert.True(child.Bounds.Bottom <= viewport.Height + 0.5);
    }
}
