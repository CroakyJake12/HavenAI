/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Application/Actions/ProviderResolution.cs, in the Application layer.
 * What: Owns IDefaultProviderStore, DefaultCategoryCatalog, ProviderResolutionInput/Outcome and
 *       DefaultProviderResolver — the shared "which App should perform this action" cascade — plus
 *       DefaultProviderDirectives for prompt guidance.
 * How: Resolution follows the documented order: explicit request → single unambiguous attached App →
 *      approved plan provider → project/space preference → user category default → sole compatible
 *      available provider → ask. Ambiguity never guesses; it returns RequiresUserChoice with options.
 * Why: Actions describe intent; providers implement capability. One shared resolver prevents each
 *      surface inventing its own provider selection rules.
 * Maintenance: Keep the catalog honest — only list apps that actually provide the category today.
 */

using Haven.Core;

namespace Haven.Application;

/// <summary>Persisted per-category default provider assignment (app key or "ask").</summary>
public interface IDefaultProviderStore
{
    Task<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken cancellationToken);
    Task SetAsync(string categoryKey, string appKeyOrAsk, CancellationToken cancellationToken);
}

/// <summary>Apps that genuinely provide a given category today. Honest, not aspirational.</summary>
public static class DefaultCategoryCatalog
{
    /// <summary>App keys known to provide each category in the current product.</summary>
    public static IReadOnlyList<string> ProvidersFor(ProviderCategory category) => category switch
    {
        ProviderCategory.Calendar => ["google-calendar", "microsoft-calendar"],
        ProviderCategory.Automation => ["automations"],
        ProviderCategory.ImageGeneration => ["imagine"],
        ProviderCategory.Browser => ["browse"],
        ProviderCategory.Maps => ["maps"],
        ProviderCategory.Documents or ProviderCategory.Notes => ["write"],
        ProviderCategory.CodingWorkspace => ["studio"],
        ProviderCategory.AudioSpeech => ["haven-voice"],
        _ => []
    };

    public static string Key(ProviderCategory category) => category.ToString().ToLowerInvariant();
}

/// <summary>Inputs to the resolution cascade.</summary>
public sealed record ProviderResolutionInput(
    ProviderCategory Category,
    string? ExplicitProviderAppKey,
    IReadOnlyList<string> AttachedAppKeys,
    string? ApprovedPlanProvider,
    string? ProjectOrSpacePreference,
    string? UserDefaultAssignment,
    IReadOnlyList<string> AvailableProviderKeys)
{
    public static ProviderResolutionInput For(
        ProviderCategory category,
        string? explicitProvider = null,
        IReadOnlyList<string>? attached = null,
        IReadOnlyList<string>? available = null,
        string? approvedPlanProvider = null,
        string? projectOrSpacePreference = null,
        string? userDefaultAssignment = null)
        => new(category, explicitProvider, attached ?? [], approvedPlanProvider, projectOrSpacePreference,
            userDefaultAssignment, available ?? []);
}

/// <summary>Result: a resolved provider, or an explicit requirement to ask the user (with options).</summary>
public sealed record ProviderResolutionOutcome(string? ResolvedAppKey, bool RequiresUserChoice, IReadOnlyList<string> Options)
{
    public static ProviderResolutionOutcome Resolved(string appKey) => new(appKey, false, []);
    public static ProviderResolutionOutcome Ask(IReadOnlyList<string> options) => new(null, true, options);
}

public static class DefaultProviderResolver
{
    public static ProviderResolutionOutcome Resolve(ProviderResolutionInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var candidates = DefaultCategoryCatalog.ProvidersFor(input.Category);

        // 1. Explicit provider named in the current request always wins.
        if (!string.IsNullOrWhiteSpace(input.ExplicitProviderAppKey))
            return ProviderResolutionOutcome.Resolved(input.ExplicitProviderAppKey);

        // 2. A single unambiguous attached App providing this category wins; several require a choice.
        var attachedProviders = input.AttachedAppKeys
            .Where(key => candidates.Contains(key, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (attachedProviders.Length == 1) return ProviderResolutionOutcome.Resolved(attachedProviders[0]);
        if (attachedProviders.Length > 1) return ProviderResolutionOutcome.Ask(attachedProviders);

        // 3. A plan/task-approved provider.
        if (!string.IsNullOrWhiteSpace(input.ApprovedPlanProvider) &&
            candidates.Contains(input.ApprovedPlanProvider, StringComparer.OrdinalIgnoreCase))
            return ProviderResolutionOutcome.Resolved(input.ApprovedPlanProvider!);

        // 4. Project / Space preference.
        if (!string.IsNullOrWhiteSpace(input.ProjectOrSpacePreference) &&
            candidates.Contains(input.ProjectOrSpacePreference, StringComparer.OrdinalIgnoreCase))
            return ProviderResolutionOutcome.Resolved(input.ProjectOrSpacePreference!);

        // 5. The user's category default — including the explicit Always Ask assignment.
        if (!string.IsNullOrWhiteSpace(input.UserDefaultAssignment))
        {
            if (input.UserDefaultAssignment.Equals(DefaultProviderAssignments.AlwaysAsk, StringComparison.OrdinalIgnoreCase))
                return ProviderResolutionOutcome.Ask(candidates.Count > 0 ? candidates : OptionsFromAvailable(input));
            if (candidates.Contains(input.UserDefaultAssignment, StringComparer.OrdinalIgnoreCase))
                return ProviderResolutionOutcome.Resolved(input.UserDefaultAssignment);
        }

        // 6. Sole compatible available provider.
        var available = input.AvailableProviderKeys
            .Where(key => candidates.Contains(key, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (available.Length == 1) return ProviderResolutionOutcome.Resolved(available[0]);

        // 7/8. No safe inference remains — ask with consequence-specific options.
        return ProviderResolutionOutcome.Ask(candidates.Count > 0 ? candidates : available);
    }

    private static IReadOnlyList<string> OptionsFromAvailable(ProviderResolutionInput input)
        => input.AvailableProviderKeys.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
}

/// <summary>Renders persisted default assignments as compact system-prompt guidance so the model asks
/// consequence-specific questions instead of guessing when a default is "Always Ask".</summary>
public static class DefaultProviderDirectives
{
    public static string Describe(IReadOnlyDictionary<string, string> assignments)
    {
        if (assignments.Count == 0) return string.Empty;
        var lines = assignments
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair =>
            {
                var category = Enum.TryParse<ProviderCategory>(pair.Key, true, out var parsed)
                    ? ProviderCategoryNames.For(parsed)
                    : pair.Key;
                var target = pair.Value.Equals(DefaultProviderAssignments.AlwaysAsk, StringComparison.OrdinalIgnoreCase)
                    ? "Always Ask"
                    : pair.Value;
                return $"{category} → {target}";
            });
        return "Default provider assignments for actions:\n" + string.Join("\n", lines) +
               "\nWhen performing one of these actions and more than one reasonable provider remains after these defaults, ask once using the clarification tag with consequence-specific option labels (for example 'Send with Gmail', 'Send with Mail'). Do not guess between providers.";
    }
}
