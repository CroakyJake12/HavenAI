/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/Views/NotesWorkspaceView.Inspectors.cs, in the Desktop view layer, where Avalonia controls connect XAML interaction to view models.
 * What: This file owns NotesWorkspaceView. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The file keeps one cohesive responsibility in a predictable location so callers can find and replace it without unrelated changes.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Views;

/// <summary>
/// Represents notes workspace view and keeps its related state and behavior together.
/// </summary>
public sealed partial class NotesWorkspaceView
{
    /// <summary>
    /// Performs the refresh inspector step owned by this component.
    /// </summary>
    private void RefreshInspector()
    {
        BuildAiInspector();
        BuildReviewInspector();
        BuildVersionsInspector();
        BuildInformationInspector();
    }

    /// <summary>
    /// Builds ai inspector from the currently available inputs.
    /// </summary>
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
        var model = new HavenComboBox
        {
            ItemsSource = _page.Models,
            SelectedItem = _page.SelectedModelName
        };
        model.SelectionChanged += (_, _) => _page.SelectedModelName = model.SelectedItem as string ?? string.Empty;
        _aiPanel.Children.Add(model);
        var instruction = new HavenTextInput
        {
            Text = _page.AiInstruction,
            PlaceholderText = "Explain, rewrite, plan, check consistency, create revision cards…",
            AcceptsReturn = true,
            MinHeight = 90,
            TextWrapping = TextWrapping.Wrap
        };
        instruction.TextChanged += (_, _) => _page.AiInstruction = instruction.Text ?? string.Empty;
        _aiPanel.Children.Add(instruction);
        var context = new HavenCheckBox
        {
            Content = "Allow the model to receive this document's text context",
            IsChecked = _page.AllowDocumentContext
        };
        context.IsCheckedChanged += (_, _) => _page.AllowDocumentContext = context.IsChecked == true;
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
            await _page.ProposeAiCommand.ExecuteAsync();
            RefreshInspector();
        }, "Generate a review-only proposal"));
        buttons.Children.Add(ActionButton("Cancel", () =>
        {
            _page.CancelAiCommand.Execute(null);
            return Task.CompletedTask;
        }, "Cancel the active AI request"));
        _aiPanel.Children.Add(buttons);

        if (_page.PendingAiChange is { } change)
            _aiPanel.Children.Add(BuildStandardAiProposal(change));

        BuildMediaAiInspector();

        _aiPanel.Children.Add(new TextBlock
        {
            Text = "PROVENANCE HISTORY",
            Classes = { "eyebrow" },
            Margin = new Thickness(0, 8, 0, 0)
        });
        foreach (var history in _page.AiHistory.OrderByDescending(item => item.CreatedAt).Take(20))
        {
            var target = NotesMediaAiReview.TryGetTarget(history, out var mediaTarget)
                ? " · media " + NotesMediaAiReview.DisplayName(mediaTarget).ToLowerInvariant()
                : string.Empty;
            _aiPanel.Children.Add(new TextBlock
            {
                Text = $"{history.CreatedAt.LocalDateTime:g} · {history.Status} · {history.ModelName}{target}\n{history.Instruction}",
                TextWrapping = TextWrapping.Wrap,
                Classes = { "muted" },
                FontSize = 9
            });
        }
    }

    /// <summary>
    /// Builds standard ai proposal from the currently available inputs.
    /// </summary>
    private Control BuildStandardAiProposal(NotesAiChange change) => Card(new StackPanel
    {
        Spacing = 7,
        Children =
        {
            new TextBlock { Text = "PROPOSAL", Classes = { "eyebrow" } },
            new TextBlock { Text = change.Explanation, TextWrapping = TextWrapping.Wrap, Classes = { "muted" } },
            new TextBlock { Text = "Original", FontWeight = FontWeight.SemiBold },
            new HavenTextInput { Text = change.OriginalContent, IsReadOnly = true, AcceptsReturn = true, MaxHeight = 130, TextWrapping = TextWrapping.Wrap },
            new TextBlock { Text = "Proposed", FontWeight = FontWeight.SemiBold },
            new HavenTextInput { Text = change.ProposedContent, IsReadOnly = true, AcceptsReturn = true, MaxHeight = 180, TextWrapping = TextWrapping.Wrap },
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
                        await _page.ApproveAiCommand.ExecuteAsync();
                        RefreshAll();
                    }, "Apply this exact proposal and create a version"),
                    ActionButton("Reject", () =>
                    {
                        _page.RejectAiCommand.Execute(null);
                        RefreshAll();
                        return Task.CompletedTask;
                    }, "Reject without changing document content", danger: true)
                }
            }
        }
    });

    /// <summary>
    /// Builds media ai inspector from the currently available inputs.
    /// </summary>
    private void BuildMediaAiInspector()
    {
        if (_page.Document is not { } document || _page.SelectedBlock is not { Media: not null } block) return;
        _aiPanel.Children.Add(new TextBlock
        {
            Text = "MEDIA ACCESSIBILITY AI",
            Classes = { "eyebrow" },
            Margin = new Thickness(0, 10, 0, 0)
        });
        _aiPanel.Children.Add(new TextBlock
        {
            Text = "The model receives verified media metadata, existing accessibility text, nearby note text, and only the document context you explicitly allow. It is told not to claim it saw or heard anything absent from that evidence.",
            Classes = { "muted" },
            TextWrapping = TextWrapping.Wrap,
            FontSize = 9
        });
        var target = new HavenComboBox
        {
            ItemsSource = Enum.GetValues<NotesMediaAiTarget>(),
            SelectedItem = block.Kind is NotesBlockKind.Audio or NotesBlockKind.Video
                ? NotesMediaAiTarget.Transcript
                : NotesMediaAiTarget.AltText
        };
        var mediaInstruction = new HavenTextInput
        {
            PlaceholderText = "Optional extra instruction for this media field",
            AcceptsReturn = true,
            MinHeight = 65,
            TextWrapping = TextWrapping.Wrap
        };
        var mediaStatus = new TextBlock { Classes = { "muted" }, TextWrapping = TextWrapping.Wrap, FontSize = 9 };
        _aiPanel.Children.Add(Labeled("Target field", target));
        _aiPanel.Children.Add(mediaInstruction);
        _aiPanel.Children.Add(ActionButton("Create media proposal", async () =>
        {
            if (target.SelectedItem is not NotesMediaAiTarget selectedTarget) return;
            var service = App.Services?.GetService<INotesAiService>();
            if (service is null)
            {
                mediaStatus.Text = "Notes AI is unavailable in this host.";
                return;
            }
            try
            {
                mediaStatus.Text = "Creating a review-only media proposal…";
                await NotesMediaAiReview.ProposeAsync(
                    service,
                    _page,
                    block,
                    selectedTarget,
                    mediaInstruction.Text ?? string.Empty,
                    CancellationToken.None);
                mediaStatus.Text = "Proposal ready. The media field is unchanged until approval.";
                RefreshInspector();
            }
            catch (Exception ex)
            {
                mediaStatus.Text = "Media proposal failed: " + ex.Message;
            }
        }, "Create an evidence-bound proposal for the selected media field"));
        _aiPanel.Children.Add(mediaStatus);

        foreach (var selectedTarget in Enum.GetValues<NotesMediaAiTarget>())
        {
            var pending = NotesMediaAiReview.FindPending(document, block.Id, selectedTarget);
            if (pending is null) continue;
            _aiPanel.Children.Add(BuildMediaAiProposal(block, selectedTarget, pending));
        }
    }

    /// <summary>
    /// Builds media ai proposal from the currently available inputs.
    /// </summary>
    private Control BuildMediaAiProposal(
        NotesBlock block,
        NotesMediaAiTarget target,
        NotesAiChange change) => Card(new StackPanel
    {
        Spacing = 7,
        Children =
        {
            new TextBlock { Text = "PROPOSED " + NotesMediaAiReview.DisplayName(target).ToUpperInvariant(), Classes = { "eyebrow" } },
            new TextBlock { Text = change.Explanation, TextWrapping = TextWrapping.Wrap, Classes = { "muted" } },
            new TextBlock { Text = "Current value", FontWeight = FontWeight.SemiBold },
            new HavenTextInput { Text = change.OriginalContent, IsReadOnly = true, AcceptsReturn = true, MaxHeight = 110, TextWrapping = TextWrapping.Wrap },
            new TextBlock { Text = "Proposed value", FontWeight = FontWeight.SemiBold },
            new HavenTextInput { Text = change.ProposedContent, IsReadOnly = true, AcceptsReturn = true, MaxHeight = 160, TextWrapping = TextWrapping.Wrap },
            new TextBlock
            {
                Text = $"{change.ProviderId} · {change.ModelName} · {change.CitationIds.Count} cited source{(change.CitationIds.Count == 1 ? string.Empty : "s")} · full document context {(change.SentDocumentContext ? "allowed" : "not sent")}",
                Classes = { "muted2" },
                FontSize = 9,
                TextWrapping = TextWrapping.Wrap
            },
            new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 7,
                Children =
                {
                    ActionButton("Approve media proposal", async () =>
                    {
                        await NotesMediaAiReview.ApplyAsync(_page, block, change, CancellationToken.None);
                        RefreshAll();
                    }, "Apply exactly this proposed media accessibility value and save a version"),
                    ActionButton("Reject media proposal", async () =>
                    {
                        await NotesMediaAiReview.RejectAsync(_page, block, change);
                        RefreshAll();
                    }, "Reject without changing media accessibility content", danger: true)
                }
            }
        }
    });

    /// <summary>
    /// Builds review inspector from the currently available inputs.
    /// </summary>
    private void BuildReviewInspector()
    {
        _reviewPanel.Children.Clear();
        _reviewPanel.Children.Add(new TextBlock { Text = "COMMENTS", Classes = { "eyebrow" } });
        var commentBox = new HavenTextInput
        {
            PlaceholderText = "Comment on the selected block",
            AcceptsReturn = true,
            MinHeight = 60,
            TextWrapping = TextWrapping.Wrap
        };
        _reviewPanel.Children.Add(commentBox);
        _reviewPanel.Children.Add(ActionButton("Add comment", () =>
        {
            _page.AddCommentCommand.Execute(commentBox.Text);
            commentBox.Text = string.Empty;
            RefreshInspector();
            return Task.CompletedTask;
        }, "Add a review comment to the selected block"));
        foreach (var comment in _page.Comments.OrderByDescending(item => item.CreatedAt))
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
                    _page.ResolveCommentCommand.Execute(comment);
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
            _page.AddCitationCommand.Execute(null);
            RefreshInspector();
            return Task.CompletedTask;
        }, "Add a bibliography source"));
        foreach (var citation in _page.Citations)
            _reviewPanel.Children.Add(BuildCitationEditor(citation));

        if (_page.SelectedBlock?.Flashcard is { } card)
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
            ratings.Children.Add(CommandButton("Again", _page.ReviewAgainCommand));
            ratings.Children.Add(CommandButton("Hard", _page.ReviewHardCommand));
            ratings.Children.Add(CommandButton("Good", _page.ReviewGoodCommand));
            ratings.Children.Add(CommandButton("Easy", _page.ReviewEasyCommand));
            _reviewPanel.Children.Add(ratings);
        }
    }

    /// <summary>
    /// Builds citation editor from the currently available inputs.
    /// </summary>
    private Control BuildCitationEditor(NotesCitation citation)
    {
        var title = new HavenTextInput { Text = citation.Title, PlaceholderText = "Source title" };
        var authors = new HavenTextInput { Text = citation.Authors, PlaceholderText = "Authors" };
        var url = new HavenTextInput { Text = citation.Url, PlaceholderText = "https://…" };
        var evidence = new HavenTextInput
        {
            Text = citation.EvidenceExcerpt,
            PlaceholderText = "Evidence excerpt",
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

    /// <summary>
    /// Builds versions inspector from the currently available inputs.
    /// </summary>
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
        foreach (var version in _page.Versions)
        {
            var button = new HavenButton
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
            button.Classes.Add(ReferenceEquals(version, _page.SelectedVersion) ? "accent" : "sidebar");
            button.Click += (_, _) =>
            {
                _page.SelectedVersion = version;
                BuildVersionsInspector();
            };
            _versionsPanel.Children.Add(button);
        }
        _versionsPanel.Children.Add(ActionButton("Restore selected version", async () =>
        {
            await _page.RestoreVersionCommand.ExecuteAsync();
            RefreshAll();
        }, "Restore as a new current version"));
    }

    /// <summary>
    /// Builds information inspector from the currently available inputs.
    /// </summary>
    private void BuildInformationInspector()
    {
        // Productivity sections are rebuilt when the document changes. Detach
        // their reusable result panels before replacing the surrounding cards;
        // Avalonia correctly rejects giving one visual two parents.
        foreach (var reusable in new Panel[]
                 {
                     _localFindResultsPanel, _languageIssuesPanel, _bookmarksPanel,
                     _fieldsPanel, _stylesPanel, _versionComparisonPanel,
                     _equationLibraryPanel, _canvasBookmarksPanel, _studyAttemptsPanel,
                     _crossReferencesPanel, _conflictsPanel
                 })
            if (reusable.Parent is Panel parent) parent.Children.Remove(reusable);
        _informationPanel.Children.Clear();
        _informationPanel.Children.Add(new TextBlock { Text = "DOCUMENT INFORMATION", Classes = { "eyebrow" } });
        _informationPanel.Children.Add(new TextBlock { Text = _page.StatisticsLabel, FontWeight = FontWeight.SemiBold });
        if (_page.Document is not { } document) return;
        _informationPanel.Children.Add(new TextBlock
        {
            Text = $"Created {document.CreatedAt.LocalDateTime:g}\nUpdated {document.UpdatedAt.LocalDateTime:g}\nNative schema {document.SchemaVersion}\nDocument version {document.Version}",
            Classes = { "muted" },
            FontSize = 10
        });
        var language = new HavenTextInput { Text = document.Language, PlaceholderText = "Language, e.g. en-GB" };
        language.GotFocus += (_, _) => BeginDocumentMetadataEdit();
        language.LostFocus += (_, _) =>
        {
            document.Language = language.Text?.Trim() ?? "en-GB";
            CommitMetadataEdit("Changed document language");
        };
        _informationPanel.Children.Add(Labeled("Language", language));
        var layout = new HavenComboBox { ItemsSource = Enum.GetValues<NotesLayoutMode>(), SelectedItem = document.LayoutMode };
        layout.SelectionChanged += (_, _) =>
        {
            if (layout.SelectedItem is not NotesLayoutMode mode || mode == document.LayoutMode) return;
            BeginDocumentMetadataEdit();
            document.LayoutMode = mode;
            CommitMetadataEdit("Changed document layout to " + mode);
        };
        _informationPanel.Children.Add(Labeled("Layout", layout));
        _informationPanel.Children.Add(BuildPageSetup(document));
        BuildProductivityInspector();
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

    /// <summary>
    /// Builds page setup from the currently available inputs.
    /// </summary>
    private Control BuildPageSetup(NotesDocument document)
    {
        var width = new HavenNumericInput { Minimum = 72, Maximum = 5000, Value = (decimal)document.PageSetup.WidthPoints };
        var height = new HavenNumericInput { Minimum = 72, Maximum = 5000, Value = (decimal)document.PageSetup.HeightPoints };
        var margins = new HavenNumericInput { Minimum = 0, Maximum = 1000, Value = (decimal)document.PageSetup.MarginTopPoints };
        var orientation = new HavenComboBox { ItemsSource = new[] { "Portrait", "Landscape" }, SelectedItem = document.PageSetup.Orientation };
        var pageNumbers = new HavenCheckBox { Content = "Show page numbers", IsChecked = document.PageSetup.ShowPageNumbers };
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
