namespace Haven.UI.Components;

public sealed record HavenSelectPopupItemLayout(int Index, string Text, HavenRect Bounds);
public sealed record HavenSelectPopupLayout(HavenRect Bounds, IReadOnlyList<HavenSelectPopupItemLayout> Items, bool OpensAbove);

public sealed class Select : HavenElement
{
    internal const double PopupGap = 6d;
    internal const double PopupPadding = 6d;
    internal const double PopupRowHeight = 40d;
    internal const double PopupRadius = 16d;
    private IReadOnlyList<string> _items = [];
    private int _selectedIndex = -1;
    private int _popupFirstIndex = -1;

    public event EventHandler? SelectionChanged;

    public Select()
    {
        Accessibility.Role = HavenAccessibleRole.List;
        Accessibility.Focusable = true;
        SetValue(HavenProperties.Hover, true, HavenValueSource.Default);
        SetValue(HavenProperties.MinHeight, HavenLength.Px(48), HavenValueSource.Default);
        SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(24)), HavenValueSource.Default);
        SetValue(HavenProperties.Background, "SurfaceRaised", HavenValueSource.Default);
    }

    public IReadOnlyList<string> Items
    {
        get => _items;
        set
        {
            _items = value ?? [];
            _popupFirstIndex = -1;
            if (_selectedIndex >= _items.Count) SelectedIndex = _items.Count - 1;
            Invalidate();
        }
    }

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            var next = value >= 0 && value < _items.Count ? value : -1;
            if (_selectedIndex == next) return;
            _selectedIndex = next;
            _popupFirstIndex = -1;
            Accessibility.AccessibleName = SelectedItem;
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            Invalidate();
        }
    }

    public string? SelectedItem => _selectedIndex >= 0 && _selectedIndex < _items.Count ? _items[_selectedIndex] : null;
    public bool IsExpanded
    {
        get => State.HasFlag(HavenElementState.Expanded);
        set
        {
            if (IsExpanded == value) return;
            _popupFirstIndex = -1;
            SetState(HavenElementState.Expanded, value);
        }
    }

    public HavenSelectPopupLayout? GetPopupLayout(HavenRect viewport)
    {
        if (!IsExpanded || _items.Count == 0 || Bounds.Width <= 0 || Bounds.Height <= 0 || viewport.Width <= 0 || viewport.Height <= 0) return null;

        const double margin = 6d;
        var usableWidth = Math.Max(0d, viewport.Width - margin * 2d);
        if (usableWidth <= 0) return null;
        var width = Math.Min(Math.Max(Bounds.Width, 160d), usableWidth);
        var desiredHeight = PopupPadding * 2d + _items.Count * PopupRowHeight;
        var below = Math.Max(0d, viewport.Bottom - Bounds.Bottom - PopupGap);
        var above = Math.Max(0d, Bounds.Y - viewport.Y - PopupGap);
        var minimumUsefulHeight = PopupPadding * 2d + PopupRowHeight * 2d;
        var opensAbove = below < Math.Min(desiredHeight, minimumUsefulHeight) && above > below;
        var availableHeight = opensAbove ? above : below;
        var rowCount = Math.Min(_items.Count, (int)Math.Floor(Math.Max(0d, availableHeight - PopupPadding * 2d) / PopupRowHeight));
        if (rowCount <= 0) return null;

        var height = PopupPadding * 2d + rowCount * PopupRowHeight;
        var maxFirst = Math.Max(0, _items.Count - rowCount);
        var preferredFirst = _popupFirstIndex >= 0
            ? _popupFirstIndex
            : _selectedIndex >= 0
                ? _selectedIndex - rowCount / 2
                : 0;
        var first = Math.Clamp(preferredFirst, 0, maxFirst);
        var x = Math.Clamp(Bounds.X, viewport.X + margin, Math.Max(viewport.X + margin, viewport.Right - margin - width));
        var y = opensAbove ? Bounds.Y - PopupGap - height : Bounds.Bottom + PopupGap;
        var panel = new HavenRect(x, y, width, height);
        var rows = new List<HavenSelectPopupItemLayout>(rowCount);
        for (var offset = 0; offset < rowCount; offset++)
        {
            var index = first + offset;
            rows.Add(new HavenSelectPopupItemLayout(
                index,
                _items[index],
                new HavenRect(panel.X + PopupPadding, panel.Y + PopupPadding + offset * PopupRowHeight, panel.Width - PopupPadding * 2d, PopupRowHeight)));
        }
        return new HavenSelectPopupLayout(panel, rows, opensAbove);
    }

    internal bool ScrollPopup(double deltaY, HavenRect viewport)
    {
        if (Math.Abs(deltaY) < double.Epsilon) return false;
        var popup = GetPopupLayout(viewport);
        if (popup is null || popup.Items.Count == 0 || popup.Items.Count >= _items.Count) return false;

        var first = popup.Items[0].Index;
        var maxFirst = Math.Max(0, _items.Count - popup.Items.Count);
        var next = Math.Clamp(first + Math.Sign(deltaY), 0, maxFirst);
        if (next == first) return false;

        _popupFirstIndex = next;
        Invalidate();
        return true;
    }

    internal void MoveSelection(int direction)
    {
        if (_items.Count == 0) return;
        var start = _selectedIndex < 0 ? (direction >= 0 ? -1 : 0) : _selectedIndex;
        SelectedIndex = Math.Clamp(start + Math.Sign(direction), 0, _items.Count - 1);
    }

    internal void SelectBoundary(bool end)
    {
        if (_items.Count == 0) return;
        SelectedIndex = end ? _items.Count - 1 : 0;
    }

    public override HavenComponentMetadata Metadata => new(
        "Select",
        "Components/Select/Select.cs",
        ["Select"],
        ["SelectExpand"],
        "Selection, keyboard navigation, expansion and backend-neutral popup placement live here; popup rendering stays in Haven.UI.");
}
