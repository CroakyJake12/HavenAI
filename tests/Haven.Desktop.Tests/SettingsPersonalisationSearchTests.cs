using Haven.Desktop.Views.Pages.Settings;

namespace Haven.Desktop.Tests;

public sealed class SettingsPersonalisationSearchTests
{
    [Theory]
    [InlineData("font")]
    [InlineData("accent colour")]
    [InlineData("profile picture")]
    public void Personalisation_search_routes_to_real_customisation_controls(string query)
    {
        using var scene = new SettingsHavenScene();
        scene.SearchInput.Text = query;

        Assert.True(scene.RunSearch());
        Assert.Equal("appearance", scene.ActiveSection);
        Assert.Equal("Personalisation", scene.PageTitle.Content);
    }
}
