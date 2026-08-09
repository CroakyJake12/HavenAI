/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/Views/NotesWorkspaceView.cs, in the Desktop view layer, where Avalonia controls connect XAML interaction to view models.
 * What: This file owns NotesWorkspaceView. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The file keeps one cohesive responsibility in a predictable location so callers can find and replace it without unrelated changes.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Haven.Core;
using Haven.Desktop.Controls;
using Haven.Desktop.Views.Pages.Notes;

namespace Haven.Desktop.Views;

using NotesPageView = Pages.Notes.NotesPage;

/// <summary>
/// Represents notes workspace view and keeps its related state and behavior together.
/// </summary>
public sealed partial class NotesWorkspaceView : UserControl, IDisposable
{
    /// <summary>
    /// Stores page locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly NotesPageView _page;
    /// <summary>
    /// Stores documents panel locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly StackPanel _documentsPanel = new() { Spacing = 5 };
    /// <summary>
    /// Stores sections panel locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly StackPanel _sectionsPanel = new() { Spacing = 4 };
    /// <summary>
    /// Stores pages panel locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly StackPanel _pagesPanel = new() { Spacing = 4 };
    /// <summary>
    /// Stores search panel locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly StackPanel _searchPanel = new() { Spacing = 5 };
    /// <summary>
    /// Stores blocks panel locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly StackPanel _blocksPanel = new() { Spacing = 12 };
    /// <summary>
    /// Stores ai panel locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly StackPanel _aiPanel = new() { Spacing = 10 };
    /// <summary>
    /// Stores review panel locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly StackPanel _reviewPanel = new() { Spacing = 10 };
    /// <summary>
    /// Stores versions panel locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly StackPanel _versionsPanel = new() { Spacing = 7 };
    /// <summary>
    /// Stores information panel locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly StackPanel _informationPanel = new() { Spacing = 9 };
    /// <summary>Hosts the document library and outline only when Navigation is open.</summary>
    private readonly Border _libraryHost = new() { Width = 285, IsVisible = false };
    /// <summary>Hosts formatting, AI, review, version and document tools on demand.</summary>
    private readonly Border _inspectorHost = new() { Width = 365, IsVisible = false };
    private readonly GridSplitter _librarySplitter = Splitter();
    private readonly GridSplitter _inspectorSplitter = Splitter();
    /// <summary>
    /// Stores document title locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly TextBox _documentTitle = new() { FontSize = 24, FontWeight = FontWeight.SemiBold, MinWidth = 280 };
    /// <summary>
    /// Stores status locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly TextBlock _status = new() { Classes = { "muted" }, TextWrapping = TextWrapping.Wrap, FontSize = 10 };
    /// <summary>
    /// Stores save state locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly TextBlock _saveState = new() { Classes = { "muted2" }, FontSize = 10 };
    /// <summary>
    /// Stores statistics locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly TextBlock _statistics = new() { Classes = { "muted" }, FontSize = 10, VerticalAlignment = VerticalAlignment.Center };
    /// <summary>
    /// Stores search box locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly TextBox _searchBox = new() { PlaceholderText = "Search every Notes document" };
    /// <summary>
    /// Stores delete confirmation locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly Grid _deleteConfirmation = new() { IsVisible = false, ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 7 };
    /// <summary>
    /// Stores initialized locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _initialized;
    /// <summary>
    /// Stores refresh queued locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _refreshQueued;
    /// <summary>
    /// Stores disposed locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _disposed;
    /// <summary>
    /// Stores active edit block id locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private Guid? _activeEditBlockId;
    private bool _dirtyDocuments = true;
    private bool _dirtySearch = true;
    private bool _dirtyStructure = true;
    private bool _dirtyBlocks = true;
    private bool _dirtyInspector = true;

    public NotesWorkspaceView(NotesPageView page)
    {
        _page = page ?? throw new ArgumentNullException(nameof(page));
        DataContext = page;
        AutomationProperties.SetName(this, "Haven Notes document workspace");
        Content = BuildLayout();
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        _page.DocumentChanged += OnDocumentChanged;
        _page.PropertyChanged += OnViewModelPropertyChanged;
        _page.SearchNavigationRequested += OnSearchNavigationRequested;
    }

    /// <summary>
    /// Builds layout from the currently available inputs.
    /// </summary>
    private Control BuildLayout()
    {
        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*"),
            Background = ResourceBrush("HavenBackgroundBrush", Color.FromRgb(18, 18, 20))
        };
        root.Children.Add(BuildTopBar());
        root.Children.Add(WithRow(BuildDocumentRibbon(), 1));
        var body = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,6,*,6,Auto"),
            Margin = new Thickness(12, 8, 12, 12)
        };
        _libraryHost.Child = BuildLibraryPane();
        _inspectorHost.Child = BuildInspectorPane();
        _librarySplitter.IsVisible = false;
        _inspectorSplitter.IsVisible = false;
        Grid.SetRow(body, 2);
        body.Children.Add(_libraryHost);
        body.Children.Add(WithColumn(_librarySplitter, 1));
        body.Children.Add(WithColumn(BuildEditorPane(), 2));
        body.Children.Add(WithColumn(_inspectorSplitter, 3));
        body.Children.Add(WithColumn(_inspectorHost, 4));
        root.Children.Add(body);
        return root;
    }

    /// <summary>
    /// Builds top bar from the currently available inputs.
    /// </summary>
    private Control BuildTopBar()
    {
        var panel = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,Auto,Auto,*,Auto,Auto,Auto,Auto"),
            ColumnSpacing = 7,
            Margin = new Thickness(14, 10, 14, 0)
        };
        panel.Children.Add(ActionButton("New", async () => await _page.NewDocumentCommand.ExecuteAsync(), "Create a new local Notes document"));
        panel.Children.Add(WithColumn(ActionButton("Import", ImportDocumentAsync, "Open or import a supported document into Haven Documents"), 1));
        panel.Children.Add(WithColumn(ActionButton("Save", async () => await _page.SaveCommand.ExecuteAsync(), "Save now; autosave also runs automatically"), 2));
        panel.Children.Add(WithColumn(ActionButton("Undo", () => { _page.UndoCommand.Execute(null); return Task.CompletedTask; }, "Undo the last document edit"), 3));
        panel.Children.Add(WithColumn(ActionButton("Redo", () => { _page.RedoCommand.Execute(null); return Task.CompletedTask; }, "Redo the last undone edit"), 4));
        var identity = new StackPanel
        {
            Spacing = 1,
            HorizontalAlignment = HorizontalAlignment.Center,
            Children =
            {
                new TextBlock { Text = "HAVEN NOTES", Classes = { "eyebrow" }, HorizontalAlignment = HorizontalAlignment.Center },
                new TextBlock { Text = "Documents", Classes = { "muted2" }, FontSize = 9, HorizontalAlignment = HorizontalAlignment.Center },
                _saveState
            }
        };
        panel.Children.Add(WithColumn(identity, 5));
        panel.Children.Add(WithColumn(ActionButton("Navigation", ToggleLibraryPane, "Show or hide documents, search, sections and pages"), 6));
        panel.Children.Add(WithColumn(ActionButton("Advanced tools", ToggleInspectorPane, "Show or hide block, AI, review, version and document tools"), 7));
        panel.Children.Add(WithColumn(ActionButton("Export", ExportDocumentAsync, "Export the current document truthfully"), 8));
        panel.Children.Add(WithColumn(ActionButton("Print", async () => await _page.PrintAsync(CancellationToken.None), "Create a print-ready PDF and open the Windows print handler"), 9));
        return panel;
    }

    /// <summary>
    /// Builds the compact word-processor ribbon. The frequently used writing and
    /// insertion tools remain one click away without permanently surrounding the
    /// page with website-editor sidebars.
    /// </summary>
    private Control BuildDocumentRibbon()
    {
        var home = new WrapPanel { ItemHeight = 32, ItemWidth = 88 };
        home.Children.Add(AddBlockButton("Paragraph", _page.AddParagraphCommand));
        home.Children.Add(AddBlockButton("Heading", _page.AddHeadingCommand));
        home.Children.Add(AddBlockButton("List", _page.AddListCommand));

        var insert = new WrapPanel { ItemHeight = 32, ItemWidth = 82 };
        insert.Children.Add(AddBlockButton("Table", _page.AddTableCommand));
        insert.Children.Add(AddBlockButton("Equation", _page.AddEquationCommand));
        insert.Children.Add(AddBlockButton("Canvas", _page.AddCanvasCommand));
        insert.Children.Add(AddBlockButton("Flashcard", _page.AddFlashcardCommand));
        insert.Children.Add(AddBlockButton("HTML", _page.AddHtmlCommand));
        insert.Children.Add(ActionButton("Media", ImportMediaAsync, "Insert image, audio, video or document media"));

        static Control Group(string label, Control tools) => new StackPanel
        {
            Spacing = 3,
            Children =
            {
                tools,
                new TextBlock
                {
                    Text = label,
                    Classes = { "muted2" },
                    FontSize = 9,
                    HorizontalAlignment = HorizontalAlignment.Center
                }
            }
        };

        var toolsGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto"),
            ColumnSpacing = 16,
            Margin = new Thickness(14, 6)
        };
        toolsGrid.Children.Add(Group("WRITE", home));
        toolsGrid.Children.Add(WithColumn(Group("INSERT", insert), 1));
        toolsGrid.Children.Add(WithColumn(ActionButton("Delete document", () =>
        {
            _page.RequestDeleteDocumentCommand.Execute(null);
            RefreshDeleteConfirmation();
            return Task.CompletedTask;
        }, "Move this document to recoverable trash", danger: true), 3));

        return new HavenAdaptiveSurface
        {
            Background = ResourceBrush("HavenSurfaceBrush", Color.FromRgb(28, 28, 31)),
            BorderBrush = ResourceBrush("HavenBorderBrush", Color.FromRgb(55, 55, 62)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = toolsGrid
        };
    }

    /// <summary>Shows or hides document navigation while leaving all navigation features available.</summary>
    private Task ToggleLibraryPane()
    {
        var show = !_libraryHost.IsVisible;
        _libraryHost.IsVisible = show;
        _librarySplitter.IsVisible = show;
        return Task.CompletedTask;
    }

    /// <summary>Shows or hides advanced editing tools while leaving their state intact.</summary>
    private Task ToggleInspectorPane()
    {
        var show = !_inspectorHost.IsVisible;
        _inspectorHost.IsVisible = show;
        _inspectorSplitter.IsVisible = show;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Builds library pane from the currently available inputs.
    /// </summary>
    private Control BuildLibraryPane()
    {
        var searchButton = ActionButton("Search", SearchFromBoxAsync, "Search all local Notes documents");
        var clearSearch = ActionButton("Clear", () =>
        {
            _page.ClearSearchCommand.Execute(null);
            _searchBox.Text = string.Empty;
            RefreshSearchResults();
            return Task.CompletedTask;
        }, "Clear Notes search");
        var searchGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 5 };
        searchGrid.Children.Add(_searchBox);
        searchGrid.Children.Add(WithColumn(searchButton, 1));
        searchGrid.Children.Add(WithColumn(clearSearch, 2));
        _searchBox.KeyDown += async (_, args) =>
        {
            if (args.Key != Key.Enter) return;
            args.Handled = true;
            await SearchFromBoxAsync();
        };

        var structureHeader = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 5 };
        structureHeader.Children.Add(new TextBlock { Text = "STRUCTURE", Classes = { "eyebrow" }, VerticalAlignment = VerticalAlignment.Center });
        structureHeader.Children.Add(WithColumn(ActionButton("+ Section", () => { _page.AddSectionCommand.Execute(null); QueueRefresh(); return Task.CompletedTask; }, "Add document section"), 1));
        structureHeader.Children.Add(WithColumn(ActionButton("+ Page", () => { _page.AddPageCommand.Execute(null); QueueRefresh(); return Task.CompletedTask; }, "Add page to selected section"), 2));

        var structure = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 6 };
        structure.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, Content = _sectionsPanel });
        structure.Children.Add(WithColumn(new ScrollViewer { VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, Content = _pagesPanel }, 1));

        var content = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = "LIBRARY", Classes = { "eyebrow" } },
                searchGrid,
                new HavenAdaptiveSurface { Height = 145, Child = new ScrollViewer { VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, Content = _searchPanel } },
                new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                    Children =
                    {
                        new TextBlock { Text = "DOCUMENTS", Classes = { "eyebrow" }, VerticalAlignment = VerticalAlignment.Center },
                        WithColumn(ActionButton("+", async () => await _page.NewDocumentCommand.ExecuteAsync(), "New document"), 1)
                    }
                },
                new HavenAdaptiveSurface { MinHeight = 180, MaxHeight = 320, Child = new ScrollViewer { VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, Content = _documentsPanel } },
                structureHeader,
                new HavenAdaptiveSurface { MinHeight = 150, Child = structure }
            }
        };
        return Card(content);
    }

    /// <summary>
    /// Builds editor pane from the currently available inputs.
    /// </summary>
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

        _deleteConfirmation.Children.Add(new TextBlock { Text = "Move this document to recoverable trash?", VerticalAlignment = VerticalAlignment.Center });
        _deleteConfirmation.Children.Add(WithColumn(ActionButton("Cancel", () => { _page.CancelDeleteDocumentCommand.Execute(null); RefreshDeleteConfirmation(); return Task.CompletedTask; }, "Cancel delete"), 1));
        _deleteConfirmation.Children.Add(WithColumn(ActionButton("Move to trash", async () => { await _page.DeleteDocumentCommand.ExecuteAsync(); RefreshDeleteConfirmation(); }, "Confirm recoverable delete", danger: true), 2));

        var page = new HavenAdaptiveSurface
        {
            MaxWidth = 900,
            MinHeight = 1080,
            Margin = new Thickness(28, 18, 28, 48),
            Padding = new Thickness(64, 52),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Background = ResourceBrush("HavenElevatedBrush", Color.FromRgb(35, 35, 39)),
            BorderBrush = ResourceBrush("HavenBorderBrush", Color.FromRgb(63, 63, 70)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Child = new StackPanel
            {
                Spacing = 14,
                Children =
                {
                    titleBar,
                    new Separator(),
                    _deleteConfirmation,
                    _blocksPanel
                }
            }
        };

        var statusBar = new HavenAdaptiveSurface
        {
            Background = ResourceBrush("HavenSurfaceBrush", Color.FromRgb(28, 28, 31)),
            BorderBrush = ResourceBrush("HavenBorderBrush", Color.FromRgb(55, 55, 62)),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(12, 5),
            Child = _status
        };
        return new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto"),
            Children =
            {
                new ScrollViewer
                {
                    VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                    Content = page
                },
                WithRow(statusBar, 1)
            }
        };
    }

    /// <summary>
    /// Builds inspector pane from the currently available inputs.
    /// </summary>
    private Control BuildInspectorPane()
    {
        var tabs = new HavenTabView
        {
            Focusable = true,
            ItemsSource = new object[]
            {
                new HavenTabItem { Header = "Block", Content = InspectorScroll(_blockPanel) },
                new HavenTabItem { Header = "AI", Content = InspectorScroll(_aiPanel) },
                new HavenTabItem { Header = "Review", Content = InspectorScroll(_reviewPanel) },
                new HavenTabItem { Header = "Versions", Content = InspectorScroll(_versionsPanel) },
                new HavenTabItem { Header = "Document", Content = InspectorScroll(_informationPanel) }
            }
        };
        AutomationProperties.SetName(tabs, "Advanced document tools");
        // The inspector starts collapsed, so its tab containers do not exist yet.
        // Select Block when the pane is actually attached; selecting it earlier
        // is discarded by Avalonia while the ItemsSource is still unrealized.
        tabs.AttachedToVisualTree += (_, _) =>
        {
            if (tabs.SelectedIndex < 0) tabs.SelectedIndex = 0;
        };
        return Card(tabs);
    }

    /// <summary>
    /// Performs the inspector scroll step owned by this component.
    /// </summary>
    private static ScrollViewer InspectorScroll(Control content) => new()
    {
        VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        Content = content
    };

    /// <summary>
    /// Handles the attached to visual tree event raised by the UI or runtime.
    /// </summary>
    private async void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (_initialized) return;
        _initialized = true;
        try
        {
            await _page.InitializeAsync(CancellationToken.None);
            MarkAllDirty();
            RefreshAll();
        }
        catch (Exception ex)
        {
            _status.Text = "Notes could not start: " + ex.Message;
        }
    }

    /// <summary>
    /// Handles the detached from visual tree event raised by the UI or runtime.
    /// </summary>
    private async void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (_activeEditBlockId is { } id && _page.Blocks.FirstOrDefault(block => block.Id == id) is { } block)
            await _page.CommitBlockEditAsync(block, "Edited Notes content");
        _activeEditBlockId = null;
    }

    /// <summary>
    /// Handles the document changed event raised by the UI or runtime.
    /// </summary>
    private void OnDocumentChanged(object? sender, EventArgs e)
    {
        if (_activeEditBlockId is not null)
        {
            RefreshStatusOnly();
            return;
        }
        QueueRefresh();
    }

    /// <summary>
    /// Handles the view model property changed event raised by the UI or runtime.
    /// </summary>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(NotesPageView.Status)
            or nameof(NotesPageView.IsDirty)
            or nameof(NotesPageView.Document)
            or nameof(NotesPageView.PendingAiChange))
            QueueRefresh();
    }

    /// <summary>
    /// Handles the search navigation requested event raised by the UI or runtime.
    /// </summary>
    private void OnSearchNavigationRequested(object? sender, NotesSearchHit e) => QueueRefresh();

    /// <summary>
    /// Marks all panels as dirty so the next RefreshAll rebuilds them.
    /// </summary>
    private void MarkAllDirty()
    {
        _dirtyDocuments = true;
        _dirtySearch = true;
        _dirtyStructure = true;
        _dirtyBlocks = true;
        _dirtyInspector = true;
    }

    /// <summary>
    /// Performs the queue refresh step owned by this component.
    /// </summary>
    private void QueueRefresh()
    {
        if (_disposed || _refreshQueued) return;
        MarkAllDirty();
        _refreshQueued = true;
        UiBatcher.Defer(() =>
        {
            _refreshQueued = false;
            if (!_disposed) RefreshAll();
        });
    }

    /// <summary>
    /// Performs the refresh all step owned by this component.
    /// </summary>
    private void RefreshAll()
    {
        using (UiBatcher.BeginBatch())
        {
            RefreshStatusOnly();
            if (_dirtyDocuments) { RefreshDocuments(); _dirtyDocuments = false; }
            if (_dirtySearch) { RefreshSearchResults(); _dirtySearch = false; }
            if (_dirtyStructure) { RefreshStructure(); _dirtyStructure = false; }
            if (_dirtyBlocks) { RefreshBlocks(); _dirtyBlocks = false; }
            if (_dirtyInspector) { BuildSelectedBlockInspector(); RefreshInspector(); _dirtyInspector = false; }
            RefreshDeleteConfirmation();
        }
    }

    /// <summary>
    /// Performs the refresh status only step owned by this component.
    /// </summary>
    private void RefreshStatusOnly()
    {
        _status.Text = _page.Status;
        _saveState.Text = _page.SaveState;
        _statistics.Text = _page.StatisticsLabel;
        if (_page.Document is not null && !_documentTitle.IsFocused)
            _documentTitle.Text = _page.Document.Title;
    }

    /// <summary>
    /// Performs the refresh documents step owned by this component.
    /// </summary>
    private void RefreshDocuments()
    {
        UiBatcher.RebuildChildren(_documentsPanel, panel =>
        {
            foreach (var document in _page.Documents)
            {
                var button = new HavenButton
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
                button.Classes.Add(_page.Document?.Id == document.Id ? "accent" : "sidebar");
                button.Click += async (_, _) =>
                {
                    try { await _page.OpenDocumentAsync(document.Id, CancellationToken.None); MarkAllDirty(); RefreshAll(); }
                    catch (Exception ex) { _status.Text = "Document could not open: " + ex.Message; }
                };
                panel.Children.Add(button);
            }
        });
    }

    /// <summary>
    /// Performs the refresh search results step owned by this component.
    /// </summary>
    private void RefreshSearchResults()
    {
        UiBatcher.RebuildChildren(_searchPanel, panel =>
        {
            foreach (var hit in _page.SearchResults)
            {
                var button = new HavenButton
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
                button.Click += (_, _) => _page.SelectedSearchHit = hit;
                panel.Children.Add(button);
            }
            if (_page.SearchResults.Count == 0)
                panel.Children.Add(new TextBlock { Text = "Search results appear here.", Classes = { "muted2" }, FontSize = 9, Margin = new Thickness(4) });
        });
    }

    /// <summary>
    /// Performs the refresh structure step owned by this component.
    /// </summary>
    private void RefreshStructure()
    {
        UiBatcher.RebuildChildren(_sectionsPanel, panel =>
        {
            foreach (var section in _page.Sections)
            {
                var button = new HavenButton { Content = section.Title, HorizontalContentAlignment = HorizontalAlignment.Stretch };
                button.Classes.Add(ReferenceEquals(section, _page.CurrentSection) ? "accent" : "sidebar");
                button.Click += (_, _) => _page.SelectSection(section);
                panel.Children.Add(button);
            }
        });
        UiBatcher.RebuildChildren(_pagesPanel, panel =>
        {
            foreach (var page in _page.Pages)
            {
                var button = new HavenButton { Content = page.Title, HorizontalContentAlignment = HorizontalAlignment.Stretch };
                button.Classes.Add(ReferenceEquals(page, _page.CurrentPage) ? "accent" : "sidebar");
                button.Click += (_, _) => _page.SelectPage(page);
                panel.Children.Add(button);
            }
        });
    }

    /// <summary>
    /// Performs the refresh blocks step owned by this component.
    /// </summary>
    private void RefreshBlocks()
    {
        foreach (var preview in _blocksPanel.GetVisualDescendants().OfType<NotesHtmlPreviewControl>()) preview.Dispose();
        _blocksPanel.Children.Clear();
        foreach (var block in _page.Blocks)
            _blocksPanel.Children.Add(BuildDocumentBlock(block));
        if (_page.Blocks.Count == 0)
            _blocksPanel.Children.Add(new TextBlock { Text = "Use the block toolbar to add content.", Classes = { "muted" }, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(20) });
    }

    /// <summary>
    /// Presents ordinary prose like a word processor. The complete structural
    /// editor remains available in a collapsed section, so formatting, runs,
    /// ordering and deletion are preserved without dominating the page.
    /// </summary>
    private Control BuildDocumentBlock(NotesBlock block)
    {
        var advanced = NotesBlockEditorFactory.Build(
            _page, block, BeginEditingAsync, EndEditingAsync, QueueRefresh, ImportMediaAsync);
        if (block.Kind is not (NotesBlockKind.Paragraph or NotesBlockKind.Heading or NotesBlockKind.Quote))
            return advanced;

        var editor = new HavenTextInput
        {
            Text = block.PlainText,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(2, 5),
            MinHeight = block.Kind == NotesBlockKind.Heading ? 54 : 40,
            FontSize = block.Kind == NotesBlockKind.Heading ? 27 : 15,
            FontWeight = block.Kind == NotesBlockKind.Heading ? FontWeight.SemiBold : FontWeight.Normal,
            FontStyle = block.Kind == NotesBlockKind.Quote ? FontStyle.Italic : FontStyle.Normal,
            PlaceholderText = block.Kind == NotesBlockKind.Heading ? "Heading" : "Start writing…"
        };
        AutomationProperties.SetName(editor, block.Kind == NotesBlockKind.Heading ? "Document heading" : "Document paragraph");
        editor.GotFocus += (_, _) => _ = BeginEditingAsync(block);
        editor.TextChanged += (_, _) =>
        {
            var text = editor.Text ?? string.Empty;
            if (block.Runs.Count == 1) block.Runs[0].Text = text;
            _page.UpdateBlockText(block, text);
        };
        editor.LostFocus += (_, _) => _ = EndEditingAsync(block, "Edited " + block.Kind);

        Control writingSurface = editor;
        if (block.Kind == NotesBlockKind.Quote)
            writingSurface = new HavenAdaptiveSurface
            {
                BorderBrush = ResourceBrush("HavenAccentBrush", Color.FromRgb(47, 128, 237)),
                BorderThickness = new Thickness(3, 0, 0, 0),
                Padding = new Thickness(12, 0, 0, 0),
                Child = editor
            };

        var tools = new HavenExpander
        {
            Header = "Formatting and block tools",
            Content = advanced,
            IsExpanded = block.Runs.Count > 1,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        AutomationProperties.SetName(tools, "Formatting and block tools for " + block.Kind);
        return new StackPanel
        {
            Spacing = 3,
            Margin = new Thickness(0, 1, 0, 6),
            Children = { writingSurface, tools }
        };
    }

    /// <summary>
    /// Performs search from box asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task SearchFromBoxAsync()
    {
        _page.SearchQuery = _searchBox.Text ?? string.Empty;
        await _page.SearchCommand.ExecuteAsync();
        RefreshSearchResults();
    }

    /// <summary>
    /// Performs the begin editing step owned by this component.
    /// </summary>
    private async Task BeginEditingAsync(NotesBlock block)
    {
        if (_activeEditBlockId == block.Id) return;
        if (_activeEditBlockId is { } previousId && _page.Blocks.FirstOrDefault(item => item.Id == previousId) is { } previous)
            await _page.CommitBlockEditAsync(previous, "Edited " + previous.Kind);
        _activeEditBlockId = block.Id;
        _page.SelectedBlock = block;
        await _page.BeginBlockEditAsync(block);
    }

    /// <summary>
    /// Performs the end editing step owned by this component.
    /// </summary>
    private async Task EndEditingAsync(NotesBlock block, string summary)
    {
        await _page.CommitBlockEditAsync(block, summary);
        if (_activeEditBlockId == block.Id) _activeEditBlockId = null;
        QueueRefresh();
    }

    /// <summary>
    /// Performs the begin document metadata edit step owned by this component.
    /// </summary>
    private void BeginDocumentMetadataEdit()
    {
        var anchor = _page.SelectedBlock ?? _page.Blocks.FirstOrDefault();
        if (anchor is not null) _ = BeginEditingAsync(anchor);
    }

    /// <summary>
    /// Performs the commit metadata edit step owned by this component.
    /// </summary>
    private void CommitMetadataEdit(string summary)
    {
        var anchor = _activeEditBlockId is { } id
            ? _page.Blocks.FirstOrDefault(item => item.Id == id)
            : _page.SelectedBlock;
        if (anchor is not null) _ = EndEditingAsync(anchor, summary);
    }

    /// <summary>
    /// Performs the commit document title step owned by this component.
    /// </summary>
    private void CommitDocumentTitle()
    {
        if (_page.Document is null) return;
        var value = _documentTitle.Text?.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            _documentTitle.Text = _page.Document.Title;
            CommitMetadataEdit("Kept document title");
            return;
        }
        _page.Document.Title = value;
        CommitMetadataEdit("Renamed document");
    }

    /// <summary>
    /// Performs import document asynchronously so I/O does not block the caller's thread.
    /// </summary>
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
                new FilePickerFileType("Notes and documents")
                {
                    Patterns = ["*.haven-notes.json", "*.json", "*.txt", "*.md", "*.markdown", "*.html", "*.htm", "*.csv", "*.rtf", "*.docx", "*.odt"]
                }
            ]
        });
        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path)) return;
        try { await _page.ImportDocumentAsync(path, CancellationToken.None); MarkAllDirty(); RefreshAll(); }
        catch (Exception ex) { _status.Text = "Import failed: " + ex.Message; }
    }

    /// <summary>
    /// Performs export document asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task ExportDocumentAsync()
    {
        if (_page.Document is null) return;
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return;
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export Haven Notes",
            SuggestedFileName = SafeFileName(_page.Document.Title) + ".haven-notes.json",
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
        try { await _page.ExportDocumentAsync(path, CancellationToken.None); RefreshStatusOnly(); }
        catch (Exception ex) { _status.Text = "Export failed: " + ex.Message; }
    }

    /// <summary>
    /// Performs import media asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task ImportMediaAsync()
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Insert media into Haven Notes",
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
        try { await _page.ImportMediaAsync(path, CancellationToken.None); MarkAllDirty(); RefreshAll(); }
        catch (Exception ex) { _status.Text = "Media import failed: " + ex.Message; }
    }

    /// <summary>
    /// Performs the refresh delete confirmation step owned by this component.
    /// </summary>
    private void RefreshDeleteConfirmation() => _deleteConfirmation.IsVisible = _page.IsDeleteConfirming;

    /// <summary>
    /// Performs the add block button step owned by this component.
    /// </summary>
    private Button AddBlockButton(string label, System.Windows.Input.ICommand command)
    {
        var button = new HavenButton { Content = label, Command = command, Margin = new Thickness(2) };
        button.Click += (_, _) => QueueRefresh();
        ToolTip.SetTip(button, "Insert " + label.ToLowerInvariant() + " block");
        return button;
    }

    /// <summary>
    /// Performs the command button step owned by this component.
    /// </summary>
    private static Button CommandButton(string label, System.Windows.Input.ICommand command) => new()
    {
        Content = label,
        Command = command,
        Margin = new Thickness(2)
    };

    /// <summary>
    /// Performs the action button step owned by this component.
    /// </summary>
    private static Button ActionButton(string label, Func<Task> action, string tooltip, bool danger = false)
    {
        var button = new HavenButton { Content = label };
        button.Classes.Add(danger ? "danger" : "secondary");
        button.Click += async (_, _) => await action();
        ToolTip.SetTip(button, tooltip);
        AutomationProperties.SetName(button, tooltip);
        return button;
    }

    /// <summary>
    /// Performs the card step owned by this component.
    /// </summary>
    private static Border Card(Control child) => new()
    {
        Background = ResourceBrush("HavenPanelBrush", Color.FromRgb(30, 30, 34)),
        BorderBrush = ResourceBrush("HavenLineBrush", Color.FromArgb(50, 255, 255, 255)),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(12),
        Padding = new Thickness(11),
        Child = child
    };

    /// <summary>
    /// Performs the labeled step owned by this component.
    /// </summary>
    private static Control Labeled(string label, Control control) => new StackPanel
    {
        Spacing = 3,
        Children =
        {
            new TextBlock { Text = label, Classes = { "muted" }, FontSize = 9 },
            control
        }
    };

    /// <summary>
    /// Performs the splitter step owned by this component.
    /// </summary>
    private static GridSplitter Splitter() => new()
    {
        Width = 6,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        Background = Brushes.Transparent,
        ResizeDirection = GridResizeDirection.Columns
    };

    /// <summary>
    /// Performs the resource brush step owned by this component.
    /// </summary>
    private static IBrush ResourceBrush(string key, Color fallback) =>
        Avalonia.Application.Current?.Resources[key] as IBrush ?? new SolidColorBrush(fallback);

    private static T WithColumn<T>(T control, int column) where T : Control
    {
        Grid.SetColumn(control, column);
        return control;
    }

    private static T WithRow<T>(T control, int row) where T : Control
    {
        Grid.SetRow(control, row);
        return control;
    }

    /// <summary>
    /// Performs the safe file name step owned by this component.
    /// </summary>
    private static string SafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var normalized = new string(value.Where(character => !invalid.Contains(character)).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(normalized) ? "Haven Note" : normalized;
    }

    /// <summary>
    /// Performs the short hash step owned by this component.
    /// </summary>
    private static string ShortHash(string value) =>
        string.IsNullOrWhiteSpace(value) ? "not saved yet" : value[..Math.Min(16, value.Length)] + "…";

    /// <summary>
    /// Performs the dispose step owned by this component.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        AttachedToVisualTree -= OnAttachedToVisualTree;
        DetachedFromVisualTree -= OnDetachedFromVisualTree;
        _page.DocumentChanged -= OnDocumentChanged;
        _page.PropertyChanged -= OnViewModelPropertyChanged;
        _page.SearchNavigationRequested -= OnSearchNavigationRequested;
        foreach (var preview in this.GetVisualDescendants().OfType<NotesHtmlPreviewControl>()) preview.Dispose();
        _page.Dispose();
        Content = null;
        GC.SuppressFinalize(this);
    }
}
