using Haven.Application;
using Haven.Core;
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

    [Fact]
    public void Connections_render_live_state_with_secure_secret_entry()
    {
        using var scene = new SettingsHavenScene();
        scene.NavigateTo("integrations");
        var service = new ServiceConnectionSnapshot(CalendarProviderKind.Google, "Google Calendar", "Read and manage calendar events", "Connected", "student@example.com", "Synced just now", "Ready", true, true, false);
        var provider = new ProviderConnectionSnapshot("openai", "OpenAI", "OpenAI-compatible", "https://api.openai.com/v1", "Connected", "Healthy", "GPT models available", true, true, false);
        scene.SetConnections([service], [provider], "Connection status is up to date.");
        var all = scene.Root.DescendantsAndSelf().ToArray();
        var serviceCard = Assert.Single(all, item => item.Name == "Settings.Integrations.Service.Google");
        var providerCard = Assert.Single(all, item => item.Name == "Settings.Integrations.Provider.openai");
        var endpoint = Assert.IsType<Input>(Assert.Single(providerCard.DescendantsAndSelf(), item => item.Name == "Settings.Integrations.Provider.openai.Endpoint"));
        Assert.Equal("https://api.openai.com/v1", endpoint.Text);
        Assert.True(endpoint.GetValue(HavenProperties.Enabled));
        var connect = Assert.IsType<Button>(Assert.Single(serviceCard.DescendantsAndSelf(), item => item.Name == "Settings.Integrations.Service.Google.Connect"));
        var disconnect = Assert.IsType<Button>(Assert.Single(serviceCard.DescendantsAndSelf(), item => item.Name == "Settings.Integrations.Service.Google.Disconnect"));
        Assert.False(connect.GetValue(HavenProperties.Enabled));
        Assert.True(disconnect.GetValue(HavenProperties.Enabled));
        Assert.Contains(providerCard.DescendantsAndSelf().OfType<Button>(), b => b.Content == "Update connection");
        Assert.Contains(providerCard.DescendantsAndSelf().OfType<Button>(), b => b.Content == "Test connection");
        Assert.Contains(providerCard.DescendantsAndSelf().OfType<Button>(), b => b.Content == "Disconnect");
        var secret = Assert.Single(providerCard.DescendantsAndSelf().OfType<Input>(), input => input.IsSecret);
        Assert.Equal("Leave blank to keep the saved API key", secret.Placeholder);
        Assert.False(secret.CanExposeSecretToClipboard);
    }

    [Fact]
    public void Unconfigured_provider_exposes_secure_connect_input_but_not_unsafe_actions()
    {
        using var scene = new SettingsHavenScene();
        scene.NavigateTo("integrations");
        var provider = new ProviderConnectionSnapshot("anthropic", "Anthropic", "Anthropic", "https://api.anthropic.com", "Not connected", string.Empty, string.Empty, false, false, false);
        scene.SetConnections([], [provider], "Provider setup requires secure secret entry.");
        var card = Assert.Single(scene.Root.DescendantsAndSelf(), item => item.Name == "Settings.Integrations.Provider.anthropic");
        var buttons = card.DescendantsAndSelf().OfType<Button>().ToArray();
        Assert.True(Assert.Single(buttons, button => button.Content == "Connect").GetValue(HavenProperties.Enabled));
        Assert.False(Assert.Single(buttons, button => button.Content == "Test connection").GetValue(HavenProperties.Enabled));
        Assert.False(Assert.Single(buttons, button => button.Content == "Disconnect").GetValue(HavenProperties.Enabled));
        Assert.True(Assert.Single(card.DescendantsAndSelf().OfType<Input>(), input => input.IsSecret).GetValue(HavenProperties.Enabled));
    }

    [Fact]
    public void Privacy_section_exposes_persistable_controls_and_loads_store_values()
    {
        using var scene = new SettingsHavenScene();
        scene.LoadPrivacyPreferences(new PrivacyPreferences(
            LocalOnlyMode: true,
            BackgroundLearningEnabled: true,
            ModelImprovementSharingEnabled: false,
            DateTimeOffset.UtcNow));

        scene.NavigateTo("privacy");

        Assert.True(scene.LocalOnlyToggle.IsChecked);
        Assert.True(scene.BackgroundLearningToggle.IsChecked);
        Assert.False(scene.ModelImprovementSharingToggle.IsChecked);
        Assert.NotNull(scene.SavePrivacyButton);
        Assert.Equal("Privacy & Memory", scene.PageTitle.Content);
    }
}
