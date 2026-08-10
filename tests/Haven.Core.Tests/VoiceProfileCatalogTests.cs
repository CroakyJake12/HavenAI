using Haven.Application;
using Haven.Core;

namespace Haven.Core.Tests;

public sealed class VoiceProfileCatalogTests
{
    [Fact]
    public void BuiltInProfilesExposeGeneralAndLessonProfiles()
    {
        var catalog = new VoiceProfileCatalog();

        Assert.Contains(catalog.GetAll(), profile => profile.Id == "general");
        Assert.Contains(catalog.GetAll(), profile => profile.Id == "lesson");
    }

    [Fact]
    public void UserProfileCanBeAddedAndRemovedWithoutChangingBuiltIns()
    {
        var catalog = new VoiceProfileCatalog();
        var profile = catalog.UpsertUserProfile(new VoiceProfile(
            "user.study", "Study Companion", "A focused study profile.", "Ask concise study questions."));

        Assert.False(profile.IsBuiltIn);
        Assert.Same(profile, catalog.Find("user.study"));
        Assert.True(catalog.RemoveUserProfile("user.study"));
        Assert.Null(catalog.Find("user.study"));
        Assert.False(catalog.RemoveUserProfile("lesson"));
        Assert.NotNull(catalog.Find("lesson"));
    }

    [Fact]
    public void UserProfileRequiresIdentity()
    {
        var catalog = new VoiceProfileCatalog();

        Assert.Throws<ArgumentException>(() => catalog.UpsertUserProfile(
            new VoiceProfile("", "", "", "")));
    }
}
