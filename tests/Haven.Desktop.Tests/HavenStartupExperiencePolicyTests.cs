using Haven.Core;
using Haven.Desktop.Views.Shell;

namespace Haven.Desktop.Tests;

public sealed class HavenStartupExperiencePolicyTests
{
    [Fact]
    public void StartupAlwaysUsesTheSingleCurrentHavenExperience()
    {
        Assert.Equal(HavenShellEdition.New, HavenStartupExperiencePolicy.Edition);
        Assert.Equal(HavenUiAppearance.SuperDark, HavenStartupExperiencePolicy.Appearance);
    }
}
