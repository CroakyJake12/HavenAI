/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/Views/Pages/Settings/SettingsHavenScene.Updates.cs, a partial of the Haven.UI Settings scene.
 * What: This file owns the Updates section: installation source, current version, latest known state, channel select, background-checks toggle and check-now control.
 * How: Builds through the shared scene helpers (Section/Card/Heading/Muted/SettingRow) so tokens, typography and interaction stay canonical; rendering is driven by SettingsHavenPage via SetUpdateStatus.
 * Why: Users need one honest place describing how their copy of Haven receives updates without pretending Store-managed installs are Haven-controlled.
 * Maintenance: Keep every state string honest; never imply an update installed or that staged packages replaced running binaries.
 */

using Haven.Core;
using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;
using HavenText = Haven.UI.Components.Text;

namespace Haven.Desktop.Views.Pages.Settings;

internal sealed partial class SettingsHavenScene
{
    public HavenText UpdatesInstallSourceText { get; private set; } = null!;
    public HavenText UpdatesCurrentVersionText { get; private set; } = null!;
    public HavenText UpdatesLatestStateText { get; private set; } = null!;
    public Select UpdatesChannelSelect { get; private set; } = null!;
    public Toggle UpdatesBackgroundChecksToggle { get; private set; } = null!;
    public HavenButton UpdatesCheckNowButton { get; private set; } = null!;
    public HavenText UpdatesStatusText { get; private set; } = null!;

    private Container BuildUpdates()
    {
        var section = Section("Settings.Updates");
        var card = Card("Settings.Updates.Card");
        card.Add(Heading("Settings.Updates.Heading", "Updates", 18));
        card.Add(Muted("Settings.Updates.Description",
            "Shows how this copy of Haven receives updates. Microsoft Store installs are updated by the Store itself; direct installs stage hash-verified packages for an external installer to apply on the next start."));

        UpdatesInstallSourceText = Muted("Settings.Updates.InstallSource", "Installation source: detecting…");
        UpdatesCurrentVersionText = Muted("Settings.Updates.CurrentVersion", "Current version: unknown");
        UpdatesLatestStateText = Muted("Settings.Updates.LatestState", "No update check has run in this session.");

        UpdatesChannelSelect = NewSelect("Settings.Updates.Channel", ["Stable", "Preview", "Development"]);
        UpdatesChannelSelect.Accessibility.AccessibleName = "Update channel";
        UpdatesBackgroundChecksToggle = NewToggle("Settings.Updates.BackgroundChecks");
        UpdatesBackgroundChecksToggle.Accessibility.AccessibleName = "Background update checks";
        UpdatesCheckNowButton = new HavenButton
        {
            Name = "Settings.Updates.CheckNow",
            Content = "Check now",
            Variant = ButtonVariant.Secondary
        };
        UpdatesCheckNowButton.Accessibility.AccessibleName = "Check for updates now";

        card.Add(UpdatesInstallSourceText);
        card.Add(UpdatesCurrentVersionText);
        card.Add(UpdatesLatestStateText);
        card.Add(SettingRow("Update channel", "Release lane used when checking for updates.", UpdatesChannelSelect));
        card.Add(SettingRow("Background update checks", "Let Haven check for updates while it runs in the background.", UpdatesBackgroundChecksToggle));
        card.Add(UpdatesCheckNowButton);
        UpdatesStatusText = Muted("Settings.Updates.Status", string.Empty);
        UpdatesStatusText.SetValue(HavenProperties.MinHeight, HavenLength.Px(24));
        card.Add(UpdatesStatusText);
        section.Add(card);
        return section;
    }

    /// <summary>
    /// Renders one update status report honestly. <paramref name="feedsConfigured"/> is false while the release-feed URLs
    /// remain on the placeholder template, which must read as "not yet configured" rather than as a silent failure.
    /// </summary>
    public void SetUpdateStatus(UpdateStatusReport report, bool feedsConfigured)
    {
        ArgumentNullException.ThrowIfNull(report);
        var sourceLabel = report.Source switch
        {
            InstallationSource.MicrosoftStore => "Microsoft Store",
            InstallationSource.DirectInstall => "Direct install",
            _ => "Unconfirmed"
        };
        var storeControlled = report.StoreManaged || report.Source == InstallationSource.MicrosoftStore;

        UpdatesInstallSourceText.Content = storeControlled
            ? $"Installation source: {sourceLabel} (Store-managed)"
            : report.Source == InstallationSource.Unknown
                ? $"{sourceLabel} — signals were inconclusive, treated as a direct install"
                : $"Installation source: {sourceLabel}";
        UpdatesCurrentVersionText.Content = $"Current version: {(string.IsNullOrWhiteSpace(report.CurrentVersion) ? "unknown" : report.CurrentVersion)}";
        UpdatesLatestStateText.Content = DescribeUpdateState(report);

        UpdatesChannelSelect.SetValue(HavenProperties.Enabled, !storeControlled);
        UpdatesStatusText.Content = ComposeUpdateStatusLine(report, storeControlled, feedsConfigured);
    }

    private static string DescribeUpdateState(UpdateStatusReport report) => report.State switch
    {
        UpdateState.Checking => "Checking for updates…",
        UpdateState.Available when report.AvailableVersion is { } available
            => $"Version {available} is available to download and stage.",
        UpdateState.Downloading => report.DownloadPercent is { } percent
            ? $"Downloading update… {percent}%"
            : "Downloading update…",
        UpdateState.StagedPendingRestart when report.AvailableVersion is { } staged
            => $"Version {staged} is verified and staged; the external installer applies it on the next start.",
        UpdateState.StagedPendingRestart
            => "A verified package is staged; the external installer applies it on the next start.",
        UpdateState.UpToDate => report.StoreManaged
            ? "Nothing for Haven to act on — the Microsoft Store owns availability."
            : "You are up to date.",
        UpdateState.Failed => "The last update check did not complete.",
        _ => "No update check has run in this session."
    };

    private static string ComposeUpdateStatusLine(UpdateStatusReport report, bool storeControlled, bool feedsConfigured)
    {
        var lines = new List<string>(2);
        if (storeControlled)
        {
            lines.Add("Updates are managed by the Microsoft Store.");
        }
        else if (!feedsConfigured)
        {
            lines.Add("Update feeds are not yet configured in this build, so update checks cannot reach a release feed.");
        }
        if (!string.IsNullOrWhiteSpace(report.Message))
        {
            lines.Add(report.Message);
        }
        return lines.Count == 0 ? string.Empty : string.Join(" ", lines);
    }
}
