using Haven.Core;

namespace Haven.Application;

public sealed partial class PresentEditor
{
    private string? _liveTextEditBefore;

    public bool IsLiveTextEditActive => _liveTextEditBefore is not null;

    public void BeginLiveTextEdit()
    {
        _liveTextEditBefore ??= Snapshot(Document);
    }

    public bool PreviewSlideTitle(Guid slideId, string? title)
    {
        var slide = RequireSlide(slideId);
        var value = title ?? string.Empty;
        if (string.Equals(slide.Title, value, StringComparison.Ordinal)) return false;
        BeginLiveTextEdit();
        slide.Title = value;
        return true;
    }

    public bool PreviewElementText(Guid slideId, Guid elementId, string? text)
    {
        var slide = RequireSlide(slideId);
        var element = slide.Elements.FirstOrDefault(item => item.Id == elementId)
            ?? throw new ArgumentOutOfRangeException(nameof(elementId));
        var value = text ?? string.Empty;
        if (string.Equals(element.Text, value, StringComparison.Ordinal)) return false;
        BeginLiveTextEdit();
        element.Text = value;
        return true;
    }

    public bool CommitLiveTextEdit()
    {
        if (_liveTextEditBefore is null) return false;
        var before = _liveTextEditBefore;
        _liveTextEditBefore = null;
        Document.Normalize();
        var after = Snapshot(Document);
        if (string.Equals(before, after, StringComparison.Ordinal)) return false;
        Push(_undo, before);
        _redo.Clear();
        Document.UpdatedAt = _timeProvider.GetUtcNow();
        EnsureSelectionIsValid();
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public bool CancelLiveTextEdit()
    {
        if (_liveTextEditBefore is null) return false;
        var before = _liveTextEditBefore;
        _liveTextEditBefore = null;
        Restore(before);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }
}
