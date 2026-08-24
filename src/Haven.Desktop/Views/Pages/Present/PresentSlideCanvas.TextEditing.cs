using Haven.Core;
using Haven.UI;

namespace Haven.Desktop.Views.Pages.Present;

internal enum PresentTextEditTarget { None = 0, SlideTitle = 1, Element = 2 }

internal sealed partial class PresentSlideCanvas
{
    private PresentTextEditTarget _textEditTarget;
    private Guid? _textEditElementId;
    private string _editingText = string.Empty;
    private string _editingOriginal = string.Empty;
    private int _textCaret;
    private int? _textSelectionAnchor;
    private bool _textPointerSelecting;

    public event Action<string>? TitleTextPreviewRequested;
    public event Action<Guid, string>? ElementTextPreviewRequested;
    public event EventHandler? TextEditCommitRequested;
    public event EventHandler? TextEditCancelRequested;

    public bool IsTextEditing => _textEditTarget != PresentTextEditTarget.None;
    public string SelectedText
    {
        get
        {
            if (!TrySelection(out var start, out var end)) return string.Empty;
            return _editingText[start..end];
        }
    }

    private HavenRect TitleRect(HavenRect slideRect) => new(
        slideRect.X + slideRect.Width * .07,
        slideRect.Y + slideRect.Height * .06,
        slideRect.Width * .86,
        slideRect.Height * .14);

    private bool TryBeginTitleEdit(HavenPoint local, HavenInputModifiers modifiers)
    {
        if (_slide is null) return false;
        var rect = TitleRect(SlideRectLocal());
        if (!rect.Contains(local)) return false;
        BeginTextEdit(PresentTextEditTarget.SlideTitle, null, _slide.Title, rect, local, modifiers);
        SelectionSetRequested?.Invoke(Array.Empty<Guid>());
        return true;
    }

    private void BeginElementTextEdit(PresentElement element, HavenPoint local, HavenInputModifiers modifiers)
    {
        var rect = ElementRect(element, SlideRectLocal(), false);
        var point = InverseRotatePoint(local, rect, element.RotationDegrees);
        BeginTextEdit(PresentTextEditTarget.Element, element.Id, element.Text, rect, point, modifiers);
    }

    private void BeginTextEdit(PresentTextEditTarget target, Guid? elementId, string? text, HavenRect rect, HavenPoint local, HavenInputModifiers modifiers)
    {
        _textEditTarget = target;
        _textEditElementId = elementId;
        _editingText = text ?? string.Empty;
        _editingOriginal = _editingText;
        _textCaret = CaretFromPoint(_editingText, rect, local, TextFontSize(target, elementId, rect));
        _textSelectionAnchor = modifiers.Shift ? 0 : null;
        _textPointerSelecting = true;
        ResetDirectGesture();
        Invalidate();
    }

    private bool TryHandleTextPointerPress(HavenPointerInput input)
    {
        if (!IsTextEditing || !TryEditingRect(out var rect, out var rotation)) return false;
        var point = rotation == 0 ? input.LocalPosition : InverseRotatePoint(input.LocalPosition, rect, rotation);
        if (!rect.Contains(point)) return false;
        var next = CaretFromPoint(_editingText, rect, point, TextFontSize(_textEditTarget, _textEditElementId, rect));
        if (!input.Modifiers.HasFlag(HavenKeyModifiers.Shift)) _textSelectionAnchor = next;
        else _textSelectionAnchor ??= _textCaret;
        _textCaret = next;
        _textPointerSelecting = true;
        Invalidate();
        return true;
    }

    private bool MoveTextPointer(HavenPointerInput input)
    {
        if (!_textPointerSelecting || !IsTextEditing || !TryEditingRect(out var rect, out var rotation)) return false;
        var point = rotation == 0 ? input.LocalPosition : InverseRotatePoint(input.LocalPosition, rect, rotation);
        _textSelectionAnchor ??= _textCaret;
        _textCaret = CaretFromPoint(_editingText, rect, point, TextFontSize(_textEditTarget, _textEditElementId, rect));
        Invalidate();
        return true;
    }

    private bool ReleaseTextPointer()
    {
        if (!_textPointerSelecting) return false;
        _textPointerSelecting = false;
        if (_textSelectionAnchor == _textCaret) _textSelectionAnchor = null;
        Invalidate();
        return true;
    }

    public bool TextInput(string? text) => InsertText(text);

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

    public bool InsertText(string? text)
    {
        if (!IsTextEditing || string.IsNullOrEmpty(text)) return false;
        var replacement = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        DeleteSelectionCore();
        _editingText = _editingText.Insert(_textCaret, replacement);
        _textCaret += replacement.Length;
        _textSelectionAnchor = null;
        PreviewText();
        return true;
    }

    public bool DeleteSelection()
    {
        if (!IsTextEditing || !TrySelection(out _, out _)) return false;
        DeleteSelectionCore();
        PreviewText();
        return true;
    }

    public bool KeyDown(HavenKey key, HavenInputModifiers modifiers)
    {
        if (!IsTextEditing)
        {
            if (key == HavenKey.Enter && _slide is not null && _selectedIds.Count == 1
                && _slide.Elements.FirstOrDefault(element => _selectedIds.Contains(element.Id)) is { Kind: PresentElementKind.Text } text)
            {
                BeginElementTextEdit(text, Center(ElementRect(text, SlideRectLocal(), false)), default);
                return true;
            }
            return false;
        }

        if (modifiers.Control && key == HavenKey.A)
        {
            _textSelectionAnchor = 0; _textCaret = _editingText.Length; Invalidate(); return true;
        }
        if (key == HavenKey.Escape) { CancelTextEdit(); return true; }
        if (modifiers.Control && key == HavenKey.Enter) { CommitTextEdit(); return true; }
        if (key == HavenKey.Enter && _textEditTarget == PresentTextEditTarget.SlideTitle) { CommitTextEdit(); return true; }
        switch (key)
        {
            case HavenKey.Left: MoveCaret(-1, modifiers.Shift); return true;
            case HavenKey.Right: MoveCaret(1, modifiers.Shift); return true;
            case HavenKey.Home: SetCaret(0, modifiers.Shift); return true;
            case HavenKey.End: SetCaret(_editingText.Length, modifiers.Shift); return true;
            case HavenKey.Backspace:
                if (DeleteSelection()) return true;
                if (_textCaret <= 0) return true;
                _editingText = _editingText.Remove(--_textCaret, 1); PreviewText(); return true;
            case HavenKey.Delete:
                if (DeleteSelection()) return true;
                if (_textCaret >= _editingText.Length) return true;
                _editingText = _editingText.Remove(_textCaret, 1); PreviewText(); return true;
            case HavenKey.Enter: return InsertText("\n");
            default: return false;
        }
    }

    public bool KeyUp(HavenKey key) => IsTextEditing && key is HavenKey.Left or HavenKey.Right or HavenKey.Home or HavenKey.End or HavenKey.Backspace or HavenKey.Delete or HavenKey.Enter or HavenKey.Escape or HavenKey.A;

    private static HavenInputModifiers ToInputModifiers(HavenKeyInput input) => new(
        Shift: input.Shift,
        Control: input.Control,
        Alt: input.Alt,
        Meta: input.Meta);

    private static HavenInputModifiers ToInputModifiers(HavenKeyModifiers modifiers) => new(
        Shift: modifiers.HasFlag(HavenKeyModifiers.Shift),
        Control: modifiers.HasFlag(HavenKeyModifiers.Control),
        Alt: modifiers.HasFlag(HavenKeyModifiers.Alt),
        Meta: modifiers.HasFlag(HavenKeyModifiers.Meta));

    public void CommitTextEdit()
    {
        if (!IsTextEditing) return;
        var changed = !string.Equals(_editingOriginal, _editingText, StringComparison.Ordinal);
        ClearTextEditState();
        if (changed) TextEditCommitRequested?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    public void CancelTextEdit()
    {
        if (!IsTextEditing) return;
        var changed = !string.Equals(_editingOriginal, _editingText, StringComparison.Ordinal);
        ClearTextEditState();
        if (changed) TextEditCancelRequested?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    private void PreviewText()
    {
        if (_textEditTarget == PresentTextEditTarget.SlideTitle) TitleTextPreviewRequested?.Invoke(_editingText);
        else if (_textEditTarget == PresentTextEditTarget.Element && _textEditElementId is { } id) ElementTextPreviewRequested?.Invoke(id, _editingText);
        Invalidate();
    }

    private void ClearTextEditState()
    {
        _textEditTarget = PresentTextEditTarget.None;
        _textEditElementId = null;
        _editingText = string.Empty;
        _editingOriginal = string.Empty;
        _textCaret = 0;
        _textSelectionAnchor = null;
        _textPointerSelecting = false;
    }

    private void MoveCaret(int delta, bool extend) => SetCaret(Math.Clamp(_textCaret + delta, 0, _editingText.Length), extend);

    private void SetCaret(int value, bool extend)
    {
        var next = Math.Clamp(value, 0, _editingText.Length);
        if (extend) _textSelectionAnchor ??= _textCaret;
        else _textSelectionAnchor = null;
        _textCaret = next;
        if (_textSelectionAnchor == _textCaret) _textSelectionAnchor = null;
        Invalidate();
    }

    private bool TrySelection(out int start, out int end)
    {
        if (_textSelectionAnchor is not { } anchor || anchor == _textCaret) { start = end = _textCaret; return false; }
        start = Math.Min(anchor, _textCaret); end = Math.Max(anchor, _textCaret); return true;
    }

    private void DeleteSelectionCore()
    {
        if (!TrySelection(out var start, out var end)) return;
        _editingText = _editingText.Remove(start, end - start);
        _textCaret = start; _textSelectionAnchor = null;
    }

    private bool TryEditingRect(out HavenRect rect, out double rotation)
    {
        rotation = 0;
        if (_textEditTarget == PresentTextEditTarget.SlideTitle) { rect = TitleRect(SlideRectLocal()); return true; }
        if (_textEditTarget == PresentTextEditTarget.Element && _slide?.Elements.FirstOrDefault(element => element.Id == _textEditElementId) is { } element)
        {
            rect = ElementRect(element, SlideRectLocal(), false); rotation = element.RotationDegrees; return true;
        }
        rect = default; return false;
    }

    private double TextFontSize(PresentTextEditTarget target, Guid? elementId, HavenRect rect)
    {
        if (target == PresentTextEditTarget.SlideTitle) return Math.Clamp(rect.Height * .34, 18, 34);
        var element = _slide?.Elements.FirstOrDefault(value => value.Id == elementId);
        return element is null ? 16 : Math.Max(8, element.TextStyle.FontSizePoints * Math.Max(.45, rect.Height / 180));
    }

    private static int CaretFromPoint(string text, HavenRect rect, HavenPoint point, double fontSize)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        var lineHeight = Math.Max(12, fontSize * 1.25);
        var charWidth = Math.Max(5, fontSize * .55);
        var lines = text.Split('\n');
        var lineIndex = Math.Clamp((int)Math.Floor((point.Y - rect.Y) / lineHeight), 0, lines.Length - 1);
        var offset = 0;
        for (var index = 0; index < lineIndex; index++) offset += lines[index].Length + 1;
        var column = Math.Clamp((int)Math.Round((point.X - rect.X) / charWidth), 0, lines[lineIndex].Length);
        return Math.Clamp(offset + column, 0, text.Length);
    }

    private void DrawSlideTitle(HavenDrawingContext context, HavenRect slideRect, double opacity)
    {
        if (_slide is null) return;
        var rect = TitleRect(slideRect);
        var text = _textEditTarget == PresentTextEditTarget.SlideTitle ? _editingText : _slide.Title;
        DrawEditableText(context, rect, text, "Segoe UI", Math.Clamp(rect.Height * .34, 18, 34), 700, new HavenTokenBrush("TextPrimary"), opacity, _textEditTarget == PresentTextEditTarget.SlideTitle);
    }

    private void DrawEditableElementText(HavenDrawingContext context, PresentElement element, HavenRect rect, double opacity)
    {
        var editing = _textEditTarget == PresentTextEditTarget.Element && _textEditElementId == element.Id;
        var text = editing ? _editingText : element.Text;
        if (string.IsNullOrWhiteSpace(text) && !editing) return;
        DrawEditableText(context, rect, text, string.IsNullOrWhiteSpace(element.TextStyle.FontFamily) ? "Segoe UI" : element.TextStyle.FontFamily, Math.Max(8, element.TextStyle.FontSizePoints * Math.Max(.45, rect.Height / 180)), element.TextStyle.Bold ? 700 : 400, Brush(element.TextStyle.Color, "TextPrimary"), opacity * element.Opacity, editing, element.TextStyle.Italic);
    }

    private void DrawEditableText(HavenDrawingContext context, HavenRect rect, string text, string fontFamily, double fontSize, int weight, HavenBrush brush, double opacity, bool editing, bool italic = false)
    {
        var layout = new HavenTextLayout(text, fontFamily, fontSize, weight, rect.Width, true, italic);
        context.Add(new HavenTextCommand(rect, layout, brush, opacity));
        if (!editing) return;
        if (TrySelection(out var start, out var end))
            context.Add(new HavenTextSelectionCommand(rect, layout, start, end, new HavenSolidBrush(88, 56, 132, 255), opacity));
        context.Add(new HavenCaretCommand(rect, new HavenTextLayout(text[..Math.Clamp(_textCaret, 0, text.Length)], fontFamily, fontSize, weight, rect.Width, true, italic), new HavenTokenBrush("Accent"), opacity));
    }
}
