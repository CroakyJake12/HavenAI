using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Haven.Core;

namespace Haven.Desktop.Views.Pages.Boards;

public sealed partial class BoardsPage
{
    private async Task AddAttachmentAsync()
    {
        if (_page is null || _document is null) return;
        if (_attachments is null) { SetStatus("Attachments are unavailable."); return; }
        var top = TopLevel.GetTopLevel(this);
        if (top?.StorageProvider is null) { SetStatus("File picker unavailable."); return; }
        try
        {
            var file = (await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Add file to Boards",
                AllowMultiple = false
            })).FirstOrDefault();
            if (file is null) return;
            var path = file.TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(path)) { SetStatus("This file must be available locally before Boards can attach it."); return; }
            await _boards.AttachAsync(_document, _page.Id, path, CancellationToken.None);
            RebuildEditor();
            await SaveAsync("Added Boards attachment");
        }
        catch (OperationCanceledException) { SetStatus("Attachment import cancelled."); }
        catch (UnauthorizedAccessException) { SetStatus("Attachment permission was denied."); }
        catch (Exception) { SetStatus("Couldn’t add that attachment."); }
    }

    private async Task AddEmbedAsync()
    {
        if (_page is null || _document is null) return;
        var block = _boards.AddBlock(_document, _page.Id, NotesBlockKind.HtmlWidget);
        block.Html!.FallbackText = "Embedded content";
        block.Metadata["boards.embed"] = bool.TrueString;
        RebuildEditor();
        await SaveAsync("Added Boards embed");
    }

    private Control BuildAttachmentBlock(NotesBlock block)
    {
        var host = new StackPanel { Spacing = 5, Margin = new Thickness(0, 5, 0, 10) };
        var name = block.Media?.OriginalName ?? "Attachment";
        host.Children.Add(new TextBlock { Text = name, FontWeight = Avalonia.Media.FontWeight.SemiBold });
        var state = new TextBlock { Text = "Checking attachment...", TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        host.Children.Add(state);
        _ = RefreshAttachmentStateAsync(block, state);
        return host;
    }

    private async Task RefreshAttachmentStateAsync(NotesBlock block, TextBlock state)
    {
        if (block.Media is null) return;
        try
        {
            var result = await _boards.ResolveAttachmentAsync(block.Media, CancellationToken.None);
            if (_disposed) return;
            state.Text = result.Status switch
            {
                BoardsAttachmentStatus.Available => "Available locally" + (string.IsNullOrWhiteSpace(result.ResolvedPath) ? string.Empty : Environment.NewLine + result.ResolvedPath),
                BoardsAttachmentStatus.Missing => "Missing attachment · " + result.Message,
                _ => "Attachment unavailable · " + result.Message
            };
        }
        catch (Exception)
        {
            if (!_disposed) state.Text = "Attachment unavailable.";
        }
    }
    private void BuildEmbed(StackPanel host, NotesBlock block)
    {
        if (block.Html is null) return;
        var source = new TextBox { Text = block.Html.HtmlSource, AcceptsReturn = true, MinHeight = 120 };
        var fallback = new TextBox { Text = block.Html.FallbackText, PlaceholderText = "Accessible fallback text" };
        source.TextChanged += (_, _) => { block.Html.HtmlSource = source.Text ?? string.Empty; _document!.UpdatedAt = DateTimeOffset.UtcNow; SetStatus("Unsaved embed changes"); };
        fallback.TextChanged += (_, _) => { block.Html.FallbackText = fallback.Text ?? string.Empty; _document!.UpdatedAt = DateTimeOffset.UtcNow; SetStatus("Unsaved embed changes"); };
        host.Children.Add(source);
        host.Children.Add(fallback);
    }
}
