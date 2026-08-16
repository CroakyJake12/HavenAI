using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Events;
using Haven.Desktop.HavenUI.Backend;

namespace Haven.Desktop.Views.Pages.Present;

public sealed partial class PresentPage : UserControl, IDisposable
{
    private readonly HavenEventBus _bus;
    private readonly IPresentRepository _repository;
    private readonly IPresentExportService _exporter;
    private readonly PresentHavenScene _route;
    private readonly DispatcherTimer _autosaveTimer;
    private IReadOnlyList<PresentDocumentSummary> _documents = [];
    private int _deckIndex;
    private int _slideIndex;
    private int _saveRunning;
    private bool _initialized;
    private bool _busy;
    private bool _dirty;
    private bool _disposed;

    public PresentPage(HavenEventBus bus, IPresentRepository repository, IPresentExportService exporter)
    {
        _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _exporter = exporter ?? throw new ArgumentNullException(nameof(exporter));
        InitializeComponent();
        _route = new PresentHavenScene();
        Scene.Root = _route.Root;
        _route.PreviousDeckRequested += OnPreviousDeckRequested; _route.NextDeckRequested += OnNextDeckRequested; _route.NewDeckRequested += OnNewDeckRequested;
        _route.SaveRequested += OnSaveRequested; _route.ExportRequested += OnExportRequested;
        _route.PreviousSlideRequested += OnPreviousSlideRequested; _route.NextSlideRequested += OnNextSlideRequested; _route.AddSlideRequested += OnAddSlideRequested; _route.DeleteSlideRequested += OnDeleteSlideRequested;
        _route.DeckTitleChanged += OnDeckTitleChanged; _route.SlideTitleChanged += OnSlideTitleChanged; _route.BodyChanged += OnBodyChanged; _route.NotesChanged += OnNotesChanged;
        _autosaveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _autosaveTimer.Tick += OnAutosaveTick;
        Loaded += OnLoaded; DetachedFromVisualTree += OnDetachedFromVisualTree;
    }

    public PresentDocument? Document { get; private set; }
    public bool IsDirty => _dirty;
    internal PresentHavenScene Route => _route;
    internal HavenSceneControl SceneHost => Scene;
    internal Haven.UI.Components.Page SceneRoot => _route.Root;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized || _disposed) return;
        _initialized = true; SetBusy(true);
        try
        {
            await RefreshDocumentsAsync(cancellationToken);
            if (_documents.Count == 0) await CreateDeckAsync(cancellationToken);
            else await OpenDeckAtAsync(0, cancellationToken, false);
            _autosaveTimer.Start(); _bus.Fire("Present.Opened");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { _initialized = false; throw; }
        catch (Exception ex) { _initialized = false; _route.SetStatus("Couldn’t open local presentations: " + ex.Message); }
        finally { SetBusy(false); }
    }

    public async Task<bool> SaveAsync(string reason = "Manual save", CancellationToken cancellationToken = default)
    {
        if (Document is null || !_dirty) return true;
        if (Interlocked.Exchange(ref _saveRunning, 1) != 0) return false;
        try
        {
            if (string.IsNullOrWhiteSpace(Document.Title)) Document.Title = "Untitled presentation";
            var result = await _repository.SaveAsync(Document, reason, cancellationToken);
            Document.Version = result.Version; _dirty = false;
            await RefreshDocumentsAsync(cancellationToken); _deckIndex = IndexOfDocument(Document.Id); RenderCurrent();
            _route.SetStatus($"Saved locally at {result.SavedAt.LocalDateTime:t} · v{result.Version}"); _bus.Fire("Present.Saved"); return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) { _route.SetStatus("Couldn’t save this presentation: " + ex.Message); return false; }
        finally { Interlocked.Exchange(ref _saveRunning, 0); }
    }

    private async void OnLoaded(object? sender, RoutedEventArgs e) { await InitializeAsync(); if (!_disposed) _autosaveTimer.Start(); }
    private async void OnDetachedFromVisualTree(object? sender, Avalonia.VisualTreeAttachmentEventArgs e) { _autosaveTimer.Stop(); if (_dirty && Document is not null) await SaveAsync("Autosave on leaving Present"); }
    private async void OnAutosaveTick(object? sender, EventArgs e) { if (!_disposed && _dirty && !_busy && Document is not null) await SaveAsync("Autosave"); }
    private async void OnPreviousDeckRequested(object? sender, EventArgs e) => await RunBusyAsync(() => MoveDeckAsync(-1), "open the previous presentation");
    private async void OnNextDeckRequested(object? sender, EventArgs e) => await RunBusyAsync(() => MoveDeckAsync(1), "open the next presentation");
    private async void OnNewDeckRequested(object? sender, EventArgs e) => await RunBusyAsync(() => CreateDeckAsync(CancellationToken.None), "create a presentation");
    private async void OnSaveRequested(object? sender, EventArgs e) => await SaveAsync();
    private async void OnExportRequested(object? sender, EventArgs e) => await RunBusyAsync(PickExportAsync, "export this presentation");
    private void OnPreviousSlideRequested(object? sender, EventArgs e) => MoveSlide(-1);
    private void OnNextSlideRequested(object? sender, EventArgs e) => MoveSlide(1);
    private void OnAddSlideRequested(object? sender, EventArgs e) => AddSlide();
    private void OnDeleteSlideRequested(object? sender, EventArgs e) => DeleteSlide();

    private PresentSlide? CurrentSlide => Document is null || Document.Slides.Count == 0 ? null : Document.Slides[Math.Clamp(_slideIndex, 0, Document.Slides.Count - 1)];
    private void OnDeckTitleChanged(string value) { if (Document is null || Document.Title == value) return; Document.Title = value; MarkDirty(); }
    private void OnSlideTitleChanged(string value) { var slide = CurrentSlide; if (slide is null || slide.Title == value) return; slide.Title = value; MarkDirty(); }
    private void OnBodyChanged(string value) { var slide = CurrentSlide; if (slide is null) return; var body = slide.GetOrCreateBodyText(); if (body.Text == value) return; body.Text = value; MarkDirty(); }
    private void OnNotesChanged(string value) { var slide = CurrentSlide; if (slide is null || slide.SpeakerNotes == value) return; slide.SpeakerNotes = value; MarkDirty(); }

    private void MarkDirty()
    {
        if (Document is null) return; Document.UpdatedAt = DateTimeOffset.UtcNow; _dirty = true; _route.SetStatus("Unsaved changes · autosave is on");
    }

    private void MoveSlide(int offset)
    {
        if (Document is null || Document.Slides.Count <= 1) return;
        _slideIndex = (_slideIndex + offset + Document.Slides.Count) % Document.Slides.Count; RenderCurrent();
    }

    private void AddSlide()
    {
        if (Document is null) return;
        var slide = PresentSlide.Create(Document.Slides.Count);
        slide.Title = $"Slide {Document.Slides.Count + 1}"; Document.Slides.Add(slide); _slideIndex = Document.Slides.Count - 1;
        MarkDirty(); RenderCurrent(); _bus.Fire("Present.Slide.Added");
    }

    private void DeleteSlide()
    {
        if (Document is null || Document.Slides.Count == 0) return;
        if (Document.Slides.Count == 1) Document.Slides[0] = PresentSlide.Create(0);
        else Document.Slides.RemoveAt(Math.Clamp(_slideIndex, 0, Document.Slides.Count - 1));
        for (var index = 0; index < Document.Slides.Count; index++) Document.Slides[index].Order = index;
        _slideIndex = Math.Clamp(_slideIndex, 0, Document.Slides.Count - 1); MarkDirty(); RenderCurrent(); _bus.Fire("Present.Slide.Deleted");
    }

    private async Task MoveDeckAsync(int offset)
    {
        if (_documents.Count <= 1) return;
        if (_dirty && !await SaveAsync("Autosave before switching presentation")) return;
        var next = (_deckIndex + offset + _documents.Count) % _documents.Count; await OpenDeckAtAsync(next, CancellationToken.None, false);
    }

    private async Task CreateDeckAsync(CancellationToken cancellationToken)
    {
        if (Document is not null && _dirty && !await SaveAsync("Autosave before creating presentation", cancellationToken)) return;
        var document = PresentDocument.Create("Untitled presentation");
        var result = await _repository.SaveAsync(document, "Presentation created", cancellationToken); document.Version = result.Version;
        await RefreshDocumentsAsync(cancellationToken); Document = document; _deckIndex = IndexOfDocument(document.Id); _slideIndex = 0; _dirty = false;
        RenderCurrent(); _route.SetStatus("Created a new local presentation."); _bus.Fire("Present.Document.Created");
    }

    private async Task OpenDeckAtAsync(int index, CancellationToken cancellationToken, bool saveBeforeSwitch)
    {
        if (_documents.Count == 0) return;
        if (saveBeforeSwitch && Document is not null && _dirty && !await SaveAsync("Autosave before switching presentation", cancellationToken)) return;
        index = Math.Clamp(index, 0, _documents.Count - 1);
        var loaded = await _repository.LoadAsync(_documents[index].Id, cancellationToken);
        if (loaded is null) { await RefreshDocumentsAsync(cancellationToken); _route.SetStatus("That local presentation no longer exists."); return; }
        loaded.Normalize(); Document = loaded; _deckIndex = index; _slideIndex = 0; _dirty = false; RenderCurrent();
        _route.SetStatus(loaded.Recovery.RecoveredFromBackup ? loaded.Recovery.Message : "Saved locally · autosave is on"); _bus.Fire("Present.Document.Opened");
    }

    internal async Task<bool> ExportToPathAsync(string destinationPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath); if (Document is null) return false;
        if (_dirty && !await SaveAsync("Save before export", cancellationToken)) return false;
        try
        {
            var path = await _exporter.ExportAsync(Document, destinationPath, cancellationToken); _route.SetStatus("Exported " + Path.GetFileName(path)); _bus.Fire("Present.Document.Exported"); return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) { _route.SetStatus("Couldn’t export this presentation: " + ex.Message); return false; }
    }

    private async Task PickExportAsync()
    {
        if (Document is null) return; var top = TopLevel.GetTopLevel(this);
        if (top?.StorageProvider is null) { _route.SetStatus("Export isn’t available from this platform surface."); return; }
        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export presentation", SuggestedFileName = SanitizeFileName(Document.Title) + ".pptx", DefaultExtension = "pptx",
            FileTypeChoices = [new FilePickerFileType("PowerPoint presentation") { Patterns = ["*.pptx"] }], ShowOverwritePrompt = true
        });
        if (file is null) return; var localPath = file.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(localPath)) { await ExportToPathAsync(localPath); return; }
        var temporary = Path.Combine(Path.GetTempPath(), $"haven-present-export-{Guid.NewGuid():N}.pptx");
        try
        {
            if (!await ExportToPathAsync(temporary)) return;
            await using var source = File.OpenRead(temporary); await using var destination = await file.OpenWriteAsync(); destination.SetLength(0);
            await source.CopyToAsync(destination); await destination.FlushAsync(); _route.SetStatus("Exported " + file.Name);
        }
        finally { TryDeleteTemporary(temporary); }
    }

    private void RenderCurrent()
    {
        if (Document is null) return; Document.Normalize(); _slideIndex = Math.Clamp(_slideIndex, 0, Document.Slides.Count - 1); _route.SetDocument(Document, _deckIndex, _documents.Count, _slideIndex);
    }
    private async Task RefreshDocumentsAsync(CancellationToken cancellationToken) => _documents = await _repository.ListAsync(cancellationToken);
    private int IndexOfDocument(Guid id) { for (var index = 0; index < _documents.Count; index++) if (_documents[index].Id == id) return index; return 0; }
    private async Task RunBusyAsync(Func<Task> action, string description)
    {
        if (_busy || _disposed) return; SetBusy(true);
        try { await action(); } catch (Exception ex) { _route.SetStatus($"Couldn’t {description}: {ex.Message}"); } finally { SetBusy(false); }
    }
    private void SetBusy(bool busy) { _busy = busy; _route.SetBusy(busy); }
    private static string SanitizeFileName(string title)
    {
        var value = string.IsNullOrWhiteSpace(title) ? "Untitled presentation" : title.Trim(); foreach (var invalid in Path.GetInvalidFileNameChars()) value = value.Replace(invalid, '_'); return value;
    }
    private void TryDeleteTemporary(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _route.SetStatus(_route.StatusText.Content + " Temporary-file cleanup failed: " + ex.Message); }
    }

    public void Dispose()
    {
        if (_disposed) return; _disposed = true; _autosaveTimer.Stop(); _autosaveTimer.Tick -= OnAutosaveTick; Loaded -= OnLoaded; DetachedFromVisualTree -= OnDetachedFromVisualTree;
        _route.PreviousDeckRequested -= OnPreviousDeckRequested; _route.NextDeckRequested -= OnNextDeckRequested; _route.NewDeckRequested -= OnNewDeckRequested; _route.SaveRequested -= OnSaveRequested; _route.ExportRequested -= OnExportRequested;
        _route.PreviousSlideRequested -= OnPreviousSlideRequested; _route.NextSlideRequested -= OnNextSlideRequested; _route.AddSlideRequested -= OnAddSlideRequested; _route.DeleteSlideRequested -= OnDeleteSlideRequested;
        _route.DeckTitleChanged -= OnDeckTitleChanged; _route.SlideTitleChanged -= OnSlideTitleChanged; _route.BodyChanged -= OnBodyChanged; _route.NotesChanged -= OnNotesChanged; _route.Dispose();
    }
}
