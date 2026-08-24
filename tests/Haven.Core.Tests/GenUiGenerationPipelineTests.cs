using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace Haven.Core.Tests;

public sealed class GenUiGenerationPipelineTests
{
    [Fact]
    public void ExecuteRunsValidationRenderAndRuntimeInspectionInOrder()
    {
        var store = new GenUiInstanceStore();
        var origin = new GenUiOrigin(Guid.NewGuid(), "chat", null, Guid.NewGuid());
        var document = new GenUiDocument(
            Guid.NewGuid(),
            GenerativeUiContractValidator.CurrentContractVersion,
            origin,
            "Status",
            "chat",
            new GenUiComponent("root", "HavenText",
                new Dictionary<string, JsonElement> { ["text"] = JsonSerializer.SerializeToElement("Ready") }, [], []),
            new Dictionary<string, JsonElement> { ["ready"] = JsonSerializer.SerializeToElement(true) },
            DateTimeOffset.UtcNow);
        var plan = new GenUiGenerationPlan("Show status", "chat", "custom");
        var specification = GenUiGenerationPipeline.CreateSpecification(plan, document);

        var result = GenUiGenerationPipeline.Execute(
            plan,
            specification,
            store.Register,
            definition => GenUiGenerationPipeline.InspectRegisteredRuntime(definition, store));

        Assert.Equal(GenUiGenerationStage.Planned, result.CompletedStages[0]);
        Assert.Contains(GenUiGenerationStage.StructurallyValidated, result.CompletedStages);
        Assert.Contains(GenUiGenerationStage.SemanticallyValidated, result.CompletedStages);
        Assert.Contains(GenUiGenerationStage.Rendered, result.CompletedStages);
        Assert.Equal(GenUiGenerationStage.RuntimeInspected, result.CompletedStages[^1]);
        Assert.NotNull(store.TryGet(origin.InstanceId));
        Assert.Equal(GenUiValueType.Boolean, result.Definition.StateSchema.Single().Type);
    }

    [Fact]
    public void ExecuteRejectsADeclaredRuntimeThatDoesNotMountTheValidatedDocument()
    {
        var origin = new GenUiOrigin(Guid.NewGuid(), "chat", null, Guid.NewGuid());
        var document = new GenUiDocument(
            Guid.NewGuid(), GenerativeUiContractValidator.CurrentContractVersion, origin, "Status", "chat",
            new GenUiComponent("root", "HavenText",
                new Dictionary<string, JsonElement> { ["text"] = JsonSerializer.SerializeToElement("Ready") }, [], []),
            new Dictionary<string, JsonElement>(), DateTimeOffset.UtcNow);
        var plan = new GenUiGenerationPlan("Show status", "chat", "custom");
        var specification = GenUiGenerationPipeline.CreateSpecification(plan, document);

        var error = Assert.Throws<InvalidOperationException>(() => GenUiGenerationPipeline.Execute(
            plan, specification, _ => { }, _ => ["surface was not mounted"]));

        Assert.Contains("runtime inspection failed", error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
