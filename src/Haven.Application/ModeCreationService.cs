/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Application/ModeCreationService.cs, in the Application layer, which coordinates use cases through abstractions without owning platform details.
 * What: This file owns ModeCreationService, ModeCreationResult. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The implementation depends on interfaces so policy remains testable and platform-specific details can be replaced.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Core;

namespace Haven.Application;

/// <summary>
/// Represents mode creation service and keeps its related state and behavior together.
/// </summary>
public sealed class ModeCreationService
{
    /// <summary>
    /// Stores registry locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IModeRegistry _registry;
    /// <summary>
    /// Stores usage locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IModeUsageRepository _usage;
    /// <summary>
    /// Stores ollama locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IOllamaClient _ollama;
    /// <summary>
    /// Stores validator locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly ModeManifestValidator _validator;

    public ModeCreationService(IModeRegistry registry, IModeUsageRepository usage, IOllamaClient ollama, ModeManifestValidator validator)
    {
        _registry = registry;
        _usage = usage;
        _ollama = ollama;
        _validator = validator;
    }

    /// <summary>
    /// Creates from description async with the invariants required by its callers.
    /// </summary>
    public async Task<ModeCreationResult> CreateFromDescriptionAsync(
        string name,
        string description,
        string purpose,
        HavenMode baseMode,
        string? author,
        CancellationToken cancellationToken)
    {
        var models = await _ollama.GetModelsAsync(cancellationToken).ConfigureAwait(false);
        var model = models.FirstOrDefault(m => m.Supports(ToolCapability.Text)) ?? models.FirstOrDefault();
        if (model is null)
            return new ModeCreationResult(false, "No local model available for AI-assisted creation.", null, [], []);

        var prompt = $"""
            Create a Haven mode configuration based on this description: {purpose}
            
            Return a JSON object with these fields:
            - "systemPromptSuffix": System instructions for this mode (string)
            - "surfaces": Array of surface names this mode can use (e.g., ["Do"], ["Studio", "Do"])
            - "plugins": Array of plugin names to activate (e.g., ["BrowserUse"], ["Automate"])
            - "toolAllowlist": Array of tool names to allow (e.g., ["write_file", "run_tests"])
            - "toolDenylist": Array of tool names to deny
            
            Return ONLY valid JSON. No markdown, no explanation.
            """;

        var result = await _ollama.CompleteAsync(new OllamaChatRequest(
            model.Name,
            [new OllamaMessage("user", prompt)],
            EffortLevel.Medium), cancellationToken).ConfigureAwait(false);

        try
        {
            var start = result.IndexOf('{');
            var end = result.LastIndexOf('}');
            if (start < 0 || end <= start)
                return new ModeCreationResult(false, "AI did not return valid JSON.", null, [], []);

            var json = result[start..(end + 1)];
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;

            var systemPromptSuffix = root.TryGetProperty("systemPromptSuffix", out var sp) ? sp.GetString() ?? "" : "";
            var surfaces = root.TryGetProperty("surfaces", out var s) ? s.EnumerateArray().Select(x => x.GetString() ?? "").ToList() : [];
            var plugins = root.TryGetProperty("plugins", out var p) ? p.EnumerateArray().Select(x => x.GetString() ?? "").ToList() : [];
            var toolAllowlist = root.TryGetProperty("toolAllowlist", out var ta) ? ta.EnumerateArray().Select(x => x.GetString() ?? "").ToList() : [];
            var toolDenylist = root.TryGetProperty("toolDenylist", out var td) ? td.EnumerateArray().Select(x => x.GetString() ?? "").ToList() : [];

            var now = DateTimeOffset.UtcNow;
            var key = name.Trim().ToLowerInvariant().Replace(" ", "-");
            var mode = new ModeDefinition(
                Guid.NewGuid(),
                key,
                name.Trim(),
                description.Trim(),
                "puzzle",
                baseMode,
                System.Text.Json.JsonSerializer.Serialize(surfaces),
                System.Text.Json.JsonSerializer.Serialize(toolAllowlist),
                System.Text.Json.JsonSerializer.Serialize(toolDenylist),
                System.Text.Json.JsonSerializer.Serialize(plugins),
                systemPromptSuffix,
                ModeSource.Created,
                ModeInstallState.InstalledByUser,
                author ?? "AI",
                "1.0.0",
                "[]",
                now, now);

            var validation = _validator.Validate(mode, "");
            if (!validation.IsValid)
                return new ModeCreationResult(false, $"Mode validation failed: {string.Join("; ", validation.Errors)}", null, validation.Errors, validation.Warnings);

            await _registry.UpsertModeAsync(mode, cancellationToken).ConfigureAwait(false);
            await _registry.AddVersionAsync(new ModeVersion(
                Guid.NewGuid(), mode.Id, 1, 0, 0, "{}", "AI-generated initial version", now), cancellationToken).ConfigureAwait(false);

            return new ModeCreationResult(true, $"Mode '{name}' created successfully.", mode, [], validation.Warnings);
        }
        catch (System.Text.Json.JsonException)
        {
            return new ModeCreationResult(false, "AI returned malformed JSON. Please try again.", null, [], []);
        }
    }

    /// <summary>
    /// Creates from manifest async with the invariants required by its callers.
    /// </summary>
    public async Task<ModeCreationResult> CreateFromManifestAsync(
        string name,
        string description,
        string manifestJson,
        HavenMode baseMode,
        string? author,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var key = name.Trim().ToLowerInvariant().Replace(" ", "-");
        var mode = new ModeDefinition(
            Guid.NewGuid(),
            key,
            name.Trim(),
            description.Trim(),
            "puzzle",
            baseMode,
            "[]", "[]", "[]", "[]",
            "",
            ModeSource.Created,
            ModeInstallState.InstalledByUser,
            author ?? "User",
            "1.0.0",
            "[]",
            now, now);

        var validation = _validator.Validate(mode, manifestJson);
        if (!validation.IsValid)
            return new ModeCreationResult(false, $"Manifest validation failed: {string.Join("; ", validation.Errors)}", null, validation.Errors, validation.Warnings);

        await _registry.UpsertModeAsync(mode, cancellationToken).ConfigureAwait(false);
        await _registry.AddVersionAsync(new ModeVersion(
            Guid.NewGuid(), mode.Id, 1, 0, 0, manifestJson, "Initial version from manifest", now), cancellationToken).ConfigureAwait(false);

        return new ModeCreationResult(true, $"Mode '{name}' created from manifest.", mode, [], validation.Warnings);
    }
}

/// <summary>
/// Represents mode creation result and keeps its related state and behavior together.
/// </summary>
public sealed record ModeCreationResult(
    bool Succeeded,
    string Message,
    ModeDefinition? Mode,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);
