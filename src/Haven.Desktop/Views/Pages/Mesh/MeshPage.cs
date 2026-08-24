using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Haven.Application;
using Haven.Desktop.HavenUI.Backend;
using Haven.Desktop.ViewModels;

namespace Haven.Desktop.Views.Pages.Mesh;

/// <summary>Thin Avalonia host for the Haven-native Mesh devices and Work Mode scene.</summary>
public sealed class MeshPage : UserControl, IDisposable
{
    private readonly MeshHavenScene _scene;
    private readonly MeshPageViewModel _viewModel;
    private bool _disposed;

    public MeshPage(MeshPageViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _scene = new MeshHavenScene(viewModel);
        _scene.ClipboardSendRequested += OnClipboardSendRequested;
        _scene.FileSendRequested += OnFileSendRequested;
        _scene.ClipboardApplyRequested += OnClipboardApplyRequested;
        Scene = new HavenSceneControl { Root = _scene.Root };
        AutomationProperties.SetAutomationId(this, "HavenNativeMeshPage");
        AutomationProperties.SetName(this, "Haven Mesh and Work Mode");
        AutomationProperties.SetAutomationId(Scene, "HavenNativeMeshScene");
        AutomationProperties.SetName(Scene, "Mesh device and AI team management");
        Content = Scene;
        _ = _scene.InitialiseAsync();
    }

    public HavenSceneControl Scene { get; }
    internal MeshHavenScene HavenScene => _scene;

    private async void OnClipboardSendRequested(Guid deviceId)
    {
        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is null) { SetStatus("The platform clipboard is unavailable."); return; }
            var text = await Avalonia.Input.Platform.ClipboardExtensions.TryGetTextAsync(clipboard) ?? string.Empty;
            await _viewModel.SendClipboardAsync(deviceId, text, CancellationToken.None);
            _scene.RenderCurrent();
        }
        catch (Exception ex) { SetStatus("Could not send clipboard: " + ex.Message); }
    }

    private async void OnFileSendRequested(Guid deviceId)
    {
        try
        {
            var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
            if (storage is null) { SetStatus("The platform file picker is unavailable."); return; }
            var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Send file with Haven Mesh", AllowMultiple = false });
            var file = files.FirstOrDefault();
            if (file is null) return;
            await using var stream = await file.OpenReadAsync();
            if (stream.CanSeek)
            {
                await _viewModel.SendFileAsync(deviceId, file.Name, stream, CancellationToken.None);
            }
            else
            {
                var temporary = Path.Combine(Path.GetTempPath(), "haven-mesh-send-" + Guid.NewGuid().ToString("N") + ".tmp");
                try
                {
                    await using (var buffered = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
                    {
                        var buffer = new byte[64 * 1024];
                        long total = 0;
                        while (true)
                        {
                            var read = await stream.ReadAsync(buffer, CancellationToken.None);
                            if (read <= 0) break;
                            total += read;
                            if (total > MeshCoordinator.MaximumFileBytes)
                                throw new InvalidDataException($"Mesh files are limited to {MeshCoordinator.MaximumFileBytes / 1024 / 1024} MiB.");
                            await buffered.WriteAsync(buffer.AsMemory(0, read), CancellationToken.None);
                        }
                        buffered.Position = 0;
                        await _viewModel.SendFileAsync(deviceId, file.Name, buffered, CancellationToken.None);
                    }
                }
                finally
                {
                    try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
                }
            }
            _scene.RenderCurrent();
        }
        catch (Exception ex) { SetStatus("Could not send file: " + ex.Message); }
    }

    private async void OnClipboardApplyRequested(string text)
    {
        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is null) { SetStatus("The platform clipboard is unavailable."); return; }
            await clipboard.SetTextAsync(text);
            SetStatus("Copied the received Mesh value to this device's clipboard.");
        }
        catch (Exception ex) { SetStatus("Could not update the clipboard: " + ex.Message); }
    }

    private void SetStatus(string status)
    {
        _viewModel.SetStatus(status);
        _scene.RenderCurrent();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _scene.ClipboardSendRequested -= OnClipboardSendRequested;
        _scene.FileSendRequested -= OnFileSendRequested;
        _scene.ClipboardApplyRequested -= OnClipboardApplyRequested;
        Scene.Root = null;
        _scene.Dispose();
    }
}
