using Haven.Desktop.Views.Pages.Settings;
using Haven.UI;
using Haven.UI.Components;

namespace Haven.Desktop.Tests;

public sealed class SettingsHavenSceneTests
{
    [Fact]
    public void Scene_uses_persistent_haven_ui_categories()
    {
        using var scene = new SettingsHavenScene();

        Assert.IsType<Page>(scene.Root);
        Assert.Equal("home", scene.ActiveSection);
        Assert.NotEmpty(scene.Sidebar.Conditions);
        Assert.NotEmpty(scene.CompactNavigation.Conditions);
        scene.NavigateTo("permissions");

        Assert.Equal("permissions", scene.ActiveSection);
        Assert.Equal("Permissions & Sandboxing", scene.PageTitle.Content);
        Assert.NotNull(scene.FilePermissionSelect);
        Assert.NotNull(scene.SavePermissionsButton);
    }

    [Fact]
    public void Search_routes_to_real_settings_metadata()
    {
        using var scene = new SettingsHavenScene();
        scene.SearchInput.Text = "tool permissions";

        Assert.True(scene.RunSearch());
        Assert.Equal("permissions", scene.ActiveSection);
        Assert.Contains("Opened Permissions & Sandboxing", scene.StatusText.Content);
    }

    [Fact]
    public void Provider_secret_search_routes_to_integration_transparency_surface()
    {
        using var scene = new SettingsHavenScene();
        scene.SearchInput.Text = "api key";

        Assert.True(scene.RunSearch());
        Assert.Equal("integrations", scene.ActiveSection);
    }

    [Fact]
    public void Destructive_model_removal_has_a_second_confirmation_surface()
    {
        using var scene = new SettingsHavenScene();
        scene.SetModels(["qwen3:4b"], "qwen3:4b");

        Assert.Equal(HavenVisibility.Collapsed, scene.DeleteConfirmation.GetValue(HavenProperties.Visibility));
        scene.SetDeleteConfirmation(true);
        Assert.Equal(HavenVisibility.Visible, scene.DeleteConfirmation.GetValue(HavenProperties.Visibility));
        Assert.NotNull(scene.ConfirmDeleteButton);
        Assert.NotNull(scene.CancelDeleteButton);
    }
}
