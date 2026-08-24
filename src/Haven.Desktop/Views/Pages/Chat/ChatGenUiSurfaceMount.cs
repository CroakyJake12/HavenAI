using Avalonia.Automation;
using Avalonia.Controls;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Controls;
using Haven.Desktop.HavenUI.Backend;
using Haven.Desktop.HavenUI.GenerativeUi;
using Haven.UI;
using HavenInput = Haven.UI.Components.Input;
using HavenNativeHost = Haven.UI.Components.NativeHost;

namespace Haven.Desktop.Views.Pages.Chat;

internal sealed class ChatGenUiSurfaceMount : IDisposable
{
    private readonly ChatGenUiNativeControlResolver _nativeResolver;
    private readonly HavenGenUiSceneSurface? _sceneSurface;
    private readonly GenerativeUiSurface? _nativeSurface;
    private readonly HavenNativeHost? _nativeHost;
    private bool _disposed;

    private ChatGenUiSurfaceMount(ChatGenUiNativeControlResolver nativeResolver, HavenGenUiSceneSurface? sceneSurface, GenerativeUiSurface? nativeSurface, HavenNativeHost? nativeHost)
    {
        _nativeResolver = nativeResolver;
        _sceneSurface = sceneSurface;
        _nativeSurface = nativeSurface;
        _nativeHost = nativeHost;
        Root = sceneSurface is not null
            ? sceneSurface.Root
            : nativeHost ?? throw new InvalidOperationException("A GenUI mount requires a physical host.");
    }

    public HavenElement Root { get; }
    public GenUiDocument? Document => _sceneSurface?.Document ?? _nativeSurface?.Document;
    public bool UsesNativeHost => _nativeSurface is not null;

    public static ChatGenUiSurfaceMount Create(GenUiRenderingDecision rendering, GenerativeUiEventRouter router, GenUiInstanceStore instances, ChatGenUiNativeControlResolver nativeResolver)
    {
        ArgumentNullException.ThrowIfNull(rendering);
        ArgumentNullException.ThrowIfNull(router);
        ArgumentNullException.ThrowIfNull(instances);
        ArgumentNullException.ThrowIfNull(nativeResolver);
        if (rendering.AllowsExecutableCode) throw new InvalidOperationException("Generated executable code cannot be mounted by Chat GenUI.");

        if (rendering.Layer is GenUiRenderingLayer.Native or GenUiRenderingLayer.Composite)
        {
            var host = new HavenNativeHost();
            host.SetValue(HavenProperties.Width, HavenLength.Percent(100));
            host.SetValue(HavenProperties.MinHeight, HavenLength.Px(180));
            host.Accessibility.AccessibleName = "Generated native interface";
            var surface = new GenerativeUiSurface(router, instances)
            {
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch,
                MinHeight = 180
            };
            AutomationProperties.SetAutomationId(surface, "ChatGeneratedNativeSurface");
            nativeResolver.Register(host, surface);
            return new ChatGenUiSurfaceMount(nativeResolver, null, surface, host);
        }

        return new ChatGenUiSurfaceMount(nativeResolver, new HavenGenUiSceneSurface(router, instances), null, null);
    }

    public void Present(GenUiDocument document)
    {
        ThrowIfDisposed();
        if (_sceneSurface is not null) _sceneSurface.Present(document); else _nativeSurface!.Present(document);
    }

    public void PresentExisting(GenUiDocument document)
    {
        ThrowIfDisposed();
        if (_sceneSurface is not null) _sceneSurface.PresentExisting(document); else _nativeSurface!.PresentExisting(document);
    }

    public bool OwnsInput(HavenInput input) => _sceneSurface?.OwnsInput(input) == true;

    public Task SubmitInputAsync(HavenInput input, CancellationToken cancellationToken = default) =>
        _sceneSurface is null ? Task.CompletedTask : _sceneSurface.SubmitInputAsync(input, cancellationToken);

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(ChatGenUiSurfaceMount));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_nativeHost is not null) _nativeResolver.Unregister(_nativeHost);
        _nativeSurface?.Dispose();
        _sceneSurface?.Dispose();
    }
}

internal sealed class ChatGenUiNativeControlResolver : IHavenAvaloniaNativeControlResolver
{
    private readonly Dictionary<HavenElement, Control> _controls = [];

    public void Register(HavenElement element, Control control)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(control);
        if (!_controls.TryAdd(element, control)) throw new InvalidOperationException("This generated native host is already registered.");
    }

    public void Unregister(HavenElement element) => _controls.Remove(element);

    public bool TryCreate(HavenElement element, out Control? control)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (_controls.TryGetValue(element, out var found))
        {
            control = found;
            return true;
        }
        control = null;
        return false;
    }
}
