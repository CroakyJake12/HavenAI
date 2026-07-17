using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Haven.Application;
using Haven.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Views;

public sealed partial class NotesWorkspaceView
{
    private readonly StackPanel _blockPanel = new() { Spacing = 9 };

    private void BuildSelectedBlockInspector()
    {
        _blockPanel.Children.Clear();
        _blockPanel.Children.Add(new TextBlock { Text = "SELECTED BLOCK", Classes = { "eyebrow" } });
        var block = _viewModel.SelectedBlock;
        if (block is null)
        {
            _blockPanel.Children.Add(new TextBlock
            {
                Text = "Select a block to inspect its structure and use block-specific tools.",
                Classes = { "muted" },
                TextWrapping = TextWrapping.Wrap
            });
            return;
        }

        _blockPanel.Children.Add(new TextBlock
        {
            Text = block.Kind + " · " + block.Id.ToString("D"),
            Classes = { "muted2" },
            FontSize = 9,
            TextWrapping = TextWrapping.Wrap
        });

        if (block.Table is not null) BuildTableInspector(block);
        if (block.Media is not null) BuildMediaInspector(block);
        if (block.Table is null && block.Media is null)
        {
            _blockPanel.Children.Add(new TextBlock
            {
                Text = "This block is edited directly in the document surface. Table and managed-media blocks expose additional verified tools here.",
                Classes = { "muted" },
                TextWrapping = TextWrapping.Wrap,
                FontSize = 10
            });
        }
    }

    private void BuildTableInspector(NotesBlock block)
    {
        var table = block.Table!;
        _blockPanel.Children.Add(new TextBlock
        {
            Text = "TABLE TOOLS",
            Classes = { "eyebrow" },
            Margin = new Thickness(0, 6, 0, 0)
        });
        var maximumColumns = Math.Max(1, table.Rows.Count == 0 ? 1 : table.Rows.Max(row => row.Cells.Count));
        var column = new NumericUpDown
        {
            Minimum = 1,
            Maximum = maximumColumns,
            Value = 1,
            Increment = 1
        };
        var result = new TextBlock
        {
            Classes = { "muted" },
            TextWrapping = TextWrapping.Wrap,
            FontSize = 10
        };
        _blockPanel.Children.Add(Labeled("Column", column));
        var actions = new WrapPanel();
        actions.Children.Add(ActionButton("Sort ↑", () =>
        {
            BeginEditing(block);
            NotesTableOperations.Sort(table, Math.Max(0, Decimal.ToInt32(column.Value ?? 1) - 1), descending: false);
            EndEditing(block, "Sorted table ascending");
            return Task.CompletedTask;
        }, "Sort the selected table column ascending"));
        actions.Children.Add(ActionButton("Sort ↓", () =>
        {
            BeginEditing(block);
            NotesTableOperations.Sort(table, Math.Max(0, Decimal.ToInt32(column.Value ?? 1) - 1), descending: true);
            EndEditing(block, "Sorted table descending");
            return Task.CompletedTask;
        }, "Sort the selected table column descending"));
        actions.Children.Add(ActionButton("Sum", () =>
        {
            var value = NotesTableOperations.Sum(table, Math.Max(0, Decimal.ToInt32(column.Value ?? 1) - 1));
            result.Text = "Column total: " + value.ToString(System.Globalization.CultureInfo.CurrentCulture);
            return Task.CompletedTask;
        }, "Calculate the numeric total for the selected column"));
        _blockPanel.Children.Add(actions);
        _blockPanel.Children.Add(result);

        var delimited = new TextBox
        {
            Text = NotesTableOperations.ToDelimitedText(table),
            AcceptsReturn = true,
            MinHeight = 120,
            TextWrapping = TextWrapping.NoWrap,
            Watermark = "Tab-separated table data"
        };
        _blockPanel.Children.Add(Labeled("Tab-separated data", delimited));
        _blockPanel.Children.Add(ActionButton("Apply table data", () =>
        {
            BeginEditing(block);
            block.Table = NotesTableOperations.FromDelimitedText(delimited.Text ?? string.Empty);
            EndEditing(block, "Imported tab-separated table data");
            return Task.CompletedTask;
        }, "Replace the selected table with the tab-separated data above"));
    }

    private void BuildMediaInspector(NotesBlock block)
    {
        var media = block.Media!;
        _blockPanel.Children.Add(new TextBlock
        {
            Text = "MANAGED MEDIA",
            Classes = { "eyebrow" },
            Margin = new Thickness(0, 6, 0, 0)
        });
        var verification = new TextBlock
        {
            Text = "Not verified in this session.",
            Classes = { "muted" },
            TextWrapping = TextWrapping.Wrap,
            FontSize = 10
        };
        _blockPanel.Children.Add(new TextBlock
        {
            Text = media.OriginalName + "\n" + media.MediaType + " · " + FormatBytes(media.SizeBytes),
            TextWrapping = TextWrapping.Wrap
        });
        _blockPanel.Children.Add(verification);

        var actions = new WrapPanel();
        actions.Children.Add(ActionButton("Verify", async () =>
        {
            try
            {
                var service = ResolveMediaService();
                var checkedMedia = await service.VerifyAsync(media, CancellationToken.None);
                verification.Text = checkedMedia.SizeMatches && checkedMedia.HashMatches
                    ? $"Verified {checkedMedia.VerifiedAt.LocalDateTime:g} · SHA-256 {ShortHash(checkedMedia.Sha256)}"
                    : "Blocked: the managed file does not match its recorded size or SHA-256 hash.";
                verification.Foreground = checkedMedia.SizeMatches && checkedMedia.HashMatches
                    ? ResourceBrush("HavenTextSoftBrush", Colors.LightGreen)
                    : ResourceBrush("HavenDangerBrush", Colors.IndianRed);
            }
            catch (Exception ex)
            {
                verification.Text = "Verification failed: " + ex.Message;
                verification.Foreground = ResourceBrush("HavenDangerBrush", Colors.IndianRed);
            }
        }, "Verify the managed media file before use"));
        actions.Children.Add(ActionButton("Open", async () =>
        {
            try { await ResolveMediaService().OpenAsync(media, CancellationToken.None); }
            catch (Exception ex) { _status.Text = "Media could not open: " + ex.Message; }
        }, "Verify and open the media through the operating system"));
        actions.Children.Add(ActionButton("Replace", async () => await ReplaceSelectedMediaAsync(block), "Replace through the managed attachment store"));
        actions.Children.Add(ActionButton("Save copy", async () => await SaveSelectedMediaCopyAsync(media), "Verify and save an external copy"));
        _blockPanel.Children.Add(actions);

        var transform = NotesMediaTransformStore.Load(block);
        var transcript = new TextBox
        {
            Text = transform.Transcript,
            AcceptsReturn = true,
            MinHeight = 75,
            TextWrapping = TextWrapping.Wrap,
            Watermark = "Accessible transcript for audio or video"
        };
        transcript.GotFocus += (_, _) => BeginEditing(block);
        transcript.LostFocus += (_, _) =>
        {
            transform.Transcript = transcript.Text ?? string.Empty;
            NotesMediaTransformStore.Save(block, transform);
            EndEditing(block, "Edited media transcript");
        };
        _blockPanel.Children.Add(Labeled("Transcript", transcript));

        var captions = new TextBox
        {
            Text = transform.Captions,
            AcceptsReturn = true,
            MinHeight = 60,
            TextWrapping = TextWrapping.Wrap,
            Watermark = "Captions or subtitle text"
        };
        captions.GotFocus += (_, _) => BeginEditing(block);
        captions.LostFocus += (_, _) =>
        {
            transform.Captions = captions.Text ?? string.Empty;
            NotesMediaTransformStore.Save(block, transform);
            EndEditing(block, "Edited media captions");
        };
        _blockPanel.Children.Add(Labeled("Captions", captions));
    }

    private INotesMediaAssetService ResolveMediaService() =>
        App.Services?.GetRequiredService<INotesMediaAssetService>()
        ?? throw new InvalidOperationException("The verified Notes media service is unavailable.");

    private async Task ReplaceSelectedMediaAsync(NotesBlock block)
    {
        if (block.Media is null) return;
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Replace managed Notes media",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Media")
                {
                    Patterns = ["*.png", "*.jpg", "*.jpeg", "*.gif", "*.webp", "*.svg", "*.mp3", "*.wav", "*.m4a", "*.mp4", "*.webm", "*.pdf"]
                }
            ]
        });
        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            var replacement = await ResolveMediaService().ReplaceAsync(block.Media, path, CancellationToken.None);
            BeginEditing(block);
            block.Media = replacement;
            EndEditing(block, "Replaced managed media");
            RefreshAll();
        }
        catch (Exception ex)
        {
            _status.Text = "Media replacement failed: " + ex.Message;
        }
    }

    private async Task SaveSelectedMediaCopyAsync(NotesMediaData media)
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return;
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save verified media copy",
            SuggestedFileName = string.IsNullOrWhiteSpace(media.OriginalName) ? "Haven Notes media" : media.OriginalName
        });
        var path = file?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            await ResolveMediaService().SaveCopyAsync(media, path, CancellationToken.None);
            _status.Text = "Verified media copy saved: " + file!.Name;
        }
        catch (Exception ex)
        {
            _status.Text = "Media copy failed: " + ex.Message;
        }
    }

    private static string FormatBytes(long value) => value switch
    {
        >= 1024L * 1024 * 1024 => $"{value / (1024d * 1024 * 1024):0.00} GB",
        >= 1024L * 1024 => $"{value / (1024d * 1024):0.00} MB",
        >= 1024 => $"{value / 1024d:0.0} KB",
        _ => value + " B"
    };
}
