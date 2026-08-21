using Haven.Core;

namespace Haven.Application;

public readonly record struct WriteDocumentPosition(Guid BlockId, int Offset);

public sealed partial class WriteDocumentEditor
{
    private WriteDocumentPosition? _documentAnchor;
    private WriteDocumentPosition? _documentCaret;

    public WriteDocumentPosition DocumentCaret
    {
        get
        {
            if (_documentCaret is { } caret && TextBlocks().Any(block => block.Id == caret.BlockId)) return Normalise(caret);
            var block = TextBlocks().FirstOrDefault();
            return block is null ? default : new WriteDocumentPosition(block.Id, 0);
        }
    }

    public WriteDocumentPosition? DocumentAnchor => _documentAnchor;
    public bool HasDocumentSelection => _documentAnchor is { } anchor && DocumentCaret != Normalise(anchor);

    public string SelectedDocumentText
    {
        get
        {
            if (!TryOrderedSelection(out var start, out var end)) return string.Empty;
            var blocks = TextBlocks();
            var startIndex = IndexOf(blocks, start.BlockId);
            var endIndex = IndexOf(blocks, end.BlockId);
            if (startIndex < 0 || endIndex < 0) return string.Empty;
            var parts = new List<string>();
            for (var index = startIndex; index <= endIndex; index++)
            {
                var block = blocks[index];
                var text = EditableText(block);
                var from = index == startIndex ? Math.Clamp(start.Offset, 0, text.Length) : 0;
                var to = index == endIndex ? Math.Clamp(end.Offset, 0, text.Length) : text.Length;
                parts.Add(text[from..Math.Max(from, to)]);
            }
            return string.Join("\n", parts);
        }
    }

    public IReadOnlyList<NotesBlock> TextBlocks() => Blocks().Where(IsTextBlock).ToArray();

    public void SetDocumentCaret(Guid blockId, int offset, bool extendSelection = false)
    {
        var blocks = TextBlocks();
        var block = blocks.FirstOrDefault(candidate => candidate.Id == blockId);
        if (block is null) return;
        var next = new WriteDocumentPosition(block.Id, Math.Clamp(offset, 0, EditableText(block).Length));
        var previous = DocumentCaret;
        if (extendSelection) _documentAnchor ??= previous;
        else _documentAnchor = null;
        SetCaretState(next);
    }

    public void SelectAllDocumentText()
    {
        var blocks = TextBlocks();
        if (blocks.Count == 0) return;
        _documentAnchor = new WriteDocumentPosition(blocks[0].Id, 0);
        var last = blocks[^1];
        SetCaretState(new WriteDocumentPosition(last.Id, EditableText(last).Length));
    }

    public (int Start, int End)? SelectionForBlock(Guid blockId)
    {
        if (!TryOrderedSelection(out var start, out var end)) return null;
        var blocks = TextBlocks();
        var blockIndex = IndexOf(blocks, blockId);
        var startIndex = IndexOf(blocks, start.BlockId);
        var endIndex = IndexOf(blocks, end.BlockId);
        if (blockIndex < startIndex || blockIndex > endIndex || blockIndex < 0) return null;
        var length = EditableText(blocks[blockIndex]).Length;
        var from = blockIndex == startIndex ? start.Offset : 0;
        var to = blockIndex == endIndex ? end.Offset : length;
        return (Math.Clamp(from, 0, length), Math.Clamp(to, 0, length));
    }

    public bool InsertDocumentText(string? text)
    {
        text ??= string.Empty;
        if (text.Length == 0 && !HasDocumentSelection) return false;
        var caret = DocumentCaret;
        if (caret.BlockId == Guid.Empty) return false;
        Mutate(() =>
        {
            if (HasDocumentSelection) DeleteDocumentSelectionCore();
            caret = DocumentCaret;
            var block = TextBlocks().First(candidate => candidate.Id == caret.BlockId);
            var current = EditableText(block);
            var offset = Math.Clamp(caret.Offset, 0, current.Length);
            var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
            var lines = normalized.Split('\n');
            if (lines.Length == 1)
            {
                ReplaceTextPreservingRuns(block, current.Insert(offset, lines[0]));
                SetCaretState(new WriteDocumentPosition(block.Id, offset + lines[0].Length));
                return;
            }

            var prefix = current[..offset] + lines[0];
            var suffix = current[offset..];
            ReplaceTextPreservingRuns(block, prefix);
            var page = PageFor(block);
            var insertion = page.Blocks.IndexOf(block) + 1;
            NotesBlock? last = null;
            for (var index = 1; index < lines.Length; index++)
            {
                var value = index == lines.Length - 1 ? lines[index] + suffix : lines[index];
                var next = CreateContinuationBlock(block, value);
                page.Blocks.Insert(insertion++, next);
                last = next;
            }
            Renumber(page);
            if (last is not null) SetCaretState(new WriteDocumentPosition(last.Id, lines[^1].Length));
        }, text.Contains('\n') || text.Contains('\r') ? "Inserted paragraph break" : "Edited document text");
        return true;
    }

    public bool DeleteDocumentSelection()
    {
        if (!HasDocumentSelection) return false;
        Mutate(DeleteDocumentSelectionCore, "Deleted selected text");
        return true;
    }

    public bool BackspaceDocument()
    {
        if (HasDocumentSelection) return DeleteDocumentSelection();
        var caret = DocumentCaret;
        var blocks = TextBlocks();
        var index = IndexOf(blocks, caret.BlockId);
        if (index < 0) return false;
        var block = blocks[index];
        var text = EditableText(block);
        if (caret.Offset > 0)
        {
            Mutate(() =>
            {
                ReplaceTextPreservingRuns(block, text.Remove(caret.Offset - 1, 1));
                SetCaretState(new WriteDocumentPosition(block.Id, caret.Offset - 1));
            }, "Deleted text");
            return true;
        }
        if (index == 0) return false;
        var previous = blocks[index - 1];
        var previousText = EditableText(previous);
        Mutate(() =>
        {
            ReplaceTextPreservingRuns(previous, previousText + text);
            RemoveBlock(block);
            SetCaretState(new WriteDocumentPosition(previous.Id, previousText.Length));
        }, "Merged paragraphs");
        return true;
    }

    public bool DeleteForwardDocument()
    {
        if (HasDocumentSelection) return DeleteDocumentSelection();
        var caret = DocumentCaret;
        var blocks = TextBlocks();
        var index = IndexOf(blocks, caret.BlockId);
        if (index < 0) return false;
        var block = blocks[index];
        var text = EditableText(block);
        if (caret.Offset < text.Length)
        {
            Mutate(() => ReplaceTextPreservingRuns(block, text.Remove(caret.Offset, 1)), "Deleted text");
            SetCaretState(caret);
            return true;
        }
        if (index >= blocks.Count - 1) return false;
        var next = blocks[index + 1];
        var nextText = EditableText(next);
        Mutate(() =>
        {
            ReplaceTextPreservingRuns(block, text + nextText);
            RemoveBlock(next);
            SetCaretState(new WriteDocumentPosition(block.Id, text.Length));
        }, "Merged paragraphs");
        return true;
    }

    public bool MoveDocumentCaret(int delta, bool extendSelection = false)
    {
        if (delta == 0) return false;
        var blocks = TextBlocks();
        if (blocks.Count == 0) return false;
        var caret = DocumentCaret;
        var index = IndexOf(blocks, caret.BlockId);
        if (index < 0) return false;
        var next = caret;
        if (delta < 0)
        {
            if (caret.Offset > 0) next = caret with { Offset = caret.Offset - 1 };
            else if (index > 0) next = new WriteDocumentPosition(blocks[index - 1].Id, EditableText(blocks[index - 1]).Length);
            else return false;
        }
        else
        {
            var length = EditableText(blocks[index]).Length;
            if (caret.Offset < length) next = caret with { Offset = caret.Offset + 1 };
            else if (index < blocks.Count - 1) next = new WriteDocumentPosition(blocks[index + 1].Id, 0);
            else return false;
        }
        SetDocumentCaret(next.BlockId, next.Offset, extendSelection);
        return true;
    }

    public void MoveDocumentCaretToBoundary(bool end, bool extendSelection = false)
    {
        var caret = DocumentCaret;
        var block = TextBlocks().FirstOrDefault(candidate => candidate.Id == caret.BlockId);
        if (block is null) return;
        SetDocumentCaret(block.Id, end ? EditableText(block).Length : 0, extendSelection);
    }

    private bool TryOrderedSelection(out WriteDocumentPosition start, out WriteDocumentPosition end)
    {
        start = end = default;
        if (_documentAnchor is not { } anchor) return false;
        anchor = Normalise(anchor);
        var caret = DocumentCaret;
        if (anchor == caret) return false;
        var blocks = TextBlocks();
        var anchorIndex = IndexOf(blocks, anchor.BlockId);
        var caretIndex = IndexOf(blocks, caret.BlockId);
        if (anchorIndex < 0 || caretIndex < 0) return false;
        if (anchorIndex < caretIndex || (anchorIndex == caretIndex && anchor.Offset <= caret.Offset))
        {
            start = anchor; end = caret;
        }
        else
        {
            start = caret; end = anchor;
        }
        return true;
    }

    private void DeleteDocumentSelectionCore()
    {
        if (!TryOrderedSelection(out var start, out var end)) return;
        var blocks = TextBlocks();
        var startIndex = IndexOf(blocks, start.BlockId);
        var endIndex = IndexOf(blocks, end.BlockId);
        if (startIndex < 0 || endIndex < 0) return;
        var first = blocks[startIndex];
        var firstText = EditableText(first);
        if (startIndex == endIndex)
        {
            ReplaceTextPreservingRuns(first, firstText.Remove(start.Offset, end.Offset - start.Offset));
            _documentAnchor = null;
            SetCaretState(start);
            return;
        }
        var last = blocks[endIndex];
        var combined = firstText[..start.Offset] + EditableText(last)[end.Offset..];
        ReplaceTextPreservingRuns(first, combined);
        foreach (var block in blocks.Skip(startIndex + 1).Take(endIndex - startIndex).ToArray()) RemoveBlock(block);
        _documentAnchor = null;
        SetCaretState(new WriteDocumentPosition(first.Id, start.Offset));
    }

    private IEnumerable<NotesTextRun> SelectedRuns(bool splitBoundaries = false)
    {
        if (!TryOrderedSelection(out var start, out var end)) yield break;
        var blocks = TextBlocks();
        var startIndex = IndexOf(blocks, start.BlockId);
        var endIndex = IndexOf(blocks, end.BlockId);
        for (var blockIndex = startIndex; blockIndex <= endIndex; blockIndex++)
        {
            var block = blocks[blockIndex];
            EnsureRuns(block);
            var length = EditableText(block).Length;
            var from = blockIndex == startIndex ? start.Offset : 0;
            var to = blockIndex == endIndex ? end.Offset : length;
            if (splitBoundaries) SplitRunsAt(block, from, to);
            var offset = 0;
            foreach (var run in block.Runs)
            {
                var runStart = offset;
                var runEnd = offset + run.Text.Length;
                offset = runEnd;
                if (runEnd > from && runStart < to) yield return run;
            }
        }
    }

    private void SplitRunsAt(NotesBlock block, int start, int end)
    {
        EnsureRuns(block);
        SplitRunBoundary(block, end);
        SplitRunBoundary(block, start);
    }

    private static void SplitRunBoundary(NotesBlock block, int offset)
    {
        if (offset <= 0) return;
        var total = block.Runs.Sum(run => run.Text.Length);
        if (offset >= total) return;
        var position = 0;
        for (var index = 0; index < block.Runs.Count; index++)
        {
            var run = block.Runs[index];
            var end = position + run.Text.Length;
            if (offset > position && offset < end)
            {
                var local = offset - position;
                var tail = CloneRun(run);
                tail.Id = Guid.NewGuid();
                tail.Text = run.Text[local..];
                run.Text = run.Text[..local];
                block.Runs.Insert(index + 1, tail);
                return;
            }
            position = end;
        }
    }

    private void SetCaretState(WriteDocumentPosition position)
    {
        var normalized = Normalise(position);
        _documentCaret = normalized;
        SelectedBlockId = normalized.BlockId;
        CaretIndex = normalized.Offset;
        var block = TextBlocks().FirstOrDefault(candidate => candidate.Id == normalized.BlockId);
        ActiveRunIndex = block is null ? 0 : RunIndexAtCaret(block, normalized.Offset);
    }

    private WriteDocumentPosition Normalise(WriteDocumentPosition position)
    {
        var block = TextBlocks().FirstOrDefault(candidate => candidate.Id == position.BlockId);
        return block is null ? DocumentCaretFallback() : new WriteDocumentPosition(block.Id, Math.Clamp(position.Offset, 0, EditableText(block).Length));
    }

    private WriteDocumentPosition DocumentCaretFallback()
    {
        var block = TextBlocks().FirstOrDefault();
        return block is null ? default : new WriteDocumentPosition(block.Id, 0);
    }

    private void RemoveBlock(NotesBlock block)
    {
        var page = PageFor(block);
        page.Blocks.Remove(block);
        if (page.Blocks.Count == 0) page.Blocks.Add(NotesBlock.CreateParagraph());
        Renumber(page);
    }

    private static NotesBlock CreateContinuationBlock(NotesBlock source, string text)
    {
        var run = source.Runs.FirstOrDefault();
        var nextRun = run is null ? new NotesTextRun { Text = text } : CloneRun(run);
        nextRun.Id = Guid.NewGuid();
        nextRun.Text = text;
        return new NotesBlock
        {
            Kind = NotesBlockKind.Paragraph,
            StyleId = "normal",
            PlainText = text,
            Runs = [nextRun],
            Paragraph = new NotesParagraphFormat()
        };
    }

    private static bool IsTextBlock(NotesBlock block) => block.Kind is NotesBlockKind.Paragraph or NotesBlockKind.Heading or NotesBlockKind.Quote or NotesBlockKind.Code;

    private static int IndexOf(IReadOnlyList<NotesBlock> blocks, Guid id)
    {
        for (var index = 0; index < blocks.Count; index++) if (blocks[index].Id == id) return index;
        return -1;
    }
}
