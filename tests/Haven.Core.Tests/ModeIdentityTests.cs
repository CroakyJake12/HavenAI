using Haven.Application;
using Haven.Core;

namespace Haven.Core.Tests;

/// <summary>
/// Protects persisted mode values and the built-in App catalogue from accidental drift.
/// </summary>
public sealed class ModeIdentityTests
{
    [Fact]
    public void PersistedConversationModeValuesRemainStableAfterProductRenames()
    {
        Assert.Equal(0, (int)HavenMode.Chat);
        Assert.Equal(1, (int)HavenMode.Study);
        Assert.Equal(2, (int)HavenMode.Tasks);
        Assert.Equal(3, (int)HavenMode.Studio);
        Assert.Equal(HavenMode.Study, HavenMode.Teach);
        Assert.Equal(HavenMode.Tasks, HavenMode.Do);

        Assert.Equal(2, (int)ConversationScopeKind.StudyQuickChat);
        Assert.Equal(3, (int)ConversationScopeKind.StudyLesson);
        Assert.Equal(ConversationScopeKind.StudyQuickChat, ConversationScopeKind.TeachQuickChat);
        Assert.Equal(ConversationScopeKind.StudyLesson, ConversationScopeKind.TeachLesson);
    }

    [Fact]
    public void BuiltInAppsUseStableIdsAndCurrentProductNames()
    {
        string[] expectedKeys =
        [
            "chat", "study", "automations", "terminal", "tasks", "studio", "browse", "plan", "training",
            "imagine", "canvas", "present", "data", "vision", "play", "translate", "launcher", "go", "dashboard", "write", "mesh", "spaces", "boards", "maps", "motion"
        ];

        Assert.Equal(expectedKeys, BuiltInModeSeed.Modes.Select(mode => mode.Key));
        Assert.Equal(BuiltInModeSeed.Modes.Count, BuiltInModeSeed.Modes.Select(mode => mode.Id).Distinct().Count());
        Assert.DoesNotContain(BuiltInModeSeed.Modes, mode => mode.Key is "teach" or "do");
        Assert.Equal(HavenMode.Study, BuiltInModeSeed.Modes.Single(mode => mode.Key == "study").BaseMode);
        Assert.Equal(HavenMode.Tasks, BuiltInModeSeed.Modes.Single(mode => mode.Key == "automations").BaseMode);
        Assert.Equal(Guid.Parse("a0000000-0000-0000-0000-000000000003"), BuiltInModeSeed.Modes.Single(mode => mode.Key == "automations").Id);
        Assert.Equal(Guid.Parse("a0000000-0000-0000-0000-000000000020"), BuiltInModeSeed.Modes.Single(mode => mode.Key == "terminal").Id);
        Assert.Equal(HavenMode.Tasks, BuiltInModeSeed.Modes.Single(mode => mode.Key == "tasks").BaseMode);
        Assert.Equal(Guid.Parse("a0000000-0000-0000-0000-000000000019"), BuiltInModeSeed.Modes.Single(mode => mode.Key == "tasks").Id);
        Assert.Equal(HavenMode.Tasks, BuiltInModeSeed.Modes.Single(mode => mode.Key == "mesh").BaseMode);
        Assert.Equal(Guid.Parse("a0000000-0000-0000-0000-000000000021"), BuiltInModeSeed.Modes.Single(mode => mode.Key == "mesh").Id);
        Assert.NotEqual(SurfaceKind.Tasks, SurfaceKind.Go);
    }
}
