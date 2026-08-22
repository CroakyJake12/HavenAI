using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;
using HavenContainer = Haven.UI.Components.Container;

namespace Haven.Desktop.Views.Pages.Imagine;

public sealed partial class VisionPage
{
    private Func<CancellationToken, Task<string?>>? _captureImageAsync;
    private VisionWorkspaceStateStore? _stateStore;
    private VisionWorkspaceState _workspaceState = VisionWorkspaceState.Empty;

    internal event Action<VisionHandoff>? SendToChatRequested;
    internal event Action<VisionHandoff>? SendToGoRequested;
    internal event Action<VisionHandoff>? OpenOverlayRequested;

    internal void ConfigureProductIntegration(
        VisionWorkspaceStateStore stateStore,
        Func<CancellationToken, Task<string?>>? captureImageAsync)
    {
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
        _captureImageAsync = captureImageAsync;
        AttachProductButton("Vision.Capture", "Capture", "vision", CaptureAsync);
        AttachProductButton("Vision.SendChat", "Send to Chat", "chat", () => RaiseHandoff(SendToChatRequested, "Send to Chat"));
        AttachProductButton("Vision.SendGo", "Send to Go", "send", () => RaiseHandoff(SendToGoRequested, "Send to Go"));
        AttachProductButton("Vision.OpenOverlay", "Open in Overlay", "sparkles", () => RaiseHandoff(OpenOverlayRequested, "Open in Overlay"));
        _ = RestoreWorkspaceStateAsync();
    }

    private void AttachProductButton(string name, string content, string iconKey, Func<Task> action)
    {
        var header = _scene.Root.DescendantsAndSelf().OfType<HavenContainer>().Single(item => item.Name == "Vision.Header");
        if (header.Children.OfType<HavenButton>().Any(button => button.Name == name)) return;
        var button = new HavenButton { Name = name, Content = content, IconKey = iconKey, Variant = ButtonVariant.Ghost };
        button.Invoked += async (_, _) => await action();
        header.Add(button);
    }

    private void AttachProductButton(string name, string content, string iconKey, Action action) =>
        AttachProductButton(name, content, iconKey, () => { action(); return Task.CompletedTask; });

    private async Task CaptureAsync()
    {
        if (_captureImageAsync is null)
        {
            _scene.SetStatus("Screen capture is unavailable on this platform.");
            return;
        }

        try
        {
            _scene.SetStatus("Choose a window or screen to capture. Haven only keeps the selected frame.");
            var path = await _captureImageAsync(CancellationToken.None);
            if (string.IsNullOrWhiteSpace(path))
            {
                _scene.SetStatus("Screen capture did not return an image.");
                return;
            }
            await LoadImageAsync(path);
            _scene.SetStatus("Captured visual context is ready for analysis.");
        }
        catch (OperationCanceledException) { _scene.SetStatus("Screen capture cancelled."); }
        catch (Exception exception) when (exception is PlatformNotSupportedException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            _scene.SetStatus("Screen capture unavailable: " + exception.Message);
        }
    }

    private async Task<string> PrepareLoadedImageAsync(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (_stateStore is null) return fullPath;
        var persistentPath = await _stateStore.PersistSourceAsync(fullPath);
        _workspaceState = new VisionWorkspaceState(persistentPath, _scene.Question.Text, string.Empty, string.Empty, null);
        _scene.Response.Content = "Import an image, then ask a question or choose Read text.";
        _scene.Model.Content = string.Empty;
        await SaveWorkspaceStateAsync();
        return persistentPath;
    }

    private bool TryUseCachedAnalysis(string analysisKey, bool regionAnalysis)
    {
        if (!string.Equals(_workspaceState.AnalysisKey, analysisKey, StringComparison.Ordinal) || string.IsNullOrWhiteSpace(_workspaceState.Response)) return false;
        _scene.SetResponse(_workspaceState.Response, _workspaceState.Model);
        _scene.SetStatus(regionAnalysis ? "Region analysis loaded from cache." : "Analysis loaded from cache.");
        return true;
    }

    private async Task StoreAnalysisAsync(string sourcePath, string prompt, string response, string model, string analysisKey)
    {
        _workspaceState = new VisionWorkspaceState(sourcePath, prompt, response, model, analysisKey);
        await SaveWorkspaceStateAsync();
    }

    private async Task SaveWorkspaceStateAsync()
    {
        if (_stateStore is null) return;
        try { await _stateStore.SaveAsync(_workspaceState); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _scene.SetStatus("Vision state could not be saved: " + exception.Message);
        }
    }

    private async Task RestoreWorkspaceStateAsync()
    {
        if (_stateStore is null) return;
        _workspaceState = await _stateStore.LoadAsync();
        if (!string.IsNullOrWhiteSpace(_workspaceState.SourcePath) && File.Exists(_workspaceState.SourcePath))
        {
            _imagePath = _workspaceState.SourcePath;
            _scene.SetImage(_imagePath);
        }
        _scene.Question.Text = _workspaceState.Question ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(_workspaceState.Response)) _scene.SetResponse(_workspaceState.Response, _workspaceState.Model);
        if (_imagePath is not null) _scene.SetStatus(string.IsNullOrWhiteSpace(_workspaceState.Response) ? "Restored previous Vision source." : "Restored previous Vision source and analysis.");
        else if (!string.IsNullOrWhiteSpace(_workspaceState.Response)) _scene.SetStatus("Previous analysis was restored, but its source image is no longer available.");
    }

    private void RaiseHandoff(Action<VisionHandoff>? handler, string action)
    {
        if (string.IsNullOrWhiteSpace(_imagePath) || !File.Exists(_imagePath))
        {
            _scene.SetStatus(action + " needs a loaded image.");
            return;
        }
        handler?.Invoke(new VisionHandoff(_imagePath, _workspaceState.Response, _workspaceState.Model));
    }
}
