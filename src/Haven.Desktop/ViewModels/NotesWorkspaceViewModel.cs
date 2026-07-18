/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/ViewModels/NotesWorkspaceViewModel.cs, in the Desktop presentation-model layer, exposing bindable state and commands to Avalonia views.
 * What: This file owns NotesWorkspaceViewModel, NotesEquationRenderResult, NotesEquationRenderer, NotesHtmlSandboxResult, NotesHtmlSandbox. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Keeping UI state here makes the XAML declarative and keeps behavior testable without recreating the full window.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using Avalonia.Threading;
using Haven.Application;
using Haven.Core;

namespace Haven.Desktop.ViewModels;

/// <summary>
/// Represents notes workspace view model and keeps its related state and behavior together.
/// </summary>
public sealed class NotesWorkspaceViewModel : ObservableObject, IDisposable
{
    /// <summary>
    /// Stores snapshot options locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly JsonSerializerOptions SnapshotOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Stores repository locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly INotesRepository _repository;
    /// <summary>
    /// Stores formats locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly INotesImportExportService _formats;
    /// <summary>
    /// Stores ai locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly INotesAiService _ai;
    /// <summary>
    /// Stores attachments locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly INotesAttachmentStore _attachments;
    /// <summary>
    /// Stores models locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IOllamaClient _models;
    /// <summary>
    /// Stores diagnostics locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IProductionDiagnostics _diagnostics;
    /// <summary>
    /// Stores autosave timer locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly DispatcherTimer _autosaveTimer;
    /// <summary>
    /// Stores undo locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly Stack<string> _undo = new();
    /// <summary>
    /// Stores redo locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly Stack<string> _redo = new();
    /// <summary>
    /// Stores edit snapshots locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly Dictionary<Guid, string> _editSnapshots = [];
    /// <summary>
    /// Stores ai cancellation locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private CancellationTokenSource? _aiCancellation;
    /// <summary>
    /// Stores document locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private NotesDocument? _document;
    /// <summary>
    /// Stores section locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private NotesSection? _section;
    /// <summary>
    /// Stores page locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private NotesPage? _page;
    /// <summary>
    /// Stores selected block locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private NotesBlock? _selectedBlock;
    /// <summary>
    /// Stores pending ai change locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private NotesAiChange? _pendingAiChange;
    /// <summary>
    /// Stores selected search hit locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private NotesSearchHit? _selectedSearchHit;
    /// <summary>
    /// Stores selected version locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private NotesVersionInfo? _selectedVersion;
    /// <summary>
    /// Stores search query locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _searchQuery = string.Empty;
    /// <summary>
    /// Stores ai instruction locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _aiInstruction = string.Empty;
    /// <summary>
    /// Stores selected model name locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _selectedModelName = string.Empty;
    /// <summary>
    /// Stores status locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private string _status = "Notes is starting…";
    /// <summary>
    /// Stores allow document context locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _allowDocumentContext;
    /// <summary>
    /// Stores is dirty locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _isDirty;
    /// <summary>
    /// Stores is busy locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _isBusy;
    /// <summary>
    /// Stores is delete confirming locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _isDeleteConfirming;
    /// <summary>
    /// Stores disposed locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _disposed;
    /// <summary>
    /// Stores autosave running locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private int _autosaveRunning;

    public NotesWorkspaceViewModel(
        INotesRepository repository,
        INotesImportExportService formats,
        INotesAiService ai,
        INotesAttachmentStore attachments,
        IOllamaClient models,
        IProductionDiagnostics diagnostics)
    {
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
        UndoCommand = new RelayCommand(Undo, () => _undo.Count > 0 && !IsBusy);
        RedoCommand = new RelayCommand(Redo, () => _redo.Count > 0 && !IsBusy);
        DeleteDocumentCommand = new AsyncRelayCommand(DeleteCurrentAsync, () => Document is not null && IsDeleteConfirming && !IsBusy);
        RequestDeleteDocumentCommand = new RelayCommand(() => IsDeleteConfirming = Document is not null);
        CancelDeleteDocumentCommand = new RelayCommand(() => IsDeleteConfirming = false);
        AddSectionCommand = new RelayCommand(AddSection, () => Document is not null);
        AddPageCommand = new RelayCommand(AddPage, () => CurrentSection is not null);
        AddParagraphCommand = new RelayCommand(() => AddBlock(NotesBlockKind.Paragraph), () => CurrentPage is not null);
        AddHeadingCommand = new RelayCommand(() => AddBlock(NotesBlockKind.Heading), () => CurrentPage is not null);
        AddListCommand = new RelayCommand(() => AddBlock(NotesBlockKind.List), () => CurrentPage is not null);
        AddTableCommand = new RelayCommand(() => AddBlock(NotesBlockKind.Table), () => CurrentPage is not null);
        AddEquationCommand = new RelayCommand(() => AddBlock(NotesBlockKind.Equation), () => CurrentPage is not null);
        AddHtmlCommand = new RelayCommand(() => AddBlock(NotesBlockKind.HtmlWidget), () => CurrentPage is not null);
        AddCanvasCommand = new RelayCommand(() => AddBlock(NotesBlockKind.Canvas), () => CurrentPage is not null);
        AddFlashcardCommand = new RelayCommand(() => AddBlock(NotesBlockKind.Flashcard), () => CurrentPage is not null);
        DeleteBlockCommand = new RelayCommand<NotesBlock>(DeleteBlock, block => block is not null && CurrentPage is not null);
        MoveBlockUpCommand = new RelayCommand<NotesBlock>(block => MoveBlock(block, -1));
        MoveBlockDownCommand = new RelayCommand<NotesBlock>(block => MoveBlock(block, 1));
        SearchCommand = new AsyncRelayCommand(SearchAsync, () => SearchQuery.Trim().Length >= 2 && !IsBusy);
        ClearSearchCommand = new RelayCommand(ClearSearch);
        RestoreVersionCommand = new AsyncRelayCommand(RestoreSelectedVersionAsync, () => SelectedVersion is not null && !IsBusy);
        ProposeAiCommand = new AsyncRelayCommand(ProposeAiAsync, () => Document is not null && !string.IsNullOrWhiteSpace(AiInstruction) && !string.IsNullOrWhiteSpace(SelectedModelName) && !IsBusy);
        CancelAiCommand = new RelayCommand(CancelAi);
        ApproveAiCommand = new AsyncRelayCommand(ApproveAiAsync, () => PendingAiChange?.Status == NotesAiChangeStatus.Proposed && !IsBusy);
        RejectAiCommand = new RelayCommand(RejectAi, () => PendingAiChange?.Status == NotesAiChangeStatus.Proposed);
        AddCommentCommand = new RelayCommand<string>(text => AddComment(SelectedBlock, text ?? string.Empty));
        ResolveCommentCommand = new RelayCommand<NotesComment>(ResolveComment);
        AddCitationCommand = new RelayCommand(AddCitation, () => Document is not null);
        ReviewAgainCommand = new RelayCommand(() => ReviewSelectedFlashcard(NotesFlashcardRating.Again));
        ReviewHardCommand = new RelayCommand(() => ReviewSelectedFlashcard(NotesFlashcardRating.Hard));
        ReviewGoodCommand = new RelayCommand(() => ReviewSelectedFlashcard(NotesFlashcardRating.Good));
        ReviewEasyCommand = new RelayCommand(() => ReviewSelectedFlashcard(NotesFlashcardRating.Easy));
    }

    /// <summary>
    /// Stores document changed locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    public event EventHandler? DocumentChanged;
    /// <summary>
    /// Stores search navigation requested locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    public event EventHandler<NotesSearchHit>? SearchNavigationRequested;

    /// <summary>
    /// Gets or updates documents, the bindable or domain state represented by this property.
    /// </summary>
    public ObservableCollection<NotesDocumentSummary> Documents { get; } = [];
    /// <summary>
    /// Gets or updates search results, the bindable or domain state represented by this property.
    /// </summary>
    public ObservableCollection<NotesSearchHit> SearchResults { get; } = [];
    /// <summary>
    /// Gets or updates versions, the bindable or domain state represented by this property.
    /// </summary>
    public ObservableCollection<NotesVersionInfo> Versions { get; } = [];
    /// <summary>
    /// Gets or updates models, the bindable or domain state represented by this property.
    /// </summary>
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
            CurrentPage = value?.Pages.OrderBy(page => page.Order).FirstOrDefault();
            RaiseDocumentProperties();
            DocumentChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public NotesPage? CurrentPage
    {
        get => _page;
        set
        {
            if (!SetProperty(ref _page, value)) return;
            SelectedBlock = value?.Blocks.OrderBy(block => block.Order).FirstOrDefault();
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

    /// <summary>
    /// Gets or updates status, the bindable or domain state represented by this property.
    /// </summary>
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    /// <summary>
    /// Gets or updates allow document context, the bindable or domain state represented by this property.
    /// </summary>
    public bool AllowDocumentContext { get => _allowDocumentContext; set => SetProperty(ref _allowDocumentContext, value); }
    /// <summary>
    /// Reports whether delete confirming applies to the current state.
    /// </summary>
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

    /// <summary>
    /// Gets or updates sections, the bindable or domain state represented by this property.
    /// </summary>
    public IReadOnlyList<NotesSection> Sections => Document?.Sections ?? [];
    /// <summary>
    /// Gets or updates pages, the bindable or domain state represented by this property.
    /// </summary>
    public IReadOnlyList<NotesPage> Pages => CurrentSection?.Pages.OrderBy(page => page.Order).ToArray() ?? [];
    /// <summary>
    /// Gets or updates blocks, the bindable or domain state represented by this property.
    /// </summary>
    public IReadOnlyList<NotesBlock> Blocks => CurrentPage?.Blocks.OrderBy(block => block.Order).ToArray() ?? [];
    /// <summary>
    /// Gets or updates comments, the bindable or domain state represented by this property.
    /// </summary>
    public IReadOnlyList<NotesComment> Comments => Document?.Comments ?? [];
    /// <summary>
    /// Gets or updates citations, the bindable or domain state represented by this property.
    /// </summary>
    public IReadOnlyList<NotesCitation> Citations => Document?.Citations ?? [];
    /// <summary>
    /// Gets or updates ai history, the bindable or domain state represented by this property.
    /// </summary>
    public IReadOnlyList<NotesAiChange> AiHistory => Document?.AiChanges ?? [];
    /// <summary>
    /// Reports whether document applies to the current state.
    /// </summary>
    public bool HasDocument => Document is not null;
    /// <summary>
    /// Reports whether selected block applies to the current state.
    /// </summary>
    public bool HasSelectedBlock => SelectedBlock is not null;
    /// <summary>
    /// Reports whether pending ai change applies to the current state.
    /// </summary>
    public bool HasPendingAiChange => PendingAiChange is not null;
    /// <summary>
    /// Reports whether selected flashcard applies to the current state.
    /// </summary>
    public bool IsSelectedFlashcard => SelectedBlock?.Flashcard is not null;
    /// <summary>
    /// Reports whether selected canvas applies to the current state.
    /// </summary>
    public bool IsSelectedCanvas => SelectedBlock?.Canvas is not null;
    /// <summary>
    /// Gets or updates selected block kind, the bindable or domain state represented by this property.
    /// </summary>
    public string SelectedBlockKind => SelectedBlock?.Kind.ToString() ?? "None";
    /// <summary>
    /// Gets or updates save state, the bindable or domain state represented by this property.
    /// </summary>
    public string SaveState => IsDirty ? "Unsaved changes" : Document is null ? "No document" : $"Saved · v{Document.Version}";
    /// <summary>
    /// Gets or updates statistics, the bindable or domain state represented by this property.
    /// </summary>
    public NotesStatistics Statistics => Document is null ? new NotesStatistics(0, 0, 0, 0, 0) : NotesTextStatistics.Calculate(Document);
    /// <summary>
    /// Gets or updates statistics label, the bindable or domain state represented by this property.
    /// </summary>
    public string StatisticsLabel => $"{Statistics.Words:N0} words · {Statistics.Characters:N0} characters · {Statistics.ReadingMinutes} min read";
    /// <summary>
    /// Gets or updates due flashcards, the bindable or domain state represented by this property.
    /// </summary>
    public IEnumerable<NotesFlashcardData> DueFlashcards => Document is null
        ? []
        : Document.Sections.SelectMany(section => section.Pages).SelectMany(page => page.Blocks).Where(block => block.Flashcard is not null).Select(block => block.Flashcard!).Where(card => card.Schedule.DueAt <= DateTimeOffset.UtcNow);

    /// <summary>
    /// Gets or updates new document command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand NewDocumentCommand { get; }
    /// <summary>
    /// Gets or updates save command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand SaveCommand { get; }
    /// <summary>
    /// Gets or updates undo command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand UndoCommand { get; }
    /// <summary>
    /// Gets or updates redo command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand RedoCommand { get; }
    /// <summary>
    /// Gets or updates request delete document command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand RequestDeleteDocumentCommand { get; }
    /// <summary>
    /// Gets or updates delete document command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand DeleteDocumentCommand { get; }
    /// <summary>
    /// Reports whether cancel delete document command is true for the current state.
    /// </summary>
    public RelayCommand CancelDeleteDocumentCommand { get; }
    /// <summary>
    /// Gets or updates add section command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand AddSectionCommand { get; }
    /// <summary>
    /// Gets or updates add page command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand AddPageCommand { get; }
    /// <summary>
    /// Gets or updates add paragraph command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand AddParagraphCommand { get; }
    /// <summary>
    /// Gets or updates add heading command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand AddHeadingCommand { get; }
    /// <summary>
    /// Gets or updates add list command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand AddListCommand { get; }
    /// <summary>
    /// Gets or updates add table command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand AddTableCommand { get; }
    /// <summary>
    /// Gets or updates add equation command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand AddEquationCommand { get; }
    /// <summary>
    /// Gets or updates add html command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand AddHtmlCommand { get; }
    /// <summary>
    /// Gets or updates add canvas command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand AddCanvasCommand { get; }
    /// <summary>
    /// Gets or updates add flashcard command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand AddFlashcardCommand { get; }
    /// <summary>
    /// Gets or updates delete block command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand<NotesBlock> DeleteBlockCommand { get; }
    /// <summary>
    /// Gets or updates move block up command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand<NotesBlock> MoveBlockUpCommand { get; }
    /// <summary>
    /// Gets or updates move block down command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand<NotesBlock> MoveBlockDownCommand { get; }
    /// <summary>
    /// Gets or updates search command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand SearchCommand { get; }
    /// <summary>
    /// Gets or updates clear search command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand ClearSearchCommand { get; }
    /// <summary>
    /// Gets or updates restore version command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand RestoreVersionCommand { get; }
    /// <summary>
    /// Gets or updates propose ai command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand ProposeAiCommand { get; }
    /// <summary>
    /// Reports whether cancel ai command is true for the current state.
    /// </summary>
    public RelayCommand CancelAiCommand { get; }
    /// <summary>
    /// Gets or updates approve ai command, the bindable or domain state represented by this property.
    /// </summary>
    public AsyncRelayCommand ApproveAiCommand { get; }
    /// <summary>
    /// Gets or updates reject ai command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand RejectAiCommand { get; }
    /// <summary>
    /// Gets or updates add comment command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand<string> AddCommentCommand { get; }
    /// <summary>
    /// Gets or updates resolve comment command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand<NotesComment> ResolveCommentCommand { get; }
    /// <summary>
    /// Gets or updates add citation command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand AddCitationCommand { get; }
    /// <summary>
    /// Gets or updates review again command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand ReviewAgainCommand { get; }
    /// <summary>
    /// Gets or updates review hard command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand ReviewHardCommand { get; }
    /// <summary>
    /// Gets or updates review good command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand ReviewGoodCommand { get; }
    /// <summary>
    /// Gets or updates review easy command, the bindable or domain state represented by this property.
    /// </summary>
    public RelayCommand ReviewEasyCommand { get; }

    /// <summary>
    /// Performs initialize asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(NotesWorkspaceViewModel));
        IsBusy = true;
        try
        {
            await RefreshDocumentsAsync(cancellationToken);
            await RefreshModelsAsync(cancellationToken);
            var first = Documents.FirstOrDefault();
            if (first is null) await NewDocumentAsync();
            else await OpenDocumentAsync(first.Id, cancellationToken);
            _autosaveTimer.Start();
            Status = "Haven Notes is ready. Changes autosave locally.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Performs refresh documents asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task RefreshDocumentsAsync(CancellationToken cancellationToken)
    {
        var documents = await _repository.ListAsync(cancellationToken);
        Documents.Clear();
        foreach (var document in documents) Documents.Add(document);
    }

    /// <summary>
    /// Performs open document asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Performs the select section step owned by this component.
    /// </summary>
    public void SelectSection(NotesSection section)
    {
        ArgumentNullException.ThrowIfNull(section);
        if (Document?.Sections.Contains(section) == true) CurrentSection = section;
    }

    /// <summary>
    /// Performs the select page step owned by this component.
    /// </summary>
    public void SelectPage(NotesPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (CurrentSection?.Pages.Contains(page) == true) CurrentPage = page;
    }

    /// <summary>
    /// Performs the begin block edit step owned by this component.
    /// </summary>
    public void BeginBlockEdit(NotesBlock block)
    {
        ArgumentNullException.ThrowIfNull(block);
        if (Document is null || _editSnapshots.ContainsKey(block.Id)) return;
        _editSnapshots[block.Id] = Snapshot(Document);
    }

    /// <summary>
    /// Performs the commit block edit step owned by this component.
    /// </summary>
    public void CommitBlockEdit(NotesBlock block, string summary)
    {
        ArgumentNullException.ThrowIfNull(block);
        if (Document is null || !_editSnapshots.Remove(block.Id, out var before)) return;
        var after = Snapshot(Document);
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

    /// <summary>
    /// Reports whether cancel block edit is true for the current state.
    /// </summary>
    public void CancelBlockEdit(NotesBlock block)
    {
        ArgumentNullException.ThrowIfNull(block);
        _editSnapshots.Remove(block.Id);
    }

    /// <summary>
    /// Performs the update block text step owned by this component.
    /// </summary>
    public void UpdateBlockText(NotesBlock block, string text)
    {
        ArgumentNullException.ThrowIfNull(block);
        block.PlainText = text ?? string.Empty;
        MarkDirtyWithoutRevision();
    }

    /// <summary>
    /// Performs the update equation step owned by this component.
    /// </summary>
    public void UpdateEquation(NotesBlock block, string source, NotesEquationViewMode mode, string accessibleAlternative)
    {
        if (block.Equation is null) return;
        BeginBlockEdit(block);
        block.Equation.Source = source ?? string.Empty;
        block.Equation.ViewMode = mode;
        block.Equation.AccessibleAlternative = accessibleAlternative?.Trim() ?? string.Empty;
        var rendered = NotesEquationRenderer.Render(block.Equation.Source);
        block.Equation.RenderedText = rendered.RenderedText;
        block.Equation.Error = rendered.Error;
        CommitBlockEdit(block, "Edited equation");
    }

    /// <summary>
    /// Performs the update html step owned by this component.
    /// </summary>
    public void UpdateHtml(
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
        BeginBlockEdit(block);
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
        CommitBlockEdit(block, "Edited sandboxed HTML widget");
    }

    /// <summary>
    /// Performs the add ink stroke step owned by this component.
    /// </summary>
    public void AddInkStroke(NotesBlock block, NotesInkStroke stroke)
    {
        if (block.Canvas is null) return;
        ArgumentNullException.ThrowIfNull(stroke);
        PushUndo(SnapshotRequired());
        block.Canvas.Strokes.Add(stroke);
        Document!.Revisions.Add(new NotesRevision { Kind = NotesRevisionKind.Edited, BlockId = block.Id, Summary = "Added editable ink stroke" });
        MarkDirty();
    }

    /// <summary>
    /// Performs the remove ink stroke step owned by this component.
    /// </summary>
    public void RemoveInkStroke(NotesBlock block, Guid strokeId)
    {
        if (block.Canvas is null) return;
        var stroke = block.Canvas.Strokes.FirstOrDefault(item => item.Id == strokeId);
        if (stroke is null) return;
        PushUndo(SnapshotRequired());
        block.Canvas.Strokes.Remove(stroke);
        foreach (var layer in block.Canvas.GhostLayers) layer.StrokeIds.Remove(strokeId);
        MarkDirty();
    }

    /// <summary>
    /// Performs the add ghost layer step owned by this component.
    /// </summary>
    public NotesGhostLayer AddGhostLayer(NotesBlock block, string name, NotesGhostRevealMode revealMode)
    {
        if (block.Canvas is null) throw new InvalidOperationException("Select a canvas block before adding Ghost Pen content.");
        PushUndo(SnapshotRequired());
        var layer = new NotesGhostLayer
        {
            Name = string.IsNullOrWhiteSpace(name) ? "Answer" : name.Trim(),
            RevealMode = revealMode
        };
        block.Canvas.GhostLayers.Add(layer);
        MarkDirty();
        return layer;
    }

    /// <summary>
    /// Performs the assign stroke to ghost layer step owned by this component.
    /// </summary>
    public void AssignStrokeToGhostLayer(NotesBlock block, NotesInkStroke stroke, NotesGhostLayer layer)
    {
        if (block.Canvas is null || !block.Canvas.Strokes.Contains(stroke) || !block.Canvas.GhostLayers.Contains(layer)) return;
        PushUndo(SnapshotRequired());
        stroke.IsGhost = true;
        stroke.GhostLayerId = layer.Id;
        if (!layer.StrokeIds.Contains(stroke.Id)) layer.StrokeIds.Add(stroke.Id);
        MarkDirty();
    }

    /// <summary>
    /// Performs the toggle ghost layer step owned by this component.
    /// </summary>
    public void ToggleGhostLayer(NotesBlock block, NotesGhostLayer layer)
    {
        if (block.Canvas is null || !block.Canvas.GhostLayers.Contains(layer)) return;
        layer.IsRevealed = !layer.IsRevealed;
        DocumentChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Performs the add occlusion mask step owned by this component.
    /// </summary>
    public void AddOcclusionMask(NotesBlock block, double x, double y, double width, double height, string answer)
    {
        var masks = block.Flashcard?.OcclusionMasks ?? block.Canvas?.GhostLayers.FirstOrDefault()?.Masks;
        if (masks is null) return;
        PushUndo(SnapshotRequired());
        masks.Add(new NotesOcclusionMask { X = x, Y = y, Width = Math.Max(10, width), Height = Math.Max(10, height), Answer = answer ?? string.Empty });
        MarkDirty();
    }

    /// <summary>
    /// Performs the add table row step owned by this component.
    /// </summary>
    public void AddTableRow(NotesBlock block)
    {
        if (block.Table is null) return;
        PushUndo(SnapshotRequired());
        var columns = Math.Max(1, block.Table.Rows.FirstOrDefault()?.Cells.Count ?? 1);
        var row = new NotesTableRow();
        for (var index = 0; index < columns; index++) row.Cells.Add(new NotesTableCell());
        block.Table.Rows.Add(row);
        MarkDirty();
    }

    /// <summary>
    /// Performs the add table column step owned by this component.
    /// </summary>
    public void AddTableColumn(NotesBlock block)
    {
        if (block.Table is null) return;
        PushUndo(SnapshotRequired());
        foreach (var row in block.Table.Rows) row.Cells.Add(new NotesTableCell());
        MarkDirty();
    }

    /// <summary>
    /// Performs the remove table row step owned by this component.
    /// </summary>
    public void RemoveTableRow(NotesBlock block)
    {
        var table = block.Table;
        if (table is null || table.Rows.Count <= 1) return;
        PushUndo(SnapshotRequired());
        table.Rows.RemoveAt(table.Rows.Count - 1);
        MarkDirty();
    }

    /// <summary>
    /// Performs the remove table column step owned by this component.
    /// </summary>
    public void RemoveTableColumn(NotesBlock block)
    {
        if (block.Table is null || block.Table.Rows.Any(row => row.Cells.Count <= 1)) return;
        PushUndo(SnapshotRequired());
        foreach (var row in block.Table.Rows) row.Cells.RemoveAt(row.Cells.Count - 1);
        MarkDirty();
    }

    /// <summary>
    /// Performs import media asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task<NotesMediaData> ImportMediaAsync(string sourcePath, CancellationToken cancellationToken)
    {
        var media = await _attachments.ImportAsync(sourcePath, cancellationToken);
        PushUndo(SnapshotRequired());
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

    /// <summary>
    /// Performs import document asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Performs export document asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Performs print asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Performs new document asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Performs save asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Performs delete current asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Performs the add section step owned by this component.
    /// </summary>
    private void AddSection()
    {
        if (Document is null) return;
        PushUndo(SnapshotRequired());
        var section = new NotesSection { Title = "Section " + (Document.Sections.Count + 1) };
        Document.Sections.Add(section);
        CurrentSection = section;
        MarkDirty();
    }

    /// <summary>
    /// Performs the add page step owned by this component.
    /// </summary>
    private void AddPage()
    {
        if (CurrentSection is null) return;
        PushUndo(SnapshotRequired());
        var page = new NotesPage { Title = "Page " + (CurrentSection.Pages.Count + 1), Order = CurrentSection.Pages.Count };
        CurrentSection.Pages.Add(page);
        CurrentPage = page;
        MarkDirty();
    }

    /// <summary>
    /// Performs the add block step owned by this component.
    /// </summary>
    private void AddBlock(NotesBlockKind kind)
    {
        if (CurrentPage is null) return;
        PushUndo(SnapshotRequired());
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

    /// <summary>
    /// Performs the delete block step owned by this component.
    /// </summary>
    private void DeleteBlock(NotesBlock? block)
    {
        if (CurrentPage is null || block is null || !CurrentPage.Blocks.Contains(block)) return;
        PushUndo(SnapshotRequired());
        CurrentPage.Blocks.Remove(block);
        NormalizeBlockOrder();
        if (CurrentPage.Blocks.Count == 0) CurrentPage.Blocks.Add(NotesBlock.CreateParagraph());
        SelectedBlock = CurrentPage.Blocks.OrderBy(item => item.Order).FirstOrDefault();
        MarkDirty();
    }

    /// <summary>
    /// Performs the move block step owned by this component.
    /// </summary>
    private void MoveBlock(NotesBlock? block, int delta)
    {
        if (CurrentPage is null || block is null) return;
        var ordered = CurrentPage.Blocks.OrderBy(item => item.Order).ToList();
        var index = ordered.IndexOf(block);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= ordered.Count) return;
        PushUndo(SnapshotRequired());
        (ordered[index], ordered[target]) = (ordered[target], ordered[index]);
        CurrentPage.Blocks = ordered;
        NormalizeBlockOrder();
        MarkDirty();
    }

    /// <summary>
    /// Performs search asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Performs the clear search step owned by this component.
    /// </summary>
    private void ClearSearch()
    {
        SearchQuery = string.Empty;
        SearchResults.Clear();
        SelectedSearchHit = null;
    }

    /// <summary>
    /// Performs navigate to search hit asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task NavigateToSearchHitAsync(NotesSearchHit hit)
    {
        if (Document?.Id != hit.DocumentId) await OpenDocumentAsync(hit.DocumentId, CancellationToken.None);
        if (Document is null) return;
        var section = Document.Sections.FirstOrDefault(item => item.Id == hit.SectionId);
        var page = section?.Pages.FirstOrDefault(item => item.Id == hit.PageId);
        var block = page?.Blocks.FirstOrDefault(item => item.Id == hit.BlockId);
        if (section is null || page is null || block is null) return;
        CurrentSection = section;
        CurrentPage = page;
        SelectedBlock = block;
        SearchNavigationRequested?.Invoke(this, hit);
    }

    /// <summary>
    /// Performs refresh versions asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task RefreshVersionsAsync(CancellationToken cancellationToken)
    {
        Versions.Clear();
        if (Document is null) return;
        foreach (var version in await _repository.GetVersionsAsync(Document.Id, cancellationToken)) Versions.Add(version);
        SelectedVersion = Versions.FirstOrDefault();
    }

    /// <summary>
    /// Performs restore selected version asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task RestoreSelectedVersionAsync()
    {
        if (Document is null || SelectedVersion is null) return;
        IsBusy = true;
        try
        {
            var restored = await _repository.LoadVersionAsync(Document.Id, SelectedVersion.VersionId, CancellationToken.None)
                           ?? throw new FileNotFoundException("The selected Notes version no longer exists.");
            PushUndo(SnapshotRequired());
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

    /// <summary>
    /// Performs propose ai asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Performs approve ai asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task ApproveAiAsync()
    {
        if (Document is null || PendingAiChange?.Status != NotesAiChangeStatus.Proposed) return;
        PushUndo(SnapshotRequired());
        var block = PendingAiChange.BlockId is { } blockId
            ? Document.Sections.SelectMany(section => section.Pages).SelectMany(page => page.Blocks).FirstOrDefault(item => item.Id == blockId)
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

    /// <summary>
    /// Performs the reject ai step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Reports whether cancel ai is true for the current state.
    /// </summary>
    private void CancelAi() => _aiCancellation?.Cancel();

    /// <summary>
    /// Performs the add comment step owned by this component.
    /// </summary>
    private void AddComment(NotesBlock? block, string text)
    {
        if (Document is null || block is null || string.IsNullOrWhiteSpace(text)) return;
        PushUndo(SnapshotRequired());
        Document.Comments.Add(new NotesComment { BlockId = block.Id, StartOffset = 0, EndOffset = block.PlainText.Length, Text = text.Trim() });
        MarkDirty();
    }

    /// <summary>
    /// Performs the resolve comment step owned by this component.
    /// </summary>
    private void ResolveComment(NotesComment? comment)
    {
        if (comment is null || comment.State == NotesCommentState.Resolved) return;
        PushUndo(SnapshotRequired());
        comment.State = NotesCommentState.Resolved;
        comment.ResolvedAt = DateTimeOffset.UtcNow;
        MarkDirty();
    }

    /// <summary>
    /// Performs the add citation step owned by this component.
    /// </summary>
    private void AddCitation()
    {
        if (Document is null) return;
        PushUndo(SnapshotRequired());
        Document.Citations.Add(new NotesCitation { Key = "source-" + (Document.Citations.Count + 1), Title = "New source", AccessedAt = DateTimeOffset.UtcNow });
        MarkDirty();
    }

    /// <summary>
    /// Performs the review selected flashcard step owned by this component.
    /// </summary>
    private void ReviewSelectedFlashcard(NotesFlashcardRating rating)
    {
        if (Document is null || SelectedBlock?.Flashcard is not { } card) return;
        PushUndo(SnapshotRequired());
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

    /// <summary>
    /// Performs the undo step owned by this component.
    /// </summary>
    private void Undo()
    {
        if (Document is null || _undo.Count == 0) return;
        _redo.Push(Snapshot(Document));
        SetDocument(Deserialize(_undo.Pop()));
        IsDirty = true;
        Status = "Undid the last Notes edit.";
        RaiseCommandStates();
    }

    /// <summary>
    /// Performs the redo step owned by this component.
    /// </summary>
    private void Redo()
    {
        if (Document is null || _redo.Count == 0) return;
        _undo.Push(Snapshot(Document));
        SetDocument(Deserialize(_redo.Pop()));
        IsDirty = true;
        Status = "Redid the Notes edit.";
        RaiseCommandStates();
    }

    /// <summary>
    /// Performs the set document step owned by this component.
    /// </summary>
    private void SetDocument(NotesDocument document)
    {
        Document = document;
        CurrentSection = document.Sections.FirstOrDefault();
        CurrentPage = CurrentSection?.Pages.OrderBy(page => page.Order).FirstOrDefault();
        SelectedBlock = CurrentPage?.Blocks.OrderBy(block => block.Order).FirstOrDefault();
        PendingAiChange = document.AiChanges.LastOrDefault(change => change.Status == NotesAiChangeStatus.Proposed);
        _undo.Clear();
        _redo.Clear();
        _editSnapshots.Clear();
        IsDirty = document.Recovery.HasUnsavedRecovery;
        _ = RefreshVersionsAsync(CancellationToken.None);
        RaiseCommandStates();
        DocumentChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Performs the push undo step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the snapshot required step owned by this component.
    /// </summary>
    private string SnapshotRequired() => Document is null ? throw new InvalidOperationException("No Notes document is open.") : Snapshot(Document);
    /// <summary>
    /// Performs the snapshot step owned by this component.
    /// </summary>
    private static string Snapshot(NotesDocument document) => JsonSerializer.Serialize(document, SnapshotOptions);
    /// <summary>
    /// Performs the deserialize step owned by this component.
    /// </summary>
    private static NotesDocument Deserialize(string snapshot) => JsonSerializer.Deserialize<NotesDocument>(snapshot, SnapshotOptions) ?? throw new InvalidDataException("The Notes edit snapshot was invalid.");

    /// <summary>
    /// Performs the mark dirty step owned by this component.
    /// </summary>
    private void MarkDirty()
    {
        IsDirty = true;
        Document!.UpdatedAt = DateTimeOffset.UtcNow;
        RaiseDocumentProperties();
        DocumentChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Performs the mark dirty without revision step owned by this component.
    /// </summary>
    private void MarkDirtyWithoutRevision()
    {
        IsDirty = true;
        if (Document is not null) Document.UpdatedAt = DateTimeOffset.UtcNow;
        RaiseDocumentProperties();
        DocumentChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Performs the normalize block order step owned by this component.
    /// </summary>
    private void NormalizeBlockOrder()
    {
        if (CurrentPage is null) return;
        for (var index = 0; index < CurrentPage.Blocks.Count; index++) CurrentPage.Blocks[index].Order = index;
    }

    /// <summary>
    /// Performs refresh models asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Performs the raise document properties step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the raise command states step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Handles the autosave tick event raised by the UI or runtime.
    /// </summary>
    private async void OnAutosaveTick(object? sender, EventArgs e)
    {
        if (_disposed || !IsDirty || IsBusy || Document is null) return;
        await SaveAsync("Autosave");
    }

    /// <summary>
    /// Performs the dispose step owned by this component.
    /// </summary>
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
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Represents notes equation render result and keeps its related state and behavior together.
/// </summary>
public sealed record NotesEquationRenderResult(string RenderedText, string Error);

/// <summary>
/// Represents notes equation renderer and keeps its related state and behavior together.
/// </summary>
public static class NotesEquationRenderer
{
    /// <summary>
    /// Stores symbols locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> Symbols = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["\\alpha"] = "α", ["\\beta"] = "β", ["\\gamma"] = "γ", ["\\delta"] = "δ", ["\\theta"] = "θ",
        ["\\lambda"] = "λ", ["\\mu"] = "μ", ["\\pi"] = "π", ["\\sigma"] = "σ", ["\\phi"] = "φ",
        ["\\omega"] = "ω", ["\\times"] = "×", ["\\div"] = "÷", ["\\pm"] = "±", ["\\leq"] = "≤",
        ["\\geq"] = "≥", ["\\neq"] = "≠", ["\\infty"] = "∞", ["\\sum"] = "∑", ["\\prod"] = "∏",
        ["\\int"] = "∫", ["\\sqrt"] = "√", ["\\rightarrow"] = "→", ["\\leftarrow"] = "←"
    };

    /// <summary>
    /// Performs the render step owned by this component.
    /// </summary>
    public static NotesEquationRenderResult Render(string source)
    {
        var value = source?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(value)) return new NotesEquationRenderResult(string.Empty, "Equation source is empty.");
        var braces = 0;
        foreach (var character in value)
        {
            if (character == '{') braces++;
            else if (character == '}') braces--;
            if (braces < 0) return new NotesEquationRenderResult(string.Empty, "Equation has an unmatched closing brace.");
        }
        if (braces != 0) return new NotesEquationRenderResult(string.Empty, "Equation has unmatched braces.");
        foreach (var symbol in Symbols) value = value.Replace(symbol.Key, symbol.Value, StringComparison.Ordinal);
        value = ReplaceSuperscripts(value);
        value = value.Replace("\\frac", "fraction", StringComparison.Ordinal).Replace("\\text", string.Empty, StringComparison.Ordinal);
        return new NotesEquationRenderResult(value, string.Empty);
    }

    /// <summary>
    /// Performs the replace superscripts step owned by this component.
    /// </summary>
    private static string ReplaceSuperscripts(string source)
    {
        var superscripts = new Dictionary<char, char> { ['0'] = '⁰', ['1'] = '¹', ['2'] = '²', ['3'] = '³', ['4'] = '⁴', ['5'] = '⁵', ['6'] = '⁶', ['7'] = '⁷', ['8'] = '⁸', ['9'] = '⁹', ['+'] = '⁺', ['-'] = '⁻', ['='] = '⁼', ['n'] = 'ⁿ' };
        var builder = new StringBuilder();
        for (var index = 0; index < source.Length; index++)
        {
            if (source[index] == '^' && index + 1 < source.Length)
            {
                var next = source[index + 1];
                if (superscripts.TryGetValue(next, out var replacement)) { builder.Append(replacement); index++; continue; }
                if (next == '{')
                {
                    var end = source.IndexOf('}', index + 2);
                    if (end > index)
                    {
                        var segment = source[(index + 2)..end];
                        if (segment.All(superscripts.ContainsKey)) { foreach (var character in segment) builder.Append(superscripts[character]); index = end; continue; }
                    }
                }
            }
            builder.Append(source[index]);
        }
        return builder.ToString();
    }
}

/// <summary>
/// Represents notes html sandbox result and keeps its related state and behavior together.
/// </summary>
public sealed record NotesHtmlSandboxResult(string DocumentHtml, string FallbackText, string Error);

/// <summary>
/// Represents notes html sandbox and keeps its related state and behavior together.
/// </summary>
public static class NotesHtmlSandbox
{
    /// <summary>
    /// Builds this member from the currently available inputs.
    /// </summary>
    public static NotesHtmlSandboxResult Build(NotesHtmlData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (!data.AllowScripts && !string.IsNullOrWhiteSpace(data.JavaScriptSource))
            return new NotesHtmlSandboxResult(string.Empty, Fallback(data.HtmlSource), "JavaScript is present but script permission is disabled.");
        if (!data.AllowNetwork && ContainsNetworkReference(data.HtmlSource + data.CssSource + data.JavaScriptSource))
            return new NotesHtmlSandboxResult(string.Empty, Fallback(data.HtmlSource), "Network references are present but network permission is disabled.");
        if (!data.AllowForms && data.HtmlSource.Contains("<form", StringComparison.OrdinalIgnoreCase))
            return new NotesHtmlSandboxResult(string.Empty, Fallback(data.HtmlSource), "Forms are present but form permission is disabled.");
        if (data.HtmlSource.Contains("window.open", StringComparison.OrdinalIgnoreCase) || data.HtmlSource.Contains("target=\"_blank\"", StringComparison.OrdinalIgnoreCase) || data.HtmlSource.Contains("target='_blank'", StringComparison.OrdinalIgnoreCase))
            return new NotesHtmlSandboxResult(string.Empty, Fallback(data.HtmlSource), "Popups are not permitted in Notes widgets.");

        var contentSecurity = new StringBuilder("default-src 'none'; img-src data: blob:");
        if (data.AllowNetwork) contentSecurity.Append(" https:");
        contentSecurity.Append("; style-src 'unsafe-inline'");
        if (data.AllowNetwork) contentSecurity.Append(" https:");
        contentSecurity.Append("; font-src data:");
        if (data.AllowNetwork) contentSecurity.Append(" https:");
        contentSecurity.Append("; script-src ");
        contentSecurity.Append(data.AllowScripts ? "'unsafe-inline'" : "'none'");
        if (data.AllowNetwork && data.AllowScripts) contentSecurity.Append(" https:");
        contentSecurity.Append("; connect-src ").Append(data.AllowNetwork ? "https:" : "'none'").Append("; form-action ").Append(data.AllowForms ? "'self'" : "'none'").Append("; frame-src 'none'; object-src 'none'; base-uri 'none'");
        var script = data.AllowScripts ? "<script>" + data.JavaScriptSource + "</script>" : string.Empty;
        var document = "<!doctype html><html><head><meta charset=\"utf-8\"><meta http-equiv=\"Content-Security-Policy\" content=\"" + System.Net.WebUtility.HtmlEncode(contentSecurity.ToString()) + "\"><style>html,body{margin:0;padding:8px;font-family:system-ui}" + data.CssSource + "</style></head><body>" + data.HtmlSource + script + "</body></html>";
        return new NotesHtmlSandboxResult(document, Fallback(data.HtmlSource), string.Empty);
    }

    /// <summary>
    /// Performs the contains network reference step owned by this component.
    /// </summary>
    private static bool ContainsNetworkReference(string value) =>
        value.Contains("http://", StringComparison.OrdinalIgnoreCase)
        || value.Contains("https://", StringComparison.OrdinalIgnoreCase)
        || value.Contains("//", StringComparison.Ordinal)
        || value.Contains("@import", StringComparison.OrdinalIgnoreCase)
        || value.Contains("url(", StringComparison.OrdinalIgnoreCase)
        || value.Contains("fetch(", StringComparison.OrdinalIgnoreCase)
        || value.Contains("XMLHttpRequest", StringComparison.OrdinalIgnoreCase)
        || value.Contains("WebSocket", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Performs the fallback step owned by this component.
    /// </summary>
    private static string Fallback(string html)
    {
        var builder = new StringBuilder();
        var inside = false;
        foreach (var character in html)
        {
            if (character == '<') { inside = true; builder.Append(' '); continue; }
            if (character == '>') { inside = false; continue; }
            if (!inside) builder.Append(character);
        }
        return System.Net.WebUtility.HtmlDecode(builder.ToString()).ReplaceLineEndings(" ").Trim();
    }
}
