using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Threading;
using Haven.Application;
using Haven.Core;
using Haven.Desktop;
using Haven.Desktop.Events;
using Haven.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Views.Pages.Notes;

using DomainNotesPage = Haven.Core.NotesPage;

public partial class NotesPage : UserControl, IDisposable, INotifyPropertyChanged
{
    public new event PropertyChangedEventHandler? PropertyChanged;

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private void RaisePropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    private static readonly JsonSerializerOptions SnapshotOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HavenEventBus _bus;
    private readonly INotesRepository _repository;
    private readonly INotesImportExportService _formats;
    private readonly INotesAiService _ai;
    private readonly INotesAttachmentStore _attachments;
    private readonly IOllamaClient _models;
    private readonly IProductionDiagnostics _diagnostics;
    private readonly DispatcherTimer _autosaveTimer;
    private readonly SemaphoreSlim _initializationGate = new(1, 1);
    private bool _isInitialized;
    private readonly Stack<string> _undo = new();
    private readonly Stack<string> _redo = new();
    private readonly Dictionary<Guid, string> _editSnapshots = [];
    private CancellationTokenSource? _aiCancellation;
    private NotesDocument? _document;
    private NotesSection? _section;
    private DomainNotesPage? _page;
    private NotesBlock? _selectedBlock;
    private NotesAiChange? _pendingAiChange;
    private NotesSearchHit? _selectedSearchHit;
    private NotesVersionInfo? _selectedVersion;
    private string _searchQuery = string.Empty;
    private string _aiInstruction = string.Empty;
    private string _selectedModelName = string.Empty;
    private string _status = "Notes is starting…";
    private bool _allowDocumentContext;
    private bool _isDirty;
    private bool _isBusy;
    private bool _isDeleteConfirming;
    private bool _disposed;
    private int _autosaveRunning;

    public NotesPage(
        HavenEventBus bus,
        INotesRepository repository,
        INotesImportExportService formats,
        INotesAiService ai,
        INotesAttachmentStore attachments,
        IOllamaClient models,
        IProductionDiagnostics diagnostics)
    {
        _bus = bus;
        _repository = repository;
        _formats = formats;
        _ai = ai;
        _attachments = attachments;
        _models = models;
        _diagnostics = diagnostics;
        _autosaveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _autosaveTimer.Tick += OnAutosaveTick;

        NewDocumentCommand = new AsyncRelayCommand(NewDocumentAsync, () => !IsBusy);
        SaveCommand = new AsyncRelayCommand(() => SaveAsync("Manual save"), () => Document is not null && IsDirty && !IsBusy);
        UndoCommand = new AsyncRelayCommand(UndoAsync, () => _undo.Count > 0 && !IsBusy);
        RedoCommand = new AsyncRelayCommand(RedoAsync, () => _redo.Count > 0 && !IsBusy);
        DeleteDocumentCommand = new AsyncRelayCommand(DeleteCurrentAsync, () => Document is not null && IsDeleteConfirming && !IsBusy);
        RequestDeleteDocumentCommand = new RelayCommand(() => IsDeleteConfirming = Document is not null);
        CancelDeleteDocumentCommand = new RelayCommand(() => IsDeleteConfirming = false);
        AddSectionCommand = new AsyncRelayCommand(AddSectionAsync, () => Document is not null);
        AddPageCommand = new AsyncRelayCommand(AddPageAsync, () => CurrentSection is not null);
        AddParagraphCommand = new AsyncRelayCommand(() => AddBlockAsync(NotesBlockKind.Paragraph), () => CurrentPage is not null);
        AddHeadingCommand = new AsyncRelayCommand(() => AddBlockAsync(NotesBlockKind.Heading), () => CurrentPage is not null);
        AddListCommand = new AsyncRelayCommand(() => AddBlockAsync(NotesBlockKind.List), () => CurrentPage is not null);
        AddTableCommand = new AsyncRelayCommand(() => AddBlockAsync(NotesBlockKind.Table), () => CurrentPage is not null);
        AddEquationCommand = new AsyncRelayCommand(() => AddBlockAsync(NotesBlockKind.Equation), () => CurrentPage is not null);
        AddHtmlCommand = new AsyncRelayCommand(() => AddBlockAsync(NotesBlockKind.HtmlWidget), () => CurrentPage is not null);
        AddCanvasCommand = new AsyncRelayCommand(() => AddBlockAsync(NotesBlockKind.Canvas), () => CurrentPage is not null);
        AddFlashcardCommand = new AsyncRelayCommand(() => AddBlockAsync(NotesBlockKind.Flashcard), () => CurrentPage is not null);
        DeleteBlockCommand = new AsyncRelayCommand<NotesBlock>(block => DeleteBlockAsync(block), block => block is not null && CurrentPage is not null);
        MoveBlockUpCommand = new AsyncRelayCommand<NotesBlock>(block => MoveBlockAsync(block, -1));
        MoveBlockDownCommand = new AsyncRelayCommand<NotesBlock>(block => MoveBlockAsync(block, 1));
        SearchCommand = new AsyncRelayCommand(SearchAsync, () => SearchQuery.Trim().Length >= 2 && !IsBusy);
        ClearSearchCommand = new RelayCommand(ClearSearch);
        RestoreVersionCommand = new AsyncRelayCommand(RestoreSelectedVersionAsync, () => SelectedVersion is not null && !IsBusy);
        ProposeAiCommand = new AsyncRelayCommand(ProposeAiAsync, () => Document is not null && !string.IsNullOrWhiteSpace(AiInstruction) && !string.IsNullOrWhiteSpace(SelectedModelName) && !IsBusy);
        CancelAiCommand = new RelayCommand(CancelAi);
        ApproveAiCommand = new AsyncRelayCommand(ApproveAiAsync, () => PendingAiChange?.Status == NotesAiChangeStatus.Proposed && !IsBusy);
        RejectAiCommand = new RelayCommand(RejectAi, () => PendingAiChange?.Status == NotesAiChangeStatus.Proposed);
        AddCommentCommand = new AsyncRelayCommand<string>(text => AddCommentAsync(SelectedBlock, text ?? string.Empty));
        ResolveCommentCommand = new AsyncRelayCommand<NotesComment>(comment => ResolveCommentAsync(comment));
        AddCitationCommand = new AsyncRelayCommand(AddCitationAsync, () => Document is not null);
        ReviewAgainCommand = new AsyncRelayCommand(() => ReviewSelectedFlashcardAsync(NotesFlashcardRating.Again));
        ReviewHardCommand = new AsyncRelayCommand(() => ReviewSelectedFlashcardAsync(NotesFlashcardRating.Hard));
        ReviewGoodCommand = new AsyncRelayCommand(() => ReviewSelectedFlashcardAsync(NotesFlashcardRating.Good));
        ReviewEasyCommand = new AsyncRelayCommand(() => ReviewSelectedFlashcardAsync(NotesFlashcardRating.Easy));

        InitializeComponent();
        WireEvents();
    }

    public event EventHandler? DocumentChanged;
    public event EventHandler<NotesSearchHit>? SearchNavigationRequested;

    public ObservableCollection<NotesDocumentSummary> Documents { get; } = [];
    public ObservableCollection<NotesSearchHit> SearchResults { get; } = [];
    public ObservableCollection<NotesVersionInfo> Versions { get; } = [];
    public ObservableCollection<string> Models { get; } = [];

    public NotesDocument? Document
    {
        get => _document;
        private set
        {
            if (!SetProperty(ref _document, value)) return;
            RaiseDocumentProperties();
        }
    }

    public NotesSection? CurrentSection
    {
        get => _section;
        set
        {
            if (!SetProperty(ref _section, value)) return;
            CurrentPage = value?.Pages.OrderBy(p => p.Order).FirstOrDefault();
            RaiseDocumentProperties();
            DocumentChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public DomainNotesPage? CurrentPage
    {
        get => _page;
        set
        {
            if (!SetProperty(ref _page, value)) return;
            SelectedBlock = value?.Blocks.OrderBy(b => b.Order).FirstOrDefault();
            RaiseDocumentProperties();
            DocumentChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public NotesBlock? SelectedBlock
    {
        get => _selectedBlock;
        set
        {
            if (!SetProperty(ref _selectedBlock, value)) return;
            RaisePropertyChanged(nameof(HasSelectedBlock));
            RaisePropertyChanged(nameof(SelectedBlockKind));
            RaisePropertyChanged(nameof(IsSelectedFlashcard));
            RaisePropertyChanged(nameof(IsSelectedCanvas));
            ApproveAiCommand.RaiseCanExecuteChanged();
            DocumentChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public NotesAiChange? PendingAiChange
    {
        get => _pendingAiChange;
        private set
        {
            if (!SetProperty(ref _pendingAiChange, value)) return;
            RaisePropertyChanged(nameof(HasPendingAiChange));
            ApproveAiCommand.RaiseCanExecuteChanged();
            RejectAiCommand.RaiseCanExecuteChanged();
        }
    }

    public NotesSearchHit? SelectedSearchHit
    {
        get => _selectedSearchHit;
        set
        {
            if (!SetProperty(ref _selectedSearchHit, value) || value is null) return;
            _ = NavigateToSearchHitAsync(value);
        }
    }

    public NotesVersionInfo? SelectedVersion
    {
        get => _selectedVersion;
        set
        {
            if (!SetProperty(ref _selectedVersion, value)) return;
            RestoreVersionCommand.RaiseCanExecuteChanged();
        }
    }

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (!SetProperty(ref _searchQuery, value)) return;
            SearchCommand.RaiseCanExecuteChanged();
        }
    }

    public string AiInstruction
    {
        get => _aiInstruction;
        set
        {
            if (!SetProperty(ref _aiInstruction, value)) return;
            ProposeAiCommand.RaiseCanExecuteChanged();
        }
    }

    public string SelectedModelName
    {
        get => _selectedModelName;
        set
        {
            if (!SetProperty(ref _selectedModelName, value)) return;
            ProposeAiCommand.RaiseCanExecuteChanged();
        }
    }

    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public bool AllowDocumentContext { get => _allowDocumentContext; set => SetProperty(ref _allowDocumentContext, value); }
    public bool IsDeleteConfirming { get => _isDeleteConfirming; set { if (SetProperty(ref _isDeleteConfirming, value)) DeleteDocumentCommand.RaiseCanExecuteChanged(); } }
    public bool IsDirty
    {
        get => _isDirty;
        private set
        {
            if (!SetProperty(ref _isDirty, value)) return;
            SaveCommand.RaiseCanExecuteChanged();
            RaisePropertyChanged(nameof(SaveState));
        }
    }
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value)) return;
            RaiseCommandStates();
        }
    }

    public IReadOnlyList<NotesSection> Sections => Document?.Sections ?? [];
    public IReadOnlyList<DomainNotesPage> Pages => CurrentSection?.Pages.OrderBy(p => p.Order).ToArray() ?? [];
    public IReadOnlyList<NotesBlock> Blocks => CurrentPage?.Blocks.OrderBy(b => b.Order).ToArray() ?? [];
    public IReadOnlyList<NotesComment> Comments => Document?.Comments ?? [];
    public IReadOnlyList<NotesCitation> Citations => Document?.Citations ?? [];
    public IReadOnlyList<NotesAiChange> AiHistory => Document?.AiChanges ?? [];
    public bool HasDocument => Document is not null;
    public bool HasSelectedBlock => SelectedBlock is not null;
    public bool HasPendingAiChange => PendingAiChange is not null;
    public bool IsSelectedFlashcard => SelectedBlock?.Flashcard is not null;
    public bool IsSelectedCanvas => SelectedBlock?.Canvas is not null;
    public string SelectedBlockKind => SelectedBlock?.Kind.ToString() ?? "None";
    public string SaveState => IsDirty ? "Unsaved changes" : Document is null ? "No document" : $"Saved · v{Document.Version}";
    public NotesStatistics Statistics => Document is null ? new NotesStatistics(0, 0, 0, 0, 0) : NotesTextStatistics.Calculate(Document);
    public string StatisticsLabel => $"{Statistics.Words:N0} words · {Statistics.Characters:N0} characters · {Statistics.ReadingMinutes} min read";
    public IEnumerable<NotesFlashcardData> DueFlashcards => Document is null
        ? []
        : Document.Sections.SelectMany(s => s.Pages).SelectMany(p => p.Blocks).Where(b => b.Flashcard is not null).Select(b => b.Flashcard!).Where(c => c.Schedule.DueAt <= DateTimeOffset.UtcNow);

    public AsyncRelayCommand NewDocumentCommand { get; }
    public AsyncRelayCommand SaveCommand { get; }
    public AsyncRelayCommand UndoCommand { get; }
    public AsyncRelayCommand RedoCommand { get; }
    public RelayCommand RequestDeleteDocumentCommand { get; }
    public AsyncRelayCommand DeleteDocumentCommand { get; }
    public RelayCommand CancelDeleteDocumentCommand { get; }
    public AsyncRelayCommand AddSectionCommand { get; }
    public AsyncRelayCommand AddPageCommand { get; }
    public AsyncRelayCommand AddParagraphCommand { get; }
    public AsyncRelayCommand AddHeadingCommand { get; }
    public AsyncRelayCommand AddListCommand { get; }
    public AsyncRelayCommand AddTableCommand { get; }
    public AsyncRelayCommand AddEquationCommand { get; }
    public AsyncRelayCommand AddHtmlCommand { get; }
    public AsyncRelayCommand AddCanvasCommand { get; }
    public AsyncRelayCommand AddFlashcardCommand { get; }
    public AsyncRelayCommand<NotesBlock> DeleteBlockCommand { get; }
    public AsyncRelayCommand<NotesBlock> MoveBlockUpCommand { get; }
    public AsyncRelayCommand<NotesBlock> MoveBlockDownCommand { get; }
    public AsyncRelayCommand SearchCommand { get; }
    public RelayCommand ClearSearchCommand { get; }
    public AsyncRelayCommand RestoreVersionCommand { get; }
    public AsyncRelayCommand ProposeAiCommand { get; }
    public RelayCommand CancelAiCommand { get; }
    public AsyncRelayCommand ApproveAiCommand { get; }
    public RelayCommand RejectAiCommand { get; }
    public AsyncRelayCommand<string> AddCommentCommand { get; }
    public AsyncRelayCommand<NotesComment> ResolveCommentCommand { get; }
    public AsyncRelayCommand AddCitationCommand { get; }
    public AsyncRelayCommand ReviewAgainCommand { get; }
    public AsyncRelayCommand ReviewHardCommand { get; }
    public AsyncRelayCommand ReviewGoodCommand { get; }
    public AsyncRelayCommand ReviewEasyCommand { get; }

    private void WireEvents()
    {
        if (Content is not Control control) return;
        _bus.RegisterElement("Notes.Workspace", control);
        _bus.WirePointerEvents("Notes.Workspace", control);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(NotesPage));
        await _initializationGate.WaitAsync(cancellationToken);
        try
        {
            if (_isInitialized) return;
            IsBusy = true;
            await RefreshDocumentsAsync(cancellationToken);
            await RefreshModelsAsync(cancellationToken);
            var first = Documents.FirstOrDefault();
            if (first is null) await NewDocumentAsync();
            else await OpenDocumentAsync(first.Id, cancellationToken);
            _autosaveTimer.Start();
            Status = "Haven Notes is ready. Changes autosave locally.";
            _isInitialized = true;
        }
        finally
        {
            IsBusy = false;
            _initializationGate.Release();
        }
    }

    public async Task RefreshDocumentsAsync(CancellationToken cancellationToken)
    {
        var documents = await _repository.ListAsync(cancellationToken);
        Documents.Clear();
        foreach (var document in documents) Documents.Add(document);
    }

    public async Task OpenDocumentAsync(Guid documentId, CancellationToken cancellationToken)
    {
        if (IsDirty) await SaveAsync("Autosave before switching document");
        var document = await _repository.LoadAsync(documentId, cancellationToken)
                       ?? throw new FileNotFoundException("The selected Notes document no longer exists.");
        SetDocument(document);
        Status = document.Recovery.HasUnsavedRecovery
            ? "Recovered the last valid version. Review and save it to confirm recovery."
            : "Opened " + document.Title;
    }

    public void SelectSection(NotesSection section)
    {
        ArgumentNullException.ThrowIfNull(section);
        if (Document?.Sections.Contains(section) == true) CurrentSection = section;
    }

    public void SelectPage(DomainNotesPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (CurrentSection?.Pages.Contains(page) == true) CurrentPage = page;
    }

    public async Task BeginBlockEditAsync(NotesBlock block)
    {
        ArgumentNullException.ThrowIfNull(block);
        if (Document is null || _editSnapshots.ContainsKey(block.Id)) return;
        _editSnapshots[block.Id] = await SnapshotAsync(Document);
    }

    public async Task CommitBlockEditAsync(NotesBlock block, string summary)
    {
        ArgumentNullException.ThrowIfNull(block);
        if (Document is null || !_editSnapshots.Remove(block.Id, out var before)) return;
        var after = await SnapshotAsync(Document);
        if (string.Equals(before, after, StringComparison.Ordinal)) return;
        PushUndo(before);
        Document.Revisions.Add(new NotesRevision
        {
            Kind = NotesRevisionKind.Edited,
            BlockId = block.Id,
            Summary = string.IsNullOrWhiteSpace(summary) ? "Edited " + block.Kind : summary.Trim(),
            CreatedAt = DateTimeOffset.UtcNow,
            Author = Environment.UserName
        });
        MarkDirty();
    }

    public void CancelBlockEdit(NotesBlock block)
    {
        ArgumentNullException.ThrowIfNull(block);
        _editSnapshots.Remove(block.Id);
    }

    public void UpdateBlockText(NotesBlock block, string text)
    {
        ArgumentNullException.ThrowIfNull(block);
        block.PlainText = text ?? string.Empty;
        MarkDirtyWithoutRevision();
    }

    public async Task UpdateEquationAsync(NotesBlock block, string source, NotesEquationViewMode mode, string accessibleAlternative)
    {
        if (block.Equation is null) return;
        await BeginBlockEditAsync(block);
        block.Equation.Source = source ?? string.Empty;
        block.Equation.ViewMode = mode;
        block.Equation.AccessibleAlternative = accessibleAlternative?.Trim() ?? string.Empty;
        var rendered = NotesEquationRenderer.Render(block.Equation.Source);
        block.Equation.RenderedText = rendered.RenderedText;
        block.Equation.Error = rendered.Error;
        await CommitBlockEditAsync(block, "Edited equation");
    }

    public async Task UpdateHtmlAsync(
        NotesBlock block,
        string html,
        string css,
        string javascript,
        bool allowScripts,
        bool allowNetwork,
        bool allowForms,
        NotesHtmlViewMode mode)
    {
        if (block.Html is null) return;
        await BeginBlockEditAsync(block);
        block.Html.HtmlSource = html ?? string.Empty;
        block.Html.CssSource = css ?? string.Empty;
        block.Html.JavaScriptSource = javascript ?? string.Empty;
        block.Html.AllowScripts = allowScripts;
        block.Html.AllowNetwork = allowNetwork;
        block.Html.AllowForms = allowForms;
        block.Html.AllowPopups = false;
        block.Html.ViewMode = mode;
        var security = NotesHtmlSandbox.Build(block.Html);
        block.Html.LastSecurityError = security.Error;
        block.Html.FallbackText = security.FallbackText;
        await CommitBlockEditAsync(block, "Edited sandboxed HTML widget");
    }

    public async Task AddInkStrokeAsync(NotesBlock block, NotesInkStroke stroke)
    {
        if (block.Canvas is null) return;
        ArgumentNullException.ThrowIfNull(stroke);
        PushUndo(await SnapshotRequiredAsync());
        block.Canvas.Strokes.Add(stroke);
        Document!.Revisions.Add(new NotesRevision { Kind = NotesRevisionKind.Edited, BlockId = block.Id, Summary = "Added editable ink stroke" });
        MarkDirty();
    }

    public async Task RemoveInkStrokeAsync(NotesBlock block, Guid strokeId)
    {
        if (block.Canvas is null) return;
        var stroke = block.Canvas.Strokes.FirstOrDefault(item => item.Id == strokeId);
        if (stroke is null) return;
        PushUndo(await SnapshotRequiredAsync());
        block.Canvas.Strokes.Remove(stroke);
        foreach (var layer in block.Canvas.GhostLayers) layer.StrokeIds.Remove(strokeId);
        MarkDirty();
    }

    public async Task<NotesGhostLayer> AddGhostLayerAsync(NotesBlock block, string name, NotesGhostRevealMode revealMode)
    {
        if (block.Canvas is null) throw new InvalidOperationException("Select a canvas block before adding Ghost Pen content.");
        PushUndo(await SnapshotRequiredAsync());
        var layer = new NotesGhostLayer
        {
            Name = string.IsNullOrWhiteSpace(name) ? "Answer" : name.Trim(),
            RevealMode = revealMode
        };
        block.Canvas.GhostLayers.Add(layer);
        MarkDirty();
        return layer;
    }

    public async Task AssignStrokeToGhostLayerAsync(NotesBlock block, NotesInkStroke stroke, NotesGhostLayer layer)
    {
        if (block.Canvas is null || !block.Canvas.Strokes.Contains(stroke) || !block.Canvas.GhostLayers.Contains(layer)) return;
        PushUndo(await SnapshotRequiredAsync());
        stroke.IsGhost = true;
        stroke.GhostLayerId = layer.Id;
        if (!layer.StrokeIds.Contains(stroke.Id)) layer.StrokeIds.Add(stroke.Id);
        MarkDirty();
    }

    public void ToggleGhostLayer(NotesBlock block, NotesGhostLayer layer)
    {
        if (block.Canvas is null || !block.Canvas.GhostLayers.Contains(layer)) return;
        layer.IsRevealed = !layer.IsRevealed;
        DocumentChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task AddOcclusionMaskAsync(NotesBlock block, double x, double y, double width, double height, string answer)
    {
        var masks = block.Flashcard?.OcclusionMasks ?? block.Canvas?.GhostLayers.FirstOrDefault()?.Masks;
        if (masks is null) return;
        PushUndo(await SnapshotRequiredAsync());
        masks.Add(new NotesOcclusionMask { X = x, Y = y, Width = Math.Max(10, width), Height = Math.Max(10, height), Answer = answer ?? string.Empty });
        MarkDirty();
    }

    public async Task AddTableRowAsync(NotesBlock block)
    {
        if (block.Table is null) return;
        PushUndo(await SnapshotRequiredAsync());
        var columns = Math.Max(1, block.Table.Rows.FirstOrDefault()?.Cells.Count ?? 1);
        var row = new NotesTableRow();
        for (var index = 0; index < columns; index++) row.Cells.Add(new NotesTableCell());
        block.Table.Rows.Add(row);
        MarkDirty();
    }

    public async Task AddTableColumnAsync(NotesBlock block)
    {
        if (block.Table is null) return;
        PushUndo(await SnapshotRequiredAsync());
        foreach (var row in block.Table.Rows) row.Cells.Add(new NotesTableCell());
        MarkDirty();
    }

    public async Task RemoveTableRowAsync(NotesBlock block)
    {
        var table = block.Table;
        if (table is null || table.Rows.Count <= 1) return;
        PushUndo(await SnapshotRequiredAsync());
        table.Rows.RemoveAt(table.Rows.Count - 1);
        MarkDirty();
    }

    public async Task RemoveTableColumnAsync(NotesBlock block)
    {
        if (block.Table is null || block.Table.Rows.Any(row => row.Cells.Count <= 1)) return;
        PushUndo(await SnapshotRequiredAsync());
        foreach (var row in block.Table.Rows) row.Cells.RemoveAt(row.Cells.Count - 1);
        MarkDirty();
    }

    public async Task<NotesMediaData> ImportMediaAsync(string sourcePath, CancellationToken cancellationToken)
    {
        var media = await _attachments.ImportAsync(sourcePath, cancellationToken);
        PushUndo(await SnapshotRequiredAsync());
        var kind = media.MediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
            ? NotesBlockKind.Image
            : media.MediaType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase)
                ? NotesBlockKind.Audio
                : NotesBlockKind.Video;
        var block = new NotesBlock { Kind = kind, Media = media, Order = CurrentPage!.Blocks.Count };
        CurrentPage.Blocks.Add(block);
        SelectedBlock = block;
        MarkDirty();
        return media;
    }

    public async Task ImportDocumentAsync(string sourcePath, CancellationToken cancellationToken)
    {
        IsBusy = true;
        try
        {
            var imported = await _formats.ImportAsync(sourcePath, cancellationToken);
            var save = await _repository.SaveAsync(imported, "Imported " + Path.GetFileName(sourcePath), cancellationToken);
            imported.Version = save.Version;
            await RefreshDocumentsAsync(cancellationToken);
            SetDocument(imported);
            Status = "Imported " + Path.GetFileName(sourcePath);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ExportDocumentAsync(string destinationPath, CancellationToken cancellationToken)
    {
        if (Document is null) return;
        if (IsDirty) await SaveAsync("Save before export");
        IsBusy = true;
        try
        {
            await _formats.ExportAsync(Document, destinationPath, cancellationToken);
            Status = "Exported " + Path.GetFileName(destinationPath);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task PrintAsync(CancellationToken cancellationToken)
    {
        if (Document is null) return;
        if (IsDirty) await SaveAsync("Save before printing");
        IsBusy = true;
        try
        {
            await _formats.PrintAsync(Document, cancellationToken);
            Status = "Sent a print-ready PDF to Windows.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task NewDocumentAsync()
    {
        IsBusy = true;
        try
        {
            if (IsDirty) await SaveAsync("Autosave before creating document");
            var document = NotesDocument.Create("Untitled note");
            var result = await _repository.SaveAsync(document, "Document created", CancellationToken.None);
            document.Version = result.Version;
            await RefreshDocumentsAsync(CancellationToken.None);
            SetDocument(document);
            Status = "Created a new local Notes document.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SaveAsync(string reason)
    {
        if (Document is null || !IsDirty && !Document.Recovery.HasUnsavedRecovery) return;
        if (Interlocked.Exchange(ref _autosaveRunning, 1) != 0) return;
        try
        {
            var result = await _repository.SaveAsync(Document, reason, CancellationToken.None);
            Document.Version = result.Version;
            Document.Recovery.HasUnsavedRecovery = false;
            IsDirty = false;
            Status = $"Saved locally at {result.SavedAt.LocalDateTime:t} · v{result.Version}";
            await RefreshDocumentsAsync(CancellationToken.None);
            await RefreshVersionsAsync(CancellationToken.None);
            RaiseDocumentProperties();
        }
        catch (Exception ex)
        {
            IsDirty = true;
            Status = "Save failed; the last valid version remains intact. " + ex.Message;
            await _diagnostics.WriteAsync(
                ReliabilitySeverity.Error,
                "notes",
                "autosave-failed",
                "Haven Notes could not complete an atomic save.",
                new Dictionary<string, string> { ["exceptionType"] = ex.GetType().Name },
                cancellationToken: CancellationToken.None);
        }
        finally
        {
            Interlocked.Exchange(ref _autosaveRunning, 0);
        }
    }

    private async Task DeleteCurrentAsync()
    {
        if (Document is null) return;
        IsBusy = true;
        try
        {
            var id = Document.Id;
            await _repository.DeleteAsync(id, CancellationToken.None);
            IsDeleteConfirming = false;
            await RefreshDocumentsAsync(CancellationToken.None);
            var next = Documents.FirstOrDefault();
            if (next is null) await NewDocumentAsync();
            else await OpenDocumentAsync(next.Id, CancellationToken.None);
            Status = "Moved the document to recoverable Notes trash.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task AddSectionAsync()
    {
        if (Document is null) return;
        PushUndo(await SnapshotRequiredAsync());
        var section = new NotesSection { Title = "Section " + (Document.Sections.Count + 1) };
        Document.Sections.Add(section);
        CurrentSection = section;
        MarkDirty();
    }

    private async Task AddPageAsync()
    {
        if (CurrentSection is null) return;
        PushUndo(await SnapshotRequiredAsync());
        var pg = new DomainNotesPage { Title = "Page " + (CurrentSection.Pages.Count + 1), Order = CurrentSection.Pages.Count };
        CurrentSection.Pages.Add(pg);
        CurrentPage = pg;
        MarkDirty();
    }

    private async Task AddBlockAsync(NotesBlockKind kind)
    {
        if (CurrentPage is null) return;
        PushUndo(await SnapshotRequiredAsync());
        NotesBlock block = kind switch
        {
            NotesBlockKind.Heading => NotesBlock.Heading(),
            NotesBlockKind.List => new NotesBlock { Kind = NotesBlockKind.List, List = new NotesListData { Kind = NotesListKind.Bulleted, Items = [new NotesListItem { Text = "List item" }] } },
            NotesBlockKind.Table => NotesBlock.TableBlock(),
            NotesBlockKind.Equation => NotesBlock.EquationBlock(),
            NotesBlockKind.HtmlWidget => NotesBlock.HtmlBlock(),
            NotesBlockKind.Canvas => NotesBlock.CanvasBlock(),
            NotesBlockKind.Flashcard => NotesBlock.FlashcardBlock(),
            _ => NotesBlock.CreateParagraph()
        };
        block.Order = CurrentPage.Blocks.Count;
        CurrentPage.Blocks.Add(block);
        SelectedBlock = block;
        MarkDirty();
    }

    private async Task DeleteBlockAsync(NotesBlock? block)
    {
        if (CurrentPage is null || block is null || !CurrentPage.Blocks.Contains(block)) return;
        PushUndo(await SnapshotRequiredAsync());
        CurrentPage.Blocks.Remove(block);
        NormalizeBlockOrder();
        if (CurrentPage.Blocks.Count == 0) CurrentPage.Blocks.Add(NotesBlock.CreateParagraph());
        SelectedBlock = CurrentPage.Blocks.OrderBy(item => item.Order).FirstOrDefault();
        MarkDirty();
    }

    private async Task MoveBlockAsync(NotesBlock? block, int delta)
    {
        if (CurrentPage is null || block is null) return;
        var ordered = CurrentPage.Blocks.OrderBy(item => item.Order).ToList();
        var index = ordered.IndexOf(block);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= ordered.Count) return;
        PushUndo(await SnapshotRequiredAsync());
        (ordered[index], ordered[target]) = (ordered[target], ordered[index]);
        CurrentPage.Blocks = ordered;
        NormalizeBlockOrder();
        MarkDirty();
    }

    private async Task SearchAsync()
    {
        IsBusy = true;
        try
        {
            var hits = await _repository.SearchAsync(SearchQuery, CancellationToken.None);
            SearchResults.Clear();
            foreach (var hit in hits) SearchResults.Add(hit);
            Status = hits.Count == 0 ? "No Notes results found." : $"Found {hits.Count} matching block{(hits.Count == 1 ? string.Empty : "s")}.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ClearSearch()
    {
        SearchQuery = string.Empty;
        SearchResults.Clear();
        SelectedSearchHit = null;
    }

    private async Task NavigateToSearchHitAsync(NotesSearchHit hit)
    {
        if (Document?.Id != hit.DocumentId) await OpenDocumentAsync(hit.DocumentId, CancellationToken.None);
        if (Document is null) return;
        var section = Document.Sections.FirstOrDefault(item => item.Id == hit.SectionId);
        var pg = section?.Pages.FirstOrDefault(item => item.Id == hit.PageId);
        var block = pg?.Blocks.FirstOrDefault(item => item.Id == hit.BlockId);
        if (section is null || pg is null || block is null) return;
        CurrentSection = section;
        CurrentPage = pg;
        SelectedBlock = block;
        SearchNavigationRequested?.Invoke(this, hit);
    }

    private async Task RefreshVersionsAsync(CancellationToken cancellationToken)
    {
        Versions.Clear();
        if (Document is null) return;
        foreach (var version in await _repository.GetVersionsAsync(Document.Id, cancellationToken)) Versions.Add(version);
        SelectedVersion = Versions.FirstOrDefault();
    }

    private async Task RestoreSelectedVersionAsync()
    {
        if (Document is null || SelectedVersion is null) return;
        IsBusy = true;
        try
        {
            var restored = await _repository.LoadVersionAsync(Document.Id, SelectedVersion.VersionId, CancellationToken.None)
                           ?? throw new FileNotFoundException("The selected Notes version no longer exists.");
            PushUndo(await SnapshotRequiredAsync());
            restored.Version = Document.Version;
            restored.Revisions.Add(new NotesRevision { Kind = NotesRevisionKind.Restored, Summary = "Restored version " + SelectedVersion.Version, Author = Environment.UserName });
            SetDocument(restored);
            IsDirty = true;
            await SaveAsync("Restored version " + SelectedVersion.Version);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ProposeAiAsync()
    {
        if (Document is null) return;
        CancelAi();
        _aiCancellation = new CancellationTokenSource();
        IsBusy = true;
        try
        {
            var selectedText = SelectedBlock?.PlainText
                               ?? SelectedBlock?.Equation?.Source
                               ?? SelectedBlock?.Flashcard?.Front
                               ?? string.Empty;
            var context = string.Join("\n", NotesTextStatistics.EnumerateText(Document));
            var result = await _ai.ProposeAsync(new NotesAiProposalRequest(
                Document.Id,
                SelectedBlock?.Id,
                AiInstruction,
                selectedText,
                context,
                SelectedModelName,
                AllowDocumentContext,
                Document.Citations), _aiCancellation.Token);
            var change = new NotesAiChange
            {
                BlockId = SelectedBlock?.Id,
                Instruction = AiInstruction.Trim(),
                OriginalContent = selectedText,
                ProposedContent = result.ProposedContent,
                Explanation = result.Explanation,
                CitationIds = result.CitationIds.ToList(),
                ProviderId = result.ProviderId,
                ModelName = result.ModelName,
                Status = NotesAiChangeStatus.Proposed,
                UserConsentRecorded = AllowDocumentContext || !string.IsNullOrWhiteSpace(selectedText),
                SentDocumentContext = AllowDocumentContext
            };
            Document.AiChanges.Add(change);
            PendingAiChange = change;
            MarkDirtyWithoutRevision();
            Status = "AI proposal ready for review. Nothing has been applied.";
        }
        catch (OperationCanceledException)
        {
            Status = "AI proposal cancelled. Nothing changed.";
        }
        catch (Exception ex)
        {
            Status = "AI proposal failed: " + ex.Message;
        }
        finally
        {
            _aiCancellation?.Dispose();
            _aiCancellation = null;
            IsBusy = false;
        }
    }

    private async Task ApproveAiAsync()
    {
        if (Document is null || PendingAiChange?.Status != NotesAiChangeStatus.Proposed) return;
        PushUndo(await SnapshotRequiredAsync());
        var block = PendingAiChange.BlockId is { } blockId
            ? Document.Sections.SelectMany(s => s.Pages).SelectMany(p => p.Blocks).FirstOrDefault(item => item.Id == blockId)
            : null;
        if (block is null)
        {
            block = NotesBlock.CreateParagraph(PendingAiChange.ProposedContent);
            block.Order = CurrentPage?.Blocks.Count ?? 0;
            CurrentPage?.Blocks.Add(block);
        }
        else if (block.Equation is not null)
        {
            block.Equation.Source = PendingAiChange.ProposedContent;
            var rendered = NotesEquationRenderer.Render(block.Equation.Source);
            block.Equation.RenderedText = rendered.RenderedText;
            block.Equation.Error = rendered.Error;
        }
        else if (block.Flashcard is not null)
        {
            block.Flashcard.Back = PendingAiChange.ProposedContent;
        }
        else
        {
            block.PlainText = PendingAiChange.ProposedContent;
        }
        PendingAiChange.Status = NotesAiChangeStatus.Applied;
        PendingAiChange.ReviewedAt = DateTimeOffset.UtcNow;
        PendingAiChange.ReviewedBy = Environment.UserName;
        Document.Revisions.Add(new NotesRevision
        {
            Kind = NotesRevisionKind.AiApplied,
            BlockId = block.Id,
            Summary = "Applied reviewed AI proposal: " + PendingAiChange.Instruction,
            Author = Environment.UserName
        });
        SelectedBlock = block;
        PendingAiChange = null;
        MarkDirty();
        await SaveAsync("Applied reviewed AI proposal");
        Status = "Reviewed AI proposal applied and versioned.";
    }

    private void RejectAi()
    {
        if (PendingAiChange?.Status != NotesAiChangeStatus.Proposed) return;
        PendingAiChange.Status = NotesAiChangeStatus.Rejected;
        PendingAiChange.ReviewedAt = DateTimeOffset.UtcNow;
        PendingAiChange.ReviewedBy = Environment.UserName;
        PendingAiChange = null;
        MarkDirtyWithoutRevision();
        Status = "AI proposal rejected. Document content was unchanged.";
    }

    private void CancelAi() => _aiCancellation?.Cancel();

    private async Task AddCommentAsync(NotesBlock? block, string text)
    {
        if (Document is null || block is null || string.IsNullOrWhiteSpace(text)) return;
        PushUndo(await SnapshotRequiredAsync());
        Document.Comments.Add(new NotesComment { BlockId = block.Id, StartOffset = 0, EndOffset = block.PlainText.Length, Text = text.Trim() });
        MarkDirty();
    }

    private async Task ResolveCommentAsync(NotesComment? comment)
    {
        if (comment is null || comment.State == NotesCommentState.Resolved) return;
        PushUndo(await SnapshotRequiredAsync());
        comment.State = NotesCommentState.Resolved;
        comment.ResolvedAt = DateTimeOffset.UtcNow;
        MarkDirty();
    }

    private async Task AddCitationAsync()
    {
        if (Document is null) return;
        PushUndo(await SnapshotRequiredAsync());
        Document.Citations.Add(new NotesCitation { Key = "source-" + (Document.Citations.Count + 1), Title = "New source", AccessedAt = DateTimeOffset.UtcNow });
        MarkDirty();
    }

    private async Task ReviewSelectedFlashcardAsync(NotesFlashcardRating rating)
    {
        if (Document is null || SelectedBlock?.Flashcard is not { } card) return;
        PushUndo(await SnapshotRequiredAsync());
        var review = NotesFlashcardScheduler.Review(card, rating, rating switch
        {
            NotesFlashcardRating.Again => 0.15,
            NotesFlashcardRating.Hard => 0.45,
            NotesFlashcardRating.Good => 0.75,
            _ => 0.95
        }, TimeSpan.Zero, DateTimeOffset.UtcNow);
        Document.FlashcardReviews.Add(review);
        MarkDirty();
        Status = $"Card scheduled for {card.Schedule.DueAt.LocalDateTime:g}.";
    }

    private Task UndoAsync()
    {
        if (Document is null || _undo.Count == 0) return Task.CompletedTask;
        // A Notes undo is an in-memory state transition. Complete it before the
        // command returns so keyboard/menu callers observe one atomic edit.
        _redo.Push(Snapshot(Document));
        SetDocument(Deserialize(_undo.Pop()), resetHistory: false);
        IsDirty = true;
        Status = "Undid the last Notes edit.";
        RaiseCommandStates();
        return Task.CompletedTask;
    }

    private Task RedoAsync()
    {
        if (Document is null || _redo.Count == 0) return Task.CompletedTask;
        _undo.Push(Snapshot(Document));
        SetDocument(Deserialize(_redo.Pop()), resetHistory: false);
        IsDirty = true;
        Status = "Redid the Notes edit.";
        RaiseCommandStates();
        return Task.CompletedTask;
    }

    private void SetDocument(NotesDocument document, bool resetHistory = true)
    {
        Document = document;
        CurrentSection = document.Sections.FirstOrDefault();
        CurrentPage = CurrentSection?.Pages.OrderBy(p => p.Order).FirstOrDefault();
        SelectedBlock = CurrentPage?.Blocks.OrderBy(b => b.Order).FirstOrDefault();
        PendingAiChange = document.AiChanges.LastOrDefault(c => c.Status == NotesAiChangeStatus.Proposed);
        if (resetHistory)
        {
            _undo.Clear();
            _redo.Clear();
        }
        _editSnapshots.Clear();
        IsDirty = document.Recovery.HasUnsavedRecovery;
        _ = RefreshVersionsAsync(CancellationToken.None);
        RaiseCommandStates();
        DocumentChanged?.Invoke(this, EventArgs.Empty);
    }

    private void PushUndo(string snapshot)
    {
        _undo.Push(snapshot);
        while (_undo.Count > 100)
        {
            var keep = _undo.Reverse().TakeLast(100).Reverse().ToArray();
            _undo.Clear();
            foreach (var value in keep) _undo.Push(value);
        }
        _redo.Clear();
        RaiseCommandStates();
    }

    private async Task<string> SnapshotRequiredAsync() => Document is null ? throw new InvalidOperationException("No Notes document is open.") : await SnapshotAsync(Document);
    private static string Snapshot(NotesDocument document) => JsonSerializer.Serialize(document, SnapshotOptions);
    private static NotesDocument Deserialize(string snapshot) => JsonSerializer.Deserialize<NotesDocument>(snapshot, SnapshotOptions) ?? throw new InvalidDataException("The Notes edit snapshot was invalid.");
    private static async Task<string> SnapshotAsync(NotesDocument document) => await ParallelHelper.RunOnBackground(() => Snapshot(document));
    private static async Task<NotesDocument> DeserializeAsync(string snapshot) => await ParallelHelper.RunOnBackground(() => Deserialize(snapshot));

    private void MarkDirty()
    {
        IsDirty = true;
        Document!.UpdatedAt = DateTimeOffset.UtcNow;
        RaiseDocumentProperties();
        DocumentChanged?.Invoke(this, EventArgs.Empty);
    }

    private void MarkDirtyWithoutRevision()
    {
        IsDirty = true;
        if (Document is not null) Document.UpdatedAt = DateTimeOffset.UtcNow;
        RaiseDocumentProperties();
        DocumentChanged?.Invoke(this, EventArgs.Empty);
    }

    private void NormalizeBlockOrder()
    {
        if (CurrentPage is null) return;
        for (var index = 0; index < CurrentPage.Blocks.Count; index++) CurrentPage.Blocks[index].Order = index;
    }

    private async Task RefreshModelsAsync(CancellationToken cancellationToken)
    {
        Models.Clear();
        try
        {
            foreach (var model in await _models.GetModelsAsync(cancellationToken)) Models.Add(model.Name);
            SelectedModelName = Models.FirstOrDefault() ?? string.Empty;
        }
        catch (Exception ex)
        {
            Status = "Models could not be loaded for Notes AI: " + ex.Message;
        }
    }

    private void RaiseDocumentProperties()
    {
        RaisePropertyChanged(nameof(Sections));
        RaisePropertyChanged(nameof(Pages));
        RaisePropertyChanged(nameof(Blocks));
        RaisePropertyChanged(nameof(Comments));
        RaisePropertyChanged(nameof(Citations));
        RaisePropertyChanged(nameof(AiHistory));
        RaisePropertyChanged(nameof(HasDocument));
        RaisePropertyChanged(nameof(Statistics));
        RaisePropertyChanged(nameof(StatisticsLabel));
        RaisePropertyChanged(nameof(DueFlashcards));
        RaisePropertyChanged(nameof(SaveState));
        AddSectionCommand.RaiseCanExecuteChanged();
        AddPageCommand.RaiseCanExecuteChanged();
        AddParagraphCommand.RaiseCanExecuteChanged();
        AddHeadingCommand.RaiseCanExecuteChanged();
        AddListCommand.RaiseCanExecuteChanged();
        AddTableCommand.RaiseCanExecuteChanged();
        AddEquationCommand.RaiseCanExecuteChanged();
        AddHtmlCommand.RaiseCanExecuteChanged();
        AddCanvasCommand.RaiseCanExecuteChanged();
        AddFlashcardCommand.RaiseCanExecuteChanged();
        ProposeAiCommand.RaiseCanExecuteChanged();
    }

    private void RaiseCommandStates()
    {
        NewDocumentCommand.RaiseCanExecuteChanged();
        SaveCommand.RaiseCanExecuteChanged();
        UndoCommand.RaiseCanExecuteChanged();
        RedoCommand.RaiseCanExecuteChanged();
        DeleteDocumentCommand.RaiseCanExecuteChanged();
        SearchCommand.RaiseCanExecuteChanged();
        RestoreVersionCommand.RaiseCanExecuteChanged();
        ProposeAiCommand.RaiseCanExecuteChanged();
        ApproveAiCommand.RaiseCanExecuteChanged();
    }

    private async void OnAutosaveTick(object? sender, EventArgs e)
    {
        if (_disposed || !IsDirty || IsBusy || Document is null) return;
        await SaveAsync("Autosave");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _autosaveTimer.Stop();
        _autosaveTimer.Tick -= OnAutosaveTick;
        _aiCancellation?.Cancel();
        _aiCancellation?.Dispose();
        _aiCancellation = null;
        DocumentChanged = null;
        SearchNavigationRequested = null;
        _bus.UnregisterElement("Notes.Workspace");
        GC.SuppressFinalize(this);
    }
}

