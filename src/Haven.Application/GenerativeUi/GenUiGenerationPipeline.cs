using System.Text.Json;
using Haven.Core;

namespace Haven.Application;

public enum GenUiGenerationStage
{
    Planned,
    Specified,
    StructurallyValidated,
    SemanticallyValidated,
    Repaired,
    Rendered,
    RuntimeInspected
}

public sealed record GenUiGenerationPlan(string Intent, string AppKey, string TemplateKey);
public sealed record GenUiGenerationSpecification(GenUiAppDefinition Definition);
public sealed record GenUiGenerationPipelineResult(
    GenUiAppDefinition Definition,
    IReadOnlyList<GenUiGenerationStage> CompletedStages,
    IReadOnlyList<string> Repairs);

/// <summary>Enforces the production GenUI plan-to-runtime-validation sequence.</summary>
public static class GenUiGenerationPipeline
{
    public const string RuntimeVersion = "haven-genui-runtime/1";

    public static GenUiGenerationSpecification CreateSpecification(GenUiGenerationPlan plan, GenUiDocument document)
    {
        ValidatePlan(plan);
        ArgumentNullException.ThrowIfNull(document);
        var stateSchema = document.State
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new GenUiStateFieldDefinition(
                pair.Key, InferType(pair.Value), GenUiPersistenceScope.Instance, Required: false, pair.Value.Clone()))
            .ToArray();
        var definition = new GenUiAppDefinition(
            plan.TemplateKey,
            GenUiSemanticValidator.CurrentSchemaVersion,
            document,
            stateSchema,
            [],
            [],
            [new GenUiNavigationRoute("root", document.Root.ComponentId, GenUiNavigationKind.Root, null, null, true)],
            RuntimeVersion);
        return new GenUiGenerationSpecification(definition);
    }

    public static GenUiGenerationPipelineResult Execute(
        GenUiGenerationPlan plan,
        GenUiGenerationSpecification specification,
        Action<GenUiDocument> render,
        Func<GenUiAppDefinition, IReadOnlyList<string>> inspectRuntime)
    {
        ValidatePlan(plan);
        ArgumentNullException.ThrowIfNull(specification);
        ArgumentNullException.ThrowIfNull(render);
        ArgumentNullException.ThrowIfNull(inspectRuntime);
        var stages = new List<GenUiGenerationStage>
        {
            GenUiGenerationStage.Planned,
            GenUiGenerationStage.Specified
        };

        var structuralErrors = GenerativeUiContractValidator.Validate(specification.Definition.Document);
        if (structuralErrors.Count > 0)
            throw new InvalidOperationException("GenUI structural validation failed: " + string.Join(" ", structuralErrors));
        stages.Add(GenUiGenerationStage.StructurallyValidated);

        var semantic = GenUiSemanticValidator.ValidateAndRepair(specification.Definition);
        if (!semantic.IsValid)
            throw new InvalidOperationException("GenUI semantic validation failed: " + string.Join(" ", semantic.Errors));
        stages.Add(GenUiGenerationStage.SemanticallyValidated);
        if (semantic.Repairs.Count > 0) stages.Add(GenUiGenerationStage.Repaired);

        var qualityIssues = GenUiDocumentQualityValidator.Validate(semantic.Definition.Document);
        if (qualityIssues.Count > 0)
            throw new InvalidOperationException("GenUI quality validation failed: " +
                string.Join(" ", qualityIssues.Select(issue => issue.Message)));

        render(semantic.Definition.Document);
        stages.Add(GenUiGenerationStage.Rendered);

        var runtimeErrors = inspectRuntime(semantic.Definition);
        if (runtimeErrors.Count > 0)
            throw new InvalidOperationException("GenUI runtime inspection failed: " + string.Join(" ", runtimeErrors));
        stages.Add(GenUiGenerationStage.RuntimeInspected);
        return new GenUiGenerationPipelineResult(semantic.Definition, stages, semantic.Repairs);
    }

    public static IReadOnlyList<string> InspectRegisteredRuntime(
        GenUiAppDefinition definition,
        GenUiInstanceStore instances)
    {
        var errors = new List<string>();
        var mounted = instances.TryGet(definition.Document.Origin.InstanceId);
        if (mounted is null)
        {
            errors.Add("Rendered instance is not registered in the live GenUI store.");
            return errors;
        }
        if (mounted.DocumentId != definition.Document.DocumentId)
            errors.Add("Mounted document identity differs from the validated specification.");
        if (!mounted.Root.ComponentId.Equals(definition.Document.Root.ComponentId, StringComparison.Ordinal))
            errors.Add("Mounted root component differs from the validated specification.");
        foreach (var stateField in definition.StateSchema.Where(field => field.Required))
            if (!mounted.State.ContainsKey(stateField.Key))
                errors.Add($"Required state '{stateField.Key}' is missing after render.");
        return errors;
    }

    private static void ValidatePlan(GenUiGenerationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (string.IsNullOrWhiteSpace(plan.Intent))
            throw new ArgumentException("GenUI plan intent is required.", nameof(plan));
        if (string.IsNullOrWhiteSpace(plan.AppKey))
            throw new ArgumentException("GenUI plan app key is required.", nameof(plan));
        if (string.IsNullOrWhiteSpace(plan.TemplateKey))
            throw new ArgumentException("GenUI plan template key is required.", nameof(plan));
    }

    private static GenUiValueType InferType(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => GenUiValueType.String,
        JsonValueKind.Number when value.TryGetInt64(out _) => GenUiValueType.Integer,
        JsonValueKind.Number => GenUiValueType.Number,
        JsonValueKind.True or JsonValueKind.False => GenUiValueType.Boolean,
        JsonValueKind.Array => GenUiValueType.Array,
        JsonValueKind.Object => GenUiValueType.Object,
        _ => GenUiValueType.Object
    };
}
