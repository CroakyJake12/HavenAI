/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Core/Updates/UpdateModels.cs, in the Core layer, which owns stable entities, enums, value objects, and contracts.
 * What: This file owns InstallationSource, UpdateChannel, UpdateState, UpdateManifest, UpdateStatusReport and UpdatePreferences. Read the member comments below as a map of each responsibility.
 * How: Enums carry explicit numeric values that are persisted and must never be renumbered; records are immutable value snapshots passed across layer boundaries and serialized as JSON.
 * Why: Source-aware update policy (Microsoft Store-managed versus direct installs) needs one shared vocabulary so Application orchestration and Infrastructure providers agree on state without any platform coupling.
 * Maintenance: Never renumber existing enum values or rename serialized record properties; extend additively only.
 */

namespace Haven.Core;

/// <summary>
/// Identifies where the running Haven installation originated. Detection is signal-based and honest: <see cref="Unknown"/> means packaged identity was seen but Store management could not be confirmed.
/// </summary>
public enum InstallationSource
{
    /// <summary>The app was installed and is kept current by the Microsoft Store.</summary>
    MicrosoftStore = 0,
    /// <summary>The app was installed outside the Store (installer, portable copy) and Haven coordinates its own update staging.</summary>
    DirectInstall = 1,
    /// <summary>Detection signals were inconclusive; callers must treat this as a direct install while reporting the uncertainty to the user.</summary>
    Unknown = 2,
}

/// <summary>
/// Selects the release lane used when checking for updates. Persisted as a number; never renumber.
/// </summary>
public enum UpdateChannel
{
    /// <summary>Production releases only.</summary>
    Stable = 0,
    /// <summary>Early public previews.</summary>
    Preview = 1,
    /// <summary>Frequent developer builds; expected to be unstable.</summary>
    Development = 2,
}

/// <summary>
/// Lifecycle state of the update system as reported by <c>Haven.Application.IUpdateService</c>. Persisted nowhere; purely runtime state.
/// </summary>
public enum UpdateState
{
    /// <summary>No check has run yet in this session.</summary>
    Idle = 0,
    /// <summary>A check against the selected provider is in flight.</summary>
    Checking = 1,
    /// <summary>An update is available but has not been downloaded.</summary>
    Available = 2,
    /// <summary>The update payload is downloading; see the percent on the report.</summary>
    Downloading = 3,
    /// <summary>A verified package sits in the pending directory awaiting application by the external installer/bootstrapper on next start. Haven never claims it swapped its own binaries.</summary>
    StagedPendingRestart = 4,
    /// <summary>The most recent check found no newer version.</summary>
    UpToDate = 5,
    /// <summary>The last check, download or verification failed; the report message explains what happened.</summary>
    Failed = 6,
}

/// <summary>
/// A validated update offer published by the release feed for one channel.
/// </summary>
/// <param name="Version">Non-empty semver-ish version string, e.g. <c>1.2.3</c> or <c>1.2.3-preview.4</c>.</param>
/// <param name="Channel">Channel identifier this manifest was served for, e.g. <c>stable</c>.</param>
/// <param name="DownloadUrl">Absolute HTTPS URL of the update package.</param>
/// <param name="Sha256">Lowercase 64-character hexadecimal SHA-256 digest of the package payload.</param>
/// <param name="SizeBytes">Expected payload size in bytes; zero means the size is unknown and only the hash gates acceptance.</param>
/// <param name="ReleaseNotes">Human-readable release notes; may be empty.</param>
/// <param name="PublishedAt">Publication timestamp; more than one day in the future rejects the manifest.</param>
public sealed record UpdateManifest(
    string Version,
    string Channel,
    string DownloadUrl,
    string Sha256,
    long SizeBytes,
    string ReleaseNotes,
    DateTimeOffset PublishedAt);

/// <summary>
/// Immutable snapshot of update system state surfaced to the shell and settings UI.
/// </summary>
/// <param name="Source">Detected installation source for the running process.</param>
/// <param name="Channel">Effective channel (preferred channel from preferences).</param>
/// <param name="CurrentVersion">Version of the running executable.</param>
/// <param name="AvailableVersion">Version offered by the latest successful check, or <c>null</c>.</param>
/// <param name="State">Current lifecycle state.</param>
/// <param name="DownloadPercent">0-100 while downloading, otherwise <c>null</c>.</param>
/// <param name="Message">Honest human-readable status detail, including failures and detection caveats.</param>
/// <param name="StoreManaged"><c>true</c> when the Microsoft Store owns availability and installation; Haven then never downloads binaries itself.</param>
public sealed record UpdateStatusReport(
    InstallationSource Source,
    UpdateChannel Channel,
    string CurrentVersion,
    string? AvailableVersion,
    UpdateState State,
    int? DownloadPercent,
    string? Message,
    bool StoreManaged);

/// <summary>
/// User-controlled update behaviour. Serialized to the versioned settings store; property names are part of the persistence contract.
/// </summary>
/// <param name="BackgroundChecksEnabled">Whether periodic background checks run at all.</param>
/// <param name="PreferredChannel">Release lane checked by subsequent update checks.</param>
public sealed record UpdatePreferences(
    bool BackgroundChecksEnabled = true,
    UpdateChannel PreferredChannel = UpdateChannel.Stable);
