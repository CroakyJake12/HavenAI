using System.Text.Json;
using Haven.Core;

namespace Haven.Application;

public enum WriteCharacterFormat { Bold, Italic, Underline, StrikeThrough }
public sealed record WriteFindResult(Guid BlockId, string Kind, string Snippet, int Offset);

/// <summary>Framework-neutral word-processing operations over Haven's persisted Notes document model.</summary>
public sealed class WriteDocumentEditor
{
    private const int HistoryLimit = 50;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly List<NotesDocument> _undo = [];
    private readonly List<NotesDocument> _redo = [];

    public WriteDocumentEditor(NotesDocument document)
    {
        Document = document ?? throw new ArgumentNullException(nameof(document));
        if (Blocks().FirstOrDefault() is { } first) SelectBlock(first.Id);
    }

    public event EventHandler? Changed;
    public NotesDocument Document { get; }
    public Guid? SelectedBlockId { get; private set; }
    public int ActiveRunIndex { get; private set; }
    public int CaretIndex { get; private set; }
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public NotesBlock? SelectedBlock => SelectedBlockId is { } id ? Blocks().FirstOrDefault(block => block.Id == id) : null;
    public NotesTextRun? ActiveRun => SelectedBlock is { } block ? RunAt(block, ActiveRunIndex) : null;
    public NotesStatistics Statistics => NotesTextStatistics.Calculate(Document);
    public IReadOnlyList<NotesBlock> Blocks() => Document.Sections.SelectMany(section => section.Pages).SelectMany(page => page.Blocks).OrderBy(block => block.Order).ToArray();

    public void SelectBlock(Guid blockId, int caretIndex = 0)
    {
        var block = Blocks().FirstOrDefault(candidate => candidate.Id == blockId);
        if (block is null) return;
        SelectedBlockId = block.Id; CaretIndex = Math.Max(0, caretIndex); ActiveRunIndex = RunIndexAtCaret(block, CaretIndex);
    }

    public void SetTitle(string title)
    {
        var next = string.IsNullOrWhiteSpace(title) ? "Untitled document" : title;
        if (Document.Title == next) return;
        Mutate(() => Document.Title = next, "Changed title");
    }

    public void ReplaceSelectedText(string text, int caretIndex)
    {
        if (SelectedBlock is not { } block || block.Kind is not (NotesBlockKind.Paragraph or NotesBlockKind.Heading or NotesBlockKind.Quote or NotesBlockKind.Code)) return;
        var next = text ?? string.Empty;
        if (EditableText(block) == next) { CaretIndex = Math.Max(0, caretIndex); ActiveRunIndex = RunIndexAtCaret(block, CaretIndex); return; }
        Mutate(() => ReplaceTextPreservingRuns(block, next), "Edited text");
        CaretIndex = Math.Clamp(caretIndex, 0, next.Length); ActiveRunIndex = RunIndexAtCaret(block, CaretIndex);
    }

    public void ApplyStyle(string styleId)
    {
        if (SelectedBlock is not { } block) return;
        var style = Document.Styles.FirstOrDefault(value => value.Id.Equals(styleId, StringComparison.OrdinalIgnoreCase));
        if (style is null) return;
        Mutate(() =>
        {
            block.StyleId = style.Id;
            block.Kind = style.Id switch { "heading-1" or "heading-2" => NotesBlockKind.Heading, "quote" => NotesBlockKind.Quote, "code" => NotesBlockKind.Code, _ => NotesBlockKind.Paragraph };
            CopyParagraph(style.Paragraph, block.Paragraph); EnsureRuns(block); foreach (var run in block.Runs) CopyCharacter(style.Character, run);
        }, "Applied style " + style.Name);
    }

    public void ToggleCharacter(WriteCharacterFormat format)
    {
        if (!TryActiveRun(out var run)) return;
        Mutate(() => { switch (format) { case WriteCharacterFormat.Bold: run.Bold = !run.Bold; break; case WriteCharacterFormat.Italic: run.Italic = !run.Italic; break; case WriteCharacterFormat.Underline: run.Underline = !run.Underline; break; case WriteCharacterFormat.StrikeThrough: run.StrikeThrough = !run.StrikeThrough; break; } }, "Changed character formatting");
    }

    public void SetFontFamily(string value) => SetRun(run => run.FontFamily = string.IsNullOrWhiteSpace(value) ? "Montserrat" : value.Trim(), "Changed font");
    public void SetFontSize(double value) => SetRun(run => run.FontSize = Math.Clamp(Finite(value, run.FontSize), 4, 300), "Changed font size");
    public void SetForeground(string value) => SetRun(run => run.Foreground = NormaliseColour(value, run.Foreground), "Changed text colour");
    public void SetBackground(string value) => SetRun(run => run.Background = NormaliseColour(value, run.Background), "Changed highlight");
    public void SetLink(string? value) => SetRun(run => run.Link = string.IsNullOrWhiteSpace(value) ? null : value.Trim(), "Changed link");

    public bool SplitRunAtCaret()
    {
        if (SelectedBlock is not { } block) return false; EnsureRuns(block); ActiveRunIndex = RunIndexAtCaret(block, CaretIndex); var start = block.Runs.Take(ActiveRunIndex).Sum(run => run.Text.Length); var run = block.Runs[ActiveRunIndex]; var local = Math.Clamp(CaretIndex - start, 0, run.Text.Length); if (local <= 0 || local >= run.Text.Length) return false;
        Mutate(() => { var after = CloneRun(run); after.Id = Guid.NewGuid(); after.Text = run.Text[local..]; run.Text = run.Text[..local]; block.Runs.Insert(ActiveRunIndex + 1, after); block.PlainText = string.Concat(block.Runs.Select(value => value.Text)); ActiveRunIndex++; }, "Split formatted text run"); return true;
    }

    public bool MergeRunWithPrevious()
    {
        if (SelectedBlock is not { } block) return false; EnsureRuns(block); if (ActiveRunIndex <= 0 || ActiveRunIndex >= block.Runs.Count) return false;
        Mutate(() => { var previous = block.Runs[ActiveRunIndex - 1]; previous.Text += block.Runs[ActiveRunIndex].Text; block.Runs.RemoveAt(ActiveRunIndex); ActiveRunIndex--; block.PlainText = string.Concat(block.Runs.Select(value => value.Text)); }, "Merged formatted text runs"); return true;
    }

    public void SetAlignment(NotesTextAlignment value) => SetParagraph(format => format.Alignment = value, "Changed paragraph alignment");
    public void SetLineSpacing(double value) => SetParagraph(format => format.LineSpacing = Math.Clamp(Finite(value, format.LineSpacing), .5, 10), "Changed line spacing");
    public void SetLeftIndent(double value) => SetParagraph(format => format.IndentLeft = Math.Clamp(Finite(value, format.IndentLeft), 0, 1000), "Changed left indent");
    public void SetFirstLineIndent(double value) => SetParagraph(format => format.FirstLineIndent = Math.Clamp(Finite(value, format.FirstLineIndent), -1000, 1000), "Changed first-line indent");
    public void SetSpaceAfter(double value) => SetParagraph(format => format.SpaceAfter = Math.Clamp(Finite(value, format.SpaceAfter), 0, 1000), "Changed paragraph spacing");

    public NotesBlock InsertBlock(NotesBlockKind kind, NotesListKind listKind = NotesListKind.Bulleted)
    {
        var page = PageForSelection(); var index = SelectedBlock is { } selected ? page.Blocks.IndexOf(selected) + 1 : page.Blocks.Count;
        var block = kind switch { NotesBlockKind.Heading => NotesBlock.Heading("Heading"), NotesBlockKind.Quote => new NotesBlock { Kind = NotesBlockKind.Quote, StyleId = "quote", PlainText = "Quote" }, NotesBlockKind.Code => new NotesBlock { Kind = NotesBlockKind.Code, StyleId = "code", PlainText = "Code" }, NotesBlockKind.List => new NotesBlock { Kind = NotesBlockKind.List, List = new NotesListData { Kind = listKind, Items = [new NotesListItem { Text = "List item" }] } }, NotesBlockKind.Table => NotesBlock.TableBlock(3, 3), NotesBlockKind.Equation => NotesBlock.EquationBlock(), NotesBlockKind.Divider => new NotesBlock { Kind = NotesBlockKind.Divider }, _ => NotesBlock.CreateParagraph() };
        Mutate(() => { page.Blocks.Insert(Math.Clamp(index, 0, page.Blocks.Count), block); Renumber(page); SelectedBlockId = block.Id; ActiveRunIndex = 0; CaretIndex = 0; }, "Inserted " + kind); return block;
    }

    public NotesBlock InsertMedia(NotesMediaData media)
    {
        ArgumentNullException.ThrowIfNull(media); var page = PageForSelection(); var index = SelectedBlock is { } selected ? page.Blocks.IndexOf(selected) + 1 : page.Blocks.Count; var block = new NotesBlock { Kind = NotesBlockKind.Image, Media = media }; Mutate(() => { page.Blocks.Insert(Math.Clamp(index, 0, page.Blocks.Count), block); Renumber(page); SelectedBlockId = block.Id; }, "Inserted image"); return block;
    }

    public NotesBlock InsertCustomShape(DocumentVectorShape shape, Guid? gallerySourceId = null)
    {
        ArgumentNullException.ThrowIfNull(shape);
        var page = PageForSelection();
        var index = SelectedBlock is { } selected ? page.Blocks.IndexOf(selected) + 1 : page.Blocks.Count;
        var block = new NotesBlock { Kind = NotesBlockKind.Shape, VectorShape = DocumentVectorShapes.CloneForInsertion(shape, gallerySourceId) };
        Mutate(() => { page.Blocks.Insert(Math.Clamp(index, 0, page.Blocks.Count), block); Renumber(page); SelectedBlockId = block.Id; ActiveRunIndex = 0; CaretIndex = 0; }, "Inserted custom vector shape");
        return block;
    }

    public bool UpdateSelectedCustomShape(Action<DocumentVectorShapeEditor> edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        if (SelectedBlock?.VectorShape is not { } shape) return false;
        var vectorEditor = new DocumentVectorShapeEditor(DocumentVectorShapes.Clone(shape));
        edit(vectorEditor);
        Mutate(() => SelectedBlock!.VectorShape = DocumentVectorShapes.Clone(vectorEditor.Shape), "Edited custom vector shape");
        return true;
    }

    public bool DeleteSelected() { if (SelectedBlock is not { } block) return false; var page = PageFor(block); if (page.Blocks.Count <= 1) return false; var index = page.Blocks.IndexOf(block); Mutate(() => { page.Blocks.Remove(block); Renumber(page); SelectedBlockId = page.Blocks[Math.Clamp(index - 1, 0, page.Blocks.Count - 1)].Id; }, "Deleted block"); return true; }
    public bool MoveSelected(int direction) { if (SelectedBlock is not { } block || direction == 0) return false; var page = PageFor(block); var index = page.Blocks.IndexOf(block); var target = Math.Clamp(index + Math.Sign(direction), 0, page.Blocks.Count - 1); if (target == index) return false; Mutate(() => { page.Blocks.RemoveAt(index); page.Blocks.Insert(target, block); Renumber(page); }, direction < 0 ? "Moved block up" : "Moved block down"); return true; }

    public void SetListKind(NotesListKind kind) { if (SelectedBlock?.List is not { } list) return; Mutate(() => list.Kind = kind, "Changed list type"); }
    public void AddListItem() { if (SelectedBlock?.List is not { } list) return; Mutate(() => list.Items.Add(new NotesListItem { Text = "New item" }), "Added list item"); }
    public void UpdateListItem(Guid itemId, string text) { if (SelectedBlock?.List?.Items.FirstOrDefault(value => value.Id == itemId) is not { } item || item.Text == text) return; Mutate(() => item.Text = text ?? string.Empty, "Edited list item"); }
    public void ToggleListItem(Guid itemId, bool value) { if (SelectedBlock?.List?.Items.FirstOrDefault(item => item.Id == itemId) is not { } item || item.Checked == value) return; Mutate(() => item.Checked = value, "Changed checklist item"); }
    public void SetListItemLevel(Guid itemId, int level) { if (SelectedBlock?.List?.Items.FirstOrDefault(item => item.Id == itemId) is not { } item) return; var next = Math.Clamp(level, 0, 8); if (item.Level == next) return; Mutate(() => item.Level = next, "Changed list nesting"); }

    public void UpdateTableCell(Guid cellId, string text) { if (SelectedBlock?.Table?.Rows.SelectMany(row => row.Cells).FirstOrDefault(cell => cell.Id == cellId) is not { } cell || cell.Text == text) return; Mutate(() => cell.Text = text ?? string.Empty, "Edited table cell"); }
    public void AddTableRow() { if (SelectedBlock?.Table is not { } table || table.Rows.Count == 0) return; Mutate(() => { var row = new NotesTableRow(); var columns = table.Rows.Max(value => value.Cells.Count); for (var i = 0; i < columns; i++) row.Cells.Add(new NotesTableCell()); table.Rows.Add(row); }, "Added table row"); }
    public void AddTableColumn() { if (SelectedBlock?.Table is not { } table) return; Mutate(() => { foreach (var row in table.Rows) row.Cells.Add(new NotesTableCell()); }, "Added table column"); }
    public void RemoveTableRow() { if (SelectedBlock?.Table is not { Rows.Count: > 1 } table) return; Mutate(() => table.Rows.RemoveAt(table.Rows.Count - 1), "Removed table row"); }
    public void RemoveTableColumn() { if (SelectedBlock?.Table is not { } table || table.Rows.Count == 0 || table.Rows.Any(row => row.Cells.Count <= 1)) return; Mutate(() => { foreach (var row in table.Rows) row.Cells.RemoveAt(row.Cells.Count - 1); }, "Removed table column"); }

    public void UpdateEquation(string source, string alternative) { if (SelectedBlock?.Equation is not { } equation) return; if (equation.Source == source && equation.AccessibleAlternative == alternative) return; Mutate(() => { equation.Source = source ?? string.Empty; equation.AccessibleAlternative = alternative ?? string.Empty; equation.RenderedText = equation.Source; equation.Error = string.Empty; }, "Edited equation"); }
    public void UpdateMedia(string alt, string caption, string wrapping) { if (SelectedBlock?.Media is not { } media) return; Mutate(() => { media.AltText = alt ?? string.Empty; media.Caption = caption ?? string.Empty; if (!string.IsNullOrWhiteSpace(wrapping)) media.Wrapping = wrapping; }, "Edited image properties"); }

    public void SetPagePreset(string preset) => Mutate(() => { if (preset.Equals("Letter", StringComparison.OrdinalIgnoreCase)) { Document.PageSetup.WidthPoints = 612; Document.PageSetup.HeightPoints = 792; } else { Document.PageSetup.WidthPoints = 595; Document.PageSetup.HeightPoints = 842; } ApplyOrientation(Document.PageSetup.Orientation); }, "Changed page size");
    public void SetOrientation(string orientation) => Mutate(() => ApplyOrientation(orientation), "Changed page orientation");
    public void SetMargins(double points) => Mutate(() => { var value = Math.Clamp(Finite(points, 72), 0, 1000); Document.PageSetup.MarginTopPoints = value; Document.PageSetup.MarginRightPoints = value; Document.PageSetup.MarginBottomPoints = value; Document.PageSetup.MarginLeftPoints = value; }, "Changed page margins");
    public void SetPageNumbers(bool value) { if (Document.PageSetup.ShowPageNumbers == value) return; Mutate(() => Document.PageSetup.ShowPageNumbers = value, "Changed page numbers"); }
    public void SetLayout(NotesLayoutMode value) { if (Document.LayoutMode == value) return; Mutate(() => Document.LayoutMode = value, "Changed document layout"); }

    public IReadOnlyList<WriteFindResult> Find(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return []; var results = new List<WriteFindResult>(); foreach (var block in Blocks()) { var text = SearchText(block); var start = 0; while ((start = text.IndexOf(query, start, StringComparison.OrdinalIgnoreCase)) >= 0) { var left = Math.Max(0, start - 25); var length = Math.Min(text.Length - left, query.Length + 50); results.Add(new WriteFindResult(block.Id, block.Kind.ToString(), text.Substring(left, length), start)); start += Math.Max(1, query.Length); } } return results;
    }

    public int ReplaceAll(string query, string replacement)
    {
        if (string.IsNullOrWhiteSpace(query)) return 0; var count = Find(query).Count; if (count == 0) return 0;
        Mutate(() =>
        {
            foreach (var block in Blocks())
            {
                if (block.Kind is NotesBlockKind.Paragraph or NotesBlockKind.Heading or NotesBlockKind.Quote or NotesBlockKind.Code) ReplaceTextPreservingRuns(block, ReplaceIgnoreCase(EditableText(block), query, replacement ?? string.Empty));
                if (block.List is not null) foreach (var item in block.List.Items) item.Text = ReplaceIgnoreCase(item.Text, query, replacement ?? string.Empty);
                if (block.Table is not null) foreach (var cell in block.Table.Rows.SelectMany(row => row.Cells)) cell.Text = ReplaceIgnoreCase(cell.Text, query, replacement ?? string.Empty);
                if (block.Equation is not null) block.Equation.Source = ReplaceIgnoreCase(block.Equation.Source, query, replacement ?? string.Empty);
            }
        }, "Replaced text"); return count;
    }

    public void AddComment(string text) { if (SelectedBlock is not { } block || string.IsNullOrWhiteSpace(text)) return; Mutate(() => Document.Comments.Add(new NotesComment { BlockId = block.Id, StartOffset = 0, EndOffset = EditableText(block).Length, Text = text.Trim() }), "Added comment"); }
    public void AddCitation(string title, string authors, string url) { if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(url)) return; Mutate(() => Document.Citations.Add(new NotesCitation { Key = "ref" + (Document.Citations.Count + 1), Title = title?.Trim() ?? string.Empty, Authors = authors?.Trim() ?? string.Empty, Url = url?.Trim() ?? string.Empty, AccessedAt = DateTimeOffset.UtcNow }), "Added citation"); }

    public bool Undo() { if (_undo.Count == 0) return false; _redo.Add(Clone(Document)); var restored = _undo[^1]; _undo.RemoveAt(_undo.Count - 1); Restore(restored); Changed?.Invoke(this, EventArgs.Empty); return true; }
    public bool Redo() { if (_redo.Count == 0) return false; _undo.Add(Clone(Document)); var restored = _redo[^1]; _redo.RemoveAt(_redo.Count - 1); Restore(restored); Changed?.Invoke(this, EventArgs.Empty); return true; }

    private void SetRun(Action<NotesTextRun> apply, string reason) { if (!TryActiveRun(out var run)) return; Mutate(() => apply(run), reason); }
    private void SetParagraph(Action<NotesParagraphFormat> apply, string reason) { if (SelectedBlock is not { } block) return; Mutate(() => apply(block.Paragraph), reason); }
    private bool TryActiveRun(out NotesTextRun run) { run = null!; if (SelectedBlock is not { } block) return false; EnsureRuns(block); ActiveRunIndex = Math.Clamp(ActiveRunIndex, 0, block.Runs.Count - 1); run = block.Runs[ActiveRunIndex]; return true; }
    private void Mutate(Action action, string reason) { _undo.Add(Clone(Document)); if (_undo.Count > HistoryLimit) _undo.RemoveAt(0); _redo.Clear(); action(); Document.UpdatedAt = DateTimeOffset.UtcNow; Changed?.Invoke(this, EventArgs.Empty); }
    private NotesPage PageForSelection() => SelectedBlock is { } block ? PageFor(block) : Document.Sections.First().Pages.First();
    private NotesPage PageFor(NotesBlock block) => Document.Sections.SelectMany(section => section.Pages).First(page => page.Blocks.Contains(block));
    private static void Renumber(NotesPage page) { for (var index = 0; index < page.Blocks.Count; index++) page.Blocks[index].Order = index; }
    private static string EditableText(NotesBlock block) => block.Runs.Count > 0 ? string.Concat(block.Runs.Select(run => run.Text)) : block.PlainText;
    private static string SearchText(NotesBlock block) => block.Kind switch
    {
        NotesBlockKind.List when block.List is not null => string.Join(Environment.NewLine, block.List.Items.Select(item => item.Text)),
        NotesBlockKind.Table when block.Table is not null => string.Join(Environment.NewLine, block.Table.Rows.SelectMany(row => row.Cells).Select(cell => cell.Text)),
        NotesBlockKind.Equation when block.Equation is not null => block.Equation.Source + " " + block.Equation.AccessibleAlternative,
        _ => EditableText(block)
    };
    private static void EnsureRuns(NotesBlock block) { if (block.Runs.Count == 0) block.Runs.Add(new NotesTextRun { Text = block.PlainText, Bold = block.Kind == NotesBlockKind.Heading, Italic = block.Kind == NotesBlockKind.Quote, FontFamily = block.Kind == NotesBlockKind.Code ? "Cascadia Mono" : "Montserrat", FontSize = block.Kind == NotesBlockKind.Heading ? 24 : 14 }); }
    private static NotesTextRun RunAt(NotesBlock block, int index) { EnsureRuns(block); return block.Runs[Math.Clamp(index, 0, block.Runs.Count - 1)]; }
    private static int RunIndexAtCaret(NotesBlock block, int caret) { EnsureRuns(block); var offset = 0; for (var index = 0; index < block.Runs.Count; index++) { offset += block.Runs[index].Text.Length; if (caret <= offset) return index; } return block.Runs.Count - 1; }
    private static void ReplaceTextPreservingRuns(NotesBlock block, string newText) { EnsureRuns(block); var oldText = string.Concat(block.Runs.Select(run => run.Text)); if (oldText == newText) return; var prefix = CommonPrefix(oldText, newText); var suffix = CommonSuffix(oldText, newText, prefix); var oldEnd = oldText.Length - suffix; var inserted = newText.Substring(prefix, newText.Length - prefix - suffix); var starts = new int[block.Runs.Count]; var lengths = new int[block.Runs.Count]; var offset = 0; for (var i = 0; i < block.Runs.Count; i++) { starts[i] = offset; lengths[i] = block.Runs[i].Text.Length; offset += lengths[i]; } var targetIndex = block.Runs.Count - 1; for (var i = 0; i < block.Runs.Count; i++) if (prefix <= starts[i] + lengths[i]) { targetIndex = i; break; } var insertion = Math.Clamp(prefix - starts[targetIndex], 0, lengths[targetIndex]); for (var i = 0; i < block.Runs.Count; i++) { var start = starts[i]; var end = start + lengths[i]; var deleteStart = Math.Max(prefix, start); var deleteEnd = Math.Min(oldEnd, end); if (deleteEnd > deleteStart) block.Runs[i].Text = block.Runs[i].Text.Remove(deleteStart - start, deleteEnd - deleteStart); } if (inserted.Length > 0) block.Runs[targetIndex].Text = block.Runs[targetIndex].Text.Insert(Math.Min(insertion, block.Runs[targetIndex].Text.Length), inserted); block.PlainText = string.Concat(block.Runs.Select(run => run.Text)); }
    private static int CommonPrefix(string a, string b) { var n = 0; while (n < Math.Min(a.Length, b.Length) && a[n] == b[n]) n++; return n; }
    private static int CommonSuffix(string a, string b, int prefix) { var n = 0; var limit = Math.Min(a.Length, b.Length) - prefix; while (n < limit && a[^(n + 1)] == b[^(n + 1)]) n++; return n; }
    private static NotesTextRun CloneRun(NotesTextRun run) => new() { Id = run.Id, Text = run.Text, FontFamily = run.FontFamily, FontSize = run.FontSize, Bold = run.Bold, Italic = run.Italic, Underline = run.Underline, StrikeThrough = run.StrikeThrough, Foreground = run.Foreground, Background = run.Background, Link = run.Link, Language = run.Language };
    private static void CopyCharacter(NotesTextRun source, NotesTextRun target) { var text = target.Text; target.FontFamily = source.FontFamily; target.FontSize = source.FontSize; target.Bold = source.Bold; target.Italic = source.Italic; target.Underline = source.Underline; target.StrikeThrough = source.StrikeThrough; target.Foreground = source.Foreground; target.Background = source.Background; target.Link = source.Link; target.Language = source.Language; target.Text = text; }
    private static void CopyParagraph(NotesParagraphFormat source, NotesParagraphFormat target) { target.Alignment = source.Alignment; target.LineSpacing = source.LineSpacing; target.SpaceBefore = source.SpaceBefore; target.SpaceAfter = source.SpaceAfter; target.IndentLeft = source.IndentLeft; target.IndentRight = source.IndentRight; target.FirstLineIndent = source.FirstLineIndent; target.KeepWithNext = source.KeepWithNext; target.PageBreakBefore = source.PageBreakBefore; }
    private static NotesDocument Clone(NotesDocument document) => JsonSerializer.Deserialize<NotesDocument>(JsonSerializer.Serialize(document, JsonOptions), JsonOptions) ?? throw new InvalidDataException("Write document state could not be cloned.");
    private void Restore(NotesDocument source) { Document.SchemaVersion = source.SchemaVersion; Document.Id = source.Id; Document.Title = source.Title; Document.Language = source.Language; Document.CreatedAt = source.CreatedAt; Document.UpdatedAt = source.UpdatedAt; Document.Version = source.Version; Document.LayoutMode = source.LayoutMode; Document.PageSetup = source.PageSetup; Document.Sections = source.Sections; Document.Styles = source.Styles; Document.Fields = source.Fields; Document.Bookmarks = source.Bookmarks; Document.Citations = source.Citations; Document.Comments = source.Comments; Document.Revisions = source.Revisions; Document.AiChanges = source.AiChanges; Document.FlashcardReviews = source.FlashcardReviews; Document.Collaboration = source.Collaboration; Document.Recovery = source.Recovery; Document.Metadata = source.Metadata; if (SelectedBlockId is { } selected && Blocks().All(block => block.Id != selected)) SelectedBlockId = Blocks().FirstOrDefault()?.Id; }
    private void ApplyOrientation(string orientation) { var landscape = orientation.Equals("Landscape", StringComparison.OrdinalIgnoreCase); Document.PageSetup.Orientation = landscape ? "Landscape" : "Portrait"; var shortSide = Math.Min(Document.PageSetup.WidthPoints, Document.PageSetup.HeightPoints); var longSide = Math.Max(Document.PageSetup.WidthPoints, Document.PageSetup.HeightPoints); Document.PageSetup.WidthPoints = landscape ? longSide : shortSide; Document.PageSetup.HeightPoints = landscape ? shortSide : longSide; }
    private static string NormaliseColour(string value, string fallback) { if (string.IsNullOrWhiteSpace(value)) return fallback; var text = value.Trim(); return text.StartsWith('#') && (text.Length == 7 || text.Length == 9) ? text.ToUpperInvariant() : fallback; }
    private static double Finite(double value, double fallback) => double.IsNaN(value) || double.IsInfinity(value) ? fallback : value;
    private static string ReplaceIgnoreCase(string source, string search, string replacement) { if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(search)) return source; var result = new System.Text.StringBuilder(); var start = 0; while (true) { var index = source.IndexOf(search, start, StringComparison.OrdinalIgnoreCase); if (index < 0) { result.Append(source, start, source.Length - start); break; } result.Append(source, start, index - start).Append(replacement); start = index + search.Length; } return result.ToString(); }
}
