using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Pages.Imagine;
using Haven.Desktop.Views.Shell.TopRail;
#if !ANDROID
using Haven.Desktop.Overlay;
#endif
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
#if ANDROID
        page.ConfigureProductIntegration(services.GetRequiredService<VisionWorkspaceStateStore>(), null);
#else
        var visualCapture = services.GetRequiredService<OverlayVisualContextCaptureService>();
        page.ConfigureProductIntegration(services.GetRequiredService<VisionWorkspaceStateStore>(), async cancellationToken =>
        {
            var context = await visualCapture.CaptureAsync(cancellationToken);
            return context.Attachments.FirstOrDefault(attachment => attachment.Kind.Equals("image", StringComparison.OrdinalIgnoreCase))?.Id;
        });
#endif
        page.OpenInImagineRequested += path => _ = OpenImagineAssetAsync(path);
        page.SendToChatRequested += handoff => _ = SendVisionToChatAsync(handoff);
        page.SendToGoRequested += handoff => _ = SendVisionToGoAsync(handoff);
        page.OpenOverlayRequested += handoff => _ = OpenVisionOverlayAsync(handoff);
        return page;
    }

    private static TaskAttachmentSnapshot VisionAttachment(VisionHandoff handoff) =>
        new([handoff.SourcePath], [], [], new HashSet<Guid>());

    private async Task SendVisionToChatAsync(VisionHandoff handoff)
    {
        var instruction = string.IsNullOrWhiteSpace(handoff.Response)
            ? "Continue working with this image from Vision."
            : "Continue working with this image from Vision. Vision's current analysis was:\n\n" + handoff.Response;
        await OpenNewChatAsync(instruction, forceNewTab: true, initialAttachments: VisionAttachment(handoff));
    }

    private async Task SendVisionToGoAsync(VisionHandoff handoff)
    {
        await OpenGoAsync();
        _goPage?.RestorePendingTask(
            string.IsNullOrWhiteSpace(handoff.Response) ? "Continue with this Vision image." : "Continue with this Vision image and its current analysis.",
            VisionAttachment(handoff));
    }

    private async Task OpenVisionOverlayAsync(VisionHandoff handoff)
    {
#if ANDROID
        _notifications.Show("Overlay unavailable", "Floating Overlay is not available on this platform.", ToastKind.Info, TimeSpan.FromSeconds(5));
        await Task.CompletedTask;
#else
        var now = DateTimeOffset.UtcNow;
        var label = Path.GetFileName(handoff.SourcePath);
        var attachment = new OverlayContextAttachmentReference(handoff.SourcePath, "image", VisionMimeType(handoff.SourcePath), label, null);
        var selection = new OverlaySelectionItem(
            Guid.NewGuid().ToString("N"),
            OverlaySelectionKind.Image,
            null,
            null,
            null,
            attachment,
            new OverlaySelectionSemanticMetadata(null, label, null, null, true, null, "image", null),
            label).Bound();
        var context = new OverlayContextEnvelope(
            string.IsNullOrWhiteSpace(handoff.Response) ? OverlayContextKind.Image : OverlayContextKind.Mixed,
            string.IsNullOrWhiteSpace(handoff.Response) ? null : handoff.Response,
            [attachment],
            null,
            new OverlayContextProvenance("Haven Vision", null, null, now, now.AddMinutes(30), OverlayContextPermissionState.NotRequired, "The user explicitly sent the current Vision source to Overlay."),
            false,
            [selection]).Bound();
        var services = App.Services ?? throw new InvalidOperationException("Haven services are unavailable while opening Overlay.");
        await services.GetRequiredService<OverlayWorkspaceController>().OpenNewGoAsync(context, "Vision", CancellationToken.None);
#endif
    }

    private static string VisionMimeType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".webp" => "image/webp",
        ".gif" => "image/gif",
        ".bmp" => "image/bmp",
        _ => "image/jpeg"
    };

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
