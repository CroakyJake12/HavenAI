using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Haven.Core;

namespace Haven.Desktop.Views;

public sealed partial class NotesWorkspaceView
{
    private void RefreshInspector()
    {
        BuildAiInspector();
        BuildReviewInspector();
        BuildVersionsInspector();
        BuildInformationInspector();
    }

    private void BuildAiInspector()
    {
        _aiPanel.Children.Clear();
        _aiPanel.Children.Add(new TextBlock { Text = "REVIEWED AI", Classes = { "eyebrow" } });
        _aiPanel.Children.Add(new TextBlock
        {
            Text = "AI can propose changes, but cannot alter the document until you approve the exact result.",
            Classes = { "muted" },
            TextWrapping = TextWrapping.Wrap,
            FontSize = 10
        });
        var model = new ComboBox
        {
            ItemsSource = _viewModel.Models,
            SelectedItem = _viewModel.SelectedModelName,
            PlaceholderText = "Choose model"
        };
        model.SelectionChanged += (_, _) => _viewModel.SelectedModelName = model.SelectedItem as string ?? string.Empty;
        _aiPanel.Children.Add(model);
        var instruction = new TextBox
        {
            Text = _viewModel.AiInstruction,
            Watermark = "Explain, rewrite, plan, check consistency, create revision cards…",
            AcceptsReturn = true,
            MinHeight = 90,
            TextWrapping = TextWrapping.Wrap
        };
        instruction.TextChanged += (_, _) => _viewModel.AiInstruction = instruction.Text ?? string.Empty;
        _aiPanel.Children.Add(instruction);
        var context = new CheckBox
        {
            Content = "Allow the model to receive this document's text context",
            IsChecked = _viewModel.AllowDocumentContext
        };
        context.IsCheckedChanged += (_, _) => _viewModel.AllowDocumentContext = context.IsChecked == true;
        _aiPanel.Children.Add(context);
        _aiPanel.Children.Add(new TextBlock
        {
            Text = "Without this permission, AI receives only the selected block. Citations are restricted to sources already stored in this note.",
            Classes = { "muted2" },
            TextWrapping = TextWrapping.Wrap,
            FontSize = 9
        });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
        buttons.Children.Add(ActionButton("Create proposal", async () =>
        {
            await _viewModel.ProposeAiCommand.ExecuteAsync();
            RefreshInspector();
        }, "Generate a review-only proposal"));
        buttons.Children.Add(ActionButton("Cancel", () =>
        {
            _viewModel.CancelAiCommand.Execute(null);
            return Task.CompletedTask;
        }, "Cancel the active AI request"));
        _aiPanel.Children.Add(buttons);

        if (_viewModel.PendingAiChange is { } change)
        {
            _aiPanel.Children.Add(Card(new StackPanel
            {
                Spacing = 7,
                Children =
                {
                    new TextBlock { Text = "PROPOSAL", Classes = { "eyebrow" } },
                    new TextBlock { Text = change.Explanation, TextWrapping = TextWrapping.Wrap, Classes = { "muted" } },
                    new TextBlock { Text = "Original", FontWeight = FontWeight.SemiBold },
                    new TextBox { Text = change.OriginalContent, IsReadOnly = true, AcceptsReturn = true, MaxHeight = 130, TextWrapping = TextWrapping.Wrap },
                    new TextBlock { Text = "Proposed", FontWeight = FontWeight.SemiBold },
                    new TextBox { Text = change.ProposedContent, IsReadOnly = true, AcceptsReturn = true, MaxHeight = 180, TextWrapping = TextWrapping.Wrap },
                    new TextBlock
                    {
                        Text = $"{change.ProviderId} · {change.ModelName} · {change.CitationIds.Count} cited source{(change.CitationIds.Count == 1 ? string.Empty : "s")}",
                        Classes = { "muted2" },
                        FontSize = 9
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 7,
                        Children =
                        {
                            ActionButton("Approve and apply", async () =>
                            {
                                await _viewModel.ApproveAiCommand.ExecuteAsync();
                                RefreshAll();
                            }, "Apply this exact proposal and create a version"),
                            ActionButton("Reject", () =>
                            {
                                _viewModel.RejectAiCommand.Execute(null);
                                RefreshAll();
                                return Task.CompletedTask;
                            }, "Reject without changing document content", danger: true)
                        }
                    }
                }
            }));
        }

        _aiPanel.Children.Add(new TextBlock
        {
            Text = "PROVENANCE HISTORY",
            Classes = { "eyebrow" },
            Margin = new Thickness(0, 8, 0, 0)
        });
        foreach (var history in _viewModel.AiHistory.OrderByDescending(item => item.CreatedAt).Take(20))
        {
            _aiPanel.Children.Add(new TextBlock
            {
                Text = $"{history.CreatedAt.LocalDateTime:g} · {history.Status} · {history.ModelName}\n{history.Instruction}",
                TextWrapping = TextWrapping.Wrap,
                Classes = { "muted" },
                FontSize = 9
            });
        }
    }

    private void BuildReviewInspector()
    {
        _reviewPanel.Children.Clear();
        _reviewPanel.Children.Add(new TextBlock { Text = "COMMENTS", Classes = { "eyebrow" } });
        var commentBox = new TextBox
        {
            Watermark = "Comment on the selected block",
            AcceptsReturn = true,
            MinHeight = 60,
            TextWrapping = TextWrapping.Wrap
        };
        _reviewPanel.Children.Add(commentBox);
        _reviewPanel.Children.Add(ActionButton("Add comment", () =>
        {
            _viewModel.AddCommentCommand.Execute(commentBox.Text);
            commentBox.Text = string.Empty;
            RefreshInspector();
            return Task.CompletedTask;
        }, "Add a review comment to the selected block"));
        foreach (var comment in _viewModel.Comments.OrderByDescending(item => item.CreatedAt))
        {
            var row = new StackPanel { Spacing = 3 };
            row.Children.Add(new TextBlock
            {
                Text = $"{comment.Author} · {comment.CreatedAt.LocalDateTime:g} · {comment.State}",
                Classes = { "muted2" },
                FontSize = 9
            });
            row.Children.Add(new TextBlock { Text = comment.Text, TextWrapping = TextWrapping.Wrap });
            if (comment.State != NotesCommentState.Resolved)
            {
                row.Children.Add(ActionButton("Resolve", () =>
                {
                    _viewModel.ResolveCommentCommand.Execute(comment);
                    RefreshInspector();
                    return Task.CompletedTask;
                }, "Resolve comment"));
            }
            _reviewPanel.Children.Add(Card(row));
        }

        _reviewPanel.Children.Add(new TextBlock
        {
            Text = "SOURCES AND CITATIONS",
            Classes = { "eyebrow" },
            Margin = new Thickness(0, 8, 0, 0)
        });
        _reviewPanel.Children.Add(ActionButton("Add source", () =>
        {
            _viewModel.AddCitationCommand.Execute(null);
            RefreshInspector();
            return Task.CompletedTask;
        }, "Add a bibliography source"));
        foreach (var citation in _viewModel.Citations)
            _reviewPanel.Children.Add(BuildCitationEditor(citation));

        if (_viewModel.SelectedBlock?.Flashcard is { } card)
        {
            _reviewPanel.Children.Add(new TextBlock
            {
                Text = "STUDY REVIEW",
                Classes = { "eyebrow" },
                Margin = new Thickness(0, 8, 0, 0)
            });
            _reviewPanel.Children.Add(new TextBlock { Text = card.Front, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
            _reviewPanel.Children.Add(new TextBlock { Text = card.Back, TextWrapping = TextWrapping.Wrap, Classes = { "muted" } });
            _reviewPanel.Children.Add(new TextBlock
            {
                Text = $"Due {card.Schedule.DueAt.LocalDateTime:g} · interval {card.Schedule.IntervalDays} days · ease {card.Schedule.EaseFactor:0.00}",
                Classes = { "muted2" },
                FontSize = 9
            });
            var ratings = new WrapPanel();
            ratings.Children.Add(CommandButton("Again", _viewModel.ReviewAgainCommand));
            ratings.Children.Add(CommandButton("Hard", _viewModel.ReviewHardCommand));
            ratings.Children.Add(CommandButton("Good", _viewModel.ReviewGoodCommand));
            ratings.Children.Add(CommandButton("Easy", _viewModel.ReviewEasyCommand));
            _reviewPanel.Children.Add(ratings);
        }
    }

    private Control BuildCitationEditor(NotesCitation citation)
    {
        var title = new TextBox { Text = citation.Title, Watermark = "Source title" };
        var authors = new TextBox { Text = citation.Authors, Watermark = "Authors" };
        var url = new TextBox { Text = citation.Url, Watermark = "https://…" };
        var evidence = new TextBox
        {
            Text = citation.EvidenceExcerpt,
            Watermark = "Evidence excerpt",
            AcceptsReturn = true,
            MinHeight = 55,
            TextWrapping = TextWrapping.Wrap
        };
        void Begin() => BeginDocumentMetadataEdit();
        void Commit()
        {
            citation.Title = title.Text ?? string.Empty;
            citation.Authors = authors.Text ?? string.Empty;
            citation.Url = url.Text ?? string.Empty;
            citation.EvidenceExcerpt = evidence.Text ?? string.Empty;
            CommitMetadataEdit("Edited citation " + citation.Key);
        }
        foreach (var box in new[] { title, authors, url, evidence })
        {
            box.GotFocus += (_, _) => Begin();
            box.LostFocus += (_, _) => Commit();
        }
        return Card(new StackPanel
        {
            Spacing = 5,
            Children =
            {
                new TextBlock { Text = "[" + citation.Key + "]", FontWeight = FontWeight.SemiBold },
                title,
                authors,
                url,
                evidence
            }
        });
    }

    private void BuildVersionsInspector()
    {
        _versionsPanel.Children.Clear();
        _versionsPanel.Children.Add(new TextBlock { Text = "VERSION HISTORY", Classes = { "eyebrow" } });
        _versionsPanel.Children.Add(new TextBlock
        {
            Text = "Every completed atomic save is retained. Restoring creates a new version rather than destroying later history.",
            Classes = { "muted" },
            TextWrapping = TextWrapping.Wrap,
            FontSize = 10
        });
        foreach (var version in _viewModel.Versions)
        {
            var button = new Button
            {
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Content = new StackPanel
                {
                    Spacing = 2,
                    Children =
                    {
                        new TextBlock { Text = $"Version {version.Version} · {version.CreatedAt.LocalDateTime:g}", FontWeight = FontWeight.SemiBold },
                        new TextBlock { Text = version.Reason + $" · {version.SizeBytes / 1024d:0.0} KB", Classes = { "muted2" }, FontSize = 9 }
                    }
                }
            };
            button.Classes.Add(ReferenceEquals(version, _viewModel.SelectedVersion) ? "accent" : "sidebar");
            button.Click += (_, _) =>
            {
                _viewModel.SelectedVersion = version;
                BuildVersionsInspector();
            };
            _versionsPanel.Children.Add(button);
        }
        _versionsPanel.Children.Add(ActionButton("Restore selected version", async () =>
        {
            await _viewModel.RestoreVersionCommand.ExecuteAsync();
            RefreshAll();
        }, "Restore as a new current version"));
    }

    private void BuildInformationInspector()
    {
        _informationPanel.Children.Clear();
        _informationPanel.Children.Add(new TextBlock { Text = "DOCUMENT INFORMATION", Classes = { "eyebrow" } });
        _informationPanel.Children.Add(new TextBlock { Text = _viewModel.StatisticsLabel, FontWeight = FontWeight.SemiBold });
        if (_viewModel.Document is not { } document) return;
        _informationPanel.Children.Add(new TextBlock
        {
            Text = $"Created {document.CreatedAt.LocalDateTime:g}\nUpdated {document.UpdatedAt.LocalDateTime:g}\nNative schema {document.SchemaVersion}\nDocument version {document.Version}",
            Classes = { "muted" },
            FontSize = 10
        });
        var language = new TextBox { Text = document.Language, Watermark = "Language, e.g. en-GB" };
        language.GotFocus += (_, _) => BeginDocumentMetadataEdit();
        language.LostFocus += (_, _) =>
        {
            document.Language = language.Text?.Trim() ?? "en-GB";
            CommitMetadataEdit("Changed document language");
        };
        _informationPanel.Children.Add(Labeled("Language", language));
        var layout = new ComboBox { ItemsSource = Enum.GetValues<NotesLayoutMode>(), SelectedItem = document.LayoutMode };
        layout.SelectionChanged += (_, _) =>
        {
            if (layout.SelectedItem is not NotesLayoutMode mode || mode == document.LayoutMode) return;
            BeginDocumentMetadataEdit();
            document.LayoutMode = mode;
            CommitMetadataEdit("Changed document layout to " + mode);
        };
        _informationPanel.Children.Add(Labeled("Layout", layout));
        _informationPanel.Children.Add(BuildPageSetup(document));
        _informationPanel.Children.Add(new TextBlock
        {
            Text = "RECOVERY",
            Classes = { "eyebrow" },
            Margin = new Thickness(0, 8, 0, 0)
        });
        _informationPanel.Children.Add(new TextBlock
        {
            Text = document.Recovery.HasUnsavedRecovery
                ? "Recovery review required: " + document.Recovery.RecoveryReason
                : $"Last autosave {document.Recovery.LastAutosaveAt?.LocalDateTime:g}\nLast validated SHA-256 {ShortHash(document.Recovery.LastValidSha256)}",
            TextWrapping = TextWrapping.Wrap,
            Classes = { "muted" },
            FontSize = 9
        });
        _informationPanel.Children.Add(new TextBlock
        {
            Text = "COLLABORATION METADATA",
            Classes = { "eyebrow" },
            Margin = new Thickness(0, 8, 0, 0)
        });
        _informationPanel.Children.Add(new TextBlock
        {
            Text = $"Owner: {document.Collaboration.OwnerId}\nConflict state: {document.Collaboration.ConflictState}\nCollaborators: {document.Collaboration.Collaborators.Count}\nOpen conflicts: {document.Collaboration.Conflicts.Count(conflict => conflict.ResolvedAt is null)}",
            Classes = { "muted" },
            FontSize = 9
        });
    }

    private Control BuildPageSetup(NotesDocument document)
    {
        var width = new NumericUpDown { Minimum = 72, Maximum = 5000, Value = (decimal)document.PageSetup.WidthPoints };
        var height = new NumericUpDown { Minimum = 72, Maximum = 5000, Value = (decimal)document.PageSetup.HeightPoints };
        var margins = new NumericUpDown { Minimum = 0, Maximum = 1000, Value = (decimal)document.PageSetup.MarginTopPoints };
        var orientation = new ComboBox { ItemsSource = new[] { "Portrait", "Landscape" }, SelectedItem = document.PageSetup.Orientation };
        var pageNumbers = new CheckBox { Content = "Show page numbers", IsChecked = document.PageSetup.ShowPageNumbers };
        var ready = false;
        void Commit()
        {
            if (!ready) return;
            BeginDocumentMetadataEdit();
            document.PageSetup.WidthPoints = (double)(width.Value ?? 595m);
            document.PageSetup.HeightPoints = (double)(height.Value ?? 842m);
            var margin = (double)(margins.Value ?? 72m);
            document.PageSetup.MarginTopPoints = margin;
            document.PageSetup.MarginRightPoints = margin;
            document.PageSetup.MarginBottomPoints = margin;
            document.PageSetup.MarginLeftPoints = margin;
            document.PageSetup.Orientation = orientation.SelectedItem as string ?? "Portrait";
            document.PageSetup.ShowPageNumbers = pageNumbers.IsChecked == true;
            CommitMetadataEdit("Changed page setup");
        }
        width.ValueChanged += (_, _) => Commit();
        height.ValueChanged += (_, _) => Commit();
        margins.ValueChanged += (_, _) => Commit();
        orientation.SelectionChanged += (_, _) => Commit();
        pageNumbers.IsCheckedChanged += (_, _) => Commit();
        ready = true;
        return Card(new StackPanel
        {
            Spacing = 5,
            Children =
            {
                new TextBlock { Text = "PAGE SETUP", Classes = { "eyebrow" } },
                Labeled("Width (pt)", width),
                Labeled("Height (pt)", height),
                Labeled("Margins (pt)", margins),
                Labeled("Orientation", orientation),
                pageNumbers
            }
        });
    }
}
