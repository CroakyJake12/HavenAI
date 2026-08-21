namespace Haven.Application;

public enum ProjectorActionPlanStatus
{
    Ready,
    EmptyRequest,
    Unsupported,
    Ambiguous,
    BlockedByCapability,
    BlockedByTrust
}

public enum ProjectorActionKind
{
    OpenExperience,
    LaunchApplication,
    RestoreWorkspace,
    OpenGeneratedExperience,
    RouteRemoteExperience,
    RouteContent
}

public sealed record ProjectorActionPlan(
    ProjectorActionPlanStatus Status,
    ProjectorActionKind? Action,
    string Request,
    string? TargetExperienceId,
    string? FallbackExperienceId,
    IReadOnlyList<string> AlternativeExperienceIds,
    string Summary)
{
    public bool CanExecute => Status == ProjectorActionPlanStatus.Ready
        && Action is not null
        && !string.IsNullOrWhiteSpace(TargetExperienceId);
}

public interface IProjectorActionPlanner
{
    ValueTask<ProjectorActionPlan> PlanAsync(
        string request,
        ProjectorSessionSnapshot session,
        CancellationToken cancellationToken);
}

/// <summary>
/// Converts user intent into a bounded Projector plan. This class never executes Android,
/// browser, workspace, model, or device actions; it can only name experiences already
/// published by registered Projector providers.
/// </summary>
public sealed class ProjectorActionPlanner : IProjectorActionPlanner
{
    private static readonly IReadOnlyDictionary<string, string[]> Aliases =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["desktop"] = ["desktop", "computer", "pc"],
            ["videos"] = ["video", "videos", "movie", "movies"],
            ["games"] = ["game", "games", "gaming"],
            ["music"] = ["music", "songs", "audio"],
            ["tv"] = ["tv", "television", "live tv"],
            ["haven"] = ["haven", "chat", "assistant"],
            ["presentation"] = ["presentation", "present", "slides", "slideshow", "slide show"],
            ["photos"] = ["photos", "photo", "pictures", "picture", "images"],
            ["browser"] = ["browser", "web", "internet"],
            ["study"] = ["study", "revision", "revise", "learning", "learn"],
            ["development"] = ["development", "developer", "coding", "code", "programming"]
        };

    private static readonly HashSet<string> IntentWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "the", "please", "open", "launch", "start", "show", "use",
        "make", "turn", "be", "become", "this", "that", "screen", "display", "projector",
        "on", "into", "for", "me", "my"
    };

    private readonly IProjectorExperienceCatalog _catalog;
    private readonly IReadOnlyList<IProjectorExperienceProvider> _providers;

    public ProjectorActionPlanner(
        IProjectorExperienceCatalog catalog,
        IEnumerable<IProjectorExperienceProvider> providers)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        ArgumentNullException.ThrowIfNull(providers);
        _providers = providers.ToArray();
    }

    public async ValueTask<ProjectorActionPlan> PlanAsync(
        string request,
        ProjectorSessionSnapshot session,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        var trimmed = request?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
            return Plan(ProjectorActionPlanStatus.EmptyRequest, trimmed, summary: "Describe what you want this screen to become.");

        cancellationToken.ThrowIfCancellationRequested();
        var all = await GetAllExperiencesAsync(session, cancellationToken).ConfigureAwait(false);
        var available = await _catalog.GetExperiencesAsync(session, cancellationToken).ConfigureAwait(false);
        var normalized = Normalize(trimmed);
        var intent = RemoveIntentWords(normalized);
        if (intent.Length == 0)
            return Plan(ProjectorActionPlanStatus.Unsupported, trimmed, alternatives: TopAlternatives(available), summary: "Name an app or Projector experience to open.");

        var scored = all
            .Select(experience => (Experience: experience, Score: Score(experience, normalized, intent)))
            .Where(candidate => candidate.Score > 0)
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Experience.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(candidate => candidate.Experience.Id, StringComparer.Ordinal)
            .ToArray();

        if (scored.Length == 0)
            return Plan(ProjectorActionPlanStatus.Unsupported, trimmed, alternatives: TopAlternatives(available), summary: "No registered Projector experience matches that request.");

        var bestScore = scored[0].Score;
        var best = scored.Where(candidate => candidate.Score == bestScore).Select(candidate => candidate.Experience).ToArray();
        if (best.Length > 1)
        {
            return Plan(
                ProjectorActionPlanStatus.Ambiguous,
                trimmed,
                alternatives: best.Take(4).Select(experience => experience.Id).ToArray(),
                summary: "More than one registered Projector experience matches that request.");
        }

        var target = best[0];
        var fallback = FindFallback(target, available);
        if (!target.IsAllowedOn(session.TargetDisplay.Trust))
        {
            var summary = $"{target.Name} is private for a {session.TargetDisplay.Trust.ToString().ToLowerInvariant()} display. Change display trust on the phone"
                + (fallback is null ? "." : $" or use {fallback.Name}.");
            return Plan(ProjectorActionPlanStatus.BlockedByTrust, trimmed, target.Id, fallback?.Id, summary: summary);
        }

        if (!target.HasRequiredCapabilities(session.TargetDisplay.Capabilities))
        {
            var summary = $"{target.Name} needs capabilities this display has not proven"
                + (fallback is null ? "." : $". {fallback.Name} is the closest supported alternative.");
            return Plan(ProjectorActionPlanStatus.BlockedByCapability, trimmed, target.Id, fallback?.Id, summary: summary);
        }

        return Plan(
            ProjectorActionPlanStatus.Ready,
            trimmed,
            target.Id,
            action: ActionFor(target),
            summary: $"Open {target.Name} on {session.TargetDisplay.Name}.");
    }

    private async ValueTask<IReadOnlyList<ProjectorExperience>> GetAllExperiencesAsync(
        ProjectorSessionSnapshot session,
        CancellationToken cancellationToken)
    {
        var results = new List<ProjectorExperience>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var provider in _providers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var provided = await provider.GetExperiencesAsync(session, cancellationToken).ConfigureAwait(false);
            foreach (var experience in provided)
            {
                if (!string.IsNullOrWhiteSpace(experience.Id) && seen.Add(experience.Id))
                    results.Add(experience);
            }
        }
        return results;
    }

    private static int Score(ProjectorExperience experience, string normalizedRequest, string normalizedIntent)
    {
        var id = Normalize(experience.Id);
        var name = Normalize(experience.Name);
        if (normalizedIntent == id || normalizedIntent == name) return 140;

        if (Aliases.TryGetValue(experience.Id, out var aliases))
        {
            if (aliases.Any(alias => normalizedIntent == Normalize(alias))) return 135;
            if (aliases.Any(alias => ContainsPhrase(normalizedRequest, Normalize(alias)))) return 120;
        }

        if (ContainsPhrase(normalizedRequest, name)) return 110;
        if (ContainsPhrase(normalizedRequest, id)) return 105;

        var requestedTokens = normalizedIntent.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        var targetTokens = name.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        var overlap = targetTokens.Count(token => requestedTokens.Contains(token));
        return overlap == 0 ? 0 : 50 + (overlap * 30 / Math.Max(1, targetTokens.Count));
    }

    private static ProjectorExperience? FindFallback(ProjectorExperience target, IReadOnlyList<ProjectorExperience> available) =>
        available
            .Where(candidate => !string.Equals(candidate.Id, target.Id, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(candidate => candidate.InteractionProfile == target.InteractionProfile)
            .ThenByDescending(candidate => candidate.Source == target.Source)
            .ThenByDescending(candidate => string.Equals(candidate.Id, "presentation", StringComparison.OrdinalIgnoreCase))
            .ThenBy(candidate => candidate.Name, StringComparer.CurrentCultureIgnoreCase)
            .FirstOrDefault();

    private static IReadOnlyList<string> TopAlternatives(IReadOnlyList<ProjectorExperience> available) =>
        available.Take(4).Select(experience => experience.Id).ToArray();

    private static ProjectorActionKind ActionFor(ProjectorExperience experience) => experience.LaunchStrategy switch
    {
        ProjectorLaunchStrategy.AndroidApplication => ProjectorActionKind.LaunchApplication,
        ProjectorLaunchStrategy.Workspace => ProjectorActionKind.RestoreWorkspace,
        ProjectorLaunchStrategy.GeneratedUi => ProjectorActionKind.OpenGeneratedExperience,
        ProjectorLaunchStrategy.RemoteDevice => ProjectorActionKind.RouteRemoteExperience,
        ProjectorLaunchStrategy.RoutedContent => ProjectorActionKind.RouteContent,
        _ => ProjectorActionKind.OpenExperience
    };

    private static ProjectorActionPlan Plan(
        ProjectorActionPlanStatus status,
        string request,
        string? target = null,
        string? fallback = null,
        ProjectorActionKind? action = null,
        IReadOnlyList<string>? alternatives = null,
        string? summary = null) => new(
            status, action, request, target, fallback, alternatives ?? [], summary ?? string.Empty);

    private static string RemoveIntentWords(string normalized) => string.Join(' ', normalized
        .Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .Where(token => !IntentWords.Contains(token)));

    private static bool ContainsPhrase(string value, string phrase)
    {
        if (phrase.Length == 0) return false;
        return string.Equals(value, phrase, StringComparison.Ordinal)
            || value.StartsWith(phrase + " ", StringComparison.Ordinal)
            || value.EndsWith(" " + phrase, StringComparison.Ordinal)
            || value.Contains(" " + phrase + " ", StringComparison.Ordinal);
    }

    private static string Normalize(string value)
    {
        var normalized = new string(value
            .Trim()
            .ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : ' ')
            .ToArray());
        return string.Join(' ', normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
