using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.HavenUI.Backend;
using Haven.Desktop.HavenUI.GenerativeUi;
using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;
using HavenText = Haven.UI.Components.Text;

namespace Haven.Desktop.Tests;

public sealed class HavenGenUiSceneSurfaceTests
{
    [AvaloniaFact]
    public void Custom_buttons_use_natural_width_by_default_and_honor_explicit_layout_props()
    {
        var store = new GenUiInstanceStore();
        var local = new GenUiLocalActionRegistry();
        var runtime = new CustomTemplateRuntime(local, store);
        var router = new GenerativeUiEventRouter([local], new BoundedGenUiEventAuditSink(), store);
        using var surface = new HavenGenUiSceneSurface(router, store);
        var inputs = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["title"] = JsonSerializer.SerializeToElement("Layout test"),
            ["components"] = JsonSerializer.SerializeToElement(new object[]
            {
                new
                {
                    id = "layout",
                    type = "HavenStack",
                    props = new { spacing = 8, horizontalAlignment = "stretch" },
                    children = new object[]
                    {
                        new { id = "natural", type = "HavenButton", props = new { label = "Continue" } },
                        new { id = "stretch", type = "HavenButton", props = new { label = "Stretch", horizontalAlignment = "stretch", height = 52 } }
                    }
                }
            })
        };

        surface.Present(runtime.Create(Guid.NewGuid(), "chat", inputs));
        var natural = surface.Root.DescendantsAndSelf().OfType<HavenButton>().Single(button => button.Content == "Continue");
        var stretch = surface.Root.DescendantsAndSelf().OfType<HavenButton>().Single(button => button.Content == "Stretch");
        var host = new HavenSceneControl { Root = surface.Root };
        var window = new Window { Width = 760, Height = 520, Content = host };
        try
        {
            window.Show();
            window.UpdateLayout();

            Assert.Equal(HavenHorizontalAlignment.Start, natural.GetValue(HavenProperties.HorizontalAlignment));
            Assert.Equal(HavenHorizontalAlignment.Stretch, stretch.GetValue(HavenProperties.HorizontalAlignment));
            Assert.Equal(52, stretch.GetValue(HavenProperties.Height).Value);
            Assert.True(natural.Bounds.Width < stretch.Bounds.Width);
            Assert.True(stretch.Bounds.Width > host.SurfaceMetrics.Viewport.Width * 0.7);
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Structured_form_routes_live_haven_values_and_updates_existing_surface_in_place()
    {
        var store = new GenUiInstanceStore();
        var local = new GenUiLocalActionRegistry();
        var runtime = new StructuredFormTemplateRuntime(local, store);
        var router = new GenerativeUiEventRouter([local], new BoundedGenUiEventAuditSink(), store);
        using var surface = new HavenGenUiSceneSurface(router, store);
        var inputs = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["title"] = JsonSerializer.SerializeToElement("Quick plan"),
            ["schema"] = JsonSerializer.SerializeToElement(new object[]
            {
                new { id = "goal", label = "Goal", type = "text", placeholder = "Goal" }
            })
        };
        var document = runtime.Create(Guid.NewGuid(), "chat", inputs);
        surface.Present(document);
        var input = surface.Root.DescendantsAndSelf().OfType<Input>().Single();
        var submit = surface.Root.DescendantsAndSelf().OfType<HavenButton>().Single(button => button.Content == "Submit");
        var status = surface.Root.DescendantsAndSelf().OfType<HavenText>().Single(text => text.Accessibility.AccessibleName == "Form status");
        var host = new HavenSceneControl { Root = surface.Root };
        var window = new Window { Width = 760, Height = 520, Content = host };
        try
        {
            window.Show();
            window.UpdateLayout();
            input.Text = "Ship native GenUI";
            Click(sceneRoot: surface.Root, submit);

            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
            GenUiDocument? registered;
            do
            {
                registered = store.TryGet(document.Origin.InstanceId);
                if (registered?.State.TryGetValue("submittedValues", out var values) == true
                    && values.ValueKind == JsonValueKind.Object
                    && values.TryGetProperty("structured-form.input.goal", out var goal)
                    && goal.GetString() == "Ship native GenUI")
                    break;
                await Task.Delay(20);
            }
            while (DateTime.UtcNow < deadline);

            registered = store.TryGet(document.Origin.InstanceId);
            Assert.NotNull(registered);
            Assert.Equal("Ship native GenUI", registered!.State["submittedValues"].GetProperty("structured-form.input.goal").GetString());
            Assert.Equal("Submitted", status.Content);
            Assert.Same(status, surface.Root.DescendantsAndSelf().OfType<HavenText>().Single(text => text.Accessibility.AccessibleName == "Form status"));
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    private static void Click(HavenElement sceneRoot, HavenElement element)
    {
        var router = new HavenInputRouter(sceneRoot);
        var point = new HavenPoint(element.Bounds.X + element.Bounds.Width / 2, element.Bounds.Y + element.Bounds.Height / 2);
        router.PointerPressed(point);
        Assert.True(router.PointerReleased(point));
    }
}
