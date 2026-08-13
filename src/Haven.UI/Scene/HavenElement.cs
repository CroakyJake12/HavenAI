namespace Haven.UI;

[Flags]
public enum HavenElementState
{
    Default = 0,
    Hover = 1 << 0,
    Pressed = 1 << 1,
    Focused = 1 << 2,
    Selected = 1 << 3,
    Disabled = 1 << 4,
    Checked = 1 << 5,
    Expanded = 1 << 6
}

public sealed record HavenComponentMetadata(
    string ComponentName,
    string CanonicalSource,
    IReadOnlyList<string> SharedClasses,
    IReadOnlyList<string> SharedAnimations,
    string Notes);

/// <summary>Base node of the Haven-owned scene tree.</summary>
public abstract class HavenElement
{
    private readonly Dictionary<HavenProperty, Dictionary<HavenValueSource, object?>> _values = [];
    private readonly List<HavenElement> _children = [];

    public HavenElement? Parent { get; private set; }
    public IReadOnlyList<HavenElement> Children => _children;
    public IList<IHavenRenderCondition> Conditions { get; } = new List<IHavenRenderCondition>();
    public IList<HavenAction> ClickActions { get; } = new List<HavenAction>();
    public HavenAccessibility Accessibility { get; } = new();
    public HavenElementState State { get; private set; }
    public HavenSize DesiredSize { get; internal set; }
    public HavenRect Bounds { get; internal set; }
    public bool IsIncluded { get; internal set; } = true;

    public event EventHandler? Invalidated;
    public event EventHandler? Invoked;

    /// <summary>Requests a new Haven measure/render pass after component-owned state changes.</summary>
    protected internal void Invalidate() => Invalidated?.Invoke(this, EventArgs.Empty);

    public string? Name
    {
        get => GetValue(HavenProperties.Name);
        set => SetValue(HavenProperties.Name, value);
    }

    public string Group
    {
        get => GetValue(HavenProperties.Group);
        set => SetValue(HavenProperties.Group, value ?? string.Empty);
    }

    public string Class
    {
        get => GetValue(HavenProperties.Class);
        set => SetValue(HavenProperties.Class, value ?? string.Empty);
    }

    public virtual HavenComponentMetadata Metadata => new(
        GetType().Name,
        GetType().Name,
        [],
        [],
        "Generic Haven scene element.");

    public IReadOnlySet<string> Groups => SplitTokens(Group).ToHashSet(StringComparer.Ordinal);
    public IReadOnlySet<string> Classes => SplitTokens(Class).ToHashSet(StringComparer.Ordinal);
    public IReadOnlyList<string> GroupTokens => SplitTokens(Group);
    public IReadOnlyList<string> ClassTokens => SplitTokens(Class);

    public T GetValue<T>(HavenProperty<T> property)
    {
        ArgumentNullException.ThrowIfNull(property);
        if (!_values.TryGetValue(property, out var slot) || slot.Count == 0) return property.DefaultValueTyped;
        var source = slot.Keys.Max();
        return slot[source] is T typed ? typed : property.DefaultValueTyped;
    }

    public void SetValue<T>(HavenProperty<T> property, T value, HavenValueSource source = HavenValueSource.Explicit)
    {
        ArgumentNullException.ThrowIfNull(property);
        if (!_values.TryGetValue(property, out var slot))
        {
            slot = [];
            _values[property] = slot;
        }
        if (slot.TryGetValue(source, out var existing) && Equals(existing, value)) return;
        slot[source] = value;
        Invalidate();
    }

    public void ClearValue(HavenProperty property, HavenValueSource source)
    {
        ArgumentNullException.ThrowIfNull(property);
        if (!_values.TryGetValue(property, out var slot) || !slot.Remove(source)) return;
        if (slot.Count == 0) _values.Remove(property);
        Invalidate();
    }

    public void Add(HavenElement child)
    {
        ArgumentNullException.ThrowIfNull(child);
        if (ReferenceEquals(child, this) || child.DescendantsAndSelf().Contains(this))
            throw new InvalidOperationException("A Haven element cannot contain itself or one of its ancestors.");
        if (child.Parent is not null)
            throw new InvalidOperationException("A Haven element already has a parent. Remove it before reparenting.");
        child.Parent = this;
        _children.Add(child);
        Invalidate();
    }

    public bool Remove(HavenElement child)
    {
        if (!_children.Remove(child)) return false;
        child.Parent = null;
        Invalidate();
        return true;
    }

    public void SetState(HavenElementState state, bool active)
    {
        var next = active ? State | state : State & ~state;
        if (next == State) return;
        State = next;
        OnStateChanged();
        Invalidate();
    }

    internal void Invoke()
    {
        Invoked?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    protected virtual void OnStateChanged() { }

    public bool MatchesConditions(HavenRenderContext context) => Conditions.All(condition => condition.Matches(context));

    public IEnumerable<HavenElement> DescendantsAndSelf()
    {
        yield return this;
        foreach (var child in _children)
        foreach (var descendant in child.DescendantsAndSelf())
            yield return descendant;
    }

    public void ValidateUniqueNames()
    {
        var duplicates = DescendantsAndSelf()
            .Where(element => !string.IsNullOrWhiteSpace(element.Name))
            .GroupBy(element => element.Name!, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicates.Length > 0)
            throw new InvalidOperationException($"Duplicate Haven element Name values: {string.Join(", ", duplicates)}.");
    }

    private static IReadOnlyList<string> SplitTokens(string value) => value
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Distinct(StringComparer.Ordinal)
        .ToArray();
}
