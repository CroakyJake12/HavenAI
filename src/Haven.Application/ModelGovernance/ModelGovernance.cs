/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Application/ModelGovernance/ModelGovernanceStores.cs, in the Application layer.
 * What: Owns IModelFallbackOrderStore, IModelPersonalisationStore, IModelPermissionStore,
 *       ModelPersonalityService and ModelPermissionEvaluator — the shared model governance contracts
 *       used by routing, chat orchestration and Settings.
 * How: Public members form the callable contract; stores persist through IVersionedSettingsStore keys.
 * Why: Routing, permissions and personality must be one shared subsystem consumed by every surface.
 * Maintenance: Preserve layer boundaries; implementations live in Infrastructure.
 */

using Haven.Core;

namespace Haven.Application;

/// <summary>Contract for the user's ordered model fallback preference (most preferred first).</summary>
public interface IModelFallbackOrderStore
{
    /// <summary>Returns ordered fallback model keys, most preferred first. Empty when unset.</summary>
    Task<IReadOnlyList<string>> GetOrderAsync(CancellationToken cancellationToken);
    /// <summary>Persists the ordered fallback model keys.</summary>
    Task SetOrderAsync(IReadOnlyList<string> orderedModelKeys, CancellationToken cancellationToken);
}

/// <summary>Contract for per-model personalisation (nicknames + personality overrides) and shared defaults.</summary>
public interface IModelPersonalisationStore
{
    Task<ModelPersonality> GetSharedDefaultsAsync(CancellationToken cancellationToken);
    Task SetSharedDefaultsAsync(ModelPersonality personality, CancellationToken cancellationToken);
    Task<IReadOnlyList<ModelPersonalisationEntry>> GetEntriesAsync(CancellationToken cancellationToken);
    Task SaveEntryAsync(ModelPersonalisationEntry entry, CancellationToken cancellationToken);
    Task RemoveEntryAsync(string modelKey, CancellationToken cancellationToken);
}

/// <summary>Contract for the persisted model permission policy.</summary>
public interface IModelPermissionStore
{
    Task<ModelPermissionPolicy> GetPolicyAsync(CancellationToken cancellationToken);
    Task SavePolicyAsync(ModelPermissionPolicy policy, CancellationToken cancellationToken);
}

/// <summary>
/// Resolves effective personality and nickname for a model: shared defaults with per-model overrides.
/// Changing one override never requires copying all global defaults into a per-model configuration.
/// </summary>
public sealed class ModelPersonalityService(IModelPersonalisationStore store)
{
    public async Task<ModelPersonality> ResolveEffectiveAsync(string? modelName, CancellationToken cancellationToken)
    {
        var shared = await store.GetSharedDefaultsAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(modelName)) return shared;
        var entry = await FindAsync(modelName, cancellationToken).ConfigureAwait(false);
        return entry?.Personality is null ? shared : Merge(shared, entry.Personality);
    }

    public async Task<string?> ResolveNicknameAsync(string? modelName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(modelName)) return null;
        var entry = await FindAsync(modelName, cancellationToken).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(entry?.Nickname) ? null : entry.Nickname;
    }

    private async Task<ModelPersonalisationEntry?> FindAsync(string modelName, CancellationToken cancellationToken)
    {
        var entries = await store.GetEntriesAsync(cancellationToken).ConfigureAwait(false);
        return entries.FirstOrDefault(item => MatchesKey(item.ModelKey, modelName));
    }

    public static bool MatchesKey(string entryKey, string modelName)
    {
        if (string.IsNullOrWhiteSpace(entryKey)) return false;
        if (entryKey.Equals(modelName, StringComparison.OrdinalIgnoreCase)) return true;
        // Provider-qualified keys ("openai:gpt-4o") match bare names ("gpt-4o").
        var separator = entryKey.IndexOf(':');
        return separator > 0 && entryKey[(separator + 1)..].Equals(modelName, StringComparison.OrdinalIgnoreCase);
    }

    private static ModelPersonality Merge(ModelPersonality shared, ModelPersonality overridden) => new(
        overridden.Friendliness,
        overridden.MemoryReferences,
        overridden.Seriousness,
        overridden.Verbosity,
        overridden.Initiative,
        overridden.ExplanationDepth);
}

/// <summary>
/// Evaluates a concrete model against restricted capabilities using the persisted deny-rule policy.
/// More specific targets win on conflict; an explicit deny always wins at equal specificity.
/// Parameter-size rules never apply when parameter size is unknown.
/// </summary>
public sealed class ModelPermissionEvaluator(IModelPermissionStore store)
{
    public async Task<ModelPermissionDecision> EvaluateAsync(
        ProviderModelDescriptor model,
        RestrictedModelCapability capability,
        bool acrossMesh = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        var policy = await store.GetPolicyAsync(cancellationToken).ConfigureAwait(false);
        return Evaluate(policy, model, capability, acrossMesh);
    }

    public static ModelPermissionDecision Evaluate(
        ModelPermissionPolicy policy,
        ProviderModelDescriptor model,
        RestrictedModelCapability capability,
        bool acrossMesh = false)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(model);
        ModelPermissionRule? denying = null;
        foreach (var rule in policy.Rules)
        {
            if (!rule.Denied.Contains(capability)) continue;
            if (!MatchesScope(rule.Scope, acrossMesh)) continue;
            if (!MatchesTarget(rule, model)) continue;
            if (denying is null || Specificity(rule.Target) >= Specificity(denying.Target)) denying = rule;
        }
        return denying is null
            ? ModelPermissionDecision.Allow
            : ModelPermissionDecision.Denied(denying, $"Denied by rule targeting {DescribeTarget(denying)}.");
    }

    /// <summary>Evaluates whether ANY denied capability in the set blocks this model (used by candidate filtering).</summary>
    public static bool IsBlockedForAny(
        ModelPermissionPolicy policy,
        ProviderModelDescriptor model,
        IEnumerable<RestrictedModelCapability> capabilities,
        bool acrossMesh = false)
        => capabilities.Any(capability => !Evaluate(policy, model, capability, acrossMesh).Allowed);

    private static bool MatchesScope(ModelPermissionScope scope, bool acrossMesh) => scope switch
    {
        ModelPermissionScope.AcrossMesh => true,
        _ => !acrossMesh
    };

    private static bool MatchesTarget(ModelPermissionRule rule, ProviderModelDescriptor model)
    {
        switch (rule.Target)
        {
            case ModelPermissionTargetKind.LocalModels:
                return model.IsLocal;
            case ModelPermissionTargetKind.CloudModels:
                return !model.IsLocal;
            case ModelPermissionTargetKind.ParameterSizeBelow:
                var billions = ParseParameterBillions(model.Model.ParameterSize);
                return billions.HasValue && rule.MaxParameterBillion.HasValue && billions.Value < rule.MaxParameterBillion.Value;
        }
        if (string.IsNullOrWhiteSpace(rule.Match)) return false;
        return rule.Target switch
        {
            ModelPermissionTargetKind.ExactModel => model.Name.Equals(rule.Match, StringComparison.OrdinalIgnoreCase)
                                                     || model.Key.Equals(rule.Match, StringComparison.OrdinalIgnoreCase),
            ModelPermissionTargetKind.ModelFamily => model.Model.Family.Equals(rule.Match, StringComparison.OrdinalIgnoreCase),
            ModelPermissionTargetKind.Provider => model.ProviderId.Equals(rule.Match, StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private static int Specificity(ModelPermissionTargetKind target) => target switch
    {
        ModelPermissionTargetKind.ExactModel => 5,
        ModelPermissionTargetKind.ModelFamily => 4,
        ModelPermissionTargetKind.Provider => 3,
        ModelPermissionTargetKind.ParameterSizeBelow => 2,
        ModelPermissionTargetKind.CloudModels => 1,
        ModelPermissionTargetKind.LocalModels => 1,
        _ => 0
    };

    private static double? ParseParameterBillions(string parameterSize)
    {
        if (string.IsNullOrWhiteSpace(parameterSize)) return null;
        var digits = new string(parameterSize.TakeWhile(value => char.IsAsciiDigit(value) || value is '.').ToArray());
        return double.TryParse(digits, out var value) && value > 0 ? value : null;
    }

    private static string DescribeTarget(ModelPermissionRule rule) => rule.Target switch
    {
        ModelPermissionTargetKind.ExactModel => $"model '{rule.Match}'",
        ModelPermissionTargetKind.ModelFamily => $"family '{rule.Match}'",
        ModelPermissionTargetKind.Provider => $"provider '{rule.Match}'",
        ModelPermissionTargetKind.ParameterSizeBelow => $"models below {rule.MaxParameterBillion}B parameters",
        ModelPermissionTargetKind.LocalModels => "local models",
        ModelPermissionTargetKind.CloudModels => "cloud models",
        _ => "matched models"
    };
}

/// <summary>
/// Renders effective model personality as compact human-readable prompt guidance.
/// Labels are words, never numbers — users must not be shown fake numerical precision.
/// </summary>
public static class ModelPersonalityPrompt
{
    public static string Describe(ModelPersonality personality)
    {
        var parts = new List<string>(6)
        {
            $"Friendliness {Label(personality.Friendliness, "Reserved and professional", "Polite but formal", "Balanced", "Warm", "Very warm and friendly")}",
            $"Memory references {Label(personality.MemoryReferences, "Rarely mention remembered information", "Mention memories only when essential", "Reference relevant memories when useful", "Use relevant memories proactively", "Frequently surface relevant memories")}",
            $"Seriousness {Label(personality.Seriousness, "Playful and casual", "Light-hearted", "Adaptive", "Serious", "Formal and serious")}",
            $"Verbosity {Label(personality.Verbosity, "Extremely concise", "Concise", "Balanced length", "Detailed", "Highly detailed")}",
            $"Initiative {Label(personality.Initiative, "Strictly reactive; act only when asked", "Mostly reactive", "Balanced initiative", "Proactive; suggest next steps", "Highly proactive; anticipate needs")}",
            $"Explanation depth {Label(personality.ExplanationDepth, "Minimal explanation", "Brief explanations", "Moderate explanations", "Thorough explanations", "Educational; teach underlying ideas")}"
        };
        return "Response style: " + string.Join("; ", parts) + ".";
    }

    public static string Label(PersonalityLevel level, string veryLow, string low, string moderate, string high, string veryHigh)
        => level switch
        {
            PersonalityLevel.VeryLow => veryLow,
            PersonalityLevel.Low => low,
            PersonalityLevel.Moderate => moderate,
            PersonalityLevel.High => high,
            _ => veryHigh
        };
}

/// <summary>Maps concrete tool names onto model-restricted capabilities for permission enforcement.</summary>
public static class ModelToolPermissionMap
{
    public static RestrictedModelCapability? Map(string? toolName) => toolName?.Trim() switch
    {
        "write_file" or "replace_in_file" or "apply_change_set" => RestrictedModelCapability.EditFiles,
        "run_command" or "run_tests" => RestrictedModelCapability.RunCommands,
        null or "" => null,
        var name when name.StartsWith("computer_", StringComparison.Ordinal) => RestrictedModelCapability.ComputerUse,
        var name when name.StartsWith("browser_", StringComparison.Ordinal) => RestrictedModelCapability.BrowserAutomation,
        _ => null
    };
}
