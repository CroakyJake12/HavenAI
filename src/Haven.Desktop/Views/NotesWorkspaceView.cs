using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Haven.Core;
using Haven.Desktop.Controls;
using Haven.Desktop.ViewModels;

namespace Haven.Desktop.Views;

public sealed class NotesWorkspaceView : UserControl, IDisposable
{
    private readonly NotesWorkspaceViewModel _viewModel;
    private readonly StackPanel _documentsPanel = new() { Spacing = 5 };
    private readonly StackPanel _sectionsPanel = new() { Spacing = 4 };
    private readonly StackPanel _pagesPanel = new() { Spacing = 4 };
    private readonly StackPanel _searchPanel = new() { Spacing = 5 };
    private readonly StackPanel _blocksPanel = new() { Spacing = 12 };
    private readonly StackPanel _aiPanel = new() { Spacing = 10 };
    private readonly StackPanel _reviewPanel = new() { Spacing = 10 };
    private readonly StackPanel _versionsPanel = new() { Spacing = 7 };
    private readonly StackPanel _informationPanel = new() { Spacing = 9 };
    private readonly TextBox _documentTitle = new() { FontSize = 24, FontWeight = FontWeight.SemiBold, MinWidth = 280 };
    private readonly TextBlock _status = new() { Classes = { "muted" }, TextWrapping = TextWrapping.Wrap, FontSize = 10 };
    private readonly TextBlock _saveState = new() { Classes = { "muted2" }, FontSize = 10 };
    private readonly TextBlock _statistics = new() { Classes = { "muted" }, FontSize = 10 };
    private readonly TextBox _searchBox = new() { PlaceholderText = "Search every Notes document" };
    private readonly Grid _deleteConfirmation = new() { IsVisible = false, ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 7 };
    private bool _initialized;
    private bool _refreshQueued;
    private bool _disposed;
    private Guid? _activeEditBlockId;

    public NotesWorkspaceView(NotesWorkspaceViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = viewModel;
        AutomationProperties.SetName(this, "Haven Notes document workspace");
        Content = BuildLayout();
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        _viewModel.DocumentChanged += OnDocumentChanged;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.SearchNavigationRequested += OnSearchNavigationRequested;
    }

    private Control BuildLayout()
    {
        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            Background = ResourceBrush("HavenBackgroundBrush", Color.FromRgb(18, 18, 20))
        };
        root.Children.Add(BuildTopBar());
        var body = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("270,6,*,6,350"),
            Margin = new Thickness(12, 8, 12, 12)
        };
        Grid.SetRow(body, 1);
        body.Children.Add(BuildLibraryPane());
        body.Children.Add(WithColumn(Splitter(), 1));
        body.Children.Add(WithColumn(BuildEditorPane(), 2));
        body.Children.Add(WithColumn(Splitter(), 3));
        body.Children.Add(WithColumn(BuildInspectorPane(), 4));
        root.Children.Add(body);
        return root;
    }

    private Control BuildTopBar()
    {
        var panel = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,Auto,Auto,Auto,*,Auto,Auto,Auto"),
            ColumnSpacing = 7,
            Margin = new Thickness(14, 10, 14, 0)
        };
        panel.Children.Add(ActionButton("New", async () => await _viewModel.NewDocumentCommand.ExecuteAsync(), "Create a new local Notes document"));
        panel.Children.Add(WithColumn(ActionButton("Save", async () => await _viewModel.SaveCommand.ExecuteAsync(), "Save now; autosave also runs automatically"), 1));
        panel.Children.Add(WithColumn(ActionButton("Undo", () => { _viewModel.UndoCommand.Execute(null); return Task.CompletedTask; }, "Undo the last document edit"), 2));
        panel.Children.Add(WithColumn(ActionButton("Redo", () => { _viewModel.RedoCommand.Execute(null); return Task.CompletedTask; }, "Redo the last undone edit"), 3));
        panel.Children.Add(WithColumn(ActionButton("Import", ImportDocumentAsync, "Import a supported document into a new native Notes file"), 4));
        panel.Children.Add(WithColumn(ActionButton("Export", ExportDocumentAsync, "Export the current document truthfully"), 5));
        var identity = new StackPanel
        {
            Spacing = 1,
            HorizontalAlignment = HorizontalAlignment.Center,
            Children =
            {
                new TextBlock { Text = "HAVEN NOTES", Classes = { "eyebrow" }, HorizontalAlignment = HorizontalAlignment.Center },
                _saveState
            }
        };
        Grid.SetColumn(identity, 6);
        panel.Children.Add(identity);
        panel.Children.Add(WithColumn(ActionButton("Print", async () => await _viewModel.PrintAsync(CancellationToken.None), "Create a print-ready PDF and open the Windows print handler"), 7));
        panel.Children.Add(WithColumn(ActionButton("Delete", () => { _viewModel.RequestDeleteDocumentCommand.Execute(null); RefreshDeleteConfirmation(); return Task.CompletedTask; }, "Move this document to recoverable Notes trash", danger: true), 8));
        panel.Children.Add(WithColumn(new TextBlock { Text = "Autosave · versions · local recovery", Classes = { "muted2" }, VerticalAlignment = VerticalAlignment.Center, FontSize = 9 }, 9));
        return panel;
    }

    private Control BuildLibraryPane()
    {
        var searchButton = ActionButton("Search", async () =>
        {
            _viewModel.SearchQuery = _searchBox.Text ?? string.Empty;
            await _viewModel.SearchCommand.ExecuteAsync();
            RefreshSearchResults();
        }, "Search all local Notes documents");
        var clearSearch = ActionButton("Clear", () => { _viewModel.ClearSearchCommand.Execute(null); _searchBox.Text = string.Empty; RefreshSearchResults(); return Task.CompletedTask; }, "Clear Notes search");
        var searchGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 5 };
        searchGrid.Children.Add(_searchBox);
        searchGrid.Children.Add(WithColumn(searchButton, 1));
        searchGrid.Children.Add(WithColumn(clearSearch, 2));
        _searchBox.KeyDown += async (_, args) =>
        {
            if (args.Key != Key.Enter) return;
            args.Handled = true;
            _viewModel.SearchQuery = _searchBox.Text ?? string.Empty;
            await _viewModel.SearchCommand.ExecuteAsync();
            RefreshSearchResults();
        };

        return Card(new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,180,Auto,*,Auto,150"),
            RowSpacing = 8,
            Children =
            {
                new TextBlock { Text = "LIBRARY", Classes = { "eyebrow" } },
                WithRow(searchGrid, 1),
                WithRow(new ScrollViewer { VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, Content = _searchPanel }, 2),
                WithRow(new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                    Children =
                    {
                        new TextBlock { Text = "DOCUMENTS", Classes = { "eyebrow" }, VerticalAlignment = VerticalAlignment.Center },
                        WithColumn(ActionButton("+", async () => await _viewModel.NewDocumentCommand.ExecuteAsync(), "New document"), 1)
                    }
                }, 3),
                WithRow(new ScrollViewer { VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, Content = _documentsPanel }, 4),
                WithRow(new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
                    Children =
                    {
                        new TextBlock { Text = "STRUCTURE", Classes = { "eyebrow" }, VerticalAlignment = VerticalAlignment.Center },
                        WithColumn(ActionButton("+ Section", () => { _viewModel.AddSectionCommand.Execute(null); QueueRefresh(); return Task.CompletedTask; }, "Add document section"), 1),
                        WithColumn(ActionButton("+ Page", () => { _viewModel.AddPageCommand.Execute(null); QueueRefresh(); return Task.CompletedTask; }, "Add page to selected section"), 2)
                    }
                }, 5),
                WithRow(new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("*,*"),
                    ColumnSpacing = 6,
                    Children =
                    {
                        new ScrollViewer { VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, Content = _sectionsPanel },
                        WithColumn(new ScrollViewer { VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, Content = _pagesPanel }, 1)
                    }
                }, 6)
            }
        });
    }

    private Control BuildEditorPane()
    {
        var titleBar = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        titleBar.Children.Add(_documentTitle);
        titleBar.Children.Add(WithColumn(_statistics, 1));
        _documentTitle.GotFocus += (_, _) => BeginDocumentMetadataEdit();
        _documentTitle.LostFocus += (_, _) => CommitDocumentTitle();
        _documentTitle.KeyDown += (_, args) =>
        {
            if (args.Key != Key.Enter) return;
            args.Handled = true;
            Focus();
        };

        var addBar = new WrapPanel { ItemHeight = 34, ItemWidth = 92 };
        addBar.Children.Add(AddBlockButton("Paragraph", _viewModel.AddParagraphCommand));
        addBar.Children.Add(AddBlockButton("Heading", _viewModel.AddHeadingCommand));
        addBar.Children.Add(AddBlockButton("List", _viewModel.AddListCommand));
        addBar.Children.Add(AddBlockButton("Table", _viewModel.AddTableCommand));
        addBar.Children.Add(AddBlockButton("Equation", _viewModel.AddEquationCommand));
        addBar.Children.Add(AddBlockButton("HTML", _viewModel.AddHtmlCommand));
        addBar.Children.Add(AddBlockButton("Canvas", _viewModel.AddCanvasCommand));
        addBar.Children.Add(AddBlockButton("Flashcard", _viewModel.AddFlashcardCommand));
        addBar.Children.Add(ActionButton("Media", ImportMediaAsync, "Import image, audio, video or document media"));

        var header = Card(new StackPanel
        {
            Spacing = 9,
            Children =
            {
                titleBar,
                addBar,
                _deleteConfirmation
            }
        });
        _deleteConfirmation.Children.Add(new TextBlock { Text = "Move this document to recoverable trash?", VerticalAlignment = VerticalAlignment.Center });
        _deleteConfirmation.Children.Add(WithColumn(ActionButton("Cancel", () => { _viewModel.CancelDeleteDocumentCommand.Execute(null); RefreshDeleteConfirmation(); return Task.CompletedTask; }, "Cancel delete"), 1));
        _deleteConfirmation.Children.Add(WithColumn(ActionButton("Move to trash", async () => { await _viewModel.DeleteDocumentCommand.ExecuteAsync(); RefreshDeleteConfirmation(); }, "Confirm recoverable delete", danger: true), 2));

        var grid = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            RowSpacing = 10,
            Children =
            {
                header,
                WithRow(new ScrollViewer
                {
                    VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                    Content = _blocksPanel
                }, 1),
                WithRow(Card(_status), 2)
            }
        };
        return grid;
    }

    private Control BuildInspectorPane()
    {
        var tabs = new TabControl
        {
            Items = new object[]
            {
                new TabItem { Header = "AI", Content = InspectorScroll(_aiPanel) },
                new TabItem { Header = "Review", Content = InspectorScroll(_reviewPanel) },
                new TabItem { Header = "Versions", Content = InspectorScroll(_versionsPanel) },
                new TabItem { Header = "Document", Content = InspectorScroll(_informationPanel) }
            }
        };
        return Card(tabs);
    }

    private static ScrollViewer InspectorScroll(Control content) => new()
    {
        VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        Content = content
    };

    private async void OnAttachedToVisualTree(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        if (_initialized) return;
        _initialized = true;
        try
        {
            await _viewModel.InitializeAsync(CancellationToken.None);
            RefreshAll();
        }
        catch (Exception ex)
        {
            _status.Text = "Notes could not start: " + ex.Message;
        }
    }

    private void OnDetachedFromVisualTree(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        if (_activeEditBlockId is { } id && _viewModel.SelectedBlock?.Id == id)
            _viewModel.CommitBlockEdit(_viewModel.SelectedBlock, "Edited Notes content");
        _activeEditBlockId = null;
    }

    private void OnDocumentChanged(object? sender, EventArgs e)
    {
        if (_activeEditBlockId is not null)
        {
            RefreshStatusOnly();
            return;
        }
        QueueRefresh();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(NotesWorkspaceViewModel.Status)
            or nameof(NotesWorkspaceViewModel.IsDirty)
            or nameof(NotesWorkspaceViewModel.Document)
            or nameof(NotesWorkspaceViewModel.PendingAiChange))
            QueueRefresh();
    }

    private void OnSearchNavigationRequested(object? sender, NotesSearchHit e) => QueueRefresh();

    private void QueueRefresh()
    {
        if (_disposed || _refreshQueued) return;
        _refreshQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _refreshQueued = false;
            if (!_disposed) RefreshAll();
        }, DispatcherPriority.Background);
    }

    private void RefreshAll()
    {
        RefreshStatusOnly();
        RefreshDocuments();
        RefreshSearchResults();
        RefreshStructure();
        RefreshBlocks();
        RefreshInspector();
        RefreshDeleteConfirmation();
    }

    private void RefreshStatusOnly()
    {
        _status.Text = _viewModel.Status;
        _saveState.Text = _viewModel.SaveState;
        _statistics.Text = _viewModel.StatisticsLabel;
        if (_viewModel.Document is not null && !_documentTitle.IsFocused)
            _documentTitle.Text = _viewModel.Document.Title;
    }

    private void RefreshDocuments()
    {
        _documentsPanel.Children.Clear();
        foreach (var document in _viewModel.Documents)
        {
            var button = new Button
            {
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Content = new StackPanel
                {
                    Spacing = 2,
                    Children =
                    {
                        new TextBlock { Text = document.Title, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis },
                        new TextBlock { Text = $"{document.WordCount:N0} words · v{document.Version} · {document.UpdatedAt.LocalDateTime:g}" + (document.HasRecovery ? " · recovered" : string.Empty), Classes = { "muted2" }, FontSize = 9 }
                    }
                }
            };
            button.Classes.Add(_viewModel.Document?.Id == document.Id ? "accent" : "sidebar");
            button.Click += async (_, _) =>
            {
                try { await _viewModel.OpenDocumentAsync(document.Id, CancellationToken.None); RefreshAll(); }
                catch (Exception ex) { _status.Text = "Document could not open: " + ex.Message; }
            };
            _documentsPanel.Children.Add(button);
        }
    }

    private void RefreshSearchResults()
    {
        _searchPanel.Children.Clear();
        foreach (var hit in _viewModel.SearchResults)
        {
            var button = new Button
            {
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Content = new StackPanel
                {
                    Spacing = 1,
                    Children =
                    {
                        new TextBlock { Text = hit.DocumentTitle + " · " + hit.BlockKind, FontWeight = FontWeight.SemiBold, FontSize = 10 },
                        new TextBlock { Text = hit.Snippet, TextWrapping = TextWrapping.Wrap, Classes = { "muted" }, FontSize = 9, MaxLines = 3 }
                    }
                }
            };
            button.Classes.Add("sidebar");
            button.Click += (_, _) => _viewModel.SelectedSearchHit = hit;
            _searchPanel.Children.Add(button);
        }
        if (_viewModel.SearchResults.Count == 0)
            _searchPanel.Children.Add(new TextBlock { Text = "Search results appear here.", Classes = { "muted2" }, FontSize = 9, Margin = new Thickness(4) });
    }

    private void RefreshStructure()
    {
        _sectionsPanel.Children.Clear();
        foreach (var section in _viewModel.Sections)
        {
            var button = new Button { Content = section.Title, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            button.Classes.Add(ReferenceEquals(section, _viewModel.CurrentSection) ? "accent" : "sidebar");
            button.Click += (_, _) => _viewModel.SelectSection(section);
            _sectionsPanel.Children.Add(button);
        }
        _pagesPanel.Children.Clear();
        foreach (var page in _viewModel.Pages)
        {
            var button = new Button { Content = page.Title, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            button.Classes.Add(ReferenceEquals(page, _viewModel.CurrentPage) ? "accent" : "sidebar");
            button.Click += (_, _) => _viewModel.SelectPage(page);
            _pagesPanel.Children.Add(button);
        }
    }

    private void RefreshBlocks()
    {
        _blocksPanel.Children.Clear();
        foreach (var block in _viewModel.Blocks)
            _blocksPanel.Children.Add(NotesBlockEditorFactory.Build(_viewModel, block, BeginEditing, EndEditing, QueueRefresh, ImportMediaAsync));
        if (_viewModel.Blocks.Count == 0)
            _blocksPanel.Children.Add(new TextBlock { Text = "Use the block toolbar to add content.", Classes = { "muted" }, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(20) });
    }

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
        _aiPanel.Children.Add(new TextBlock { Text = "AI can propose changes, but cannot alter the document until you approve the exact result.", Classes = { "muted" }, TextWrapping = TextWrapping.Wrap, FontSize = 10 });
        var model = new ComboBox { ItemsSource = _viewModel.Models, SelectedItem = _viewModel.SelectedModelName, PlaceholderText = "Choose model" };
        model.SelectionChanged += (_, _) => _viewModel.SelectedModelName = model.SelectedItem as string ?? string.Empty;
        _aiPanel.Children.Add(model);
        var instruction = new TextBox { Text = _viewModel.AiInstruction, PlaceholderText = "Explain, rewrite, plan, check consistency, create revision cards…", AcceptsReturn = true, MinHeight = 90, TextWrapping = TextWrapping.Wrap };
        instruction.TextChanged += (_, _) => _viewModel.AiInstruction = instruction.Text ?? string.Empty;
        _aiPanel.Children.Add(instruction);
        var context = new CheckBox { Content = "Allow the model to receive this document's text context", IsChecked = _viewModel.AllowDocumentContext };
        context.IsCheckedChanged += (_, _) => _viewModel.AllowDocumentContext = context.IsChecked == true;
        _aiPanel.Children.Add(context);
        _aiPanel.Children.Add(new TextBlock { Text = "Without this permission, AI receives only the selected block. Citations are restricted to sources already stored in this note.", Classes = { "muted2" }, TextWrapping = TextWrapping.Wrap, FontSize = 9 });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
        buttons.Children.Add(ActionButton("Create proposal", async () => { await _viewModel.ProposeAiCommand.ExecuteAsync(); RefreshInspector(); }, "Generate a review-only proposal"));
        buttons.Children.Add(ActionButton("Cancel", () => { _viewModel.CancelAiCommand.Execute(null); return Task.CompletedTask; }, "Cancel the active AI request"));
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
                    new TextBlock { Text = $"{change.ProviderId} · {change.ModelName} · {change.CitationIds.Count} cited source{(change.CitationIds.Count == 1 ? string.Empty : "s")}", Classes = { "muted2" }, FontSize = 9 },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 7,
                        Children =
                        {
                            ActionButton("Approve and apply", async () => { await _viewModel.ApproveAiCommand.ExecuteAsync(); RefreshAll(); }, "Apply this exact proposal and create a version"),
                            ActionButton("Reject", () => { _viewModel.RejectAiCommand.Execute(null); RefreshAll(); return Task.CompletedTask; }, "Reject without changing document content", danger: true)
                        }
                    }
                }
            }));
        }

        _aiPanel.Children.Add(new TextBlock { Text = "PROVENANCE HISTORY", Classes = { "eyebrow" }, Margin = new Thickness(0, 8, 0, 0) });
        foreach (var history in _viewModel.AiHistory.OrderByDescending(item => item.CreatedAt).Take(20))
            _aiPanel.Children.Add(new TextBlock { Text = $"{history.CreatedAt.LocalDateTime:g} · {history.Status} · {history.ModelName}\n{history.Instruction}", TextWrapping = TextWrapping.Wrap, Classes = { "muted" }, FontSize = 9 });
    }

    private void BuildReviewInspector()
    {
        _reviewPanel.Children.Clear();
        _reviewPanel.Children.Add(new TextBlock { Text = "COMMENTS", Classes = { "eyebrow" } });
        var commentBox = new TextBox { PlaceholderText = "Comment on the selected block", AcceptsReturn = true, MinHeight = 60 };
        _reviewPanel.Children.Add(commentBox);
        _reviewPanel.Children.Add(ActionButton("Add comment", () => { _viewModel.AddCommentCommand.Execute(commentBox.Text); commentBox.Text = string.Empty; RefreshInspector(); return Task.CompletedTask; }, "Add a review comment to the selected block"));
        foreach (var comment in _viewModel.Comments.OrderByDescending(item => item.CreatedAt))
        {
            var row = new StackPanel { Spacing = 3 };
            row.Children.Add(new TextBlock { Text = $"{comment.Author} · {comment.CreatedAt.LocalDateTime:g} · {comment.State}", Classes = { "muted2" }, FontSize = 9 });
            row.Children.Add(new TextBlock { Text = comment.Text, TextWrapping = TextWrapping.Wrap });
            if (comment.State != NotesCommentState.Resolved)
                row.Children.Add(ActionButton("Resolve", () => { _viewModel.ResolveCommentCommand.Execute(comment); RefreshInspector(); return Task.CompletedTask; }, "Resolve comment"));
            _reviewPanel.Children.Add(Card(row));
        }

        _reviewPanel.Children.Add(new TextBlock { Text = "SOURCES AND CITATIONS", Classes = { "eyebrow" }, Margin = new Thickness(0, 8, 0, 0) });
        _reviewPanel.Children.Add(ActionButton("Add source", () => { _viewModel.AddCitationCommand.Execute(null); RefreshInspector(); return Task.CompletedTask; }, "Add a bibliography source"));
        foreach (var citation in _viewModel.Citations)
            _reviewPanel.Children.Add(BuildCitationEditor(citation));

        if (_viewModel.SelectedBlock?.Flashcard is { } card)
        {
            _reviewPanel.Children.Add(new TextBlock { Text = "STUDY REVIEW", Classes = { "eyebrow" }, Margin = new Thickness(0, 8, 0, 0) });
            _reviewPanel.Children.Add(new TextBlock { Text = card.Front, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
            _reviewPanel.Children.Add(new TextBlock { Text = card.Back, TextWrapping = TextWrapping.Wrap, Classes = { "muted" } });
            _reviewPanel.Children.Add(new TextBlock { Text = $"Due {card.Schedule.DueAt.LocalDateTime:g} · interval {card.Schedule.IntervalDays} days · ease {card.Schedule.EaseFactor:0.00}", Classes = { "muted2" }, FontSize = 9 });
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
        var evidence = new TextBox { Text = citation.EvidenceExcerpt, Watermark = "Evidence excerpt", AcceptsReturn = true, MinHeight = 55 };
        void Begin() => BeginDocumentMetadataEdit();
        void Commit()
        {
            citation.Title = title.Text ?? string.Empty;
            citation.Authors = authors.Text ?? string.Empty;
            citation.Url = url.Text ?? string.Empty;
            citation.EvidenceExcerpt = evidence.Text ?? string.Empty;
            CommitMetadataEdit("Edited citation " + citation.Key);
        }
        foreach (var box in new[] { title, authors, url, evidence }) { box.GotFocus += (_, _) => Begin(); box.LostFocus += (_, _) => Commit(); }
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
        _versionsPanel.Children.Add(new TextBlock { Text = "Every completed atomic save is retained. Restoring creates a new version rather than destroying later history.", Classes = { "muted" }, TextWrapping = TextWrapping.Wrap, FontSize = 10 });
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
            button.Click += (_, _) => { _viewModel.SelectedVersion = version; BuildVersionsInspector(); };
            _versionsPanel.Children.Add(button);
        }
        _versionsPanel.Children.Add(ActionButton("Restore selected version", async () => { await _viewModel.RestoreVersionCommand.ExecuteAsync(); RefreshAll(); }, "Restore as a new current version"));
    }

    private void BuildInformationInspector()
    {
        _informationPanel.Children.Clear();
        _informationPanel.Children.Add(new TextBlock { Text = "DOCUMENT INFORMATION", Classes = { "eyebrow" } });
        _informationPanel.Children.Add(new TextBlock { Text = _viewModel.StatisticsLabel, FontWeight = FontWeight.SemiBold });
        if (_viewModel.Document is not { } document) return;
        _informationPanel.Children.Add(new TextBlock { Text = $"Created {document.CreatedAt.LocalDateTime:g}\nUpdated {document.UpdatedAt.LocalDateTime:g}\nNative schema {document.SchemaVersion}\nDocument version {document.Version}", Classes = { "muted" }, FontSize = 10 });
        var language = new TextBox { Text = document.Language, Watermark = "Language, e.g. en-GB" };
        language.GotFocus += (_, _) => BeginDocumentMetadataEdit();
        language.LostFocus += (_, _) => { document.Language = language.Text?.Trim() ?? "en-GB"; CommitMetadataEdit("Changed document language"); };
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
        _informationPanel.Children.Add(new TextBlock { Text = "RECOVERY", Classes = { "eyebrow" }, Margin = new Thickness(0, 8, 0, 0) });
        _informationPanel.Children.Add(new TextBlock
        {
            Text = document.Recovery.HasUnsavedRecovery
                ? "Recovery review required: " + document.Recovery.RecoveryReason
                : $"Last autosave {document.Recovery.LastAutosaveAt?.LocalDateTime:g}\nLast validated SHA-256 {ShortHash(document.Recovery.LastValidSha256)}",
            TextWrapping = TextWrapping.Wrap,
            Classes = { "muted" },
            FontSize = 9
        });
        _informationPanel.Children.Add(new TextBlock { Text = "COLLABORATION METADATA", Classes = { "eyebrow" }, Margin = new Thickness(0, 8, 0, 0) });
        _informationPanel.Children.Add(new TextBlock { Text = $"Owner: {document.Collaboration.OwnerId}\nConflict state: {document.Collaboration.ConflictState}\nCollaborators: {document.Collaboration.Collaborators.Count}\nOpen conflicts: {document.Collaboration.Conflicts.Count(conflict => conflict.ResolvedAt is null)}", Classes = { "muted" }, FontSize = 9 });
    }

    private Control BuildPageSetup(NotesDocument document)
    {
        var width = new NumericUpDown { Minimum = 72, Maximum = 5000, Value = (decimal)document.PageSetup.WidthPoints };
        var height = new NumericUpDown { Minimum = 72, Maximum = 5000, Value = (decimal)document.PageSetup.HeightPoints };
        var margins = new NumericUpDown { Minimum = 0, Maximum = 1000, Value = (decimal)document.PageSetup.MarginTopPoints };
        var orientation = new ComboBox { ItemsSource = new[] { "Portrait", "Landscape" }, SelectedItem = document.PageSetup.Orientation };
        var pageNumbers = new CheckBox { Content = "Show page numbers", IsChecked = document.PageSetup.ShowPageNumbers };
        void Commit()
        {
            BeginDocumentMetadataEdit();
            document.PageSetup.WidthPoints = (double)(width.Value ?? 595);
            document.PageSetup.HeightPoints = (double)(height.Value ?? 842);
            document.PageSetup.MarginTopPoints = document.PageSetup.MarginRightPoints = document.PageSetup.MarginBottomPoints = document.PageSetup.MarginLeftPoints = (double)(margins.Value ?? 72);
            document.PageSetup.Orientation = orientation.SelectedItem as string ?? "Portrait";
            document.PageSetup.ShowPageNumbers = pageNumbers.IsChecked == true;
            CommitMetadataEdit("Changed page setup");
        }
        width.ValueChanged += (_, _) => Commit();
        height.ValueChanged += (_, _) => Commit();
        margins.ValueChanged += (_, _) => Commit();
        orientation.SelectionChanged += (_, _) => Commit();
        pageNumbers.IsCheckedChanged += (_, _) => Commit();
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

    private void BeginEditing(NotesBlock block)
    {
        if (_activeEditBlockId == block.Id) return;
        if (_activeEditBlockId is { } previousId)
        {
            var previous = _viewModel.Blocks.FirstOrDefault(item => item.Id == previousId);
            if (previous is not null) _viewModel.CommitBlockEdit(previous, "Edited " + previous.Kind);
        }
        _activeEditBlockId = block.Id;
        _viewModel.SelectedBlock = block;
        _viewModel.BeginBlockEdit(block);
    }

    private void EndEditing(NotesBlock block, string summary)
    {
        _viewModel.CommitBlockEdit(block, summary);
        if (_activeEditBlockId == block.Id) _activeEditBlockId = null;
        QueueRefresh();
    }

    private void BeginDocumentMetadataEdit()
    {
        var anchor = _viewModel.SelectedBlock ?? _viewModel.Blocks.FirstOrDefault();
        if (anchor is null || _activeEditBlockId == anchor.Id) return;
        BeginEditing(anchor);
    }

    private void CommitMetadataEdit(string summary)
    {
        var anchor = _activeEditBlockId is { } id ? _viewModel.Blocks.FirstOrDefault(item => item.Id == id) : _viewModel.SelectedBlock;
        if (anchor is not null) EndEditing(anchor, summary);
    }

    private void CommitDocumentTitle()
    {
        if (_viewModel.Document is null) return;
        var value = _documentTitle.Text?.Trim();
        if (string.IsNullOrWhiteSpace(value)) { _documentTitle.Text = _viewModel.Document.Title; CommitMetadataEdit("Kept document title"); return; }
        _viewModel.Document.Title = value;
        CommitMetadataEdit("Renamed document");
    }

    private async Task ImportDocumentAsync()
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import into Haven Notes",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Notes and documents") { Patterns = ["*.haven-notes.json", "*.json", "*.txt", "*.md", "*.markdown", "*.html", "*.htm", "*.csv", "*.rtf", "*.docx", "*.odt"] }
            ]
        });
        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path)) return;
        try { await _viewModel.ImportDocumentAsync(path, CancellationToken.None); RefreshAll(); }
        catch (Exception ex) { _status.Text = "Import failed: " + ex.Message; }
    }

    private async Task ExportDocumentAsync()
    {
        if (_viewModel.Document is null) return;
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return;
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export Haven Notes",
            SuggestedFileName = SafeFileName(_viewModel.Document.Title) + ".haven-notes.json",
            FileTypeChoices =
            [
                new FilePickerFileType("Haven Notes") { Patterns = ["*.haven-notes.json"] },
                new FilePickerFileType("PDF") { Patterns = ["*.pdf"] },
                new FilePickerFileType("Word document") { Patterns = ["*.docx"] },
                new FilePickerFileType("OpenDocument Text") { Patterns = ["*.odt"] },
                new FilePickerFileType("Markdown") { Patterns = ["*.md"] },
                new FilePickerFileType("HTML") { Patterns = ["*.html"] },
                new FilePickerFileType("Rich Text Format") { Patterns = ["*.rtf"] },
                new FilePickerFileType("Text") { Patterns = ["*.txt"] },
                new FilePickerFileType("CSV tables") { Patterns = ["*.csv"] }
            ]
        });
        var path = file?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path)) return;
        try { await _viewModel.ExportDocumentAsync(path, CancellationToken.None); RefreshStatusOnly(); }
        catch (Exception ex) { _status.Text = "Export failed: " + ex.Message; }
    }

    private async Task ImportMediaAsync()
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Insert media into Haven Notes",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Media") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.gif", "*.webp", "*.svg", "*.mp3", "*.wav", "*.m4a", "*.mp4", "*.webm", "*.pdf"] }]
        });
        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path)) return;
        try { await _viewModel.ImportMediaAsync(path, CancellationToken.None); RefreshAll(); }
        catch (Exception ex) { _status.Text = "Media import failed: " + ex.Message; }
    }

    private void RefreshDeleteConfirmation() => _deleteConfirmation.IsVisible = _viewModel.IsDeleteConfirming;

    private Button AddBlockButton(string label, System.Windows.Input.ICommand command)
    {
        var button = new Button { Content = label, Command = command, Margin = new Thickness(2) };
        button.Click += (_, _) => QueueRefresh();
        ToolTip.SetTip(button, "Insert " + label.ToLowerInvariant() + " block");
        return button;
    }

    private static Button CommandButton(string label, System.Windows.Input.ICommand command) => new() { Content = label, Command = command, Margin = new Thickness(2) };

    private static Button ActionButton(string label, Func<Task> action, string tooltip, bool danger = false)
    {
        var button = new Button { Content = label };
        button.Classes.Add(danger ? "danger" : "secondary");
        button.Click += async (_, _) => await action();
        ToolTip.SetTip(button, tooltip);
        AutomationProperties.SetName(button, tooltip);
        return button;
    }

    private static Border Card(Control child) => new()
    {
        Background = ResourceBrush("HavenPanelBrush", Color.FromRgb(30, 30, 34)),
        BorderBrush = ResourceBrush("HavenLineBrush", Color.FromArgb(50, 255, 255, 255)),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(12),
        Padding = new Thickness(11),
        Child = child
    };

    private static Control Labeled(string label, Control control) => new StackPanel
    {
        Spacing = 3,
        Children =
        {
            new TextBlock { Text = label, Classes = { "muted" }, FontSize = 9 },
            control
        }
    };

    private static GridSplitter Splitter() => new()
    {
        Width = 6,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        Background = Brushes.Transparent,
        ResizeDirection = GridResizeDirection.Columns
    };

    private static IBrush ResourceBrush(string key, Color fallback) =>
        Application.Current?.Resources[key] as IBrush ?? new SolidColorBrush(fallback);

    private static T WithColumn<T>(T control, int column) where T : Control { Grid.SetColumn(control, column); return control; }
    private static T WithRow<T>(T control, int row) where T : Control { Grid.SetRow(control, row); return control; }
    private static string SafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var normalized = new string(value.Where(character => !invalid.Contains(character)).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(normalized) ? "Haven Note" : normalized;
    }
    private static string ShortHash(string value) => string.IsNullOrWhiteSpace(value) ? "not saved yet" : value[..Math.Min(16, value.Length)] + "…";

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        AttachedToVisualTree -= OnAttachedToVisualTree;
        DetachedFromVisualTree -= OnDetachedFromVisualTree;
        _viewModel.DocumentChanged -= OnDocumentChanged;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel.SearchNavigationRequested -= OnSearchNavigationRequested;
        foreach (var preview in Descendants(this).OfType<NotesHtmlPreviewControl>()) preview.Dispose();
        _viewModel.Dispose();
        Content = null;
        GC.SuppressFinalize(this);
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        if (root is Panel panel)
        {
            foreach (var child in panel.Children)
            {
                yield return child;
                foreach (var nested in Descendants(child)) yield return nested;
            }
        }
        else if (root is ContentControl { Content: Control content })
        {
            yield return content;
            foreach (var nested in Descendants(content)) yield return nested;
        }
        else if (root is Decorator { Child: Control child })
        {
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }
}
