using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;
using HavenContainer = Haven.UI.Components.Container;

namespace Haven.Desktop.Views.Pages.Imagine;

public sealed partial class VisionPage
{
    private string? _temporaryClipboardImagePath;

    internal static bool IsSupportedImagePath(string? path) =>
        !string.IsNullOrWhiteSpace(path) && Path.GetExtension(path).ToLowerInvariant() is
            ".png" or ".jpg" or ".jpeg" or ".webp" or ".gif" or ".bmp";

    private void WirePlatformImageInput()
    {
        AttachPlatformPasteButton(_scene, PasteImageAsync);
        AddHandler(DragDrop.DragOverEvent, OnVisionDragOver);
        AddHandler(DragDrop.DropEvent, OnVisionDrop);
    }

    internal static HavenButton AttachPlatformPasteButton(VisionScene scene, Func<Task> pasteAsync)
    {
        var header = scene.Root.DescendantsAndSelf().OfType<HavenContainer>()
            .Single(item => item.Name == "Vision.Header");
        var paste = new HavenButton
        {
            Name = "Vision.Paste",
            Content = "Paste image",
            IconKey = "file",
            Variant = ButtonVariant.Ghost
        };
        paste.Invoked += async (_, _) => await pasteAsync();
        header.Add(paste);
        return paste;
    }

    private void OnVisionDragOver(object? sender, DragEventArgs e)
    {
        var hasImage = e.DataTransfer.TryGetFiles()?.OfType<IStorageFile>()
            .Select(item => item.TryGetLocalPath())
            .Any(IsSupportedImagePath) == true;
        e.DragEffects = hasImage ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnVisionDrop(object? sender, DragEventArgs e)
    {
        var path = e.DataTransfer.TryGetFiles()?.OfType<IStorageFile>()
            .Select(item => item.TryGetLocalPath())
            .FirstOrDefault(IsSupportedImagePath);
        e.DragEffects = path is null ? DragDropEffects.None : DragDropEffects.Copy;
        e.Handled = true;
        if (path is not null) await LoadImageAsync(path);
    }

    private async Task PasteImageAsync()
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
        {
            _scene.SetStatus("The platform clipboard is unavailable.");
            return;
        }

        var files = await clipboard.TryGetFilesAsync();
        var filePath = files?.OfType<IStorageFile>()
            .Select(item => item.TryGetLocalPath())
            .FirstOrDefault(IsSupportedImagePath);
        if (filePath is not null)
        {
            await LoadImageAsync(filePath);
            return;
        }

        using var bitmap = await clipboard.TryGetBitmapAsync();
        if (bitmap is null)
        {
            _scene.SetStatus("The clipboard does not contain an image or image file.");
            return;
        }

        DeletePreviousClipboardImage();
        var directory = Path.Combine(Path.GetTempPath(), "Haven", "Vision");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "clipboard-" + Guid.NewGuid().ToString("N") + ".png");
        bitmap.Save(path);
        _temporaryClipboardImagePath = path;
        await LoadImageAsync(path);
        _scene.SetStatus("Pasted image ready for visual analysis.");
    }

    private void DeletePreviousClipboardImage()
    {
        if (string.IsNullOrWhiteSpace(_temporaryClipboardImagePath)) return;
        try { File.Delete(_temporaryClipboardImagePath); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        _temporaryClipboardImagePath = null;
    }
}
