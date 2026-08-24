using Haven.Core;

namespace Haven.Application;

public interface IProjectorExperienceCatalog
{
    ValueTask<IReadOnlyList<ProjectorExperience>> GetExperiencesAsync(
        ProjectorSessionSnapshot? session,
        CancellationToken cancellationToken);
}

public sealed class ProjectorExperienceCatalog : IProjectorExperienceCatalog
{
    private readonly IReadOnlyList<IProjectorExperienceProvider> _providers;

    public ProjectorExperienceCatalog(IEnumerable<IProjectorExperienceProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        _providers = providers.ToArray();
    }

    public async ValueTask<IReadOnlyList<ProjectorExperience>> GetExperiencesAsync(
        ProjectorSessionSnapshot? session,
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
                if (string.IsNullOrWhiteSpace(experience.Id) || !seen.Add(experience.Id))
                    continue;
                if (session is not null && !experience.IsAvailable(session.TargetDisplay))
                    continue;
                results.Add(experience);
            }
        }

        return results
            .OrderBy(experience => experience.Source)
            .ThenBy(experience => experience.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(experience => experience.Id, StringComparer.Ordinal)
            .ToArray();
    }
}

public sealed class BuiltInProjectorExperienceProvider : IProjectorExperienceProvider
{
    // Built-ins are advertised only when a production Projector host exists.
    // Desktop is currently the only shared HavenSurface with a real Android host.
    private static readonly IReadOnlyList<ProjectorExperience> Experiences =
    [
        new ProjectorExperience(
            "desktop",
            "Desktop",
            "A focused workspace with apps, Haven, status and task switching.",
            "studio",
            ArtworkKey: null,
            ProjectorExperienceSource.BuiltIn,
            ProjectorLaunchStrategy.HavenSurface,
            ProjectorInteractionProfile.Desktop,
            ProjectorExperiencePersistence.Persistent,
            [ProjectorCapability.RenderHavenSurface])
    ];

    public ValueTask<IReadOnlyList<ProjectorExperience>> GetExperiencesAsync(
        ProjectorSessionSnapshot? session,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new ValueTask<IReadOnlyList<ProjectorExperience>>(Experiences);
    }
}

public sealed class GeneratedProjectorExperienceProvider(IGenUiAppRepository repository) : IProjectorExperienceProvider
{
    public const string ExperiencePrefix = "genui:";
    private const int MaximumAdvertised = 24;

    public async ValueTask<IReadOnlyList<ProjectorExperience>> GetExperiencesAsync(
        ProjectorSessionSnapshot? session,
        CancellationToken cancellationToken)
    {
        var pinnedTask = repository.GetPinnedAsync(MaximumAdvertised, cancellationToken);
        var recentTask = repository.GetRecentAsync(MaximumAdvertised, cancellationToken);
        await Task.WhenAll(pinnedTask, recentTask).ConfigureAwait(false);

        var seen = new HashSet<Guid>();
        var experiences = new List<ProjectorExperience>();
        foreach (var definition in pinnedTask.Result.Concat(recentTask.Result))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var instanceId = definition.Document.Origin.InstanceId;
            if (instanceId == Guid.Empty || !seen.Add(instanceId))
                continue;

            var semantic = GenUiSemanticValidator.ValidateAndRepair(definition);
            if (!semantic.IsValid || semantic.Definition.Rendering.AllowsExecutableCode)
                continue;
            if (GenerativeUiContractValidator.Validate(semantic.Definition.Document).Count != 0)
                continue;

            var title = string.IsNullOrWhiteSpace(semantic.Definition.Document.Title)
                ? semantic.Definition.AppId
                : semantic.Definition.Document.Title.Trim();
            if (string.IsNullOrWhiteSpace(title))
                title = "Generated experience";

            experiences.Add(new ProjectorExperience(
                ExperienceId(instanceId),
                title,
                "A saved Haven generated experience, rendered through the current GenUI runtime.",
                "apps",
                ArtworkKey: null,
                ProjectorExperienceSource.GeneratedUi,
                ProjectorLaunchStrategy.GeneratedUi,
                ProjectorInteractionProfile.Mixed,
                ProjectorExperiencePersistence.Persistent,
                [ProjectorCapability.RenderHavenSurface]));

            if (experiences.Count >= MaximumAdvertised)
                break;
        }

        return experiences;
    }

    public static string ExperienceId(Guid instanceId) => ExperiencePrefix + instanceId.ToString("N");

    public static bool TryGetInstanceId(string experienceId, out Guid instanceId)
    {
        instanceId = Guid.Empty;
        return !string.IsNullOrWhiteSpace(experienceId)
            && experienceId.StartsWith(ExperiencePrefix, StringComparison.OrdinalIgnoreCase)
            && Guid.TryParseExact(experienceId.AsSpan(ExperiencePrefix.Length), "N", out instanceId);
    }
}
