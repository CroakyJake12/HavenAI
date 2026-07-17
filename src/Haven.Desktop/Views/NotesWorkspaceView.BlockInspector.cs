using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Views;

public sealed partial class NotesWorkspaceView
{
    private const string CodeLanguageKey = "haven.notes.code.language";
    private const string CodeWrapKey = "haven.notes.code.wrap";
    private const string CodeLineNumbersKey = "haven.notes.code.line-numbers";
    private const string CodeTabSizeKey = "haven.notes.code.tab-size";
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

        if (block.Kind is NotesBlockKind.Paragraph or NotesBlockKind.Heading or NotesBlockKind.Quote)
            BuildDictationInspector(block);
        if (block.Kind == NotesBlockKind.Code) BuildCodeInspector(block);
        if (block.Table is not null) BuildTableInspector(block);
        if (block.Media is not null) BuildMediaInspector(block);
        if (block.Kind is not (NotesBlockKind.Paragraph or NotesBlockKind.Heading or NotesBlockKind.Quote or NotesBlockKind.Code)
            && block.Table is null
            && block.Media is null)
        {
            _blockPanel.Children.Add(new TextBlock
            {
                Text = "This block is edited directly in the document surface. Text, code, table and managed-media blocks expose additional tools here.",
                Classes = { "muted" },
                TextWrapping = TextWrapping.Wrap,
                FontSize = 10
            });
        }
    }

    private void BuildDictationInspector(NotesBlock block)
    {
        var status = new TextBlock
        {
            Text = "Uses local Whisper. Raw microphone audio is discarded after transcription.",
            Classes = { "muted" },
            TextWrapping = TextWrapping.Wrap,
            FontSize = 9
        };
        var actions = new WrapPanel();
        actions.Children.Add(ActionButton("Dictate one passage", async () =>
        {
            var controller = App.Services?.GetService<NotesDictationController>();
            if (controller is null)
            {
                status.Text = "Notes dictation is unavailable in this host.";
                return;
            }
            EventHandler<NotesDictationStatus>? handler = null;
            handler = (_, update) => Dispatcher.UIThread.Post(() =>
            {
                if (_disposed) return;
                status.Text = update.Message;
                status.Foreground = update.IsError
                    ? ResourceBrush("HavenDangerBrush", Colors.IndianRed)
                    : ResourceBrush("HavenTextSoftBrush", Colors.LightGray);
                if (update.IsError || update.Message.StartsWith("Dictation inserted", StringComparison.Ordinal))
                    controller.StatusChanged -= handler;
            });
            controller.StatusChanged += handler;
            try
            {
                await controller.StartOneUtteranceAsync(async (text, cancellationToken) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        if (_disposed) return;
                        var liveBlock = _viewModel.Document?.Sections
                            .SelectMany(section => section.Pages)
                            .SelectMany(page => page.Blocks)
                            .FirstOrDefault(value => value.Id == block.Id);
                        if (liveBlock is null) return;
                        BeginEditing(liveBlock);
                        var existing = liveBlock.Runs.Count > 0
                            ? string.Concat(liveBlock.Runs.Select(run => run.Text))
                            : liveBlock.PlainText;
                        var separator = string.IsNullOrWhiteSpace(existing)
                            || char.IsWhiteSpace(existing[^1])
                                ? string.Empty
                                : " ";
                        if (liveBlock.Runs.Count == 0)
                        {
                            liveBlock.Runs.Add(new NotesTextRun
                            {
                                Text = separator + text,
                                FontFamily = "Inter",
                                FontSize = liveBlock.Kind == NotesBlockKind.Heading ? 24 : 14,
                                Bold = liveBlock.Kind == NotesBlockKind.Heading,
                                Italic = liveBlock.Kind == NotesBlockKind.Quote
                            });
                        }
                        else
                        {
                            liveBlock.Runs[^1].Text += separator + text;
                        }
                        liveBlock.PlainText = string.Concat(liveBlock.Runs.Select(run => run.Text));
                        EndEditing(liveBlock, "Inserted local speech transcript");
                        RefreshAll();
                    }, DispatcherPriority.Send);
                }, CancellationToken.None);
            }
            catch (Exception ex)
            {
                controller.StatusChanged -= handler;
                status.Text = "Dictation could not start: " + ex.Message;
                status.Foreground = ResourceBrush("HavenDangerBrush", Colors.IndianRed);
            }
        }, "Capture one passage with local Whisper and append only its final transcript"));
        actions.Children.Add(ActionButton("Stop dictation", async () =>
        {
            var controller = App.Services?.GetService<NotesDictationController>();
            if (controller is null) return;
            await controller.StopAsync(CancellationToken.None);
            status.Text = "Dictation stopped. No raw microphone audio was retained.";
        }, "Stop the active Notes microphone capture"));
        _blockPanel.Children.Add(new TextBlock
        {
            Text = "LOCAL DICTATION",
            Classes = { "eyebrow" },
            Margin = new Thickness(0, 6, 0, 0)
        });
        _blockPanel.Children.Add(actions);
        _blockPanel.Children.Add(status);
    }

    private void BuildCodeInspector(NotesBlock block)
    {
        block.Metadata.TryGetValue(CodeLanguageKey, out var savedLanguage);
        block.Metadata.TryGetValue(CodeWrapKey, out var savedWrap);
        block.Metadata.TryGetValue(CodeLineNumbersKey, out var savedLineNumbers);
        block.Metadata.TryGetValue(CodeTabSizeKey, out var savedTabSize);
        var language = new ComboBox
        {
            ItemsSource = new[]
            {
                "Plain text", "C#", "C", "C++", "CSS", "HTML", "Java", "JavaScript", "JSON",
                "Kotlin", "Markdown", "PowerShell", "Python", "Rust", "SQL", "TypeScript", "XML", "YAML"
            },
            SelectedItem = string.IsNullOrWhiteSpace(savedLanguage) ? "Plain text" : savedLanguage,
            MinWidth = 160
        };
        if (language.SelectedIndex < 0) language.SelectedIndex = 0;
        var wrap = new CheckBox
        {
            Content = "Wrap long lines",
            IsChecked = !bool.TryParse(savedWrap, out var wrapValue) || wrapValue
        };
        var lineNumbers = new CheckBox
        {
            Content = "Show line numbers in preview",
            IsChecked = !bool.TryParse(savedLineNumbers, out var lineNumberValue) || lineNumberValue
        };
        var tabSize = new NumericUpDown
        {
            Minimum = 1,
            Maximum = 16,
            Increment = 1,
            Value = int.TryParse(savedTabSize, out var parsedTabSize) ? Math.Clamp(parsedTabSize, 1, 16) : 4
        };
        var preview = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            MinHeight = 140,
            FontFamily = new FontFamily("Cascadia Mono"),
            TextWrapping = wrap.IsChecked == true ? TextWrapping.Wrap : TextWrapping.NoWrap,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        };
        var statistics = new TextBlock { Classes = { "muted" }, FontSize = 9 };
        void RebuildPreview()
        {
            var source = block.Runs.Count > 0
                ? string.Concat(block.Runs.Select(run => run.Text))
                : block.PlainText;
            var normalized = source.ReplaceLineEndings("\n");
            var lines = normalized.Split('\n');
            preview.Text = lineNumbers.IsChecked == true
                ? string.Join(Environment.NewLine, lines.Select((line, index) => $"{index + 1,4}  {line}"))
                : normalized.Replace("\n", Environment.NewLine, StringComparison.Ordinal);
            preview.TextWrapping = wrap.IsChecked == true ? TextWrapping.Wrap : TextWrapping.NoWrap;
            statistics.Text = $"{lines.Length:N0} line{(lines.Length == 1 ? string.Empty : "s")} · {source.Length:N0} characters · {language.SelectedItem}";
        }
        RebuildPreview();

        var ready = false;
        void SavePreferences()
        {
            if (!ready) return;
            BeginEditing(block);
            block.Metadata[CodeLanguageKey] = language.SelectedItem as string ?? "Plain text";
            block.Metadata[CodeWrapKey] = (wrap.IsChecked == true).ToString(System.Globalization.CultureInfo.InvariantCulture);
            block.Metadata[CodeLineNumbersKey] = (lineNumbers.IsChecked == true).ToString(System.Globalization.CultureInfo.InvariantCulture);
            block.Metadata[CodeTabSizeKey] = Decimal.ToInt32(tabSize.Value ?? 4).ToString(System.Globalization.CultureInfo.InvariantCulture);
            EndEditing(block, "Changed code block settings");
            RebuildPreview();
        }
        language.SelectionChanged += (_, _) => SavePreferences();
        wrap.IsCheckedChanged += (_, _) => SavePreferences();
        lineNumbers.IsCheckedChanged += (_, _) => SavePreferences();
        tabSize.ValueChanged += (_, _) => SavePreferences();
        ready = true;

        var actions = new WrapPanel();
        actions.Children.Add(ActionButton("Normalize indentation", () =>
        {
            var spaces = new string(' ', Decimal.ToInt32(tabSize.Value ?? 4));
            BeginEditing(block);
            if (block.Runs.Count == 0)
            {
                block.PlainText = block.PlainText.Replace("\t", spaces, StringComparison.Ordinal);
            }
            else
            {
                foreach (var run in block.Runs)
                    run.Text = run.Text.Replace("\t", spaces, StringComparison.Ordinal);
                block.PlainText = string.Concat(block.Runs.Select(run => run.Text));
            }
            EndEditing(block, "Normalized code indentation");
            RebuildPreview();
            return Task.CompletedTask;
        }, "Replace tab characters with the selected number of spaces as one undoable edit"));
        actions.Children.Add(ActionButton("Copy code", async () =>
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is null) return;
            var source = block.Runs.Count > 0
                ? string.Concat(block.Runs.Select(run => run.Text))
                : block.PlainText;
            await clipboard.SetTextAsync(source);
            _status.Text = "Code copied to the clipboard.";
        }, "Copy the exact code source without line-number decoration"));

        _blockPanel.Children.Add(new TextBlock
        {
            Text = "CODE TOOLS",
            Classes = { "eyebrow" },
            Margin = new Thickness(0, 6, 0, 0)
        });
        _blockPanel.Children.Add(Labeled("Language", language));
        _blockPanel.Children.Add(Labeled("Tab width", tabSize));
        _blockPanel.Children.Add(new WrapPanel { Children = { wrap, lineNumbers } });
        _blockPanel.Children.Add(actions);
        _blockPanel.Children.Add(statistics);
        _blockPanel.Children.Add(preview);
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
