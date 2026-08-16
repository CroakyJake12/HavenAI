using System.Xml;
using System.Xml.Linq;
using Haven.UI.Components;
using HavenImageComponent = Haven.UI.Components.Image;

namespace Haven.UI;

public sealed class HavenMarkupException(string sourceName, int line, int column, string message, Exception? inner = null)
    : FormatException($"{sourceName}:{line}:{column}: {message}", inner)
{
    public string SourceName { get; } = sourceName;
    public int Line { get; } = line;
    public int Column { get; } = column;
}

public sealed class HavenMarkupParser(HavenPrefabCatalog? prefabs = null)
{
    public HavenElement Parse(string source, string sourceName = "markup.hui")
    {
        try
        {
            var document = XDocument.Parse(source, LoadOptions.SetLineInfo | LoadOptions.PreserveWhitespace);
            var rootNode = document.Root ?? throw new HavenMarkupException(sourceName, 1, 1, "Haven markup has no root element.");
            var root = ParseElement(rootNode, sourceName);
            root.ValidateUniqueNames();
            return root;
        }
        catch (HavenMarkupException) { throw; }
        catch (XmlException exception) { throw new HavenMarkupException(sourceName, exception.LineNumber, exception.LinePosition, exception.Message, exception); }
    }

    internal HavenElement ParsePreparedElement(XElement node, string sourceName) => ParseElement(node, sourceName);

    private HavenElement ParseElement(XElement node, string sourceName)
    {
        var lineInfo = (IXmlLineInfo)node;
        if (node.Name.LocalName is "Class" or "Animation")
            throw new HavenMarkupException(sourceName, lineInfo.LineNumber, lineInfo.LinePosition, "Reusable Class/Animation declarations belong in the central resource files, not page markup.");

        if (node.Name.LocalName.Equals("DynamicUI", StringComparison.OrdinalIgnoreCase))
            throw new HavenMarkupException(sourceName, lineInfo.LineNumber, lineInfo.LinePosition, "DynamicUI is a template declaration. Register it with HavenDynamicUITemplateCatalog instead of placing it in a page scene tree.");

        if (node.Name.LocalName.Equals("Prefab", StringComparison.OrdinalIgnoreCase))
            return ParsePrefab(node, sourceName, lineInfo.LineNumber, lineInfo.LinePosition);

        if (node.Name.LocalName == "DynamicUIRuntime")
        {
            if (node.HasElements || !string.IsNullOrWhiteSpace(node.Value))
                throw new HavenMarkupException(sourceName, lineInfo.LineNumber, lineInfo.LinePosition, "DynamicUIRuntime content is runtime-owned and cannot contain authored child content.");
            var runtime = new DynamicUIRuntime();
            ApplyAttributes(runtime, node, sourceName, skipPrefabIds: false);
            return runtime;
        }

        var element = Create(node.Name.LocalName, sourceName, lineInfo.LineNumber, lineInfo.LinePosition);
        ApplyAttributes(element, node, sourceName, skipPrefabIds: false);

        if (!node.HasElements && !string.IsNullOrWhiteSpace(node.Value))
        {
            var inlineText = node.Value.Trim();
            switch (element)
            {
                case Text text when !node.Attributes().Any(attribute => attribute.Name.LocalName.Equals("Content", StringComparison.OrdinalIgnoreCase) || attribute.Name.LocalName.Equals("Text", StringComparison.OrdinalIgnoreCase)):
                    text.Content = inlineText;
                    break;
                case Button button when !node.Attributes().Any(attribute => attribute.Name.LocalName.Equals("Content", StringComparison.OrdinalIgnoreCase)):
                    button.Content = inlineText;
                    break;
            }
        }

        foreach (var child in node.Elements()) element.Add(ParseElement(child, sourceName));
        return element;
    }

    private HavenElement ParsePrefab(XElement node, string sourceName, int line, int column)
    {
        if (prefabs is null) throw new HavenMarkupException(sourceName, line, column, "Prefab markup requires a HavenPrefabCatalog.");
        if (node.HasElements || !string.IsNullOrWhiteSpace(node.Value)) throw new HavenMarkupException(sourceName, line, column, "Prefab usage is a reference and cannot contain inline child content.");

        var prefabId = ReadAlias(node, ["PrefabID", "pID", "ID"], "PrefabID/ID", sourceName, line, column);
        var instanceId = ReadAlias(node, ["InstanceID", "InstID", "iID"], "InstanceID", sourceName, line, column);
        Prefab prefab;
        try { prefab = prefabs.Create(prefabId, instanceId); }
        catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException or ArgumentException)
        {
            throw new HavenMarkupException(sourceName, line, column, exception.Message, exception);
        }
        ApplyAttributes(prefab, node, sourceName, skipPrefabIds: true);
        return prefab;
    }

    private static string ReadAlias(XElement node, IReadOnlyList<string> aliases, string displayName, string sourceName, int line, int column)
    {
        var matches = node.Attributes().Where(attribute => aliases.Any(alias => attribute.Name.LocalName.Equals(alias, StringComparison.OrdinalIgnoreCase))).ToArray();
        if (matches.Length == 0) throw new HavenMarkupException(sourceName, line, column, $"Prefab requires {displayName}.");
        var values = matches.Select(attribute => attribute.Value.Trim()).Distinct(StringComparer.Ordinal).ToArray();
        if (values.Length != 1 || string.IsNullOrWhiteSpace(values[0])) throw new HavenMarkupException(sourceName, line, column, $"Conflicting or empty {displayName} aliases were supplied.");
        return values[0];
    }

    private static bool IsPrefabIdAttribute(string name) =>
        name.Equals("PrefabID", StringComparison.OrdinalIgnoreCase) || name.Equals("pID", StringComparison.OrdinalIgnoreCase) || name.Equals("ID", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("InstanceID", StringComparison.OrdinalIgnoreCase) || name.Equals("InstID", StringComparison.OrdinalIgnoreCase) || name.Equals("iID", StringComparison.OrdinalIgnoreCase);

    private static void ApplyAttributes(HavenElement element, XElement node, string sourceName, bool skipPrefabIds)
    {
        foreach (var attribute in node.Attributes())
        {
            if (skipPrefabIds && IsPrefabIdAttribute(attribute.Name.LocalName)) continue;
            var info = (IXmlLineInfo)attribute;
            try
            {
                if (ApplyCondition(element, attribute.Name.LocalName, attribute.Value)) continue;
                if (attribute.Name.LocalName.Equals("OnClick", StringComparison.OrdinalIgnoreCase)) { element.ClickActions.Add(HavenAction.Parse(attribute.Value)); continue; }
                HavenPropertyCodec.Set(element, attribute.Name.LocalName, attribute.Value);
            }
            catch (Exception exception) when (exception is FormatException or KeyNotFoundException or ArgumentException)
            {
                throw new HavenMarkupException(sourceName, info.LineNumber, info.LinePosition, exception.Message, exception);
            }
        }
    }

    private static HavenElement Create(string name, string sourceName, int line, int column) => name switch
    {
        "Page" => new Page(), "Container" => new Container(), "Text" => new Text(), "Markdown" => new Markdown(), "Button" => new Button(), "Input" => new Input(), "Toggle" => new Toggle(), "Slider" => new Slider(), "Select" => new Select(), "Image" => new HavenImageComponent(), "Icon" => new Icon(), "Video" => new Video(), "Canvas" => new Canvas(), "Web" => new Web(), "Progress" => new Progress(), "Separator" => new Separator(),
        _ => throw new HavenMarkupException(sourceName, line, column, $"Unknown Haven component '{name}'.")
    };

    private static bool ApplyCondition(HavenElement element, string name, string value)
    {
        switch (name.ToLowerInvariant())
        {
            case "platform": element.Conditions.Add(new HavenPlatformCondition(value)); return true;
            case "requiredscreenwidth": var width = ParseRange(value); element.Conditions.Add(new HavenScreenRangeCondition(HavenScreenAxis.Width, width.Minimum, width.Maximum)); return true;
            case "requiredscreenheight": var height = ParseRange(value); element.Conditions.Add(new HavenScreenRangeCondition(HavenScreenAxis.Height, height.Minimum, height.Maximum)); return true;
            case "requiredscreensize": ApplyRequiredScreenSize(element, value); return true;
            case "minscreenwidth": element.Conditions.Add(new HavenScreenRangeCondition(HavenScreenAxis.Width, HavenLength.Parse(value))); return true;
            case "maxscreenwidth": element.Conditions.Add(new HavenScreenRangeCondition(HavenScreenAxis.Width, maximum: HavenLength.Parse(value))); return true;
            case "minscreenheight": element.Conditions.Add(new HavenScreenRangeCondition(HavenScreenAxis.Height, HavenLength.Parse(value))); return true;
            case "maxscreenheight": element.Conditions.Add(new HavenScreenRangeCondition(HavenScreenAxis.Height, maximum: HavenLength.Parse(value))); return true;
            default: return false;
        }
    }

    private static (HavenLength Minimum, HavenLength Maximum) ParseRange(string value)
    {
        var parts = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length switch
        {
            1 => (HavenLength.Parse(parts[0]), HavenLength.Parse(parts[0])),
            2 => (HavenLength.Parse(parts[0]), HavenLength.Parse(parts[1])),
            _ => throw new FormatException("A required screen range uses 'minimum,maximum'.")
        };
    }

    private static void ApplyRequiredScreenSize(HavenElement element, string value)
    {
        var axes = value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (axes.Length == 2)
        {
            var width = ParseRange(axes[0]);
            var height = ParseRange(axes[1]);
            element.Conditions.Add(new HavenScreenSizeCondition(width.Minimum, width.Maximum, height.Minimum, height.Maximum));
            return;
        }

        var parts = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 4)
            throw new FormatException("RequiredScreenSize uses 'minWidth,maxWidth,minHeight,maxHeight' or 'minWidth,maxWidth;minHeight,maxHeight'.");
        element.Conditions.Add(new HavenScreenSizeCondition(
            HavenLength.Parse(parts[0]), HavenLength.Parse(parts[1]),
            HavenLength.Parse(parts[2]), HavenLength.Parse(parts[3])));
    }
}
