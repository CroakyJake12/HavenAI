namespace Haven.UI.Components;

public sealed class Select : HavenElement
{
    private IReadOnlyList<string> _items = [];
    private int _selectedIndex = -1;

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
            if (_selectedIndex >= _items.Count) SelectedIndex = _items.Count - 1;
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
            Accessibility.AccessibleName = SelectedItem;
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public string? SelectedItem => _selectedIndex >= 0 && _selectedIndex < _items.Count ? _items[_selectedIndex] : null;
    public bool IsExpanded { get => State.HasFlag(HavenElementState.Expanded); set => SetState(HavenElementState.Expanded, value); }

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
        "Selection, keyboard navigation and expansion semantics live here; popup composition stays in Haven.UI.");
}
