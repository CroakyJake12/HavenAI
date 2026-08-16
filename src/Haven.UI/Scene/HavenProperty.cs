namespace Haven.UI;

public enum HavenValueSource
{
    Default = 0,
    SystemClass = 10,
    UserClass = 20,
    Explicit = 30,
    State = 40,
    Prefab = 45,
    Animation = 50
}

public abstract class HavenProperty
{
    protected HavenProperty(string name, Type valueType, object? defaultValue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        ValueType = valueType;
        DefaultValue = defaultValue;
    }

    public string Name { get; }
    public Type ValueType { get; }
    public object? DefaultValue { get; }
}

public sealed class HavenProperty<T>(string name, T defaultValue) : HavenProperty(name, typeof(T), defaultValue)
{
    public T DefaultValueTyped { get; } = defaultValue;
}

public static class HavenPropertyRegistry
{
    private static readonly Dictionary<string, HavenProperty> Properties = new(StringComparer.OrdinalIgnoreCase);

    public static HavenProperty<T> Register<T>(HavenProperty<T> property)
    {
        ArgumentNullException.ThrowIfNull(property);
        lock (Properties)
        {
            if (!Properties.TryAdd(property.Name, property))
                throw new InvalidOperationException($"A Haven property named '{property.Name}' is already registered.");
        }
        return property;
    }

    public static HavenProperty Resolve(string name)
    {
        lock (Properties)
            return Properties.TryGetValue(name, out var property)
                ? property
                : throw new KeyNotFoundException($"Haven.UI has no registered property named '{name}'.");
    }

    public static bool TryResolve(string name, out HavenProperty property)
    {
        lock (Properties)
            return Properties.TryGetValue(name, out property!);
    }
}
