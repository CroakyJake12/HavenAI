namespace Haven.Core;

public static class GenUiRenderingLayerSelector
{
    private static readonly HashSet<string> SceneComponents = new(StringComparer.Ordinal)
    {
        "HavenCanvas", "HavenGraph", "HavenChart", "HavenImage"
    };

    private static readonly HashSet<string> CompositeComponents = new(StringComparer.Ordinal)
    {
        "HavenWorkspace", "HavenGrid", "HavenSplitView", "HavenTabs", "HavenWizard", "HavenToolbar"
    };

    public static GenUiRenderingDecision Select(GenUiDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var components = Flatten(document.Root).ToArray();

        if (document.Origin.TemplateId is null)
            return new(GenUiRenderingLayer.GeneratedSandbox,
                "Purpose-built generated UI stays inside the bounded declarative HavenUI sandbox; executable generated code is disabled.",
                AllowsExecutableCode: false);

        if (components.Any(component => SceneComponents.Contains(component.ComponentType)))
            return new(GenUiRenderingLayer.Scene,
                "Visual or spatial primitives require the scene renderer for fidelity and interaction.");

        if (components.Any(component => CompositeComponents.Contains(component.ComponentType)) || components.Length >= 12)
            return new(GenUiRenderingLayer.Composite,
                "Multi-region composition benefits from the composite HavenUI layout path.");

        return new(GenUiRenderingLayer.Native,
            "Standard controls can use the native trusted HavenUI renderer without a scene dependency.");
    }

    private static IEnumerable<GenUiComponent> Flatten(GenUiComponent root)
    {
        yield return root;
        foreach (var child in root.Children)
        foreach (var item in Flatten(child))
            yield return item;
    }
}
