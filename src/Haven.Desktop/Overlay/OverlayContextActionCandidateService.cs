#if !ANDROID
using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace Haven.Desktop.Overlay;

/// <summary>
/// Projects the existing Capability Registry into contextual Overlay action candidates.
/// It never executes capabilities or changes permission state; risk, availability and
/// provider metadata remain attached so the eventual Overlay host can use the normal
/// Haven permission/tool routing path.
/// </summary>
internal sealed class OverlayContextActionCandidateService
{
    private static readonly HashSet<string> TextSemanticActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "search",
        "read-source",
        "create",
        "schedule"
    };

    private static readonly HashSet<string> VisualSemanticActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "inspect",
        "search"
    };

    private static readonly HashSet<string> InteractiveSemanticActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "inspect",
        "interact",
        "verify",
        "create"
    };

    private static readonly HashSet<string> MediaSemanticActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "inspect",
        "search"
    };

    private readonly CapabilityRegistryService _capabilities;
    private readonly Func<DateTimeOffset> _clock;

    public OverlayContextActionCandidateService(CapabilityRegistryService capabilities)
        : this(capabilities, () => DateTimeOffset.UtcNow)
    {
    }

    internal OverlayContextActionCandidateService(
        CapabilityRegistryService capabilities,
        Func<DateTimeOffset> clock)
    {
        _capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public Task<IReadOnlyList<OverlayContextActionDescriptor>> DiscoverAsync(
        OverlayContextEnvelope? context,
        CancellationToken cancellationToken) =>
        DiscoverAsync(context, CapabilityPlatform.Windows, cancellationToken);

    internal async Task<IReadOnlyList<OverlayContextActionDescriptor>> DiscoverAsync(
        OverlayContextEnvelope? context,
        CapabilityPlatform platform,
        CancellationToken cancellationToken)
    {
        if (context is null || !context.HasPayload || context.IsExpired(_clock()))
            return [];

        var capabilities = await _capabilities.DiscoverAsync(platform, cancellationToken).ConfigureAwait(false);
        return capabilities
            .Where(CanSuggest)
            .SelectMany(capability => SemanticActions(capability)
                .Where(action => IsRelevant(action, context))
                .Select(action => Candidate(capability, action)))
            .GroupBy(candidate => candidate.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(candidate => candidate.RiskClass)
            .ThenBy(candidate => candidate.Label, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool CanSuggest(CapabilityDefinition capability) =>
        capability.IsEnabled
        && capability.RiskClass != CapabilityRiskClass.Restricted
        && capability.Availability is not (CapabilityAvailability.Restricted or CapabilityAvailability.Unsupported);

    private static bool IsRelevant(string semanticAction, OverlayContextEnvelope context) =>
        context.HasTextualSelection && TextSemanticActions.Contains(semanticAction)
        || context.HasVisualSelection && VisualSemanticActions.Contains(semanticAction)
        || context.HasInteractiveSelection && InteractiveSemanticActions.Contains(semanticAction)
        || context.HasMediaSelection && MediaSemanticActions.Contains(semanticAction);

    private static IReadOnlyList<string> SemanticActions(CapabilityDefinition capability)
    {
        try
        {
            using var document = JsonDocument.Parse(capability.SemanticActionsJson);
            if (document.RootElement.ValueKind != JsonValueKind.Array) return [];
            return document.RootElement
                .EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString())
                .OfType<string>()
                .Where(action => !string.IsNullOrWhiteSpace(action))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static OverlayContextActionDescriptor Candidate(CapabilityDefinition capability, string semanticAction) =>
        new(
            $"capability:{capability.Key}:{semanticAction}",
            $"{capability.Name}: {Humanize(semanticAction)}",
            capability.IconKey,
            RequiresContext: true,
            IsGenerated: true,
            ToolName: capability.ImplementationKey,
            RiskClass: capability.RiskClass,
            Availability: capability.Availability,
            ProviderId: capability.ProviderId,
            ImplementationKey: capability.ImplementationKey);

    private static string Humanize(string action) =>
        string.Join(' ', action.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
}
#endif
