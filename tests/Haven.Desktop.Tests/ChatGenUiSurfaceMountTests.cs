using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Haven.Desktop.HavenUI.Backend;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Controls;
using Haven.Desktop.Views.Pages.Chat;

namespace Haven.Desktop.Tests;

public sealed class ChatGenUiSurfaceMountTests
{
    [AvaloniaFact]
    public void Native_and_composite_layers_mount_real_Avalonia_GenUi_controls()
    {
        foreach (var layer in new[] { GenUiRenderingLayer.Native, GenUiRenderingLayer.Composite })
        {
            var (router, store) = Runtime();
            var resolver = new ChatGenUiNativeControlResolver();
            using var mount = ChatGenUiSurfaceMount.Create(new GenUiRenderingDecision(layer, "test"), router, store, resolver);

            Assert.True(mount.UsesNativeHost);
            Assert.True(resolver.TryCreate(mount.Root, out var control));
            Assert.IsType<GenerativeUiSurface>(control);
        }
    }

    [AvaloniaFact]
    public void Scene_and_sandbox_layers_remain_on_Haven_scene_renderer()
    {
        foreach (var layer in new[] { GenUiRenderingLayer.Scene, GenUiRenderingLayer.GeneratedSandbox })
        {
            var (router, store) = Runtime();
            var resolver = new ChatGenUiNativeControlResolver();
            using var mount = ChatGenUiSurfaceMount.Create(new GenUiRenderingDecision(layer, "test"), router, store, resolver);

            Assert.False(mount.UsesNativeHost);
            Assert.False(resolver.TryCreate(mount.Root, out _));
        }
    }

    [AvaloniaFact]
    public void Native_mount_unregisters_control_on_dispose()
    {
        var (router, store) = Runtime();
        var resolver = new ChatGenUiNativeControlResolver();
        var mount = ChatGenUiSurfaceMount.Create(new GenUiRenderingDecision(GenUiRenderingLayer.Native, "test"), router, store, resolver);
        var root = mount.Root;
        Assert.True(resolver.TryCreate(root, out Control? _));

        mount.Dispose();

        Assert.False(resolver.TryCreate(root, out _));
    }

    [AvaloniaFact]
    public void Executable_generated_code_is_rejected_before_mount()
    {
        var (router, store) = Runtime();
        var resolver = new ChatGenUiNativeControlResolver();

        Assert.Throws<InvalidOperationException>(() => ChatGenUiSurfaceMount.Create(
            new GenUiRenderingDecision(GenUiRenderingLayer.GeneratedSandbox, "test", AllowsExecutableCode: true),
            router, store, resolver));
    }

    [AvaloniaFact]
    public async Task Native_mount_is_physically_hosted_inside_HavenSceneControl()
    {
        var (router, store) = Runtime();
        var resolver = new ChatGenUiNativeControlResolver();
        using var mount = ChatGenUiSurfaceMount.Create(new GenUiRenderingDecision(GenUiRenderingLayer.Native, "test"), router, store, resolver);
        var origin = new GenUiOrigin(Guid.NewGuid(), "chat", null, Guid.NewGuid());
        var document = new GenUiDocument(
            Guid.NewGuid(),
            GenerativeUiContractValidator.CurrentContractVersion,
            origin,
            "Native host",
            "chat",
            new GenUiComponent("root", "HavenWorkspace", new Dictionary<string, System.Text.Json.JsonElement>(), [],
                [new GenUiComponent("label", "HavenText", new Dictionary<string, System.Text.Json.JsonElement>
                {
                    ["text"] = System.Text.Json.JsonSerializer.SerializeToElement("Hosted")
                }, [], [])]),
            new Dictionary<string, System.Text.Json.JsonElement>(),
            DateTimeOffset.UtcNow);
        mount.Present(document);
        var host = new HavenSceneControl(new HavenAvaloniaImageResolver(), resolver) { Root = mount.Root };
        var window = new Window { Content = host };
        try
        {
            window.Show();
            await Dispatcher.UIThread.InvokeAsync(() => { });
            Assert.Single(host.GetVisualDescendants().OfType<GenerativeUiSurface>());
        }
        finally
        {
            window.Close();
        }
    }

    private static (GenerativeUiEventRouter Router, GenUiInstanceStore Store) Runtime()
    {
        var store = new GenUiInstanceStore();
        var local = new GenUiLocalActionRegistry();
        return (new GenerativeUiEventRouter([local], new BoundedGenUiEventAuditSink(), store), store);
    }
}
