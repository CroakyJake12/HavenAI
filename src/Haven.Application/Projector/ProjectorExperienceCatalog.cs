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
                if (session is not null && !experience.IsAvailable(session.TargetDisplay.Capabilities))
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
    private static readonly IReadOnlyList<ProjectorExperience> Experiences =
    [
        BuiltIn("desktop", "Desktop", "A focused workspace with apps, Haven, status and task switching.", "studio", ProjectorInteractionProfile.Desktop),
        BuiltIn("videos", "Videos", "A lean-back video experience using Haven-supported media and provider integrations.", "browse", ProjectorInteractionProfile.LeanBack),
        BuiltIn("games", "Games", "A controller-friendly launch surface for supported games and interactive experiences.", "rocket", ProjectorInteractionProfile.Controller),
        BuiltIn("music", "Music", "A focused playback surface for supported music providers and media sessions.", "bolt", ProjectorInteractionProfile.LeanBack),
        BuiltIn("tv", "TV", "A television-style surface for supported live and on-demand provider experiences.", "browse", ProjectorInteractionProfile.LeanBack),
        BuiltIn("haven", "Haven", "A large-screen Haven experience for conversations, tasks and contextual work.", "chat", ProjectorInteractionProfile.Mixed),
        BuiltIn("presentation", "Presentation", "A privacy-aware presentation surface that keeps private controls on the phone.", "file", ProjectorInteractionProfile.Presentation)
    ];

    public ValueTask<IReadOnlyList<ProjectorExperience>> GetExperiencesAsync(
        ProjectorSessionSnapshot? session,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new ValueTask<IReadOnlyList<ProjectorExperience>>(Experiences);
    }

    private static ProjectorExperience BuiltIn(
        string id,
        string name,
        string description,
        string iconKey,
        ProjectorInteractionProfile interactionProfile) => new(
            id,
            name,
            description,
            iconKey,
            ArtworkKey: null,
            ProjectorExperienceSource.BuiltIn,
            ProjectorLaunchStrategy.HavenSurface,
            interactionProfile,
            ProjectorExperiencePersistence.Persistent,
            [ProjectorCapability.RenderHavenSurface]);
}
