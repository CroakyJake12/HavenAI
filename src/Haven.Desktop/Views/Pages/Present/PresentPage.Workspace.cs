using System.Globalization;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Haven.Application;
using Haven.Core;

namespace Haven.Desktop.Views.Pages.Present;

public sealed partial class PresentPage
{
    private void InitializeWorkspace()
    {
        _route.OpenDocumentRequested += OnOpenDocumentRequested;
        _route.PinDocumentRequested += OnPinDocumentRequested;
        _route.TemplateRequested += OnTemplateRequested;
        _route.ReturnToLibraryRequested += OnReturnToLibraryRequested;
        _route.AiCreateRequested += OnAiCreateRequested;
        _route.AddImageRequested += OnAddImageRequested;
        _route.AddMediaRequested += OnAddMediaRequested;
        _route.AddTableRequested += OnAddTableRequested;
        _route.AddChartRequested += OnAddChartRequested;
        _route.InlineTextChanged += OnInlineTextChanged;
        _route.TableSizeRequested += OnTableSizeRequested;
        _route.TableDataRequested += OnTableDataRequested;
        _route.ChartTypeRequested += OnChartTypeRequested;
        _route.ChartDataRequested += OnChartDataRequested;
        _route.DistributeHorizontalRequested += OnDistributeHorizontalRequested;
        _route.DistributeVerticalRequested += OnDistributeVerticalRequested;
        _route.AiEditRequested += OnAiEditRequested;
        _route.ShapeFillRequested += OnShapeFillRequested;
        _route.ShapeStrokeRequested += OnShapeStrokeRequested;
        _route.ShapeStrokeWidthRequested += OnShapeStrokeWidthRequested;
    }

    public async Task<bool> OpenDocumentAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        if (!_initialized) await InitializeAsync(cancellationToken);
        await RefreshDocumentsAsync(cancellationToken);
        var index = -1;
        for (var candidate = 0; candidate < _documents.Count; candidate++)
            if (_documents[candidate].Id == documentId) { index = candidate; break; }
        if (index < 0)
        {
            _route.SetLibrary(_documents);
            _route.SetStatus("That presentation is no longer available locally.");
            return false;
        }
        await OpenDeckAtAsync(index, cancellationToken, saveBeforeSwitch: Document is not null);
        return Document?.Id == documentId;
    }

    public PresentEditApplyResult ApplyAiProposal(PresentEditProposal proposal)
    {
        if (_editor is null) throw new InvalidOperationException("Open a presentation before applying an AI edit proposal.");
        var result = PresentAiEdits.Apply(_editor, proposal);
        _route.SetStatus($"Applied {result.AppliedOperations} semantic AI edit{(result.AppliedOperations == 1 ? string.Empty : "s")}.");
        _bus.Fire("Present.Ai.Applied");
        return result;
    }

    private async void OnOpenDocumentRequested(Guid documentId) =>
        await RunBusyAsync(async () => { _ = await OpenDocumentAsync(documentId); }, "open this presentation");

    private async void OnPinDocumentRequested(Guid documentId) =>
        await RunBusyAsync(() => TogglePinnedAsync(documentId), "update the pinned presentation");

    private async Task TogglePinnedAsync(Guid documentId)
    {
        var document = Document?.Id == documentId ? Document : await _repository.LoadAsync(documentId, CancellationToken.None);
        if (document is null) { await RefreshDocumentsAsync(CancellationToken.None); _route.SetLibrary(_documents); return; }
        var pinned = document.Metadata.TryGetValue("pinned", out var raw) && bool.TryParse(raw, out var parsed) && parsed;
        document.Metadata["pinned"] = (!pinned).ToString(CultureInfo.InvariantCulture);
        var result = await _repository.SaveAsync(document, pinned ? "Presentation unpinned" : "Presentation pinned", CancellationToken.None);
        document.Version = result.Version;
        await RefreshDocumentsAsync(CancellationToken.None);
        if (Document?.Id == documentId) { Document = document; RenderCurrent(); }
        else _route.SetLibrary(_documents);
    }

    private async void OnTemplateRequested(string templateId) =>
        await RunBusyAsync(() => CreateTemplateAsync(templateId), "create this template");

    private async Task CreateTemplateAsync(string templateId)
    {
        if (Document is not null && _dirty && !await SaveAsync("Autosave before creating presentation")) return;
        var document = templateId switch
        {
            "lesson" => CreateLessonTemplate(),
            "pitch" => CreatePitchTemplate(),
            _ => PresentDocument.Create("Untitled presentation")
        };
        var result = await _repository.SaveAsync(document, $"Created from {templateId} template", CancellationToken.None);
        document.Version = result.Version;
        await RefreshDocumentsAsync(CancellationToken.None);
        Document = document; _deckIndex = IndexOfDocument(document.Id); _slideIndex = 0; _dirty = false;
        AttachEditor(document); RenderCurrent();
        _route.SetStatus("Template created · autosave is on");
        _bus.Fire("Present.Document.TemplateCreated");
    }

    private static PresentDocument CreateLessonTemplate()
    {
        var document = PresentDocument.Create("Lesson presentation");
        document.Slides[0].Title = "Lesson title";
        document.Slides[0].GetOrCreateBodyText().Text = "Learning objectives";
        var content = PresentSlide.Create(1); content.LayoutId = document.Layouts[0].Id; content.Title = "Explain"; content.GetOrCreateBodyText().Text = "Key idea and worked example";
        var review = PresentSlide.Create(2); review.LayoutId = document.Layouts[0].Id; review.Title = "Review"; review.GetOrCreateBodyText().Text = "Check understanding and next steps";
        document.Slides.Add(content); document.Slides.Add(review); document.Normalize(); return document;
    }

    private static PresentDocument CreatePitchTemplate()
    {
        var document = PresentDocument.Create("Project pitch");
        document.Slides[0].Title = "The idea"; document.Slides[0].GetOrCreateBodyText().Text = "One clear sentence describing the proposal";
        foreach (var title in new[] { "The problem", "The solution", "Evidence", "Next step" })
        {
            var slide = PresentSlide.Create(document.Slides.Count); slide.LayoutId = document.Layouts[0].Id; slide.Title = title; document.Slides.Add(slide);
        }
        document.Normalize(); return document;
    }

    private async void OnReturnToLibraryRequested(object? sender, EventArgs e) =>
        await RunBusyAsync(ReturnToLibraryAsync, "return to the presentation library");

    private async Task ReturnToLibraryAsync()
    {
        if (_dirty && !await SaveAsync("Autosave before returning to presentation library")) return;
        if (_editor is not null) _editor.Changed -= OnEditorChanged;
        Document = null; _editor = null; _playback = null; _slideIndex = 0; _dirty = false;
        await RefreshDocumentsAsync(CancellationToken.None);
        _route.SetLibrary(_documents);
        _bus.Fire("Present.Library.Opened");
    }

    private void OnAiCreateRequested(object? sender, EventArgs e)
    {
        _bus.Fire("Present.Ai.CreateRequested");
        _route.SetStatus("AI deck generation is not connected to a generator in this build; no presentation was fabricated.");
    }

    private void OnAiEditRequested(object? sender, EventArgs e)
    {
        if (_editor is null) return;
        _ = PresentAiEdits.CaptureSelection(_editor);
        _bus.Fire("Present.Ai.EditRequested");
        _route.SetStatus("AI edit request exposed with the current semantic selection; edits apply only through a returned PresentEditProposal.");
    }

    private void OnShapeFillRequested(string? color)
    {
        if (_editor?.SelectedElements is not { Count: 1 } selected || selected[0].Kind != PresentElementKind.Shape) return;
        if (selected[0].VectorShape is not null)
        {
            _editor.SetSelectedVectorFill(color);
            return;
        }
        var style = selected[0].Style;
        _editor.SetSelectedElementStyle(new PresentElementStyle
        {
            FillColor = string.IsNullOrWhiteSpace(color) ? "#00FFFFFF" : color!,
            StrokeColor = style.StrokeColor,
            StrokeWidth = style.StrokeWidth,
            CornerRadius = style.CornerRadius,
            Shadow = style.Shadow
        });
    }

    private void OnShapeStrokeRequested(string? color)
    {
        if (_editor?.SelectedElements is not { Count: 1 } selected || selected[0].Kind != PresentElementKind.Shape) return;
        if (selected[0].VectorShape is not null)
        {
            _editor.SetSelectedVectorStroke(color);
            return;
        }
        var style = selected[0].Style;
        _editor.SetSelectedElementStyle(new PresentElementStyle
        {
            FillColor = style.FillColor,
            StrokeColor = string.IsNullOrWhiteSpace(color) ? "#00000000" : color!,
            StrokeWidth = string.IsNullOrWhiteSpace(color) ? 0 : Math.Max(1, style.StrokeWidth),
            CornerRadius = style.CornerRadius,
            Shadow = style.Shadow
        });
    }

    private void OnShapeStrokeWidthRequested(double width)
    {
        if (_editor?.SelectedElements is not { Count: 1 } selected || selected[0].Kind != PresentElementKind.Shape) return;
        if (selected[0].VectorShape is not null)
        {
            _editor.SetSelectedVectorStrokeWidth(width);
            return;
        }
        var style = selected[0].Style;
        _editor.SetSelectedElementStyle(new PresentElementStyle
        {
            FillColor = style.FillColor,
            StrokeColor = style.StrokeColor,
            StrokeWidth = Math.Max(0, width),
            CornerRadius = style.CornerRadius,
            Shadow = style.Shadow
        });
    }

    private void OnAddTableRequested(object? sender, EventArgs e)
    {
        if (_editor is null) return; _editor.AddTable(_editor.Selection.SlideId, 3, 3); _bus.Fire("Present.Object.TableAdded");
    }

    private void OnAddChartRequested(object? sender, EventArgs e)
    {
        if (_editor is null) return; _editor.AddChart(_editor.Selection.SlideId); _bus.Fire("Present.Object.ChartAdded");
    }

    private async void OnAddImageRequested(object? sender, EventArgs e) => await PickAssetAsync(image: true);
    private async void OnAddMediaRequested(object? sender, EventArgs e) => await PickAssetAsync(image: false);

    private async Task PickAssetAsync(bool image)
    {
        if (_editor is null) return;
        var top = TopLevel.GetTopLevel(this);
        if (top?.StorageProvider is null) { _route.SetStatus("File insertion is unavailable from this platform surface."); return; }
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = image ? "Insert image" : "Insert media", AllowMultiple = false,
            FileTypeFilter = image
                ? [new FilePickerFileType("Images") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.webp", "*.gif"] }]
                : [new FilePickerFileType("Media") { Patterns = ["*.mp4", "*.webm", "*.mp3", "*.wav", "*.m4a"] }]
        });
        if (files.Count == 0) return;
        var file = files[0]; var localPath = file.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(localPath))
        {
            _route.SetStatus("This storage provider does not expose a durable local asset path yet, so the object was not inserted.");
            return;
        }
        if (image) _editor.AddImage(_editor.Selection.SlideId, localPath, file.Name);
        else _editor.AddMedia(_editor.Selection.SlideId, localPath, MediaType(localPath), file.Name);
        _bus.Fire(image ? "Present.Object.ImageAdded" : "Present.Object.MediaAdded");
    }

    private static string MediaType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".mp4" => "video/mp4", ".webm" => "video/webm", ".mp3" => "audio/mpeg", ".wav" => "audio/wav", ".m4a" => "audio/mp4", _ => "application/octet-stream"
    };

    private void OnInlineTextChanged(Guid elementId, string text)
    {
        if (_editor is null) return; _editor.SetElementText(_editor.Selection.SlideId, elementId, text);
    }

    private void OnTableSizeRequested(int rows, int columns)
    {
        if (_editor?.SelectedElements is not { Count: 1 } selected || selected[0].Kind != PresentElementKind.Table) return;
        _editor.ResizeTable(_editor.Selection.SlideId, selected[0].Id, rows, columns);
    }

    private void OnTableDataRequested(string text)
    {
        if (_editor?.SelectedElements is not { Count: 1 } selected || selected[0].Kind != PresentElementKind.Table) return;
        var rows = (text ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        if (rows.Length == 0) return;
        var parsed = rows.Select(row => row.Split('|').Select(cell => cell.Trim()).ToArray()).ToArray();
        var rowCount = Math.Clamp(parsed.Length, 1, 100);
        var columnCount = Math.Clamp(parsed.Max(row => row.Length), 1, 100);
        _editor.ResizeTable(_editor.Selection.SlideId, selected[0].Id, rowCount, columnCount);
        for (var row = 0; row < rowCount; row++)
        for (var column = 0; column < columnCount; column++)
            _editor.SetTableCellText(_editor.Selection.SlideId, selected[0].Id, row, column,
                column < parsed[row].Length ? parsed[row][column] : string.Empty);
        _route.SetStatus($"Updated {rowCount} × {columnCount} table cells.");
    }

    private void OnChartTypeRequested(PresentChartType type)
    {
        if (_editor?.SelectedElements is not { Count: 1 } selected || selected[0].Kind != PresentElementKind.Chart) return;
        _editor.SetChartType(_editor.Selection.SlideId, selected[0].Id, type);
    }

    private void OnChartDataRequested(string text)
    {
        if (_editor?.SelectedElements is not { Count: 1 } selected || selected[0].Kind != PresentElementKind.Chart) return;
        var categories = new List<string>(); var values = new List<double>();
        foreach (var line in (text ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var comma = line.LastIndexOf(','); if (comma <= 0) continue;
            if (!double.TryParse(line[(comma + 1)..].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) continue;
            categories.Add(line[..comma].Trim()); values.Add(value);
        }
        if (categories.Count == 0) { _route.SetStatus("Chart data needs lines in ‘Category, value’ format."); return; }
        _editor.SetChartData(_editor.Selection.SlideId, selected[0].Id, categories, [new PresentChartSeries { Name = "Series 1", Values = values }]);
    }

    private void OnDistributeHorizontalRequested(object? sender, EventArgs e) => _editor?.DistributeSelection(PresentDistribution.Horizontal);
    private void OnDistributeVerticalRequested(object? sender, EventArgs e) => _editor?.DistributeSelection(PresentDistribution.Vertical);

    private void OnPlaybackPreviousRequested(object? sender, EventArgs e)
    {
        if (_playback is null || Document is null) return; _playback.Previous(); _route.SetPlayback(Document, _playback.Frame);
    }

    private void OnPlaybackNextRequested(object? sender, EventArgs e)
    {
        if (_playback is null || Document is null) return; _playback.Advance(); _route.SetPlayback(Document, _playback.Frame);
    }

    private void OnPlaybackExitRequested(object? sender, EventArgs e)
    {
        _playback = null; _route.HidePlayback(); _route.SetStatus("Presentation ended."); _bus.Fire("Present.Playback.Ended");
    }

    private void DisposeWorkspace()
    {
        _route.OpenDocumentRequested -= OnOpenDocumentRequested; _route.PinDocumentRequested -= OnPinDocumentRequested; _route.TemplateRequested -= OnTemplateRequested;
        _route.ReturnToLibraryRequested -= OnReturnToLibraryRequested; _route.AiCreateRequested -= OnAiCreateRequested; _route.AiEditRequested -= OnAiEditRequested;
        _route.AddImageRequested -= OnAddImageRequested; _route.AddMediaRequested -= OnAddMediaRequested; _route.AddTableRequested -= OnAddTableRequested; _route.AddChartRequested -= OnAddChartRequested;
        _route.InlineTextChanged -= OnInlineTextChanged; _route.TableSizeRequested -= OnTableSizeRequested; _route.TableDataRequested -= OnTableDataRequested; _route.ChartTypeRequested -= OnChartTypeRequested; _route.ChartDataRequested -= OnChartDataRequested;
        _route.DistributeHorizontalRequested -= OnDistributeHorizontalRequested; _route.DistributeVerticalRequested -= OnDistributeVerticalRequested;
        _route.ShapeFillRequested -= OnShapeFillRequested; _route.ShapeStrokeRequested -= OnShapeStrokeRequested; _route.ShapeStrokeWidthRequested -= OnShapeStrokeWidthRequested;
    }
}
