using Haven.UI;
using Haven.UI.Components;
using Container = Haven.UI.Components.Container;

namespace Haven.Desktop.Views.Pages.Mesh;

internal sealed partial class MeshHavenScene
{
    internal event Action<Guid>? ClipboardSendRequested;
    internal event Action<Guid>? FileSendRequested;
    internal event Action<string>? ClipboardApplyRequested;

    internal void RenderCurrent() => Render();

    private void BuildTransferHistory(Container parent)
    {
        if (_viewModel.ReceivedClipboards.Count == 0 && _viewModel.ReceivedFiles.Count == 0) return;

        parent.Add(Heading("Transfers", TextLevel.H2));
        if (_viewModel.ReceivedClipboards.Count > 0)
        {
            foreach (var clipboard in _viewModel.ReceivedClipboards.Take(5))
            {
                var card = Card("Mesh.Transfer.Clipboard." + clipboard.TransferId.ToString("N"));
                card.Add(Heading("Clipboard from " + clipboard.SourceDeviceName));
                var preview = clipboard.Text.ReplaceLineEndings(" ").Trim();
                if (preview.Length > 180) preview = preview[..180] + "…";
                card.Add(Muted(string.IsNullOrWhiteSpace(preview) ? "Empty clipboard text" : preview));
                card.Add(Muted($"Received {clipboard.ReceivedAt.LocalDateTime:g} · not applied automatically"));
                var copy = Button("Mesh.Transfer.Clipboard.Copy." + clipboard.TransferId.ToString("N"), "Copy to this device", ButtonVariant.Secondary);
                copy.Invoked += (_, _) => ClipboardApplyRequested?.Invoke(clipboard.Text);
                card.Add(copy);
                parent.Add(card);
            }
        }

        if (_viewModel.ReceivedFiles.Count > 0)
        {
            foreach (var file in _viewModel.ReceivedFiles.Take(5))
            {
                var card = Card("Mesh.Transfer.File." + file.TransferId.ToString("N"));
                card.Add(Heading(file.FileName));
                card.Add(Muted($"From {file.SourceDeviceName} · {FormatBytes(file.Length)} · SHA-256 verified"));
                card.Add(Muted("Saved in Haven's controlled Mesh inbox: " + file.SavedPath));
                var copyPath = Button("Mesh.Transfer.File.CopyPath." + file.TransferId.ToString("N"), "Copy inbox path", ButtonVariant.Ghost);
                copyPath.Invoked += (_, _) => ClipboardApplyRequested?.Invoke(file.SavedPath);
                card.Add(copyPath);
                parent.Add(card);
            }
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024d:0.#} KiB";
        return $"{bytes / 1024d / 1024d:0.#} MiB";
    }
}
