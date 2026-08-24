using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Haven.Core;

namespace Haven.Desktop.Views.Pages.Imagine;

public sealed partial class ImaginePage
{
    private readonly ImagineVideoClipExporter _videoClipExporter = new();

    private void WireVideoExport()
    {
        _scene.ExportVideoClipRequested += async (_, _) => await ExportSelectedVideoClipAsync();
    }

    private async Task ExportSelectedVideoClipAsync()
    {
        if (_session is null) return;
        if (_session.Project.Selection is not { Kind: ImagineSelectionKind.Clip, TargetId: Guid clipId })
        {
            SetStatus("Select a video clip before exporting it.");
            return;
        }
        if (!ImagineVideoClipExporter.TryCreatePlan(_session.Project, clipId, out var plan, out var planStatus) || plan is null)
        {
            SetStatus(planStatus);
            return;
        }

        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null)
        {
            SetStatus("The platform save picker is unavailable.");
            return;
        }
        var destination = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export selected video clip",
            SuggestedFileName = SafeFileName(plan.ClipName) + ".mp4"
        });
        var path = destination?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path)) return;

        _operationCancellation?.Cancel();
        _operationCancellation?.Dispose();
        var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        _operationCancellation = cancellation;
        SetStatus("Exporting selected video clip…");
        try
        {
            var result = await _videoClipExporter.ExportAsync(plan, path, cancellation.Token);
            SetStatus(result.Path is { Length: > 0 } exported ? result.Status + " " + exported : result.Status);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            SetStatus("Video clip export was cancelled or timed out. The project was not changed.");
        }
        finally
        {
            if (ReferenceEquals(_operationCancellation, cancellation)) _operationCancellation = null;
            cancellation.Dispose();
        }
    }
}
