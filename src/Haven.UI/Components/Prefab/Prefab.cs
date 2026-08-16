using System.Collections.Concurrent;
using System.Reflection;

namespace Haven.UI.Components;

public enum PrefabMode
{
    Dynamic,
    Static
}

public abstract class HavenPrefabDefinition
{
    public virtual string PrefabID
    {
        get
        {
            var name = GetType().Name;
            return name.EndsWith("Prefab", StringComparison.OrdinalIgnoreCase) ? name[..^6] : name;
        }
    }

    public virtual PrefabMode Mode => PrefabMode.Dynamic;
    public virtual void OnCreated(Prefab instance) { }
}

public sealed class Prefab : Container
{
    private HavenPrefabState? _state;

    internal Prefab(string prefabId, string instanceId, PrefabMode mode)
    {
        PrefabID = prefabId;
        InstanceID = instanceId;
        Mode = mode;
        Accessibility.Role = HavenAccessibleRole.Group;
        SetValue(HavenProperties.Gap, HavenLength.Px(0), HavenValueSource.Default);
        SetValue(HavenProperties.Padding, HavenThickness.Uniform(HavenLength.Px(0)), HavenValueSource.Default);
    }

    public string PrefabID { get; }
    public string InstanceID { get; }
    public PrefabMode Mode { get; }
    public override bool CreatesNameScope => true;

    public override HavenComponentMetadata Metadata => new(
        "Prefab",
        "Components/Prefab/Prefab.cs",
        ["Prefab"],
        [],
        "Reusable Haven component tree. Dynamic instances keep state by PrefabID+InstanceID; Static instances share state app-wide by PrefabID.");

    public HavenElement GetComponent(string componentName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(componentName);
        return DescendantsAndSelf()
            .Skip(1)
            .FirstOrDefault(element => string.Equals(element.Name, componentName, StringComparison.Ordinal))
            ?? throw new KeyNotFoundException($"Prefab '{PrefabID}' instance '{InstanceID}' has no component named '{componentName}'.");
    }

    public T GetComponent<T>(string componentName) where T : HavenElement
    {
        var component = GetComponent(componentName);
        return component as T
            ?? throw new InvalidOperationException($"Prefab '{PrefabID}' component '{componentName}' is {component.GetType().Name}, not {typeof(T).Name}.");
    }

    public bool IsComponentEnabled(string componentName)
    {
        _ = GetComponent(componentName);
        return _state?.IsEnabled(componentName) ?? true;
    }

    public void SetComponentEnabled(string componentName, bool enabled)
    {
        _ = GetComponent(componentName);
        (_state ?? throw new InvalidOperationException("Prefab state has not been attached.")).SetComponentEnabled(componentName, enabled);
    }

    internal void AttachState(HavenPrefabState state)
    {
        _state = state;
        state.Attach(this);
    }

    internal void ApplyComponentState(string componentName, bool enabled)
    {
        var component = DescendantsAndSelf()
            .Skip(1)
            .FirstOrDefault(element => string.Equals(element.Name, componentName, StringComparison.Ordinal));
        if (component is null) return;
        if (enabled) component.ClearValue(HavenProperties.Visibility, HavenValueSource.Prefab);
        else component.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed, HavenValueSource.Prefab);
    }
}

internal sealed class HavenPrefabState
{
    private readonly object _gate = new();
    private readonly HashSet<string> _disabled = new(StringComparer.Ordinal);
    private readonly List<WeakReference<Prefab>> _instances = [];

    public bool IsEnabled(string componentName)
    {
        lock (_gate) return !_disabled.Contains(componentName);
    }

    public void Attach(Prefab prefab)
    {
        string[] disabled;
        lock (_gate)
        {
            _instances.RemoveAll(reference => !reference.TryGetTarget(out _));
            _instances.Add(new WeakReference<Prefab>(prefab));
            disabled = _disabled.ToArray();
        }
        foreach (var componentName in disabled) prefab.ApplyComponentState(componentName, false);
    }

    public void SetComponentEnabled(string componentName, bool enabled)
    {
        Prefab[] instances;
        lock (_gate)
        {
            if (enabled) _disabled.Remove(componentName);
            else _disabled.Add(componentName);
            _instances.RemoveAll(reference => !reference.TryGetTarget(out _));
            instances = _instances.Select(reference => reference.TryGetTarget(out var instance) ? instance : null).Where(instance => instance is not null).Cast<Prefab>().ToArray();
        }
        foreach (var instance in instances) instance.ApplyComponentState(componentName, enabled);
    }
}

public sealed class HavenPrefabCatalog
{
    private sealed record Entry(HavenPrefabDefinition Definition, string Markup, string SourceName);

    private static readonly ConcurrentDictionary<string, HavenPrefabState> StaticStates = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, HavenPrefabState> DynamicStates = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly AsyncLocal<Stack<string>?> _creationStack = new();

    public IReadOnlyCollection<string> PrefabIDs => _entries.Keys.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray();

    public void Register(HavenPrefabDefinition definition, string markup, string sourceName = "prefab.hui")
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentException.ThrowIfNullOrWhiteSpace(markup);
        var id = definition.PrefabID?.Trim();
        if (string.IsNullOrWhiteSpace(id)) throw new InvalidOperationException($"Prefab definition '{definition.GetType().FullName}' has an empty PrefabID.");
        if (!_entries.TryAdd(id, new Entry(definition, markup, sourceName)))
            throw new InvalidOperationException($"Duplicate prefab definition '{id}'.");
    }

    public static HavenPrefabCatalog FromAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        var catalog = new HavenPrefabCatalog();
        var definitions = assembly.GetTypes()
            .Where(type => !type.IsAbstract
                && (type.IsPublic || type.IsNestedPublic)
                && typeof(HavenPrefabDefinition).IsAssignableFrom(type))
            .Select(type => Activator.CreateInstance(type) as HavenPrefabDefinition
                ?? throw new InvalidOperationException($"Prefab code-behind '{type.FullName}' must have a public parameterless constructor."))
            .ToArray();
        var resources = assembly.GetManifestResourceNames()
            .Where(name => name.Contains(".Prefabs.", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".hui", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        foreach (var definition in definitions)
        {
            var suffix = $".{definition.PrefabID}.hui";
            var matches = resources.Where(name => name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length == 0) throw new InvalidOperationException($"Prefab '{definition.PrefabID}' has code-behind but no paired Prefabs/{definition.PrefabID}.hui embedded resource.");
            if (matches.Length > 1) throw new InvalidOperationException($"Prefab '{definition.PrefabID}' matches multiple .hui resources: {string.Join(", ", matches)}.");
            using var stream = assembly.GetManifestResourceStream(matches[0]) ?? throw new InvalidOperationException($"Could not open prefab resource '{matches[0]}'.");
            using var reader = new StreamReader(stream);
            catalog.Register(definition, reader.ReadToEnd(), matches[0]);
        }

        foreach (var resource in resources)
        {
            var withoutExtension = resource[..^4];
            var fileName = withoutExtension[(withoutExtension.LastIndexOf('.') + 1)..];
            if (!definitions.Any(definition => string.Equals(definition.PrefabID, fileName, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"Prefab markup '{resource}' has no paired paired .hui.cs HavenPrefabDefinition for PrefabID '{fileName}'.");
        }
        return catalog;
    }

    public Prefab Create(string prefabId, string instanceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefabId);
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        if (!_entries.TryGetValue(prefabId.Trim(), out var entry)) throw new KeyNotFoundException($"Unknown PrefabID '{prefabId}'.");

        var stack = _creationStack.Value ??= new Stack<string>();
        if (stack.Contains(entry.Definition.PrefabID, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Recursive prefab cycle detected: {string.Join(" -> ", stack.Reverse())} -> {entry.Definition.PrefabID}.");

        stack.Push(entry.Definition.PrefabID);
        try
        {
            var content = new HavenMarkupParser(this).Parse(entry.Markup, entry.SourceName);
            var prefab = new Prefab(entry.Definition.PrefabID, instanceId.Trim(), entry.Definition.Mode);
            prefab.Add(content);
            var state = entry.Definition.Mode == PrefabMode.Static
                ? StaticStates.GetOrAdd(entry.Definition.PrefabID, _ => new HavenPrefabState())
                : DynamicStates.GetOrAdd($"{entry.Definition.PrefabID}\u001f{instanceId.Trim()}", _ => new HavenPrefabState());
            prefab.AttachState(state);
            entry.Definition.OnCreated(prefab);
            return prefab;
        }
        finally
        {
            stack.Pop();
            if (stack.Count == 0) _creationStack.Value = null;
        }
    }
}
