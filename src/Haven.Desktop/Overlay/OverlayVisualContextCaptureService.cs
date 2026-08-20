#if !ANDROID
using System.Text.Json;
using Haven.Application;
namespace Haven.Desktop.Overlay;
internal sealed class OverlayVisualContextCaptureService(IScreenShareService screenShare, IAppPaths paths)
{
    public async Task<OverlayContextEnvelope> CaptureAsync(CancellationToken cancellationToken)
    {
        if (!screenShare.IsSupported) throw new PlatformNotSupportedException(screenShare.UnavailableReason ?? "Visual capture is unavailable.");
        if (screenShare.IsSharing) throw new InvalidOperationException("Visual capture is unavailable while another Haven screen share is active. Stop sharing first.");
        var firstFrame = new TaskCompletionSource<ScreenShareSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnSnapshot(object? sender, ScreenShareSnapshotEventArgs e) => firstFrame.TrySetResult(e.Snapshot);
        screenShare.SnapshotAvailable += OnSnapshot;
        try
        {
            var source = await screenShare.StartWithSystemPickerAsync(cancellationToken);
            var snapshot = await screenShare.GetLatestSnapshotAsync(cancellationToken)
                ?? await firstFrame.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            var path = await PersistAsync(snapshot, cancellationToken);
            return BuildContext(source, snapshot, path);
        }
        finally
        {
            screenShare.SnapshotAvailable -= OnSnapshot;
            try { await screenShare.StopAsync(CancellationToken.None); } catch { }
        }
    }
    private async Task<string> PersistAsync(ScreenShareSnapshot snapshot, CancellationToken cancellationToken)
    {
        var folder = Path.Combine(paths.AttachmentsDirectory, "Overlay");
        Directory.CreateDirectory(folder);
        Cleanup(folder);
        var path = Path.Combine(folder, $"overlay-{Guid.NewGuid():N}.jpg");
        await File.WriteAllBytesAsync(path, Convert.FromBase64String(snapshot.Base64Jpeg), cancellationToken);
        return path;
    }
    private static void Cleanup(string folder)
    {
        var cutoff = DateTime.UtcNow.AddHours(-1);
        try { foreach (var file in Directory.EnumerateFiles(folder, "overlay-*.jpg")) if (File.GetLastWriteTimeUtc(file) < cutoff) try { File.Delete(file); } catch { } } catch { }
    }
    internal static OverlayContextEnvelope BuildContext(ScreenShareSource source, ScreenShareSnapshot snapshot, string path)
    {
        var (kind, selectionKind) = source.Kind switch
        {
            ScreenShareSourceKind.Window => (OverlayContextKind.Window, OverlaySelectionKind.Window),
            ScreenShareSourceKind.Screen => (OverlayContextKind.Screen, OverlaySelectionKind.Screen),
            _ => (OverlayContextKind.Image, OverlaySelectionKind.Image)
        };
        var metadata = JsonSerializer.Serialize(new { snapshot.Width, snapshot.Height, SourceKind = source.Kind.ToString() });
        var attachment = new OverlayContextAttachmentReference(path, "image", "image/jpeg", source.Name, metadata);
        var item = new OverlaySelectionItem(Guid.NewGuid().ToString("N"), selectionKind, null, null, null, attachment,
            new OverlaySelectionSemanticMetadata(null, source.Name, null, null, true, null, "image", null), source.Name).Bound();
        var capturedAt = snapshot.CapturedAt == default ? DateTimeOffset.UtcNow : snapshot.CapturedAt;
        return new OverlayContextEnvelope(kind, null, [attachment], null, new OverlayContextProvenance(
            null, source.Kind == ScreenShareSourceKind.Window ? source.Name : null, null, capturedAt, capturedAt.AddMinutes(2),
            OverlayContextPermissionState.Granted, "Chosen with the Windows screen-share picker."), false, [item]).Bound();
    }
}
#endif
