namespace Haven.UI;

public sealed record HavenAction(HavenSelector Target, string Property, string Value)
{
    public static HavenAction Parse(string expression)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);
        var arrow = expression.IndexOf("->", StringComparison.Ordinal);
        var equals = expression.IndexOf('=', arrow + 2);
        if (arrow <= 0 || equals <= arrow + 2)
            throw new FormatException("Haven action syntax is '<selector> -> <safe-property>=<value>'.");
        return new HavenAction(HavenSelector.Parse(expression[..arrow].Trim()), expression[(arrow + 2)..equals].Trim(), expression[(equals + 1)..].Trim());
    }
}

public sealed class HavenActionExecutor
{
    public int Execute(HavenElement root, HavenAction action)
    {
        if (!HavenPropertyCodec.IsSafeActionProperty(action.Property))
            throw new InvalidOperationException($"Haven actions may not mutate '{action.Property}'.");
        var matches = action.Target.Select(root);
        foreach (var element in matches)
        {
            if (action.Property.Equals("Checked", StringComparison.OrdinalIgnoreCase))
            {
                if (element is not Components.Toggle toggle) throw new InvalidOperationException("Checked actions target Toggle components only.");
                toggle.IsChecked = bool.Parse(action.Value);
            }
            else if (action.Property.Equals("Selected", StringComparison.OrdinalIgnoreCase))
            {
                var selected = bool.Parse(action.Value); element.SetState(HavenElementState.Selected, selected); element.Accessibility.Selected = selected;
            }
            else if (action.Property.Equals("Expanded", StringComparison.OrdinalIgnoreCase))
            {
                element.SetState(HavenElementState.Expanded, bool.Parse(action.Value));
            }
            else HavenPropertyCodec.Set(element, action.Property, action.Value, HavenValueSource.State);
        }
        return matches.Count;
    }

    public int ExecuteClick(HavenElement root, HavenElement source)
    {
        var scope = FindActionScope(source) ?? root;
        var count = 0;
        foreach (var action in source.ClickActions) count += Execute(scope, action);
        return count;
    }

    private static HavenElement? FindActionScope(HavenElement source)
    {
        for (var current = source; current is not null; current = current.Parent)
            if (current.CreatesNameScope) return current;
        return null;
    }
}
