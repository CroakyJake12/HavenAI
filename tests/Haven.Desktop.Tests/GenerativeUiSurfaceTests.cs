using System.Text.Json;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Controls;
using Haven.Desktop.HavenUI.Components;

namespace Haven.Desktop.Tests;

public sealed class GenerativeUiSurfaceTests
{
    [AvaloniaFact]
    public async Task Trusted_calculator_surface_routes_semantic_event_and_updates_existing_result_control()
    {
        var store = new GenUiInstanceStore();
        var local = new GenUiLocalActionRegistry();
        var runtime = new CalculatorTemplateRuntime(local, store);
        var router = new GenerativeUiEventRouter([local], new BoundedGenUiEventAuditSink(), store);
        using var surface = new GenerativeUiSurface(router, store);
        var window = new Window { Content = surface };
        try
        {
            surface.Present(runtime.Create(Guid.NewGuid()));
            window.Show();
            var input = Descendant<TextBox>(surface, "calculator.expression");
            var resultControl = Descendant<HavenStatusChip>(surface, "calculator.result");
            var calculate = Descendant<Button>(surface, "calculator.calculate");
            input.Text = "6 * 7";
            var completed = new TaskCompletionSource<GenUiActionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var emitted = new TaskCompletionSource<GenUiEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
            surface.ActionCompleted += (_, result) => completed.TrySetResult(result);
            surface.SemanticEventEmitted += (_, semanticEvent) => emitted.TrySetResult(semanticEvent);

            calculate.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var routedEvent = await emitted.Task.WaitAsync(TimeSpan.FromSeconds(3));
            var actionResult = await completed.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await Dispatcher.UIThread.InvokeAsync(() => { });

            Assert.True(
                routedEvent.StructuredPayload.TryGetProperty("values", out var values)
                && values.TryGetProperty("calculator.expression", out var expression)
                && expression.GetString() == "6 * 7",
                routedEvent.StructuredPayload.GetRawText());
            Assert.True(actionResult.Status == GenUiActionStatus.Completed, actionResult.Summary);
            Assert.Equal("42", resultControl.Content);
            Assert.Same(resultControl, Descendant<HavenStatusChip>(surface, "calculator.result"));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Structured_form_can_submit_and_recreate_visual_surface_over_same_instance()
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
        using var first = new GenerativeUiSurface(router, store);
        using var second = new GenerativeUiSurface(router, store);
        var host = new StackPanel();
        var window = new Window { Content = host };
        try
        {
            first.Present(document);
            host.Children.Add(first);
            window.Show();
            var goal = Descendant<TextBox>(first, "structured-form.input.goal");
            var submit = Descendant<Button>(first, "structured-form.submit");
            goal.Text = "Ship broader GenUI";
            var completed = new TaskCompletionSource<GenUiActionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            first.ActionCompleted += (_, result) => completed.TrySetResult(result);

            submit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var result = await completed.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await Dispatcher.UIThread.InvokeAsync(() => { });

            Assert.Equal(GenUiActionStatus.Completed, result.Status);
            var registered = store.TryGet(document.Origin.InstanceId);
            Assert.NotNull(registered);
            Assert.Equal(
                "Ship broader GenUI",
                registered.State["submittedValues"].GetProperty("structured-form.input.goal").GetString());

            host.Children.Remove(first);
            first.Dispose();
            second.PresentExisting(registered);
            host.Children.Add(second);
            await Dispatcher.UIThread.InvokeAsync(() => { });

            Assert.Equal("Ship broader GenUI", Descendant<TextBox>(second, "structured-form.input.goal").Text);
            Assert.Same(second, host.Children.Single());
        }
        finally
        {
            window.Close();
        }
    }

    private static T Descendant<T>(Control root, string automationId) where T : Control =>
        Assert.Single(root.GetVisualDescendants().OfType<T>(),
            control => AutomationProperties.GetAutomationId(control) == automationId);
}
