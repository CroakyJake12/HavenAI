namespace Haven.Application;

public sealed record PrivacyPreferences(
    bool LocalOnlyMode,
    bool BackgroundLearningEnabled,
    bool ModelImprovementSharingEnabled,
    DateTimeOffset UpdatedAt)
{
    public static PrivacyPreferences Default { get; } = new(
        LocalOnlyMode: false,
        BackgroundLearningEnabled: false,
        ModelImprovementSharingEnabled: false,
        DateTimeOffset.UnixEpoch);
}

public interface IPrivacyPreferenceStore
{
    PrivacyPreferences Current { get; }
    Task UpdateAsync(PrivacyPreferences preferences, CancellationToken cancellationToken);
}
