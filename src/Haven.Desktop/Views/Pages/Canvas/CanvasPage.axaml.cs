using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Events;
using Haven.Desktop.HavenUI.Backend;

namespace Haven.Desktop.Views.Pages.Canvas;

public sealed partial class CanvasPage : UserControl, IDisposable
{
    private const string NativeExtension = ".haven-notes.json";
    private readonly HavenEventBus _bus;
    private readonly INotesRepository _repository;
    private readonly INotesImportExportService _formats;
    private readonly CanvasHavenScene _route;
    private readonly DispatcherTimer _autosaveTimer;
    private IReadOnlyList<NotesDocumentSummary> _documents = [];
    private CanvasInteractionController? _controller;
    private int _documentIndex;
    private int _saveRunning;
    private bool _initialized;
    private bool _busy;
    private bool _dirty;
    private bool _disposed;

    public CanvasPage(HavenEventBus bus, INotesRepository repository, INotesImportExportService formats)
    {
        _bus = bus ?? throw new ArgumentNullException(nameof(bus)); _repository = repository ?? throw new ArgumentNullException(nameof(repository)); _formats = formats ?? throw new ArgumentNullException(nameof(formats));
        InitializeComponent(); _route = new CanvasHavenScene(); Scene.Root = _route.Root;
        _route.PreviousRequested += OnPreviousRequested; _route.NextRequested += OnNextRequested; _route.NewRequested += OnNewRequested; _route.SaveRequested += OnSaveRequested; _route.ImportRequested += OnImportRequested; _route.ExportRequested += OnExportRequested; _route.UndoRequested += OnUndoRequested; _route.RedoRequested += OnRedoRequested; _route.ToolRequested += OnToolRequested; _route.DeleteRequested += OnDeleteRequested; _route.BringFrontRequested += OnBringFrontRequested; _route.SendBackRequested += OnSendBackRequested; _route.GroupRequested += OnGroupRequested; _route.UngroupRequested += OnUngroupRequested; _route.ResetViewRequested += OnResetViewRequested; _route.TitleChanged += OnTitleChanged; _route.ObjectTextChanged += OnObjectTextChanged; _route.XChanged += value => OnPositionChanged(value, true); _route.YChanged += value => OnPositionChanged(value, false); _route.WidthChanged += value => OnSizeChanged(value, true); _route.HeightChanged += value => OnSizeChanged(value, false); _route.RotationChanged += OnRotationChanged; _route.LockChanged += OnLockChanged; _route.ZoomChanged += OnZoomChanged; _route.InfiniteChanged += OnInfiniteChanged; _route.PenWidthChanged += value => { if (_controller is not null) _controller.PenWidth = value; };
        _route.SurfaceChanged += OnSurfaceChanged;
        _route.SurfaceSelectionChanged += OnSurfaceSelectionChanged;
        _autosaveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) }; _autosaveTimer.Tick += OnAutosaveTick; Loaded += OnLoaded; DetachedFromVisualTree += OnDetachedFromVisualTree;
    }

    public NotesDocument? Document { get; private set; } public NotesCanvasData? Board => _controller?.Board; public bool IsDirty => _dirty; internal CanvasHavenScene Route => _route; internal HavenSceneControl SceneHost => Scene; internal Haven.UI.Components.Page SceneRoot => _route.Root; internal CanvasInteractionController? Controller => _controller;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized || _disposed) return; _initialized = true; SetBusy(true);
        try { await RefreshDocumentsAsync(cancellationToken); if (_documents.Count == 0) await CreateDocumentAsync(cancellationToken); else await OpenDocumentAtAsync(0, cancellationToken, false); _autosaveTimer.Start(); _bus.Fire("Canvas.Opened"); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { _initialized = false; throw; } catch (Exception ex) { _initialized = false; _route.SetStatus("Couldn’t open local canvases: " + ex.Message); } finally { SetBusy(false); }
    }

    public async Task<bool> SaveAsync(string reason = "Manual save", CancellationToken cancellationToken = default)
    {
        if (Document is null || (!_dirty && !Document.Recovery.HasUnsavedRecovery)) return true; if (Interlocked.Exchange(ref _saveRunning, 1) != 0) return false;
        try { SyncBoardReference(); if (string.IsNullOrWhiteSpace(Document.Title)) Document.Title = "Untitled canvas"; var result = await _repository.SaveAsync(Document, reason, cancellationToken); Document.Version = result.Version; Document.Recovery.HasUnsavedRecovery = false; _dirty = false; await RefreshDocumentsAsync(cancellationToken); _documentIndex = IndexOf(Document.Id); RefreshScene(); _route.SetStatus($"Saved locally at {result.SavedAt.LocalDateTime:t} · v{result.Version}"); _bus.Fire("Canvas.Saved"); return true; }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; } catch (Exception ex) { _route.SetStatus("Couldn’t save this canvas: " + ex.Message); return false; } finally { Interlocked.Exchange(ref _saveRunning, 0); }
    }

    internal async Task<bool> ImportFromPathAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path); if (Document is not null && _dirty && !await SaveAsync("Autosave before import", cancellationToken)) return false;
        try { var imported = await _formats.ImportAsync(path, cancellationToken); if (!CanvasDocumentModel.IsCanvasDocument(imported)) { _route.SetStatus("That file is a Notes document, but it is not a Haven Canvas board."); return false; } var save = await _repository.SaveAsync(imported, "Imported Canvas board", cancellationToken); imported.Version = save.Version; await RefreshDocumentsAsync(cancellationToken); SetDocument(imported, IndexOf(imported.Id)); _route.SetStatus("Imported " + Path.GetFileName(path)); return true; } catch (Exception ex) when (ex is not OperationCanceledException) { _route.SetStatus("Couldn’t import this canvas: " + ex.Message); return false; }
    }

    internal async Task<bool> ExportToPathAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path); if (Document is null) return false; if (_dirty && !await SaveAsync("Save before export", cancellationToken)) return false;
        try { SyncBoardReference(); await _formats.ExportAsync(Document, path, cancellationToken); _route.SetStatus("Exported native Canvas board · " + Path.GetFileName(path)); return true; } catch (Exception ex) when (ex is not OperationCanceledException) { _route.SetStatus("Couldn’t export this canvas: " + ex.Message); return false; }
    }

    private async void OnLoaded(object? sender, RoutedEventArgs e) => await InitializeAsync();
    private async void OnDetachedFromVisualTree(object? sender, Avalonia.VisualTreeAttachmentEventArgs e) { _autosaveTimer.Stop(); if (_dirty) await SaveAsync("Autosave on leaving Canvas"); }
    private async void OnAutosaveTick(object? sender, EventArgs e) { if (!_disposed && _dirty && !_busy) await SaveAsync("Autosave"); }
    private async void OnNewRequested(object? sender, EventArgs e) => await RunBusyAsync(() => CreateDocumentAsync(CancellationToken.None), "create a canvas");
    private async void OnPreviousRequested(object? sender, EventArgs e) => await RunBusyAsync(() => MoveAsync(-1), "open the previous canvas");
    private async void OnNextRequested(object? sender, EventArgs e) => await RunBusyAsync(() => MoveAsync(1), "open the next canvas");
    private async void OnSaveRequested(object? sender, EventArgs e) => await SaveAsync();
    private async void OnImportRequested(object? sender, EventArgs e) => await RunBusyAsync(PickImportAsync, "import a canvas");
    private async void OnExportRequested(object? sender, EventArgs e) => await RunBusyAsync(PickExportAsync, "export a canvas");
    private void OnUndoRequested(object? sender, EventArgs e) { if (_controller?.Undo() == true) { SyncBoardReference(); MarkDirty(); RefreshScene(); } }
    private void OnRedoRequested(object? sender, EventArgs e) { if (_controller?.Redo() == true) { SyncBoardReference(); MarkDirty(); RefreshScene(); } }
    private void OnToolRequested(CanvasTool tool) { if (_controller is null) return; _controller.Tool = tool; RefreshScene(); }
    private void OnDeleteRequested(object? sender, EventArgs e) { if (_controller?.DeleteSelection() == true) { MarkDirty(); RefreshScene(); } }
    private void OnBringFrontRequested(object? sender, EventArgs e) { if (_controller?.BringSelectionToFront() == true) { MarkDirty(); RefreshScene(); } }
    private void OnSendBackRequested(object? sender, EventArgs e) { if (_controller?.SendSelectionToBack() == true) { MarkDirty(); RefreshScene(); } }
    private void OnUngroupRequested(object? sender, EventArgs e) { if (_controller?.UngroupSelection() == true) { MarkDirty(); RefreshScene(); } }
    private void OnResetViewRequested(object? sender, EventArgs e) { if (_controller is null) return; _controller.ResetView(); MarkDirty(); RefreshScene(); }
    private void OnGroupRequested(object? sender, EventArgs e) { if (_controller?.GroupSelection() is not null) { MarkDirty(); RefreshScene(); } }
    private void OnTitleChanged(string title) { if (Document is null || Document.Title == title) return; Document.Title = title; MarkDirty(); }
    private void OnObjectTextChanged(string text) { if (_controller?.UpdateSelectedText(text) == true) { MarkDirty(); RefreshScene(); } }
    private void OnPositionChanged(double value, bool x) { if (_controller?.SelectedObject is not { } selected) return; if (_controller.MoveSelected(x ? value : selected.X, x ? selected.Y : value)) { MarkDirty(); RefreshScene(); } }
    private void OnSizeChanged(double value, bool width) { if (_controller?.SelectedObject is not { } selected) return; if (_controller.ResizeSelected(width ? value : selected.Width, width ? selected.Height : value)) { MarkDirty(); RefreshScene(); } }
    private void OnRotationChanged(double value) { if (_controller?.RotateSelected(value) == true) { MarkDirty(); RefreshScene(); } }
    private void OnLockChanged(bool value) { if (_controller?.SetSelectedLocked(value) == true) { MarkDirty(); RefreshScene(); } }
    private void OnZoomChanged(double value) { if (_controller is null || Math.Abs(_controller.Board.Zoom - value) < .0001) return; _controller.SetZoom(value); MarkDirty(); RefreshScene(); }
    private void OnInfiniteChanged(bool value) { if (_controller is null || _controller.Board.Infinite == value) return; _controller.SetInfinite(value); MarkDirty(); RefreshScene(); }
    private void OnSurfaceChanged(object? sender, EventArgs e) { if (_controller is null) return; SyncBoardReference(); MarkDirty(); _route.RefreshSurfaceMetadata(_controller); }
    private void OnSurfaceSelectionChanged(object? sender, EventArgs e) { if (_controller is null) return; _route.RefreshSurfaceMetadata(_controller); }

    private async Task CreateDocumentAsync(CancellationToken cancellationToken) { if (Document is not null && _dirty && !await SaveAsync("Autosave before creating canvas", cancellationToken)) return; var document = CanvasDocumentModel.Create(); var result = await _repository.SaveAsync(document, "Canvas created", cancellationToken); document.Version = result.Version; await RefreshDocumentsAsync(cancellationToken); SetDocument(document, IndexOf(document.Id)); _route.SetStatus("Created a new local Canvas board."); }
    private async Task MoveAsync(int offset) { if (_documents.Count <= 1) return; var next = (_documentIndex + offset + _documents.Count) % _documents.Count; await OpenDocumentAtAsync(next, CancellationToken.None, true); }
    private async Task OpenDocumentAtAsync(int index, CancellationToken cancellationToken, bool saveBeforeSwitch) { if (_documents.Count == 0) return; if (saveBeforeSwitch && _dirty && !await SaveAsync("Autosave before switching canvas", cancellationToken)) return; index = Math.Clamp(index, 0, _documents.Count - 1); var loaded = await _repository.LoadAsync(_documents[index].Id, cancellationToken); if (loaded is null || !CanvasDocumentModel.IsCanvasDocument(loaded)) { await RefreshDocumentsAsync(cancellationToken); _route.SetStatus("That local Canvas board no longer exists."); return; } SetDocument(loaded, index); _route.SetStatus(loaded.Recovery.HasUnsavedRecovery ? "Recovered the last valid local Canvas version. Review it, then save to confirm recovery." : "Saved locally · autosave is on"); }
    private void SetDocument(NotesDocument document, int index) { Document = document; _documentIndex = Math.Clamp(index, 0, Math.Max(0, _documents.Count - 1)); _controller = new CanvasInteractionController(CanvasDocumentModel.GetBoard(document)); _dirty = false; RefreshScene(); }
    private async Task RefreshDocumentsAsync(CancellationToken cancellationToken) { var summaries = await _repository.ListAsync(cancellationToken); var canvases = new List<NotesDocumentSummary>(); foreach (var summary in summaries) { var document = await _repository.LoadAsync(summary.Id, cancellationToken); if (CanvasDocumentModel.IsCanvasDocument(document)) canvases.Add(summary); } _documents = canvases; }
    private int IndexOf(Guid id) { for (var i = 0; i < _documents.Count; i++) if (_documents[i].Id == id) return i; return 0; }
    private void SyncBoardReference() { if (Document is not null && _controller is not null) CanvasDocumentModel.ReplaceBoard(Document, _controller.Board); }
    private void MarkDirty() { if (Document is null) return; Document.UpdatedAt = DateTimeOffset.UtcNow; _dirty = true; _route.SetStatus("Unsaved Canvas changes · autosave is on"); }
    private void RefreshScene() { if (Document is null || _controller is null) return; SyncBoardReference(); _route.SetControllerDocument(Document, _controller, _documentIndex, _documents.Count, _controller.History.CanUndo, _controller.History.CanRedo); }
    private void SetBusy(bool busy) { _busy = busy; _route.SetBusy(busy); }
    private async Task RunBusyAsync(Func<Task> action, string description) { if (_busy || _disposed) return; SetBusy(true); try { await action(); } catch (Exception ex) { _route.SetStatus($"Couldn’t {description}: {ex.Message}"); } finally { SetBusy(false); } }

    private async Task PickImportAsync() { var top = TopLevel.GetTopLevel(this); if (top?.StorageProvider is null) { _route.SetStatus("Import isn’t available from this platform surface."); return; } var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Import Haven Canvas board", AllowMultiple = false, FileTypeFilter = [new FilePickerFileType("Haven Canvas") { Patterns = ["*.haven-notes.json", "*.json"] }] }); var file = files.FirstOrDefault(); if (file is null) return; var local = file.TryGetLocalPath(); if (!string.IsNullOrWhiteSpace(local)) await ImportFromPathAsync(local); else _route.SetStatus("Canvas import currently requires a local file path on this platform."); }
    private async Task PickExportAsync() { if (Document is null) return; var top = TopLevel.GetTopLevel(this); if (top?.StorageProvider is null) { _route.SetStatus("Export isn’t available from this platform surface."); return; } var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Export Haven Canvas board", SuggestedFileName = Sanitize(Document.Title) + NativeExtension, DefaultExtension = "json", FileTypeChoices = [new FilePickerFileType("Haven Canvas") { Patterns = ["*.haven-notes.json"] }], ShowOverwritePrompt = true }); if (file is null) return; var local = file.TryGetLocalPath(); if (!string.IsNullOrWhiteSpace(local)) await ExportToPathAsync(local); else _route.SetStatus("Canvas export currently requires a local file path on this platform."); }
    private static string Sanitize(string title) { var value = string.IsNullOrWhiteSpace(title) ? "Untitled canvas" : title.Trim(); foreach (var invalid in Path.GetInvalidFileNameChars()) value = value.Replace(invalid, '_'); return value; }

    public void Dispose() { if (_disposed) return; _disposed = true; _autosaveTimer.Stop(); _autosaveTimer.Tick -= OnAutosaveTick; Loaded -= OnLoaded; DetachedFromVisualTree -= OnDetachedFromVisualTree; _route.Dispose(); }
}
