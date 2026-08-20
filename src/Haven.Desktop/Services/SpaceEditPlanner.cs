using System.Text.Json;
using System.Text.Json.Serialization;
using Haven.Application;
using Haven.Core;

namespace Haven.Desktop.Services;

internal sealed record SpaceEditPatch(
    string? Name,
    string? Description,
    string? ModelName,
    string? Instructions,
    SpaceThinkingMode? ThinkingMode,
    string? SurfaceTemplate,
    string? SurfaceInputsJson);

internal sealed record SpaceEditPlanResult(bool Succeeded, string Message, SpaceEditPatch? Patch = null);

internal sealed class SpaceEditPlanner(IOllamaClient models, Func<string?> defaultModel)
{
    private static readonly HashSet<string> SurfaceTemplates = new(StringComparer.OrdinalIgnoreCase)
    {
        "standard", "checklist", "data-grid", "card-deck", "dashboard", "assessment", "workflow", "custom"
    };

    public async Task<SpaceEditPlanResult> PlanAsync(string instruction, SpaceDefinition space, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(space);
        instruction = instruction?.Trim() ?? string.Empty;
        if (instruction.Length == 0) return Fail("Describe the Space change you want.");
        if (instruction.Length > 1200) return Fail("Space change instructions must be 1,200 characters or fewer.");

        try
        {
            var available = await models.GetModelsAsync(cancellationToken).ConfigureAwait(false);
            var preferred = defaultModel();
            var model = available.FirstOrDefault(item => !string.IsNullOrWhiteSpace(preferred)
                                                        && item.Name.Equals(preferred, StringComparison.OrdinalIgnoreCase))
                        ?? available.FirstOrDefault();
            if (model is null) return Fail("No model is available to edit this Space.");

            var prompt = string.Join(Environment.NewLine,
            [
                $"Current Space: {space.Name}",
                $"Kind: {space.Kind} (immutable)",
                $"Description: {space.Description}",
                $"Model: {space.ModelName ?? "default"}",
                $"Thinking: {space.ThinkingMode}",
                $"Instructions: {space.Instructions}",
                $"Generated surface: {space.GeneratedSurface?.TemplateKey ?? "standard"}",
                string.Empty,
                "Requested change:",
                instruction,
                string.Empty,
                "Return exactly one JSON object. Omit fields that should stay unchanged:",
                "{\"name\":\"optional\",\"description\":\"optional\",\"modelName\":\"optional\",\"instructions\":\"optional\",\"thinkingMode\":\"default|fast|balanced|deep\",\"surfaceTemplate\":\"standard|checklist|data-grid|card-deck|dashboard|assessment|workflow|custom\",\"surfaceInputs\":{}}",
                "Never propose file paths, permissions, archive/delete operations, Space kind changes, built-in identity changes, code execution, URLs, or navigation."
            ]);

            var response = await models.CompleteAsync(
                new OllamaChatRequest(
                    model.Name,
                    [new OllamaMessage("user", prompt)],
                    EffortLevel.Low,
                    "You are Haven's Space configuration planner. Output strict JSON only. You may edit only the explicitly listed draft fields.",
                    Options: new GenerationOptions(0.2, 3072, 0)),
                cancellationToken).ConfigureAwait(false);
            return ParseAndValidate(response);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or HttpRequestException or JsonException or InvalidOperationException)
        {
            return Fail($"Haven could not create a safe Space edit: {exception.Message}");
        }
    }

    internal static SpaceEditPlanResult ParseAndValidate(string response)
    {
        if (string.IsNullOrWhiteSpace(response)) return Fail("Haven returned an empty Space edit.");
        var start = response.IndexOf('{');
        var end = response.LastIndexOf('}');
        if (start < 0 || end <= start) return Fail("Haven did not return a valid Space edit.");

        PlannerPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<PlannerPayload>(response[start..(end + 1)], new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                PropertyNameCaseInsensitive = true,
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
            });
        }
        catch (JsonException)
        {
            return Fail("Haven returned malformed Space-edit JSON.");
        }
        if (payload is null) return Fail("Haven returned an empty Space edit.");

        var name = NormalizeOptional(payload.Name);
        if (name is { Length: > 80 }) return Fail("Haven proposed a Space name longer than 80 characters.");
        var description = NormalizeOptional(payload.Description);
        if (description is { Length: > 800 }) return Fail("Haven proposed a Space description that is too long.");
        var modelName = NormalizeOptional(payload.ModelName);
        if (modelName is { Length: > 160 }) return Fail("Haven proposed an invalid model name.");
        var instructions = NormalizeOptional(payload.Instructions);
        if (instructions is { Length: > 6000 }) return Fail("Haven proposed Space instructions that are too long.");

        SpaceThinkingMode? thinking = null;
        if (!string.IsNullOrWhiteSpace(payload.ThinkingMode))
        {
            if (!Enum.TryParse<SpaceThinkingMode>(payload.ThinkingMode.Trim(), true, out var parsedThinking))
                return Fail("Haven proposed an unsupported thinking mode.");
            thinking = parsedThinking;
        }

        string? surfaceTemplate = null;
        string? surfaceInputs = null;
        if (!string.IsNullOrWhiteSpace(payload.SurfaceTemplate))
        {
            surfaceTemplate = payload.SurfaceTemplate.Trim().ToLowerInvariant();
            if (!SurfaceTemplates.Contains(surfaceTemplate)) return Fail("Haven proposed an unsupported generated-surface template.");
        }
        if (payload.SurfaceInputs is { } surfaceElement)
        {
            if (surfaceElement.ValueKind != JsonValueKind.Object) return Fail("Generated-surface inputs must be a JSON object.");
            surfaceInputs = surfaceElement.GetRawText();
            if (surfaceInputs.Length > 8000) return Fail("Generated-surface inputs are too large.");
            surfaceTemplate ??= "custom";
        }

        if (name is null && description is null && modelName is null && instructions is null && thinking is null && surfaceTemplate is null && surfaceInputs is null)
            return Fail("Haven did not propose any Space changes.");

        return new SpaceEditPlanResult(true, "Suggested Space changes are ready to review.",
            new SpaceEditPatch(name, description, modelName, instructions, thinking, surfaceTemplate, surfaceInputs));
    }

    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static SpaceEditPlanResult Fail(string message) => new(false, message);

    private sealed record PlannerPayload(
        string? Name,
        string? Description,
        string? ModelName,
        string? Instructions,
        string? ThinkingMode,
        string? SurfaceTemplate,
        JsonElement? SurfaceInputs);
}
