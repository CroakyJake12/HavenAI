using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.HavenUI.Backend;
using Haven.Desktop.HavenUI.GenerativeUi;
using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;
using HavenText = Haven.UI.Components.Text;

namespace Haven.Desktop.Tests;

public sealed class HavenNativeGenerativeUiTests
{
    [AvaloniaFact]
    public async Task Calculator_routes_through_Haven_scene_and_updates_existing_result()
    {
        var store = new GenUiInstanceStore();
        var local = new GenUiLocalActionRegistry();
        var runtime = new CalculatorTemplateRuntime(local, store);
        var router = new GenerativeUiEventRouter([local], new BoundedGenUiEventAuditSink(), store);
        using var surface = new HavenGenUiSceneSurface(router, store);
        var host = new HavenSceneControl { Root = surface.Root };
        var window = new Window { Width = 760, Height = 560, Content = host };
        try
        {
            surface.Present(runtime.Create(Guid.NewGuid()));
            window.Show();
            window.UpdateLayout();
            var input = Single<Input>(surface.Root, "calculator.expression");
            var calculate = Single<HavenButton>(surface.Root, "calculator.calculate");
            var result = Single<HavenText>(surface.Root, "calculator.result");
            input.Text = "6 * 7";
            var completed = new TaskCompletionSource<GenUiActionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            surface.ActionCompleted += (_, action) => completed.TrySetResult(action);

            Click(surface.Root, calculate);
            var actionResult = await completed.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await Dispatcher.UIThread.InvokeAsync(() => { });

            Assert.Equal(GenUiActionStatus.Completed, actionResult.Status);
            Assert.Equal("42", result.Content);
            Assert.Same(result, Single<HavenText>(surface.Root, "calculator.result"));
            Assert.Contains(surface.Root.DescendantsAndSelf().OfType<HavenText>(), text => text.Content.Contains("Calculated locally", StringComparison.Ordinal));
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Structured_form_preserves_submitted_values_when_presented_again()
    {
        var store = new GenUiInstanceStore();
        var local = new GenUiLocalActionRegistry();
        var runtime = new StructuredFormTemplateRuntime(local, store);
        var router = new GenerativeUiEventRouter([local], new BoundedGenUiEventAuditSink(), store);
        var inputs = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["title"] = JsonSerializer.SerializeToElement("Quick plan"),
            ["schema"] = JsonSerializer.SerializeToElement(new object[]
            {
                new { id = "goal", label = "Goal", type = "text", placeholder = "Goal" },
                new { id = "priority", label = "Priority", type = "select", options = new[] { "Low", "High" } }
            })
        };
        var document = runtime.Create(Guid.NewGuid(), "chat", inputs);
        using var first = new HavenGenUiSceneSurface(router, store);
        var host = new HavenSceneControl { Root = first.Root };
        var window = new Window { Width = 760, Height = 620, Content = host };
        try
        {
            first.Present(document);
            window.Show();
            window.UpdateLayout();
            var goal = Single<Input>(first.Root, "structured-form.input.goal");
            var submit = Single<HavenButton>(first.Root, "structured-form.submit");
            goal.Text = "Ship broader GenUI";
            var completed = new TaskCompletionSource<GenUiActionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            first.ActionCompleted += (_, action) => completed.TrySetResult(action);
            Click(first.Root, submit);
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await Dispatcher.UIThread.InvokeAsync(() => { });

            var registered = store.TryGet(document.Origin.InstanceId);
            Assert.NotNull(registered);
            Assert.Equal("Ship broader GenUI", registered!.State["submittedValues"].GetProperty("structured-form.input.goal").GetString());

            using var second = new HavenGenUiSceneSurface(router, store);
            second.PresentExisting(registered);
            Assert.Equal("Ship broader GenUI", Single<Input>(second.Root, "structured-form.input.goal").Text);
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Choice_prompt_toolbar_stays_compact_and_routes_selection()
    {
        var store = new GenUiInstanceStore();
        var local = new GenUiLocalActionRegistry();
        var runtime = new ChoicePromptTemplateRuntime(local);
        var router = new GenerativeUiEventRouter([local], new BoundedGenUiEventAuditSink(), store);
        using var surface = new HavenGenUiSceneSurface(router, store);
        var inputs = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["question"] = JsonSerializer.SerializeToElement("Where next?"),
            ["options"] = JsonSerializer.SerializeToElement(new[] { "Chat A", "Chat B", "New chat" })
        };
        var host = new HavenSceneControl { Root = surface.Root };
        var window = new Window { Width = 900, Height = 460, Content = host };
        try
        {
            surface.Present(runtime.Create(Guid.NewGuid(), "chat", inputs));
            window.Show();
            window.UpdateLayout();
            var buttons = surface.Root.DescendantsAndSelf().OfType<HavenButton>()
                .Where(button => button.Content is "Chat A" or "Chat B" or "New chat")
                .ToArray();
            Assert.Equal(3, buttons.Length);
            Assert.All(buttons, button => Assert.InRange(button.Bounds.Width, 1, 320));
            Assert.InRange(buttons.Max(button => button.Bounds.Y) - buttons.Min(button => button.Bounds.Y), 0, 1);
            var completed = new TaskCompletionSource<GenUiActionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            surface.ActionCompleted += (_, action) => completed.TrySetResult(action);

            Click(surface.Root, buttons.Single(button => button.Content == "Chat B"));
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await Dispatcher.UIThread.InvokeAsync(() => { });

            Assert.Contains(surface.Root.DescendantsAndSelf().OfType<HavenText>(), text => text.Content == "Selected: Chat B");
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Graph_uses_Haven_draw_commands_and_updates_same_plot_on_expression_submit()
    {
        var store = new GenUiInstanceStore();
        var local = new GenUiLocalActionRegistry();
        var runtime = new GraphTemplateRuntime(local);
        var router = new GenerativeUiEventRouter([local], new BoundedGenUiEventAuditSink(), store);
        using var surface = new HavenGenUiSceneSurface(router, store);
        var inputs = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["expressions"] = JsonSerializer.SerializeToElement(new[] { "sin(x)" })
        };
        var host = new HavenSceneControl { Root = surface.Root };
        var window = new Window { Width = 900, Height = 620, Content = host };
        try
        {
            surface.Present(runtime.Create(Guid.NewGuid(), "chat", inputs));
            window.Show();
            window.UpdateLayout();
            var plot = surface.Root.DescendantsAndSelf().Single(element => element.Name == "GenUI_graph_canvas");
            var before = new HavenSceneRenderer().Render(surface.Root).OfType<HavenLineCommand>().Count();
            Assert.True(before > 20);
            Assert.DoesNotContain(surface.Root.DescendantsAndSelf().OfType<HavenText>(), text => text.Content.Contains("HavenGraph foundation", StringComparison.Ordinal));

            var expression = Single<Input>(surface.Root, "graph.expression-input");
            expression.Text = "cos(x)";
            await surface.SubmitInputAsync(expression);
            await Dispatcher.UIThread.InvokeAsync(() => { });
            window.UpdateLayout();

            Assert.Same(plot, surface.Root.DescendantsAndSelf().Single(element => element.Name == "GenUI_graph_canvas"));
            Assert.Contains(surface.Root.DescendantsAndSelf().OfType<HavenText>(), text => text.Content == "1 expression(s)");
            Assert.True(new HavenSceneRenderer().Render(surface.Root).OfType<HavenLineCommand>().Count() > 20);
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    private static T Single<T>(HavenElement root, string componentId) where T : HavenElement =>
        Assert.Single(root.DescendantsAndSelf().OfType<T>(), element => element.Name == "GenUI_" + componentId.Replace('.', '_'));

    private static void Click(HavenElement root, HavenElement element)
    {
        var router = new HavenInputRouter(root);
        var point = new HavenPoint(element.Bounds.X + element.Bounds.Width / 2, element.Bounds.Y + element.Bounds.Height / 2);
        router.PointerPressed(point);
        Assert.True(router.PointerReleased(point));
    }
}
