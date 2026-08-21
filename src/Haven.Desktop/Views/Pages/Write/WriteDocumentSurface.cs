using System.Globalization;
using Haven.Application;
using Haven.Core;
using Haven.UI;

namespace Haven.Desktop.Views.Pages.Write;

/// <summary>Retained, document-first Write editing surface. The document is the interaction target; blocks are layout data, not controls.</summary>
internal sealed partial class WriteDocumentSurface : HavenElement, IHavenDrawCommandSource, IHavenPointerInputTarget, IHavenKeyboardInputTarget, IHavenTextInputTarget, IHavenClipboardInputTarget
{
    private readonly List<BlockLayout> _layouts = [];
    private readonly List<HavenRect> _pages = [];
    private WriteDocumentEditor? _editor;
    private bool _pointerSelecting;
    private double _zoom = 1;

    public WriteDocumentSurface()
    {
        Name = "Write.Document.Surface";
        Accessibility.Role = HavenAccessibleRole.Input;
        Accessibility.Focusable = true;
        Accessibility.AccessibleName = "Document editor";
        Accessibility.Description = "Edit the document directly. Drag to select text across paragraphs.";
        SetValue(HavenProperties.Width, HavenLength.Percent(100));
        SetValue(HavenProperties.MinHeight, HavenLength.Px(900));
        SetValue(HavenProperties.Background, "Transparent");
    }

    public event EventHandler? SelectionChanged;
    public double Zoom => _zoom;
    public string SelectedText => _editor?.SelectedDocumentText ?? string.Empty;

    public void SetEditor(WriteDocumentEditor editor)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        Accessibility.Description = $"Document editor. {editor.Statistics.Words} words.";
        InvalidateDocument();
    }

    public void SetZoom(double zoom)
    {
        var next = Math.Clamp(zoom, .5, 2.5);
        if (Math.Abs(next - _zoom) < .001) return;
        _zoom = next;
        InvalidateDocument();
    }

    public void InvalidateDocument()
    {
        BuildLayout();
        Invalidate();
    }

    public bool PointerPressed(HavenPointerInput input)
    {
        if (_editor is null) return false;
        BuildLayout();
        if (TryPointerPressSpecial(input)) return true;
        if (HitTextPosition(input.LocalPosition) is { } position)
        {
            _activeTableCellId = null;
            _editor.SetDocumentCaret(position.BlockId, position.Offset, input.Modifiers.HasFlag(HavenKeyModifiers.Shift));
            _pointerSelecting = true;
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            Invalidate();
            return true;
        }
        if (_layouts.LastOrDefault(layout => layout.Rect.Contains(input.LocalPosition)) is { } blockLayout)
        {
            _activeTableCellId = null;
            _editor.SelectBlock(blockLayout.Block.Id);
            _pointerSelecting = false;
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            Invalidate();
            return true;
        }
        return false;
    }

    public bool PointerMoved(HavenPointerInput input)
    {
        if (_editor is null) return false;
        if (TryPointerMoveSpecial(input)) return true;
        if (!_pointerSelecting) return false;
        BuildLayout();
        if (HitTextPosition(input.LocalPosition, nearest: true) is not { } position) return false;
        _editor.SetDocumentCaret(position.BlockId, position.Offset, extendSelection: true);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
        return true;
    }

    public bool PointerReleased(HavenPointerInput input)
    {
        if (TryPointerReleaseSpecial(input)) return true;
        if (!_pointerSelecting) return false;
        _pointerSelecting = false;
        if (_editor is not null && HitTextPosition(input.LocalPosition, nearest: true) is { } position)
            _editor.SetDocumentCaret(position.BlockId, position.Offset, extendSelection: true);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
        return true;
    }

    public bool TextInput(string? text)
    {
        if (TryTableTextInput(text)) return true;
        if (_editor?.InsertDocumentText(text) != true) return false;
        InvalidateDocument();
        return true;
    }

    public bool KeyDown(HavenKey key, HavenInputModifiers modifiers)
    {
        if (_editor is null) return false;
        if (TryTableKeyDown(key, modifiers)) return true;
        if (modifiers.Control)
        {
            if (key == HavenKey.A) { _editor.SelectAllDocumentText(); SelectionChanged?.Invoke(this, EventArgs.Empty); Invalidate(); return true; }
            if (key == HavenKey.Z) { if (_editor.Undo()) { InvalidateDocument(); return true; } return false; }
            if (key == HavenKey.Y) { if (_editor.Redo()) { InvalidateDocument(); return true; } return false; }
        }
        switch (key)
        {
            case HavenKey.Left: if (_editor.MoveDocumentCaret(-1, modifiers.Shift)) { SelectionChanged?.Invoke(this, EventArgs.Empty); Invalidate(); return true; } break;
            case HavenKey.Right: if (_editor.MoveDocumentCaret(1, modifiers.Shift)) { SelectionChanged?.Invoke(this, EventArgs.Empty); Invalidate(); return true; } break;
            case HavenKey.Up: if (MoveVertical(-1, modifiers.Shift)) return true; break;
            case HavenKey.Down: if (MoveVertical(1, modifiers.Shift)) return true; break;
            case HavenKey.Home: _editor.MoveDocumentCaretToBoundary(false, modifiers.Shift); SelectionChanged?.Invoke(this, EventArgs.Empty); Invalidate(); return true;
            case HavenKey.End: _editor.MoveDocumentCaretToBoundary(true, modifiers.Shift); SelectionChanged?.Invoke(this, EventArgs.Empty); Invalidate(); return true;
            case HavenKey.Backspace: if (_editor.BackspaceDocument()) { InvalidateDocument(); return true; } break;
            case HavenKey.Delete: if (_editor.DeleteForwardDocument()) { InvalidateDocument(); return true; } break;
            case HavenKey.Enter: if (_editor.InsertDocumentText("\n")) { InvalidateDocument(); return true; } break;
            case HavenKey.Tab:
                if (_editor.SelectedBlock is { } block)
                {
                    var next = Math.Max(0, block.Paragraph.IndentLeft + (modifiers.Shift ? -24 : 24));
                    _editor.SetLeftIndent(next);
                    InvalidateDocument();
                    return true;
                }
                break;
        }
        return false;
    }

    bool IHavenKeyboardInputTarget.KeyDown(HavenKeyInput input) => KeyDown(input.Key, ToInputModifiers(input));

    bool IHavenKeyboardInputTarget.KeyUp(HavenKeyInput input) => KeyUp(input.Key);

    public string? Copy() => string.IsNullOrEmpty(SelectedText) ? null : SelectedText;

    public string? Cut()
    {
        var selected = Copy();
        if (selected is not null) DeleteSelection();
        return selected;
    }

    public bool Paste(string? text) => InsertText(text);

    public bool KeyUp(HavenKey key) => key is HavenKey.Left or HavenKey.Right or HavenKey.Up or HavenKey.Down or HavenKey.Home or HavenKey.End or HavenKey.Backspace or HavenKey.Delete or HavenKey.Enter or HavenKey.Tab or HavenKey.A or HavenKey.Z or HavenKey.Y;

    private static HavenInputModifiers ToInputModifiers(HavenKeyInput input) => new(
        Shift: input.Shift,
        Control: input.Control,
        Alt: input.Alt,
        Meta: input.Meta);

    public bool DeleteSelection()
    {
        if (_editor?.DeleteDocumentSelection() != true) return false;
        InvalidateDocument();
        return true;
    }

    public bool InsertText(string? text) => TextInput(text);

    public void Draw(HavenDrawingContext context, double opacity)
    {
        if (_editor is null || Bounds.Width <= 1) return;
        BuildLayout();
        foreach (var page in _pages)
        {
            var absolute = Absolute(page);
            context.Add(new HavenShadowCommand(absolute, new HavenShadow(new HavenSolidBrush(45, 0, 0, 0), 18, 0, 5, 0, .28), 4));
            context.Add(new HavenFillRoundedRectCommand(absolute, new HavenSolidBrush(255, 255, 255, 255), 3, opacity));
            context.Add(new HavenStrokeRoundedRectCommand(absolute, new HavenPen(new HavenSolidBrush(35, 25, 35, 45), 1), 3, opacity));
        }
        foreach (var layout in _layouts) DrawBlock(context, layout, opacity);
    }

    private void DrawBlock(HavenDrawingContext context, BlockLayout layout, double opacity)
    {
        var block = layout.Block;
        var rect = Absolute(DisplayRectLocal(layout));
        switch (block.Kind)
        {
            case NotesBlockKind.Paragraph:
            case NotesBlockKind.Heading:
            case NotesBlockKind.Quote:
            case NotesBlockKind.Code:
                DrawTextBlock(context, layout, opacity);
                break;
            case NotesBlockKind.List:
                DrawList(context, layout, opacity);
                break;
            case NotesBlockKind.Table:
                DrawTable(context, layout, opacity);
                break;
            case NotesBlockKind.Image:
            case NotesBlockKind.Audio:
            case NotesBlockKind.Video:
                if (block.Media is { } media)
                {
                    context.Add(new HavenFillRoundedRectCommand(rect, new HavenSolidBrush(255, 247, 249, 252), 6, opacity));
                    if (!string.IsNullOrWhiteSpace(media.StoredPath)) context.Add(new HavenImageCommand(rect, new HavenImage(media.StoredPath), HavenImageLayout.Contain, opacity));
                    else context.Add(new HavenTextCommand(rect, new HavenTextLayout(string.IsNullOrWhiteSpace(media.AltText) ? media.OriginalName : media.AltText, "Montserrat", 14 * _zoom, 500, rect.Width, true), new HavenSolidBrush(255, 50, 60, 72), opacity));
                    if (!string.IsNullOrWhiteSpace(media.Caption)) context.Add(new HavenTextCommand(new HavenRect(rect.X, rect.Bottom + 4, rect.Width, 22 * _zoom), new HavenTextLayout(media.Caption, "Montserrat", 11 * _zoom, 400, rect.Width), new HavenSolidBrush(255, 75, 82, 94), opacity));
                }
                break;
            case NotesBlockKind.Shape:
                context.Add(new HavenFillRoundedRectCommand(rect, new HavenSolidBrush(26, 57, 110, 220), 10, opacity));
                context.Add(new HavenStrokeRoundedRectCommand(rect, new HavenPen(new HavenSolidBrush(255, 57, 110, 220), 2), 10, opacity));
                context.Add(new HavenTextCommand(rect, new HavenTextLayout(block.VectorShape?.Name ?? "Shape", "Montserrat", 14 * _zoom, 600, rect.Width, true), new HavenSolidBrush(255, 30, 45, 65), opacity));
                break;
            case NotesBlockKind.Equation:
                context.Add(new HavenTextCommand(rect, new HavenTextLayout(block.Equation?.RenderedText is { Length: > 0 } rendered ? rendered : block.Equation?.Source ?? string.Empty, "Cambria Math", 18 * _zoom, 400, rect.Width, true), new HavenSolidBrush(255, 24, 28, 34), opacity));
                break;
            case NotesBlockKind.Divider:
                context.Add(new HavenLineCommand(new HavenPoint(rect.X, rect.Y + rect.Height / 2), new HavenPoint(rect.Right, rect.Y + rect.Height / 2), new HavenPen(new HavenSolidBrush(75, 80, 90, 105), 1), opacity));
                break;
            default:
                context.Add(new HavenTextCommand(rect, new HavenTextLayout(block.Kind.ToString(), "Montserrat", 12 * _zoom, 500, rect.Width), new HavenSolidBrush(255, 90, 96, 106), opacity));
                break;
        }

        if (_editor?.SelectedBlockId == block.Id && !IsTextBlock(block))
        {
            context.Add(new HavenStrokeRoundedRectCommand(Absolute(DisplayRectLocal(layout)), new HavenPen(new HavenSolidBrush(255, 57, 110, 220), 2), 5, opacity));
            DrawObjectHandles(context, layout, opacity);
        }
        if (_editor?.Document.Comments.Any(comment => comment.BlockId == block.Id) == true)
            context.Add(new HavenEllipseCommand(new HavenRect(Absolute(layout.Rect).Right + 6, Absolute(layout.Rect).Y + 4, 8, 8), new HavenSolidBrush(255, 255, 166, 0), null, opacity));
    }

    private void DrawTextBlock(HavenDrawingContext context, BlockLayout layout, double opacity)
    {
        if (_editor is null) return;
        var block = layout.Block;
        var text = TextOf(block);
        var rect = Absolute(layout.ContentRect);
        var first = block.Runs.FirstOrDefault();
        var family = first?.FontFamily ?? (block.Kind == NotesBlockKind.Code ? "Cascadia Mono" : "Montserrat");
        var fontSize = layout.FontSize;
        var weight = first?.Bold == true || block.Kind == NotesBlockKind.Heading ? 700 : 400;
        var fullLayout = new HavenTextLayout(text, family, fontSize, weight, rect.Width);
        if (_editor.SelectionForBlock(block.Id) is { } selected && selected.End > selected.Start)
            context.Add(new HavenTextSelectionCommand(rect, fullLayout, selected.Start, selected.End - selected.Start, new HavenSolidBrush(95, 57, 110, 220), opacity));

        if (block.Runs.Count <= 1)
        {
            var run = first ?? new NotesTextRun { Text = text, FontFamily = family, FontSize = fontSize / _zoom, Bold = weight >= 700 };
            var background = Colour(run.Background, transparentFallback: true);
            if (background is HavenSolidBrush { A: > 0 }) context.Add(new HavenFillRoundedRectCommand(rect, background, 1, opacity));
            context.Add(new HavenTextCommand(rect, fullLayout, DocumentTextColour(run.Foreground), opacity));
        }
        else
        {
            var offset = 0;
            foreach (var run in block.Runs)
            {
                DrawRunSegments(context, rect, run.Text, run, layout, offset, opacity);
                offset += run.Text.Length;
            }
        }

        if (State.HasFlag(HavenElementState.Focused) && _editor.DocumentCaret.BlockId == block.Id)
        {
            var caret = Math.Clamp(_editor.DocumentCaret.Offset, 0, text.Length);
            context.Add(new HavenCaretCommand(rect, fullLayout, new HavenSolidBrush(255, 25, 30, 36), opacity)
            {
                FullLayout = fullLayout,
                CaretIndex = caret
            });
        }
    }

    private void DrawRunSegments(HavenDrawingContext context, HavenRect rect, string runText, NotesTextRun run, BlockLayout layout, int globalStart, double opacity)
    {
        if (runText.Length == 0) return;
        var line = globalStart == 0 ? 0 : VisualPosition(TextOf(layout.Block)[..Math.Min(globalStart, TextOf(layout.Block).Length)], layout.Columns).Line;
        var column = globalStart == 0 ? 0 : VisualPosition(TextOf(layout.Block)[..Math.Min(globalStart, TextOf(layout.Block).Length)], layout.Columns).Column;
        var buffer = new System.Text.StringBuilder();
        var segmentColumn = column;
        void Flush()
        {
            if (buffer.Length == 0) return;
            var text = buffer.ToString();
            var x = rect.X + LineAlignmentOffset(layout, line) + segmentColumn * layout.CharacterWidth;
            var y = rect.Y + line * layout.LineHeight;
            var width = Math.Max(layout.CharacterWidth, text.Length * layout.CharacterWidth);
            var segmentRect = new HavenRect(x, y, Math.Min(width, Math.Max(layout.CharacterWidth, rect.Right - x)), layout.LineHeight);
            var background = Colour(run.Background, transparentFallback: true);
            if (background is HavenSolidBrush { A: > 0 }) context.Add(new HavenFillRoundedRectCommand(segmentRect, background, 1, opacity));
            var foreground = DocumentTextColour(run.Foreground);
            context.Add(new HavenTextCommand(segmentRect, new HavenTextLayout(text, string.IsNullOrWhiteSpace(run.FontFamily) ? "Montserrat" : run.FontFamily, Math.Max(8, run.FontSize * _zoom), run.Bold ? 700 : 400, segmentRect.Width, false, run.Italic), foreground, opacity));
            if (run.Underline) context.Add(new HavenLineCommand(new HavenPoint(segmentRect.X, segmentRect.Bottom - 2), new HavenPoint(segmentRect.Right, segmentRect.Bottom - 2), new HavenPen(foreground, 1), opacity));
            if (run.StrikeThrough) context.Add(new HavenLineCommand(new HavenPoint(segmentRect.X, segmentRect.Y + segmentRect.Height * .55), new HavenPoint(segmentRect.Right, segmentRect.Y + segmentRect.Height * .55), new HavenPen(foreground, 1), opacity));
            buffer.Clear();
        }
        foreach (var character in runText)
        {
            if (character == '\n')
            {
                Flush(); line++; column = 0; segmentColumn = 0; continue;
            }
            if (column >= layout.Columns)
            {
                Flush(); line++; column = 0; segmentColumn = 0;
            }
            if (buffer.Length == 0) segmentColumn = column;
            buffer.Append(character);
            column++;
        }
        Flush();
    }

    private void DrawList(HavenDrawingContext context, BlockLayout layout, double opacity)
    {
        if (layout.Block.List is not { } list) return;
        var rect = Absolute(layout.ContentRect);
        var y = rect.Y;
        for (var index = 0; index < list.Items.Count; index++)
        {
            var item = list.Items[index];
            var indent = item.Level * 22 * _zoom;
            var marker = list.Kind switch { NotesListKind.Numbered => $"{list.StartNumber + index}.", NotesListKind.Checklist => item.Checked ? "☑" : "☐", _ => "•" };
            context.Add(new HavenTextCommand(new HavenRect(rect.X + indent, y, 26 * _zoom, 24 * _zoom), new HavenTextLayout(marker, "Montserrat", 13 * _zoom, 600, 26 * _zoom), new HavenSolidBrush(255, 28, 34, 42), opacity));
            context.Add(new HavenTextCommand(new HavenRect(rect.X + indent + 28 * _zoom, y, Math.Max(20, rect.Width - indent - 28 * _zoom), 24 * _zoom), new HavenTextLayout(item.Text, "Montserrat", 13 * _zoom, 400, Math.Max(20, rect.Width - indent - 28 * _zoom)), new HavenSolidBrush(255, 28, 34, 42), opacity));
            y += 27 * _zoom;
        }
    }

    private void DrawTable(HavenDrawingContext context, BlockLayout layout, double opacity)
    {
        if (layout.Block.Table is not { Rows.Count: > 0 } table) return;
        foreach (var cellLayout in BuildTableCellLayouts(layout))
        {
            var cell = cellLayout.Cell;
            var cellRect = Absolute(cellLayout.Rect);
            if (cellLayout.Row == 0 && table.HeaderRow)
                context.Add(new HavenFillRoundedRectCommand(cellRect, new HavenSolidBrush(255, 244, 246, 250), 0, opacity));
            if (Colour(cell.Background, transparentFallback: true) is HavenSolidBrush { A: > 0 } background)
                context.Add(new HavenFillRoundedRectCommand(cellRect, background, 0, opacity));
            if (_activeTableCellId == cell.Id)
                context.Add(new HavenFillRoundedRectCommand(cellRect, new HavenSolidBrush(28, 57, 110, 220), 0, opacity));
            var selected = _activeTableCellId == cell.Id;
            context.Add(new HavenStrokeRoundedRectCommand(cellRect, new HavenPen(selected ? new HavenSolidBrush(210, 57, 110, 220) : new HavenSolidBrush(80, 92, 103, 117), selected ? 2 : 1), 0, opacity));
            var textRect = new HavenRect(cellRect.X + 6, cellRect.Y + 4, Math.Max(1, cellRect.Width - 12), Math.Max(1, cellRect.Height - 8));
            var weight = cellLayout.Row == 0 && table.HeaderRow ? 600 : 400;
            context.Add(new HavenTextCommand(textRect, new HavenTextLayout(cell.Text, "Montserrat", 11 * _zoom, weight, textRect.Width, true), new HavenSolidBrush(255, 35, 42, 52), opacity));
            if (State.HasFlag(HavenElementState.Focused) && selected)
            {
                var caret = Math.Clamp(_tableCellCaret, 0, cell.Text.Length);
                context.Add(new HavenCaretCommand(textRect, new HavenTextLayout(cell.Text[..caret], "Montserrat", 11 * _zoom, weight, textRect.Width, true), new HavenSolidBrush(255, 25, 30, 36), opacity));
            }
        }
    }

    private void BuildLayout()
    {
        _layouts.Clear();
        _pages.Clear();
        if (_editor is null) return;
        var setup = _editor.Document.PageSetup;
        var pageWidth = Math.Max(360, setup.WidthPoints * _zoom);
        var pageHeight = Math.Max(480, setup.HeightPoints * _zoom);
        var viewportWidth = Math.Max(pageWidth + 20, Bounds.Width);
        var pageX = Math.Max(10, (viewportWidth - pageWidth) / 2);
        var marginLeft = Math.Clamp(setup.MarginLeftPoints * _zoom, 18 * _zoom, pageWidth * .32);
        var marginRight = Math.Clamp(setup.MarginRightPoints * _zoom, 18 * _zoom, pageWidth * .32);
        var marginTop = Math.Clamp(setup.MarginTopPoints * _zoom, 18 * _zoom, pageHeight * .25);
        var marginBottom = Math.Clamp(setup.MarginBottomPoints * _zoom, 18 * _zoom, pageHeight * .25);
        var contentWidth = Math.Max(120, pageWidth - marginLeft - marginRight);
        var pageGap = 26 * _zoom;
        var pageY = 4d;
        var cursorY = marginTop;
        var paginated = _editor.Document.LayoutMode == NotesLayoutMode.Paginated;
        _pages.Add(new HavenRect(pageX, pageY, pageWidth, pageHeight));

        foreach (var block in _editor.Blocks())
        {
            var measure = Measure(block, contentWidth);
            if (paginated && cursorY + measure.Height > pageHeight - marginBottom && cursorY > marginTop + 2)
            {
                pageY += pageHeight + pageGap;
                cursorY = marginTop;
                _pages.Add(new HavenRect(pageX, pageY, pageWidth, pageHeight));
            }
            var left = pageX + marginLeft + measure.Indent;
            var width = Math.Max(80, contentWidth - measure.Indent);
            var rect = new HavenRect(left, pageY + cursorY, width, measure.Height);
            var content = new HavenRect(rect.X, rect.Y + measure.TopInset, rect.Width, Math.Max(1, rect.Height - measure.TopInset));
            _layouts.Add(new BlockLayout(block, rect, content, measure.FontSize, measure.LineHeight, measure.CharacterWidth, measure.Columns));
            cursorY += measure.Height + 7 * _zoom;
        }

        if (!paginated)
        {
            var height = Math.Max(pageHeight, cursorY + marginBottom);
            _pages.Clear();
            _pages.Add(new HavenRect(pageX, 4, pageWidth, height));
        }
        var totalHeight = _pages.Count == 0 ? pageHeight : _pages[^1].Bottom + 10;
        SetValue(HavenProperties.Height, HavenLength.Px(Math.Max(700, totalHeight)));
    }

    private MeasureResult Measure(NotesBlock block, double width)
    {
        var first = block.Runs.FirstOrDefault();
        var baseSize = Math.Max(9, (first?.FontSize ?? (block.Kind == NotesBlockKind.Heading ? 24 : 14)) * _zoom);
        var charWidth = Math.Max(4, baseSize * .56);
        var indent = Math.Clamp(block.Paragraph.IndentLeft * _zoom, 0, width * .65);
        var available = Math.Max(80, width - indent);
        var columns = Math.Max(1, (int)Math.Floor(available / charWidth));
        var lineHeight = Math.Max(14, baseSize * 1.35 * Math.Clamp(block.Paragraph.LineSpacing, .7, 4));
        var topInset = Math.Max(0, block.Paragraph.SpaceBefore * _zoom);
        var height = block.Kind switch
        {
            NotesBlockKind.Paragraph or NotesBlockKind.Heading or NotesBlockKind.Quote or NotesBlockKind.Code => Math.Max(lineHeight, CountVisualLines(TextOf(block), columns) * lineHeight) + topInset + Math.Max(4, block.Paragraph.SpaceAfter * _zoom),
            NotesBlockKind.List => Math.Max(34 * _zoom, (block.List?.Items.Count ?? 1) * 27 * _zoom + 4 * _zoom),
            NotesBlockKind.Table => Math.Max(50 * _zoom, (block.Table?.Rows.Count ?? 1) * 42 * _zoom),
            NotesBlockKind.Image or NotesBlockKind.Audio or NotesBlockKind.Video => Math.Clamp((block.Media?.Height ?? 240) * _zoom, 120 * _zoom, 380 * _zoom) + (string.IsNullOrWhiteSpace(block.Media?.Caption) ? 0 : 28 * _zoom),
            NotesBlockKind.Shape => 180 * _zoom,
            NotesBlockKind.Equation => 70 * _zoom,
            NotesBlockKind.Divider => 18 * _zoom,
            _ => 54 * _zoom
        };
        return new MeasureResult(height, topInset, baseSize, lineHeight, charWidth, columns, indent);
    }

    private WriteDocumentPosition? HitTextPosition(HavenPoint point, bool nearest = false)
    {
        var textLayouts = _layouts.Where(layout => IsTextBlock(layout.Block)).ToArray();
        if (textLayouts.Length == 0) return null;
        var layout = textLayouts.LastOrDefault(value => value.ContentRect.Contains(point));
        if (layout is null && nearest) layout = textLayouts.OrderBy(value => DistanceToRectY(point.Y, value.ContentRect)).First();
        if (layout is null) return null;
        var localY = Math.Clamp(point.Y - layout.ContentRect.Y, 0, Math.Max(0, layout.ContentRect.Height));
        var line = Math.Max(0, (int)Math.Floor(localY / Math.Max(1, layout.LineHeight)));
        var alignmentOffset = LineAlignmentOffset(layout, line);
        var localX = Math.Max(0, point.X - layout.ContentRect.X - alignmentOffset);
        var column = Math.Max(0, (int)Math.Round(localX / Math.Max(1, layout.CharacterWidth)));
        return new WriteDocumentPosition(layout.Block.Id, OffsetForVisualPosition(TextOf(layout.Block), layout.Columns, line, column));
    }

    private bool MoveVertical(int direction, bool extend)
    {
        if (_editor is null) return false;
        BuildLayout();
        var caret = _editor.DocumentCaret;
        var layout = _layouts.FirstOrDefault(value => value.Block.Id == caret.BlockId);
        if (layout is null || !IsTextBlock(layout.Block)) return false;
        var pos = VisualPosition(TextOf(layout.Block)[..Math.Clamp(caret.Offset, 0, TextOf(layout.Block).Length)], layout.Columns);
        var targetLine = pos.Line + direction;
        if (targetLine >= 0 && targetLine < CountVisualLines(TextOf(layout.Block), layout.Columns))
        {
            _editor.SetDocumentCaret(layout.Block.Id, OffsetForVisualPosition(TextOf(layout.Block), layout.Columns, targetLine, pos.Column), extend);
            SelectionChanged?.Invoke(this, EventArgs.Empty); Invalidate(); return true;
        }
        var textLayouts = _layouts.Where(value => IsTextBlock(value.Block)).ToArray();
        var index = Array.FindIndex(textLayouts, value => value.Block.Id == layout.Block.Id);
        var target = index + direction;
        if (target < 0 || target >= textLayouts.Length) return false;
        var next = textLayouts[target];
        var nextLine = direction < 0 ? Math.Max(0, CountVisualLines(TextOf(next.Block), next.Columns) - 1) : 0;
        _editor.SetDocumentCaret(next.Block.Id, OffsetForVisualPosition(TextOf(next.Block), next.Columns, nextLine, pos.Column), extend);
        SelectionChanged?.Invoke(this, EventArgs.Empty); Invalidate(); return true;
    }

    private double LineAlignmentOffset(BlockLayout layout, int line)
    {
        var alignment = layout.Block.Paragraph.Alignment;
        if (alignment is not (NotesTextAlignment.Center or NotesTextAlignment.Right)) return 0;
        var text = TextOf(layout.Block);
        var length = VisualLineLength(text, layout.Columns, line);
        var spare = Math.Max(0, layout.ContentRect.Width - length * layout.CharacterWidth);
        return alignment == NotesTextAlignment.Center ? spare / 2 : spare;
    }

    private static int CountVisualLines(string text, int columns)
    {
        if (text.Length == 0) return 1;
        var position = VisualPosition(text, columns);
        return position.Line + 1;
    }

    private static (int Line, int Column) VisualPosition(string text, int columns)
    {
        var line = 0; var column = 0; columns = Math.Max(1, columns);
        foreach (var character in text)
        {
            if (character == '\n') { line++; column = 0; continue; }
            column++;
            if (column >= columns) { line++; column = 0; }
        }
        return (line, column);
    }

    private static int OffsetForVisualPosition(string text, int columns, int targetLine, int targetColumn)
    {
        columns = Math.Max(1, columns); targetLine = Math.Max(0, targetLine); targetColumn = Math.Max(0, targetColumn);
        var line = 0; var column = 0;
        for (var index = 0; index < text.Length; index++)
        {
            if (line == targetLine && column >= targetColumn) return index;
            if (text[index] == '\n') { if (line == targetLine) return index; line++; column = 0; continue; }
            column++;
            if (column >= columns) { line++; column = 0; }
            if (line > targetLine) return index + 1;
        }
        return text.Length;
    }

    private static int VisualLineLength(string text, int columns, int targetLine)
    {
        var count = 0; var line = 0; var column = 0;
        foreach (var character in text)
        {
            if (character == '\n') { if (line == targetLine) return count; line++; column = 0; count = 0; continue; }
            if (line == targetLine) count++;
            column++;
            if (column >= columns) { if (line == targetLine) return count; line++; column = 0; count = 0; }
            if (line > targetLine) break;
        }
        return count;
    }

    private HavenRect Absolute(HavenRect local) => new(Bounds.X + local.X, Bounds.Y + local.Y, local.Width, local.Height);
    private static double DistanceToRectY(double y, HavenRect rect) => y < rect.Y ? rect.Y - y : y > rect.Bottom ? y - rect.Bottom : 0;
    private static string TextOf(NotesBlock block) => block.Runs.Count > 0 ? string.Concat(block.Runs.Select(run => run.Text)) : block.PlainText;
    private static bool IsTextBlock(NotesBlock block) => block.Kind is NotesBlockKind.Paragraph or NotesBlockKind.Heading or NotesBlockKind.Quote or NotesBlockKind.Code;

    private static HavenBrush Colour(string? value, bool transparentFallback = false)
    {
        if (string.IsNullOrWhiteSpace(value)) return transparentFallback ? new HavenSolidBrush(0, 0, 0, 0) : new HavenSolidBrush(255, 28, 34, 42);
        var text = value.Trim().TrimStart('#');
        if (text.Length == 6 && uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
            return new HavenSolidBrush(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        if (text.Length == 8 && uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var argb))
            return new HavenSolidBrush((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);
        return transparentFallback ? new HavenSolidBrush(0, 0, 0, 0) : new HavenSolidBrush(255, 28, 34, 42);
    }

    private static HavenBrush DocumentTextColour(string? value)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized)
            || normalized.Equals("#FFEEEEEE", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("#EEEEEE", StringComparison.OrdinalIgnoreCase))
            return new HavenSolidBrush(255, 28, 34, 42);
        return Colour(normalized);
    }

    private sealed record BlockLayout(NotesBlock Block, HavenRect Rect, HavenRect ContentRect, double FontSize, double LineHeight, double CharacterWidth, int Columns);
    private readonly record struct MeasureResult(double Height, double TopInset, double FontSize, double LineHeight, double CharacterWidth, int Columns, double Indent);
}
