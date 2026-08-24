using Haven.Core;

namespace Haven.Desktop.Views.Shell;

/// <summary>
/// Declares the single supported Haven startup experience. Keeping the decision
/// pure prevents a retired chooser or Classic default from reappearing during
/// later shell refactors.
/// </summary>
public static class HavenStartupExperiencePolicy
{
    public const HavenShellEdition Edition = HavenShellEdition.New;
    public const HavenUiAppearance Appearance = HavenUiAppearance.SuperDark;
}
