namespace Haven.UI;

public enum HavenSelectorKind { Name, Group, Class, Type }

public readonly record struct HavenSelector(HavenSelectorKind Kind, string Value)
{
    public static HavenSelector Parse(string selector)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(selector);
        var separator = selector.IndexOf('.');
        if (separator <= 0 || separator == selector.Length - 1)
            throw new FormatException("Haven selectors use Name.X, Group.X, Class.X, or Type.X.");
        var prefix = selector[..separator];
        var value = selector[(separator + 1)..];
        if (!Enum.TryParse<HavenSelectorKind>(prefix, true, out var kind))
            throw new FormatException($"Unknown Haven selector kind '{prefix}'.");
        return new HavenSelector(kind, value);
    }

    public bool Matches(HavenElement element) => Kind switch
    {
        HavenSelectorKind.Name => string.Equals(element.Name, Value, StringComparison.Ordinal),
        HavenSelectorKind.Group => element.Groups.Contains(Value),
        HavenSelectorKind.Class => element.Classes.Contains(Value),
        HavenSelectorKind.Type => string.Equals(element.GetType().Name, Value, StringComparison.OrdinalIgnoreCase),
        _ => false
    };

    public IReadOnlyList<HavenElement> Select(HavenElement root) =>
        root.DescendantsAndSelf().Where(Matches).ToArray();
}
