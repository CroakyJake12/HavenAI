using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.HavenUI.Backend;
using Haven.Desktop.HavenUI.Creative;
using Haven.Desktop.HavenUI.GenerativeUi;
using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;
using HavenText = Haven.UI.Components.Text;

namespace Haven.Desktop.Tests;

public sealed class HavenNativeWhiteboardTests
{
    [AvaloniaFact]
    public async Task Canvas_is_native_persisted_editable_and_restores_on_the_canonical_canvas_engine()
    {
        var store = new GenUiInstanceStore();
        var local = new GenUiLocalActionRegistry();
        var router = new GenerativeUiEventRouter([local], new BoundedGenUiEventAuditSink(), store);
        var instanceId = Guid.NewGuid();
        var component = new GenUiComponent(
            "board.canvas",
            "HavenCanvas",
            new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["title"] = JsonSerializer.SerializeToElement("Native board"),
                ["prompt"] = JsonSerializer.SerializeToElement("Sketch the flow"),
                ["minHeight"] = JsonSerializer.SerializeToElement(460),
                ["automationName"] = JsonSerializer.SerializeToElement("Native whiteboard")
            },
            [],
            []);
        var document = new GenUiDocument(
            Guid.NewGuid(),
            GenerativeUiContractValidator.CurrentContractVersion,
            new GenUiOrigin(Guid.NewGuid(), "chat", null, instanceId),
            "Native board",
            "chat",
            component,
            new Dictionary<string, JsonElement>(StringComparer.Ordinal),
            DateTimeOffset.UtcNow);

        using var surface = new HavenGenUiSceneSurface(router, store);
        var host = new HavenSceneControl { Root = surface.Root };
        var window = new Window { Width = 980, Height = 760, Content = host };
        try
        {
            surface.Present(document);
            window.Show();
            window.UpdateLayout();

            var canvas = Assert.Single(surface.Root.DescendantsAndSelf().OfType<UnifiedCanvasSurface>());
            Assert.True(canvas.Bounds.Width > 400);
            Assert.True(canvas.Bounds.Height >= 280);
            Assert.DoesNotContain(surface.Root.DescendantsAndSelf().OfType<HavenText>(),
                text => text.Content.Contains("HavenCanvas foundation", StringComparison.Ordinal));

            var labels = surface.Root.DescendantsAndSelf().OfType<HavenButton>()
                .Select(button => button.Content)
                .ToHashSet(StringComparer.Ordinal);
            foreach (var tool in new[]
                     {
                         "Select", "Pen", "Highlight", "Eraser", "Text", "Rectangle",
                         "Ellipse", "Line", "Pan", "Undo", "Redo", "Copy", "Paste",
                         "Delete", "Clear"
                     })
                Assert.Contains(tool, labels);

            var input = new HavenInputRouter(surface.Root);
            var start = new HavenPoint(canvas.Bounds.X + 80, canvas.Bounds.Y + 80);
            var middle = new HavenPoint(canvas.Bounds.X + 130, canvas.Bounds.Y + 105);
            var end = new HavenPoint(canvas.Bounds.X + 190, canvas.Bounds.Y + 130);
            input.PointerPressed(start, HavenPointerKind.Pen);
            input.PointerMoved(middle, HavenPointerKind.Pen);
            input.PointerMoved(end, HavenPointerKind.Pen);
            Assert.True(input.PointerReleased(end));
            await Dispatcher.UIThread.InvokeAsync(() => { });

            const string stateKey = "canvas.board.canvas";
            var state = State(store, instanceId, stateKey);
            Assert.Equal(3, state.GetProperty("version").GetInt32());
            Assert.Single(Board(state).GetProperty("strokes").EnumerateArray());
            Assert.True(new HavenSceneRenderer().Render(surface.Root).OfType<HavenLineCommand>().Any());

            var centre = new HavenPoint(canvas.Bounds.X + canvas.Bounds.Width / 2, canvas.Bounds.Y + canvas.Bounds.Height / 2);
            Assert.True(input.Scroll(centre, 0, -48));
            await Dispatcher.UIThread.InvokeAsync(() => { });
            state = State(store, instanceId, stateKey);
            Assert.True(Board(state).GetProperty("zoom").GetDouble() > 1);

            Click(surface.Root, Button(surface.Root, "Undo"));
            await Dispatcher.UIThread.InvokeAsync(() => { });
            Assert.Empty(Board(State(store, instanceId, stateKey)).GetProperty("strokes").EnumerateArray());

            Click(surface.Root, Button(surface.Root, "Redo"));
            Click(surface.Root, Button(surface.Root, "Fit Canvas"));
            Click(surface.Root, Button(surface.Root, "Rectangle"));
            var objectStart = new HavenPoint(canvas.Bounds.X + 240, canvas.Bounds.Y + 200);
            var objectEnd = new HavenPoint(canvas.Bounds.X + 360, canvas.Bounds.Y + 280);
            input.PointerPressed(objectStart);
            input.PointerMoved(objectEnd);
            Assert.True(input.PointerReleased(objectEnd));
            await Dispatcher.UIThread.InvokeAsync(() => { });

            state = State(store, instanceId, stateKey);
            var objects = Board(state).GetProperty("objects");
            Assert.Single(objects.EnumerateArray());
            var originalX = objects[0].GetProperty("x").GetDouble();

            var selectPoint = new HavenPoint(canvas.Bounds.X + 280, canvas.Bounds.Y + 235);
            input.PointerPressed(selectPoint);
            input.PointerMoved(new HavenPoint(selectPoint.X + 32, selectPoint.Y + 20));
            Assert.True(input.PointerReleased(new HavenPoint(selectPoint.X + 32, selectPoint.Y + 20)));
            await Dispatcher.UIThread.InvokeAsync(() => { });
            state = State(store, instanceId, stateKey);
            Assert.True(Board(state).GetProperty("objects")[0].GetProperty("x").GetDouble() > originalX);

            var latest = store.TryGet(instanceId);
            Assert.NotNull(latest);
            using var restored = new HavenGenUiSceneSurface(router, store);
            var restoredHost = new HavenSceneControl { Root = restored.Root };
            var restoredWindow = new Window { Width = 980, Height = 760, Content = restoredHost };
            try
            {
                restored.PresentExisting(latest!);
                restoredWindow.Show();
                restoredWindow.UpdateLayout();
                Assert.Single(restored.Root.DescendantsAndSelf().OfType<UnifiedCanvasSurface>());
                Assert.True(new HavenSceneRenderer().Render(restored.Root).OfType<HavenLineCommand>().Any());
                var restoredState = State(store, instanceId, stateKey);
                Assert.Single(Board(restoredState).GetProperty("strokes").EnumerateArray());
                Assert.Single(Board(restoredState).GetProperty("objects").EnumerateArray());
            }
            finally
            {
                restoredWindow.Content = null;
                restoredWindow.Close();
            }
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    private static JsonElement State(GenUiInstanceStore store, Guid instanceId, string key)
    {
        var document = store.TryGet(instanceId);
        Assert.NotNull(document);
        Assert.True(document!.State.TryGetValue(key, out var state));
        return state;
    }

    private static JsonElement Board(JsonElement state) => state.GetProperty("board");

    private static HavenButton Button(HavenElement root, string content) =>
        Assert.Single(root.DescendantsAndSelf().OfType<HavenButton>(), button => button.Content == content);

    private static void Click(HavenElement root, HavenElement element)
    {
        var input = new HavenInputRouter(root);
        var point = new HavenPoint(element.Bounds.X + element.Bounds.Width / 2, element.Bounds.Y + element.Bounds.Height / 2);
        input.PointerPressed(point);
        Assert.True(input.PointerReleased(point));
    }
}
