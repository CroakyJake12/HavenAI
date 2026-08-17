using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Pages.Imagine;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Views.Shell;

/// <summary>
/// Owns the dedicated creative-app navigation seam. Imagine and Vision are product
/// workspaces, not mode-flavoured Chat pages; all other ModeWorkspace routes remain unchanged.
/// </summary>
public sealed partial class MainView
{
    private ImaginePage? _imagineWorkspace;
    private VisionPage? _visionWorkspace;

    private Task OpenCreativeModeWorkspaceAsync(ModeDefinition mode, HavenSurface surface, bool forceNewTab)
    {
        return surface switch
        {
            HavenSurface.Imagine => OpenImagineWorkspaceAsync(mode, forceNewTab),
            HavenSurface.Vision => OpenVisionWorkspaceAsync(mode, forceNewTab),
            _ => Task.CompletedTask
        };
    }

    private Task OpenImagineWorkspaceAsync(ModeDefinition mode, bool forceNewTab)
    {
        var page = forceNewTab ? CreateImagineWorkspace() : _imagineWorkspace ??= CreateImagineWorkspace();
        var key = forceNewTab ? $"app-{mode.Key}-{Guid.NewGuid():N}" : $"app-{mode.Key}";
        AddOrSelectTab(key, mode.Name, page, forceNewTab, HavenSurface.Imagine, forceNewTab);
        ApplyShellVisualState();
        return Task.CompletedTask;
    }

    private Task OpenVisionWorkspaceAsync(ModeDefinition mode, bool forceNewTab)
    {
        var page = forceNewTab ? CreateVisionWorkspace() : _visionWorkspace ??= CreateVisionWorkspace();
        var key = forceNewTab ? $"app-{mode.Key}-{Guid.NewGuid():N}" : $"app-{mode.Key}";
        AddOrSelectTab(key, mode.Name, page, forceNewTab, HavenSurface.Vision, forceNewTab);
        ApplyShellVisualState();
        return Task.CompletedTask;
    }

    private ImaginePage CreateImagineWorkspace()
    {
        var services = App.Services ?? throw new InvalidOperationException("Haven services are unavailable while opening Imagine.");
        var page = new ImaginePage(
            services.GetRequiredService<IImagineProjectRepository>(),
            services.GetRequiredService<IImagineSemanticService>(),
            services.GetRequiredService<IImagineAssistantService>());
        page.InspectInVisionRequested += path => _ = OpenVisionAssetAsync(path);
        return page;
    }

    private VisionPage CreateVisionWorkspace()
    {
        var services = App.Services ?? throw new InvalidOperationException("Haven services are unavailable while opening Vision.");
        var page = new VisionPage(services.GetRequiredService<IProviderModelClient>());
        page.OpenInImagineRequested += path => _ = OpenImagineAssetAsync(path);
        return page;
    }

    private async Task OpenVisionAssetAsync(string path)
    {
        try
        {
            var mode = await _modeRegistry.GetModeByKeyAsync("vision", CancellationToken.None);
            if (mode is null)
            {
                _notifications.Show("Vision unavailable", "The Vision App is not registered in this profile.", ToastKind.Warning, TimeSpan.FromSeconds(5));
                return;
            }
            await OpenVisionWorkspaceAsync(mode, false);
            if (_visionWorkspace is not null) await _visionWorkspace.LoadImageAsync(path);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            _notifications.Show("Vision handoff failed", exception.Message, ToastKind.Warning, TimeSpan.FromSeconds(6));
        }
    }

    private async Task OpenImagineAssetAsync(string path)
    {
        try
        {
            var mode = await _modeRegistry.GetModeByKeyAsync("imagine", CancellationToken.None);
            if (mode is null)
            {
                _notifications.Show("Imagine unavailable", "The Imagine App is not registered in this profile.", ToastKind.Warning, TimeSpan.FromSeconds(5));
                return;
            }
            await OpenImagineWorkspaceAsync(mode, false);
            if (_imagineWorkspace is not null) await _imagineWorkspace.ImportPathAsync(path);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            _notifications.Show("Imagine handoff failed", exception.Message, ToastKind.Warning, TimeSpan.FromSeconds(6));
        }
    }
}
