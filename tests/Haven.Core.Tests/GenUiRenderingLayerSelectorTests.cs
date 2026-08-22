using System.Text.Json;
using Haven.Core;

namespace Haven.Core.Tests;

public sealed class GenUiRenderingLayerSelectorTests
{
    [Fact] public void SelectsNativeForSimpleTrustedControls() => Assert.Equal(GenUiRenderingLayer.Native, Select("HavenText").Layer);
    [Fact] public void SelectsCompositeForMultiRegionLayout() => Assert.Equal(GenUiRenderingLayer.Composite, Select("HavenGrid").Layer);
    [Fact] public void SelectsSceneForVisualPrimitives() => Assert.Equal(GenUiRenderingLayer.Scene, Select("HavenCanvas").Layer);

    [Fact]
    public void SelectsBoundedSandboxForCustomGeneratedUi()
    {
        var decision = Select("HavenText", custom: true);
        Assert.Equal(GenUiRenderingLayer.GeneratedSandbox, decision.Layer);
        Assert.False(decision.AllowsExecutableCode);
    }

    [Fact]
    public void SemanticValidatorRejectsExecutableGeneratedCode()
    {
        var document = Document("HavenText", custom: true);
        var app = new GenUiAppDefinition("custom", 1, document, [], [], [],
            [new("root", "root", GenUiNavigationKind.Root, null, null, true)], "haven-genui-runtime/1")
        {
            Rendering = new(GenUiRenderingLayer.GeneratedSandbox, "unsafe test", AllowsExecutableCode: true)
        };
        var result = GenUiSemanticValidator.ValidateAndRepair(app);
        Assert.Contains(result.Errors, error => error.Contains("executable code", StringComparison.OrdinalIgnoreCase));
    }

    private static GenUiRenderingDecision Select(string type, bool custom = false) => GenUiRenderingLayerSelector.Select(Document(type, custom));

    private static GenUiDocument Document(string type, bool custom)
    {
        var origin = new GenUiOrigin(Guid.NewGuid(), "chat", custom ? null : Guid.NewGuid(), Guid.NewGuid());
        var root = new GenUiComponent("root", type, new Dictionary<string, JsonElement>(), [], []);
        return new GenUiDocument(Guid.NewGuid(), GenerativeUiContractValidator.CurrentContractVersion, origin, "Test", "chat", root, new Dictionary<string, JsonElement>(), DateTimeOffset.UtcNow);
    }
}
