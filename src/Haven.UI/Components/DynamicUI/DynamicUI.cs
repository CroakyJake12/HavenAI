using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Haven.UI.Components;

/// <summary>Runtime-only host for zero-to-many instances created from a DynamicUI template.</summary>
public sealed class DynamicUIRuntime : Container
{
    private readonly List<DynamicUIItem> _items = [];

    public DynamicUIRuntime()
    {
        Accessibility.Role = HavenAccessibleRole.Group;
        SetValue(HavenProperties.Gap, HavenLength.Px(0), HavenValueSource.Default);
        SetValue(HavenProperties.Padding, HavenThickness.Zero, HavenValueSource.Default);
    }

    public IReadOnlyList<DynamicUIItem> Items => _items;

    public override HavenComponentMetadata Metadata => new(
        "DynamicUIRuntime",
        "Components/DynamicUI/DynamicUI.cs",
        ["DynamicUIRuntime"],
        [],
        "Runtime-only host for ordered DynamicUIItem instances. Dynamic data is not persisted by Haven.UI.");

    public DynamicUIItem GetItem(string instanceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        return _items.FirstOrDefault(item => string.Equals(item.InstanceID, instanceId.Trim(), StringComparison.Ordinal))
            ?? throw new KeyNotFoundException($"DynamicUIRuntime '{Name ?? "<unnamed>"}' has no item with InstanceID '{instanceId}'.");
    }

    public bool TryGetItem(string instanceId, out DynamicUIItem item)
    {
        item = null!;
        if (string.IsNullOrWhiteSpace(instanceId)) return false;
        var match = _items.FirstOrDefault(candidate => string.Equals(candidate.InstanceID, instanceId.Trim(), StringComparison.Ordinal));
        if (match is null) return false;
        item = match;
        return true;
    }

    public bool DeleteItem(string instanceId)
    {
        if (!TryGetItem(instanceId, out var item)) return false;
        _items.Remove(item);
        Remove(item);
        item.Detach();
        return true;
    }

    public void ClearItems()
    {
        foreach (var item in _items.ToArray()) DeleteItem(item.InstanceID);
    }

    public void MoveItem(string instanceId, int index)
    {
        var item = GetItem(instanceId);
        if (index < 0 || index >= _items.Count) throw new ArgumentOutOfRangeException(nameof(index));
        var oldIndex = _items.IndexOf(item);
        if (oldIndex == index) return;
        _items.RemoveAt(oldIndex);
        _items.Insert(index, item);
        RebuildChildren();
    }

    internal void AddItem(DynamicUIItem item, int? index)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (TryGetItem(item.InstanceID, out _))
            throw new InvalidOperationException($"DynamicUIRuntime '{Name ?? "<unnamed>"}' already contains InstanceID '{item.InstanceID}'.");
        var target = index ?? _items.Count;
        if (target < 0 || target > _items.Count) throw new ArgumentOutOfRangeException(nameof(index));
        _items.Insert(target, item);
        item.Attach(this);
        RebuildChildren();
    }

    private void RebuildChildren()
    {
        Update(() =>
        {
            foreach (var child in Children.ToArray()) Remove(child);
            foreach (var item in _items) Add(item);
        });
    }
}

/// <summary>Stable handle and name-scope root for one instantiated DynamicUI template.</summary>
public sealed class DynamicUIItem : Container
{
    private readonly HavenDynamicUITemplate _template;
    private readonly HavenPrefabCatalog? _prefabs;
    private readonly Dictionary<string, object?> _values;
    private readonly Dictionary<DynamicPropertyOverrideKey, string> _propertyOverrides = [];
    private DynamicUIRuntime? _owner;
    private bool _deleted;

    internal DynamicUIItem(HavenDynamicUITemplate template, string instanceId, IReadOnlyDictionary<string, object?>? values, HavenPrefabCatalog? prefabs)
    {
        _template = template;
        _prefabs = prefabs;
        InstanceID = instanceId;
        TemplateName = template.Name;
        _values = values is null
            ? new Dictionary<string, object?>(StringComparer.Ordinal)
            : new Dictionary<string, object?>(values, StringComparer.Ordinal);
        Accessibility.Role = HavenAccessibleRole.Group;
        SetValue(HavenProperties.Gap, HavenLength.Px(0), HavenValueSource.Default);
        SetValue(HavenProperties.Padding, HavenThickness.Zero, HavenValueSource.Default);
        RefreshContents();
    }

    public string TemplateName { get; }
    public string InstanceID { get; }
    public bool IsDeleted => _deleted;
    public override bool CreatesNameScope => true;
    public IReadOnlyDictionary<string, object?> Values => new Dictionary<string, object?>(_values, StringComparer.Ordinal);

    public override HavenComponentMetadata Metadata => new(
        "DynamicUIItem",
        "Components/DynamicUI/DynamicUI.cs",
        ["DynamicUIItem"],
        [],
        "Per-instance DynamicUI name scope and lifecycle handle. Runtime values are intentionally non-persistent.");

    public HavenElement GetComponent(string componentName)
    {
        EnsureAlive();
        ArgumentException.ThrowIfNullOrWhiteSpace(componentName);
        return EnumerateOwnNameScope()
            .FirstOrDefault(element => string.Equals(element.Name, componentName.Trim(), StringComparison.Ordinal))
            ?? throw new KeyNotFoundException($"DynamicUI '{TemplateName}' instance '{InstanceID}' has no component named '{componentName}'.");
    }

    public T GetComponent<T>(string componentName) where T : HavenElement
    {
        var component = GetComponent(componentName);
        return component as T
            ?? throw new InvalidOperationException($"DynamicUI '{TemplateName}' component '{componentName}' is {component.GetType().Name}, not {typeof(T).Name}.");
    }

    public void SetVariable(string name, object? value)
    {
        EnsureAlive();
        ValidateVariable(name);
        var snapshot = new Dictionary<string, object?>(_values, StringComparer.Ordinal);
        _values[name] = value;
        ApplyVariableChanges(new HashSet<string>(StringComparer.Ordinal) { name }, snapshot);
    }

    public void SetVariables(IReadOnlyDictionary<string, object?> values)
    {
        EnsureAlive();
        ArgumentNullException.ThrowIfNull(values);
        foreach (var name in values.Keys) ValidateVariable(name);
        var snapshot = new Dictionary<string, object?>(_values, StringComparer.Ordinal);
        foreach (var pair in values) _values[pair.Key] = pair.Value;
        ApplyVariableChanges(values.Keys.ToHashSet(StringComparer.Ordinal), snapshot);
    }

    public void SetProperty(string componentName, string propertyName, object? value)
    {
        EnsureAlive();
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        var component = GetComponent(componentName);
        var path = GetPath(component);
        var key = new DynamicPropertyOverrideKey(path, NormalizeProperty(propertyName));
        var formatted = HavenDynamicUITemplate.FormatValue(value);
        try
        {
            HavenPropertyCodec.Set(component, propertyName, formatted);
            ValidateUniqueNames();
            _propertyOverrides[key] = formatted;
        }
        catch
        {
            RefreshContents();
            throw;
        }
    }

    public bool ClearProperty(string componentName, string propertyName)
    {
        EnsureAlive();
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        var component = GetComponent(componentName);
        var key = new DynamicPropertyOverrideKey(GetPath(component), NormalizeProperty(propertyName));
        if (!_propertyOverrides.Remove(key, out var previous)) return false;
        try
        {
            RefreshContents();
            return true;
        }
        catch
        {
            _propertyOverrides[key] = previous;
            RefreshContents();
            throw;
        }
    }

    public bool Delete()
    {
        EnsureAlive();
        return (_owner ?? throw new InvalidOperationException("DynamicUI item is not attached to a runtime host."))
            .DeleteItem(InstanceID);
    }

    internal void Attach(DynamicUIRuntime owner)
    {
        if (_deleted) throw new InvalidOperationException("A deleted DynamicUI item cannot be reattached.");
        if (_owner is not null && !ReferenceEquals(_owner, owner))
            throw new InvalidOperationException("DynamicUI item is already attached to another runtime host.");
        _owner = owner;
    }

    internal void Detach()
    {
        _owner = null;
        _deleted = true;
        Update(() =>
        {
            foreach (var child in Children.ToArray()) Remove(child);
            _propertyOverrides.Clear();
            _values.Clear();
        });
    }

    private void ApplyVariableChanges(IReadOnlySet<string> changedNames, IReadOnlyDictionary<string, object?> snapshot)
    {
        var structural = _template.RequiresRebuild(changedNames);
        try
        {
            if (structural) RefreshContents();
            else
            {
                _template.ApplyBindings(this, _values, changedNames);
                ValidateUniqueNames();
            }
        }
        catch
        {
            _values.Clear();
            foreach (var pair in snapshot) _values[pair.Key] = pair.Value;
            if (structural) RefreshContents();
            else
            {
                _template.ApplyBindings(this, _values, changedNames);
                ValidateUniqueNames();
            }
            throw;
        }
    }

    private void RefreshContents()
    {
        var nextChildren = _template.Instantiate(_values, _prefabs);
        Update(() =>
        {
            foreach (var child in Children.ToArray()) Remove(child);
            foreach (var child in nextChildren) Add(child);
            ApplyPropertyOverrides();
            ValidateUniqueNames();
        });
    }

    private void ApplyPropertyOverrides()
    {
        foreach (var pair in _propertyOverrides)
        {
            var target = FindByPath(pair.Key.Path)
                ?? throw new InvalidOperationException($"DynamicUI '{TemplateName}' changed structure while reapplying a runtime property override.");
            HavenPropertyCodec.Set(target, pair.Key.PropertyName, pair.Value);
        }
    }

    private void ValidateVariable(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!_template.Variables.Contains(name))
            throw new KeyNotFoundException($"DynamicUI template '{TemplateName}' has no variable '{{{{{name}}}}}'.");
    }

    private IEnumerable<HavenElement> EnumerateOwnNameScope()
    {
        foreach (var child in Children)
        {
            yield return child;
            if (child.CreatesNameScope) continue;
            foreach (var descendant in EnumerateNameScopeChildren(child)) yield return descendant;
        }
    }

    private static IEnumerable<HavenElement> EnumerateNameScopeChildren(HavenElement parent)
    {
        foreach (var child in parent.Children)
        {
            yield return child;
            if (child.CreatesNameScope) continue;
            foreach (var descendant in EnumerateNameScopeChildren(child)) yield return descendant;
        }
    }

    private string GetPath(HavenElement target)
    {
        var path = new List<int>();
        if (!TryBuildPath(this, target, path))
            throw new InvalidOperationException("DynamicUI property target is not part of this item.");
        return string.Join('/', path);
    }

    internal HavenElement? FindByPath(string path)
    {
        HavenElement current = this;
        if (string.IsNullOrEmpty(path)) return current;
        foreach (var token in path.Split('/'))
        {
            if (!int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var index) || index < 0 || index >= current.Children.Count)
                return null;
            current = current.Children[index];
        }
        return current;
    }

    private static bool TryBuildPath(HavenElement current, HavenElement target, List<int> path)
    {
        if (ReferenceEquals(current, target)) return true;
        for (var index = 0; index < current.Children.Count; index++)
        {
            path.Add(index);
            if (TryBuildPath(current.Children[index], target, path)) return true;
            path.RemoveAt(path.Count - 1);
        }
        return false;
    }

    internal bool IsPropertyOverridden(string path, string propertyName) =>
        _propertyOverrides.ContainsKey(new DynamicPropertyOverrideKey(path, NormalizeProperty(propertyName)));

    private static string NormalizeProperty(string propertyName) => propertyName.Trim().ToLowerInvariant();

    private void EnsureAlive()
    {
        if (_deleted) throw new InvalidOperationException($"DynamicUI '{TemplateName}' instance '{InstanceID}' has been deleted.");
    }

    private readonly record struct DynamicPropertyOverrideKey(string Path, string PropertyName);
}

/// <summary>Page/scope runtime API for creating, locating, updating, ordering and deleting DynamicUI instances.</summary>
public sealed class DynamicUI
{
    private readonly HavenElement _root;
    private readonly HavenDynamicUITemplateCatalog _templates;
    private readonly HavenPrefabCatalog? _prefabs;

    public DynamicUI(HavenElement root, HavenDynamicUITemplateCatalog templates, HavenPrefabCatalog? prefabs = null)
    {
        _root = root ?? throw new ArgumentNullException(nameof(root));
        _templates = templates ?? throw new ArgumentNullException(nameof(templates));
        _prefabs = prefabs;
    }

    public DynamicUIItem CreateItem(string template, string location, string? instanceId = null, IReadOnlyDictionary<string, object?>? values = null, int? index = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(template);
        var id = string.IsNullOrWhiteSpace(instanceId) ? $"{template.Trim()}-{Guid.NewGuid():N}" : instanceId.Trim();
        var item = _templates.CreateItem(template, id, values, _prefabs);
        ResolveLocation(location).AddItem(item, index);
        return item;
    }

    public DynamicUIItem GetItem(string location, string instanceId) => ResolveLocation(location).GetItem(instanceId);
    public bool TryGetItem(string location, string instanceId, out DynamicUIItem item) => ResolveLocation(location).TryGetItem(instanceId, out item);
    public bool DeleteItem(string location, string instanceId) => ResolveLocation(location).DeleteItem(instanceId);
    public void Clear(string location) => ResolveLocation(location).ClearItems();
    public void MoveItem(string location, string instanceId, int index) => ResolveLocation(location).MoveItem(instanceId, index);
    public DynamicUIRuntime GetRuntime(string location) => ResolveLocation(location);

    private DynamicUIRuntime ResolveLocation(string location)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(location);
        var matches = _root.DescendantsAndSelf()
            .OfType<DynamicUIRuntime>()
            .Where(runtime => string.Equals(runtime.Name, location.Trim(), StringComparison.Ordinal))
            .ToArray();
        return matches.Length switch
        {
            1 => matches[0],
            0 => throw new KeyNotFoundException($"No DynamicUIRuntime named '{location}' exists in the supplied Haven scope."),
            _ => throw new InvalidOperationException($"DynamicUIRuntime name '{location}' is ambiguous in the supplied Haven scope. Create the DynamicUI API from a narrower name scope.")
        };
    }
}

/// <summary>Parsed-once catalog of DynamicUI template declarations.</summary>
public sealed class HavenDynamicUITemplateCatalog
{
    private readonly Dictionary<string, HavenDynamicUITemplate> _templates = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<string> TemplateNames => _templates.Keys.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray();

    public void Register(string markup, string sourceName = "dynamicUI.hui")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(markup);
        XDocument document;
        try { document = XDocument.Parse(markup, LoadOptions.SetLineInfo | LoadOptions.PreserveWhitespace); }
        catch (XmlException exception)
        {
            throw new HavenMarkupException(sourceName, exception.LineNumber, exception.LinePosition, exception.Message, exception);
        }

        var root = document.Root ?? throw new HavenMarkupException(sourceName, 1, 1, "DynamicUI markup has no root declaration.");
        var rootInfo = (IXmlLineInfo)root;
        if (!root.Name.LocalName.Equals("DynamicUI", StringComparison.OrdinalIgnoreCase))
            throw new HavenMarkupException(sourceName, rootInfo.LineNumber, rootInfo.LinePosition, "DynamicUI template files must use <DynamicUI Name=\"...\"> as the root declaration.");

        var nameAttributes = root.Attributes().Where(attribute => attribute.Name.LocalName.Equals("Name", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (nameAttributes.Length != 1 || string.IsNullOrWhiteSpace(nameAttributes[0].Value))
            throw new HavenMarkupException(sourceName, rootInfo.LineNumber, rootInfo.LinePosition, "DynamicUI requires exactly one non-empty Name attribute.");
        var unknownAttributes = root.Attributes().Where(attribute => !attribute.Name.LocalName.Equals("Name", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (unknownAttributes.Length > 0)
            throw new HavenMarkupException(sourceName, rootInfo.LineNumber, rootInfo.LinePosition, $"DynamicUI declaration has unsupported attribute '{unknownAttributes[0].Name.LocalName}'.");
        if (!root.Elements().Any())
            throw new HavenMarkupException(sourceName, rootInfo.LineNumber, rootInfo.LinePosition, "DynamicUI requires at least one Haven component inside the declaration.");
        if (root.Nodes().OfType<XText>().Any(text => !string.IsNullOrWhiteSpace(text.Value)))
            throw new HavenMarkupException(sourceName, rootInfo.LineNumber, rootInfo.LinePosition, "DynamicUI declaration may contain Haven components, comments, and whitespace only.");
        if (root.Descendants().Any(element => element.Name.LocalName.Equals("DynamicUI", StringComparison.OrdinalIgnoreCase)))
            throw new HavenMarkupException(sourceName, rootInfo.LineNumber, rootInfo.LinePosition, "DynamicUI declarations cannot be nested.");

        var template = HavenDynamicUITemplate.Compile(nameAttributes[0].Value.Trim(), root.Elements(), sourceName);
        if (!_templates.TryAdd(template.Name, template))
            throw new InvalidOperationException($"Duplicate DynamicUI template '{template.Name}'.");
    }

    public static HavenDynamicUITemplateCatalog FromAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        var catalog = new HavenDynamicUITemplateCatalog();
        var resources = assembly.GetManifestResourceNames()
            .Where(name => name.Contains(".DynamicUI.", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".hui", StringComparison.OrdinalIgnoreCase))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        foreach (var resource in resources)
        {
            using var stream = assembly.GetManifestResourceStream(resource) ?? throw new InvalidOperationException($"Could not open DynamicUI resource '{resource}'.");
            using var reader = new StreamReader(stream);
            catalog.Register(reader.ReadToEnd(), resource);
        }
        return catalog;
    }

    internal DynamicUIItem CreateItem(string templateName, string instanceId, IReadOnlyDictionary<string, object?>? values, HavenPrefabCatalog? prefabs)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateName);
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        if (!_templates.TryGetValue(templateName.Trim(), out var template))
            throw new KeyNotFoundException($"Unknown DynamicUI template '{templateName}'.");
        return new DynamicUIItem(template, instanceId.Trim(), values, prefabs);
    }
}

internal sealed class HavenDynamicUITemplate
{
    private static readonly Regex VariablePattern = new(@"\{\{\s*([A-Za-z_][A-Za-z0-9_.-]*)\s*\}\}", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private readonly XElement[] _elements;
    private readonly HavenDynamicUIBinding[] _bindings;

    private HavenDynamicUITemplate(
        string name,
        XElement[] elements,
        HavenDynamicUIBinding[] bindings,
        IReadOnlySet<string> variables,
        string sourceName)
    {
        Name = name;
        _elements = elements;
        _bindings = bindings;
        Variables = variables;
        SourceName = sourceName;
    }

    public string Name { get; }
    public IReadOnlySet<string> Variables { get; }
    public string SourceName { get; }

    public static HavenDynamicUITemplate Compile(string name, IEnumerable<XElement> elements, string sourceName)
    {
        var cachedElements = elements.Select(element => new XElement(element)).ToArray();
        var variables = new HashSet<string>(StringComparer.Ordinal);
        var bindings = new List<HavenDynamicUIBinding>();
        for (var index = 0; index < cachedElements.Length; index++)
        {
            var element = cachedElements[index];
            CollectVariables(element, variables, sourceName);
            CollectBindings(element, index.ToString(CultureInfo.InvariantCulture), bindings);
        }
        return new HavenDynamicUITemplate(name, cachedElements, bindings.ToArray(), variables, sourceName);
    }

    public IReadOnlyList<HavenElement> Instantiate(IReadOnlyDictionary<string, object?> values, HavenPrefabCatalog? prefabs)
    {
        var missing = Variables.Where(variable => !values.ContainsKey(variable)).ToArray();
        if (missing.Length > 0)
            throw new KeyNotFoundException($"DynamicUI template '{Name}' requires value(s): {string.Join(", ", missing.Select(value => $"{{{{{value}}}}}"))}.");

        var parser = new HavenMarkupParser(prefabs);
        var result = new List<HavenElement>(_elements.Length);
        foreach (var cached in _elements)
        {
            var instance = new XElement(cached);
            Interpolate(instance, values);
            result.Add(parser.ParsePreparedElement(instance, SourceName));
        }
        return result;
    }

    public bool RequiresRebuild(IReadOnlySet<string> changedNames) =>
        _bindings.Any(binding => binding.Structural && binding.Dependencies.Any(changedNames.Contains));

    public void ApplyBindings(DynamicUIItem item, IReadOnlyDictionary<string, object?> values, IReadOnlySet<string> changedNames)
    {
        foreach (var binding in _bindings)
        {
            if (binding.Structural || !binding.Dependencies.Any(changedNames.Contains)) continue;
            if (item.IsPropertyOverridden(binding.Path, binding.PropertyName)) continue;
            var target = item.FindByPath(binding.Path)
                ?? throw new InvalidOperationException($"DynamicUI template '{Name}' could not resolve runtime binding path '{binding.Path}'.");
            HavenPropertyCodec.Set(target, binding.PropertyName, Expand(binding.Template, values));
        }
    }

    internal static string FormatValue(object? value) => value switch
    {
        null => string.Empty,
        bool boolean => boolean ? "true" : "false",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
        _ => value.ToString() ?? string.Empty
    };

    private static void CollectBindings(XElement element, string path, ICollection<HavenDynamicUIBinding> bindings)
    {
        foreach (var attribute in element.Attributes())
        {
            var dependencies = GetDependencies(attribute.Value);
            if (dependencies.Count == 0) continue;
            bindings.Add(new HavenDynamicUIBinding(
                path,
                attribute.Name.LocalName,
                attribute.Value,
                dependencies,
                IsStructuralBinding(element, attribute.Name.LocalName)));
        }

        if (!element.HasElements && !string.IsNullOrWhiteSpace(element.Value))
        {
            var hasContentAttribute = element.Attributes().Any(attribute =>
                attribute.Name.LocalName.Equals("Content", StringComparison.OrdinalIgnoreCase) ||
                attribute.Name.LocalName.Equals("Text", StringComparison.OrdinalIgnoreCase));
            var isInlineContent =
                (element.Name.LocalName == "Text" && !hasContentAttribute) ||
                (element.Name.LocalName == "Button" && !element.Attributes().Any(attribute => attribute.Name.LocalName.Equals("Content", StringComparison.OrdinalIgnoreCase)));
            var inlineTemplate = element.Value.Trim();
            var dependencies = GetDependencies(inlineTemplate);
            if (isInlineContent && dependencies.Count > 0)
                bindings.Add(new HavenDynamicUIBinding(path, "Content", inlineTemplate, dependencies, false));
        }

        var childIndex = 0;
        foreach (var child in element.Elements())
        {
            CollectBindings(child, $"{path}/{childIndex}", bindings);
            childIndex++;
        }
    }

    private static bool IsStructuralBinding(XElement element, string attributeName)
    {
        if (attributeName.Equals("OnClick", StringComparison.OrdinalIgnoreCase)) return true;
        if (attributeName.ToLowerInvariant() is
            "platform" or "requiredscreenwidth" or "requiredscreenheight" or "requiredscreensize" or
            "minscreenwidth" or "maxscreenwidth" or "minscreenheight" or "maxscreenheight") return true;
        if (!element.Name.LocalName.Equals("Prefab", StringComparison.OrdinalIgnoreCase)) return false;
        return attributeName.Equals("PrefabID", StringComparison.OrdinalIgnoreCase) ||
               attributeName.Equals("pID", StringComparison.OrdinalIgnoreCase) ||
               attributeName.Equals("ID", StringComparison.OrdinalIgnoreCase) ||
               attributeName.Equals("InstanceID", StringComparison.OrdinalIgnoreCase) ||
               attributeName.Equals("InstID", StringComparison.OrdinalIgnoreCase) ||
               attributeName.Equals("iID", StringComparison.OrdinalIgnoreCase);
    }

    private static HashSet<string> GetDependencies(string value) =>
        VariablePattern.Matches(value)
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

    private static void CollectVariables(XElement element, ISet<string> variables, string sourceName)
    {
        foreach (var attribute in element.Attributes()) CollectVariables(attribute.Value, variables, sourceName);
        foreach (var text in element.Nodes().OfType<XText>()) CollectVariables(text.Value, variables, sourceName);
        foreach (var child in element.Elements()) CollectVariables(child, variables, sourceName);
    }

    private static void CollectVariables(string value, ISet<string> variables, string sourceName)
    {
        foreach (Match match in VariablePattern.Matches(value)) variables.Add(match.Groups[1].Value);
        var residue = VariablePattern.Replace(value, string.Empty);
        if (residue.Contains("{{", StringComparison.Ordinal) || residue.Contains("}}", StringComparison.Ordinal))
            throw new FormatException($"{sourceName}: malformed DynamicUI variable expression '{value}'. Variables use '{{{{NAME}}}}'.");
    }

    private static void Interpolate(XElement element, IReadOnlyDictionary<string, object?> values)
    {
        foreach (var attribute in element.Attributes()) attribute.Value = Expand(attribute.Value, values);
        foreach (var text in element.Nodes().OfType<XText>()) text.Value = Expand(text.Value, values);
        foreach (var child in element.Elements()) Interpolate(child, values);
    }

    private static string Expand(string value, IReadOnlyDictionary<string, object?> values) => VariablePattern.Replace(value, match =>
    {
        var name = match.Groups[1].Value;
        return values.TryGetValue(name, out var replacement)
            ? FormatValue(replacement)
            : throw new KeyNotFoundException($"DynamicUI value '{{{{{name}}}}}' was not supplied.");
    });

    private sealed record HavenDynamicUIBinding(
        string Path,
        string PropertyName,
        string Template,
        IReadOnlySet<string> Dependencies,
        bool Structural);
}
