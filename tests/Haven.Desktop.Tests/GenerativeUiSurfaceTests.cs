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

    private static T Descendant<T>(Control root, string automationId) where T : Control =>
        Assert.Single(root.GetVisualDescendants().OfType<T>(),
            control => AutomationProperties.GetAutomationId(control) == automationId);
}
