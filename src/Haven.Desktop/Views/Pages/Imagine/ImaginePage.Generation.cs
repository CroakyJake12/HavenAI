using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Haven.Application;
using Haven.Core;

namespace Haven.Desktop.Views.Pages.Imagine;

public sealed partial class ImaginePage
{
    private readonly ImagineGenerationCommand _generationCommand;
    private string? _referenceImagePath;

    public event EventHandler? ProviderSettingsRequested;

    private void WireGenerationScene()
    {
        _scene.GenerateRequested += prompt => _ = GenerateImageAsync(prompt);
        _scene.ReferenceRequested += async (_, _) => await PickReferenceAsync();
        _scene.NewBlankRequested += async (_, _) => await CreateBlankAsync();
        _scene.HomeRequested += async (_, _) => await ReturnHomeAsync();
        _scene.HomeProjectRequested += project => _ = OpenProjectAsync(project.Id);
        _scene.CancelRequested += (_, _) => _operationCancellation?.Cancel();
        _scene.ProviderSettingsRequested += (_, _) => ProviderSettingsRequested?.Invoke(this, EventArgs.Empty);
    }

    private async Task PickReferenceAsync()
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) { SetStatus("The platform file picker is unavailable."); return; }
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose an Imagine reference image",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Images") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.webp"] }]
        });
        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path)) return;
        _referenceImagePath = path;
        _scene.SetReference(path);
        SetStatus("Reference image ready. It will be sent only when you choose Generate.");
    }

    private async Task CreateBlankAsync()
    {
        await RunOperationAsync("Creating blank canvas…", async token =>
        {
            var project = await _projects.CreateAsync("Untitled Imagine Project", 1600, 1000, token);
            AttachSession(new ImagineProjectSession(project));
            await RefreshRecentAsync(token);
            SetStatus("Blank canvas ready.");
        });
    }

    private async Task GenerateImageAsync(string prompt)
    {
        var trimmed = prompt.Trim();
        if (string.IsNullOrWhiteSpace(trimmed)) { SetStatus("Describe the image you want Imagine to create."); return; }
        _scene.SetConnectionRequired(false);
        await RunOperationAsync(_referenceImagePath is null ? "Generating image…" : "Generating from reference…", async token =>
        {
            var result = await _generationCommand.ExecuteAsync(
                new ImagineGenerationRequest(trimmed, _referenceImagePath, _scene.GenerationSize, _scene.GenerationQuality),
                ProjectName(trimmed),
                token);
            if (!result.Succeeded || result.Project is null)
            {
                _scene.SetConnectionRequired(result.Generation.FailureKind == ImagineGenerationFailureKind.ConnectionRequired);
                SetStatus(result.Generation.Status);
                return;
            }
            AttachSession(new ImagineProjectSession(result.Project));
            _scene.SetMode(ImagineMediaKind.Image);
            await RefreshRecentAsync(token);
            SetStatus(result.Generation.Status);
        });
    }

    private async Task ReturnHomeAsync()
    {
        if (_session is { } session)
        {
            try { await _projects.SaveAsync(session.Project, CancellationToken.None); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                SetStatus("Could not save before returning home: " + exception.Message);
                return;
            }
            session.Changed -= OnSessionChanged;
            _session = null;
        }
        await RefreshRecentAsync(CancellationToken.None);
        _scene.ShowHome(_recent);
        SetStatus("Ready to create.");
    }

    private static string ProjectName(string prompt)
    {
        var singleLine = string.Join(' ', prompt.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)).Trim();
        if (singleLine.Length > 56) singleLine = singleLine[..56].TrimEnd() + "…";
        return string.IsNullOrWhiteSpace(singleLine) ? "Untitled Imagine Project" : singleLine;
    }
}
