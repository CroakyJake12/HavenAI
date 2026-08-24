using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Events;
using Haven.Desktop.HavenUI.Backend;
using Haven.UI.Components;

namespace Haven.Desktop.Views.Pages.Write;

/// <summary>
/// Thin Avalonia backend host for the Haven.UI Write scene and the recovered local document services.
/// </summary>
public sealed partial class WritePage : UserControl, IDisposable
{
    private readonly HavenEventBus _bus;
    private readonly INotesRepository _repository;
    private readonly INotesImportExportService _formats;
    private readonly WordWriteHavenScene _route;
    private readonly DispatcherTimer _autosaveTimer;
    private readonly Guid? _initialDocumentId;
    private readonly INotesAiService? _ai;
    private readonly IOllamaClient? _aiModels;
    private IReadOnlyList<NotesDocumentSummary> _documents = [];
    private int _documentIndex;
    private int _saveRunning;
    private bool _initialized;
    private bool _busy;
    private bool _dirty;
    private bool _disposed;

    public WritePage(
        HavenEventBus bus,
        INotesRepository repository,
        INotesImportExportService formats,
        INotesAttachmentStore? attachments = null,
        Guid? initialDocumentId = null,
        INotesAiService? ai = null,
        IOllamaClient? aiModels = null)
    {
        _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _formats = formats ?? throw new ArgumentNullException(nameof(formats));
        _wordAttachments = attachments;
        _initialDocumentId = initialDocumentId;
        _ai = ai;
        _aiModels = aiModels;

        InitializeComponent();
        _route = new WordWriteHavenScene();
        Scene.Root = _route.Root;
        _route.LibraryRequested += OnLibraryRequested;
        _route.DocumentOpenRequested += OnDocumentOpenRequested;
        _route.AiProposalRequested += OnAiProposalRequested;
        _route.AiApplyRequested += OnAiApplyRequested;
        _route.AiRejectRequested += OnAiRejectRequested;
        _route.NewRequested += OnNewRequested;
        _route.ImportRequested += OnImportRequested;
        _route.ExportRequested += OnExportRequested;
        _route.SaveRequested += OnSaveRequested;
        _route.PreviousRequested += OnPreviousRequested;
        _route.NextRequested += OnNextRequested;
        _route.DocumentChanged += OnWordDocumentChanged;
        _route.ImageRequested += OnWordImageRequested;

        _autosaveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _autosaveTimer.Tick += OnAutosaveTick;
        Loaded += OnLoaded;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
    }

    public NotesDocument? Document { get; private set; }
    public bool IsDirty => _dirty;

    internal WordWriteHavenScene Route => _route;
    internal HavenSceneControl SceneHost => Scene;
    internal Haven.UI.Components.Page SceneRoot => _route.Root;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized || _disposed)
            return;

        _initialized = true;
        SetBusy(true);
        try
        {
            await RefreshDocumentsAsync(cancellationToken);
            await RefreshAiModelsAsync(cancellationToken);
            if (_initialDocumentId is { } initialDocumentId)
            {
                if (!await OpenDocumentByIdAsync(initialDocumentId, cancellationToken, saveBeforeSwitch: false))
                    ShowLibrary();
            }
            else
            {
                ShowLibrary();
            }

            _autosaveTimer.Start();
            _bus.Fire("Write.Opened");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _initialized = false;
            throw;
        }
        catch (Exception ex)
        {
            _initialized = false;
            _route.SetStatus("Couldnâ€™t open local documents: " + ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task RefreshAiModelsAsync(CancellationToken cancellationToken)
    {
        if (_aiModels is null) { _route.SetAiModels([]); return; }
        try { var models = await _aiModels.GetModelsAsync(cancellationToken); _route.SetAiModels(models.Select(model => model.Name).Where(name => !string.IsNullOrWhiteSpace(name)).ToArray()); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { _route.SetAiModels([]); }
    }

    public async Task<bool> SaveAsync(
        string reason = "Manual save",
        CancellationToken cancellationToken = default)
    {
        if (Document is null || (!_dirty && !Document.Recovery.HasUnsavedRecovery))
            return true;

        if (Interlocked.Exchange(ref _saveRunning, 1) != 0)
            return false;

        try
        {
            if (string.IsNullOrWhiteSpace(Document.Title))
            {
                Document.Title = "Untitled document";
                _route.SetTitleFromModel(Document.Title);
            }

            var result = await _repository.SaveAsync(Document, reason, cancellationToken);
            Document.Version = result.Version;
            Document.Recovery.HasUnsavedRecovery = false;
            _dirty = false;

            await RefreshDocumentsAsync(cancellationToken);
            _documentIndex = IndexOfDocument(Document.Id);
            UpdatePosition();
            _route.SetStatus($"Saved locally at {result.SavedAt.LocalDateTime:t} Â· v{result.Version}");
            _bus.Fire("Write.Saved");
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _route.SetStatus("Couldn't save this document: " + ex.Message);
            return false;
        }
        finally
        {
            Interlocked.Exchange(ref _saveRunning, 0);
        }
    }

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        await InitializeAsync();
        if (!_disposed)
            _autosaveTimer.Start();
    }

    private async void OnDetachedFromVisualTree(
        object? sender,
        Avalonia.VisualTreeAttachmentEventArgs e)
    {
        _autosaveTimer.Stop();
        if (_dirty && Document is not null)
            await SaveAsync("Autosave on leaving Write");
    }

    private async void OnAutosaveTick(object? sender, EventArgs e)
    {
        if (_disposed || !_dirty || _busy || Document is null)
            return;

        await SaveAsync("Autosave");
    }

    private async void OnLibraryRequested(object? sender, EventArgs e) =>
        await RunBusyAsync(
            () => ShowLibraryAsync(saveBeforeSwitch: true, CancellationToken.None),
            "open the document library");

    private async void OnDocumentOpenRequested(Guid documentId) =>
        await RunBusyAsync(
            async () => { await OpenDocumentByIdAsync(documentId, CancellationToken.None, saveBeforeSwitch: true); },
            "open this document");

    private async void OnAiProposalRequested(string instruction, bool allowDocumentContext, string modelName) =>
        await RunBusyAsync(() => ProposeAiAsync(instruction, allowDocumentContext, modelName, CancellationToken.None), "create an AI proposal");

    private async Task ProposeAiAsync(string instruction, bool allowDocumentContext, string modelName, CancellationToken cancellationToken)
    {
        if (Document is null) return;
        if (_ai is null) { _route.SetStatus("AI proposals are unavailable because the Notes AI service is not registered."); return; }
        var selectedText = _route.SelectedText;
        if (!allowDocumentContext && string.IsNullOrWhiteSpace(selectedText)) { _route.SetStatus("Select text, or explicitly allow document context, before requesting an AI edit."); return; }
        var context = allowDocumentContext ? string.Join("\n", NotesTextStatistics.EnumerateText(Document)) : string.Empty;
        var result = await _ai.ProposeAsync(new NotesAiProposalRequest(Document.Id, _route.SelectedBlockId, instruction, selectedText, context, modelName, allowDocumentContext, Document.Citations), cancellationToken);
        var change = new NotesAiChange { BlockId = _route.SelectedBlockId, Instruction = instruction.Trim(), OriginalContent = selectedText, ProposedContent = result.ProposedContent, Explanation = result.Explanation, CitationIds = result.CitationIds.ToList(), ProviderId = result.ProviderId, ModelName = result.ModelName, Status = NotesAiChangeStatus.Proposed, UserConsentRecorded = allowDocumentContext || !string.IsNullOrWhiteSpace(selectedText), SentDocumentContext = allowDocumentContext };
        Document.AiChanges.Add(change); MarkDirty(); _route.SetPendingAiChange(change); _route.SetStatus("AI proposal ready for review. Nothing has been applied."); _bus.Fire("Write.Ai.Proposed");
    }

    private async void OnAiApplyRequested(object? sender, EventArgs e)
    {
        if (await SaveAsync("Applied reviewed AI proposal")) { _route.SetStatus("Reviewed AI proposal applied and saved."); _bus.Fire("Write.Ai.Applied"); }
    }

    private async void OnAiRejectRequested(object? sender, EventArgs e)
    {
        if (await SaveAsync("Rejected AI proposal")) { _route.SetStatus("AI proposal rejected. Document content was unchanged."); _bus.Fire("Write.Ai.Rejected"); }
    }

    private async void OnNewRequested(object? sender, EventArgs e) =>
        await RunBusyAsync(
            () => CreateDocumentAsync(CancellationToken.None),
            "create a document");

    private async void OnSaveRequested(object? sender, EventArgs e) => await SaveAsync();

    private async void OnPreviousRequested(object? sender, EventArgs e) =>
        await RunBusyAsync(() => MoveAsync(-1), "open the previous document");

    private async void OnNextRequested(object? sender, EventArgs e) =>
        await RunBusyAsync(() => MoveAsync(1), "open the next document");

    private async void OnImportRequested(object? sender, EventArgs e) =>
        await RunBusyAsync(PickImportAsync, "import a document");

    private async void OnExportRequested(object? sender, EventArgs e) =>
        await RunBusyAsync(PickExportAsync, "export this document");

    internal async Task<bool> ImportFromPathAsync(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        if (Document is not null && _dirty
            && !await SaveAsync("Autosave before import", cancellationToken))
        {
            return false;
        }

        try
        {
            var imported = await _formats.ImportAsync(sourcePath, cancellationToken);
            var save = await _repository.SaveAsync(
                imported,
                "Imported " + Path.GetFileName(sourcePath),
                cancellationToken);
            imported.Version = save.Version;
            await RefreshDocumentsAsync(cancellationToken);
            Document = imported;
            _documentIndex = IndexOfDocument(imported.Id);
            _dirty = false;
            _route.SetDocument(imported, _documentIndex, _documents.Count);
            _route.SetStatus("Imported " + Path.GetFileName(sourcePath));
            _bus.Fire("Write.Document.Imported");
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _route.SetStatus("Couldnâ€™t import this document: " + ex.Message);
            return false;
        }
    }

    internal async Task<bool> ExportToPathAsync(
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        if (Document is null)
            return false;

        if (_dirty && !await SaveAsync("Save before export", cancellationToken))
            return false;

        try
        {
            await _formats.ExportAsync(Document, destinationPath, cancellationToken);
            _route.SetStatus(BuildExportStatus(destinationPath));
            _bus.Fire("Write.Document.Exported");
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _route.SetStatus("Couldnâ€™t export this document: " + ex.Message);
            return false;
        }
    }

    private async Task PickImportAsync()
    {
        var top = TopLevel.GetTopLevel(this);
        if (top?.StorageProvider is null)
        {
            _route.SetStatus("Import isnâ€™t available from this platform surface.");
            return;
        }

        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import a Write document",
            AllowMultiple = false,
            FileTypeFilter = BuildFileTypes(_formats.ImportExtensions)
        });
        var file = files.FirstOrDefault();
        if (file is null)
            return;

        var localPath = file.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(localPath))
        {
            await ImportFromPathAsync(localPath);
            return;
        }

        var extension = Path.GetExtension(file.Name);
        var temporaryPath = Path.Combine(
            Path.GetTempPath(),
            $"haven-write-import-{Guid.NewGuid():N}{extension}");
        try
        {
            await using (var source = await file.OpenReadAsync())
            await using (var destination = File.Create(temporaryPath))
                await source.CopyToAsync(destination);

            await ImportFromPathAsync(temporaryPath);
        }
        finally
        {
            DeleteTemporaryFile(temporaryPath);
        }
    }

    private async Task PickExportAsync()
    {
        if (Document is null)
            return;

        var top = TopLevel.GetTopLevel(this);
        if (top?.StorageProvider is null)
        {
            _route.SetStatus("Export isnâ€™t available from this platform surface.");
            return;
        }

        var defaultExtension = _formats.ExportExtensions
            .FirstOrDefault(extension => extension.Equals(".docx", StringComparison.OrdinalIgnoreCase))
            ?? _formats.ExportExtensions.FirstOrDefault()
            ?? ".haven-notes.json";
        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export Write document",
            SuggestedFileName = SanitizeFileName(Document.Title) + defaultExtension,
            DefaultExtension = defaultExtension.TrimStart('.'),
            FileTypeChoices = BuildFileTypes(_formats.ExportExtensions),
            ShowOverwritePrompt = true
        });
        if (file is null)
            return;

        var localPath = file.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(localPath))
        {
            await ExportToPathAsync(localPath);
            return;
        }

        var selectedExtension = Path.GetExtension(file.Name);
        if (string.IsNullOrWhiteSpace(selectedExtension))
            selectedExtension = defaultExtension;
        var temporaryPath = Path.Combine(
            Path.GetTempPath(),
            $"haven-write-export-{Guid.NewGuid():N}{selectedExtension}");
        try
        {
            if (!await ExportToPathAsync(temporaryPath))
                return;

            await using var source = File.OpenRead(temporaryPath);
            await using var destination = await file.OpenWriteAsync();
            destination.SetLength(0);
            await source.CopyToAsync(destination);
            await destination.FlushAsync();
            _route.SetStatus(BuildExportStatus(file.Name));
        }
        finally
        {
            DeleteTemporaryFile(temporaryPath);
        }
    }

    private static string BuildExportStatus(string path)
    {
        var name = Path.GetFileName(path);
        var extension = Path.GetExtension(path);
        var native = extension.Equals(".json", StringComparison.OrdinalIgnoreCase)
                     || path.EndsWith(".haven-notes.json", StringComparison.OrdinalIgnoreCase);
        return native
            ? "Exported " + name
            : "Exported " + name + " · This format may not preserve every Haven-only object or formatting detail.";
    }

    private static IReadOnlyList<FilePickerFileType> BuildFileTypes(
        IReadOnlyCollection<string> extensions) =>
        extensions
            .Where(extension => !string.IsNullOrWhiteSpace(extension))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(extension => new FilePickerFileType(
                extension.TrimStart('.').ToUpperInvariant() + " document")
            {
                Patterns = ["*" + extension]
            })
            .ToArray();

    private static string SanitizeFileName(string title)
    {
        var value = string.IsNullOrWhiteSpace(title) ? "Untitled document" : title.Trim();
        foreach (var invalid in Path.GetInvalidFileNameChars())
            value = value.Replace(invalid, '_');
        return value;
    }

    private void DeleteTemporaryFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _route.SetStatus(_route.StatusText.Content + " Temporary-file cleanup failed: " + ex.Message);
        }
    }

    private void OnTitleChanged(string title)
    {
        if (Document is null || Document.Title == title)
            return;

        Document.Title = title;
        MarkDirty();
    }

    private void OnBlockTextChanged(WriteBlockTextChangedEventArgs e)
    {
        if (Document is null)
            return;

        var block = Document.Sections
            .SelectMany(section => section.Pages)
            .SelectMany(page => page.Blocks)
            .FirstOrDefault(candidate => candidate.Id == e.BlockId);
        if (block is null)
            return;

        if (!e.IsList)
        {
            if (EditableText(block) == e.Text)
                return;

            ReplaceTextPreservingRuns(block, e.Text);
        }
        else if (block.List is not null)
        {
            var lines = e.Text
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n')
                .Split('\n');

            for (var index = 0; index < lines.Length; index++)
            {
                if (index < block.List.Items.Count)
                    block.List.Items[index].Text = lines[index];
                else
                    block.List.Items.Add(new NotesListItem { Text = lines[index], Level = 0 });
            }

            while (block.List.Items.Count > lines.Length)
                block.List.Items.RemoveAt(block.List.Items.Count - 1);
        }

        MarkDirty();
    }

    private static string EditableText(NotesBlock block) =>
        block.Runs.Count > 0
            ? string.Concat(block.Runs.Select(run => run.Text))
            : block.PlainText;

    private static void ReplaceTextPreservingRuns(NotesBlock block, string newText)
    {
        newText ??= string.Empty;
        if (block.Runs.Count == 0)
        {
            block.PlainText = newText;
            return;
        }

        var oldText = string.Concat(block.Runs.Select(run => run.Text));
        if (string.Equals(oldText, newText, StringComparison.Ordinal))
        {
            block.PlainText = newText;
            return;
        }

        var prefixLength = CommonPrefixLength(oldText, newText);
        var suffixLength = CommonSuffixLength(oldText, newText, prefixLength);
        var oldReplacementEnd = oldText.Length - suffixLength;
        var insertedText = newText.Substring(
            prefixLength,
            newText.Length - prefixLength - suffixLength);

        var starts = new int[block.Runs.Count];
        var lengths = new int[block.Runs.Count];
        var offset = 0;
        for (var index = 0; index < block.Runs.Count; index++)
        {
            starts[index] = offset;
            lengths[index] = block.Runs[index].Text.Length;
            offset += lengths[index];
        }

        var targetIndex = block.Runs.Count - 1;
        for (var index = 0; index < block.Runs.Count; index++)
        {
            var runEnd = starts[index] + lengths[index];
            if (prefixLength <= runEnd)
            {
                targetIndex = index;
                break;
            }
        }

        var targetInsertion = Math.Clamp(
            prefixLength - starts[targetIndex],
            0,
            lengths[targetIndex]);

        for (var index = 0; index < block.Runs.Count; index++)
        {
            var runStart = starts[index];
            var runEnd = runStart + lengths[index];
            var deleteStart = Math.Max(prefixLength, runStart);
            var deleteEnd = Math.Min(oldReplacementEnd, runEnd);
            if (deleteEnd <= deleteStart)
                continue;

            var localStart = deleteStart - runStart;
            block.Runs[index].Text = block.Runs[index].Text.Remove(
                localStart,
                deleteEnd - deleteStart);
        }

        if (insertedText.Length > 0)
        {
            var target = block.Runs[targetIndex];
            target.Text = target.Text.Insert(
                Math.Min(targetInsertion, target.Text.Length),
                insertedText);
        }

        block.PlainText = string.Concat(block.Runs.Select(run => run.Text));
    }

    private static int CommonPrefixLength(string left, string right)
    {
        var limit = Math.Min(left.Length, right.Length);
        var length = 0;
        while (length < limit && left[length] == right[length])
            length++;
        return length;
    }

    private static int CommonSuffixLength(string left, string right, int prefixLength)
    {
        var limit = Math.Min(left.Length, right.Length) - prefixLength;
        var length = 0;
        while (length < limit
               && left[left.Length - 1 - length] == right[right.Length - 1 - length])
        {
            length++;
        }

        return length;
    }

    private void MarkDirty()
    {
        if (Document is null)
            return;

        Document.UpdatedAt = DateTimeOffset.UtcNow;
        _dirty = true;
        _route.SetStatus("Unsaved changes Â· autosave is on");
    }


    private async Task<bool> OpenDocumentByIdAsync(Guid documentId, CancellationToken cancellationToken, bool saveBeforeSwitch)
    {
        var index = -1;
        for (var i = 0; i < _documents.Count; i++) if (_documents[i].Id == documentId) { index = i; break; }
        if (index < 0) { _route.SetStatus("That local document no longer exists."); return false; }
        await OpenDocumentAtAsync(index, cancellationToken, saveBeforeSwitch);
        return Document?.Id == documentId;
    }

    private async Task ShowLibraryAsync(bool saveBeforeSwitch, CancellationToken cancellationToken)
    {
        if (saveBeforeSwitch && Document is not null && _dirty && !await SaveAsync("Autosave before opening document library", cancellationToken)) return;
        await RefreshDocumentsAsync(cancellationToken);
        ShowLibrary();
    }

    private void ShowLibrary()
    {
        Document = null; _dirty = false; _documentIndex = 0;
        _route.SetLibrary(_documents);
        _route.SetStatus(_documents.Count == 0 ? "No local documents yet. Create one or import a supported file." : "Choose a local document to open.");
        _bus.Fire("Write.Library.Opened");
    }

    private async Task MoveAsync(int offset)
    {
        if (_documents.Count <= 1)
            return;

        var next = (_documentIndex + offset + _documents.Count) % _documents.Count;
        await OpenDocumentAtAsync(next, CancellationToken.None, saveBeforeSwitch: true);
    }

    public async Task<bool> OpenDocumentAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        if (_disposed) return false;
        await InitializeAsync(cancellationToken);
        await RefreshDocumentsAsync(cancellationToken);
        var index = -1;
        for (var candidate = 0; candidate < _documents.Count; candidate++)
        {
            if (_documents[candidate].Id != documentId) continue;
            index = candidate;
            break;
        }

        if (index < 0)
        {
            _route.SetStatus("That local document no longer exists.");
            return false;
        }

        await OpenDocumentAtAsync(index, cancellationToken, saveBeforeSwitch: true);
        return Document?.Id == documentId;
    }

    private async Task CreateDocumentAsync(CancellationToken cancellationToken)
    {
        if (Document is not null && _dirty
            && !await SaveAsync("Autosave before creating document", cancellationToken))
        {
            return;
        }

        var document = NotesDocument.Create("Untitled document");
        var result = await _repository.SaveAsync(document, "Write document created", cancellationToken);
        document.Version = result.Version;

        await RefreshDocumentsAsync(cancellationToken);
        Document = document;
        _documentIndex = IndexOfDocument(document.Id);
        _dirty = false;
        _route.SetDocument(document, _documentIndex, _documents.Count);
        _route.SetStatus("Created a new local Write document.");
        _bus.Fire("Write.Document.Created");
    }

    private async Task OpenDocumentAtAsync(
        int index,
        CancellationToken cancellationToken,
        bool saveBeforeSwitch)
    {
        if (_documents.Count == 0)
            return;

        if (saveBeforeSwitch
            && Document is not null
            && _dirty
            && !await SaveAsync("Autosave before switching document", cancellationToken))
        {
            return;
        }

        index = Math.Clamp(index, 0, _documents.Count - 1);
        var loaded = await _repository.LoadAsync(_documents[index].Id, cancellationToken);
        if (loaded is null)
        {
            await RefreshDocumentsAsync(cancellationToken);
            _route.SetStatus("That local document no longer exists.");
            return;
        }

        Document = loaded;
        _documentIndex = index;
        _dirty = false;
        _route.SetDocument(loaded, index, _documents.Count);
        _route.SetStatus(
            loaded.Recovery.HasUnsavedRecovery
                ? "Recovered the last valid local version. Review it, then save to confirm recovery."
                : "Saved locally Â· autosave is on");
        _bus.Fire("Write.Document.Opened");
    }

    private async Task RefreshDocumentsAsync(CancellationToken cancellationToken) =>
        _documents = await _repository.ListAsync(cancellationToken);

    private int IndexOfDocument(Guid id)
    {
        for (var index = 0; index < _documents.Count; index++)
        {
            if (_documents[index].Id == id)
                return index;
        }

        return 0;
    }

    private void UpdatePosition()
    {
        if (Document is null)
            return;

        _route.DocumentPositionText.Content = _documents.Count == 0
            ? "Local document"
            : $"{_documentIndex + 1} of {_documents.Count} Â· v{Document.Version}";
    }

    private async Task RunBusyAsync(Func<Task> action, string description)
    {
        if (_busy || _disposed)
            return;

        SetBusy(true);
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            _route.SetStatus($"Couldnâ€™t {description}: {ex.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        _route.SetBusy(busy);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _autosaveTimer.Stop();
        _autosaveTimer.Tick -= OnAutosaveTick;
        Loaded -= OnLoaded;
        DetachedFromVisualTree -= OnDetachedFromVisualTree;
        _route.LibraryRequested -= OnLibraryRequested;
        _route.DocumentOpenRequested -= OnDocumentOpenRequested;
        _route.AiProposalRequested -= OnAiProposalRequested;
        _route.AiApplyRequested -= OnAiApplyRequested;
        _route.AiRejectRequested -= OnAiRejectRequested;
        _route.NewRequested -= OnNewRequested;
        _route.ImportRequested -= OnImportRequested;
        _route.ExportRequested -= OnExportRequested;
        _route.SaveRequested -= OnSaveRequested;
        _route.PreviousRequested -= OnPreviousRequested;
        _route.NextRequested -= OnNextRequested;
        _route.TitleChanged -= OnTitleChanged;
        _route.BlockTextChanged -= OnBlockTextChanged;
        _route.Dispose();
    }
}
