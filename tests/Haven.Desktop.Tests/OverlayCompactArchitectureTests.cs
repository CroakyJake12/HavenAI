using Haven.Desktop.Overlay;
using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;

namespace Haven.Desktop.Tests;

public sealed class OverlayCompactArchitectureTests
{
    [Fact]
    public void Scene_switches_between_expanded_system_surface_and_collapsed_screen_prompt()
    {
        using var scene = new OverlayShellHavenScene();

        scene.SetCollapsed(false);
        Assert.Equal(HavenVisibility.Visible, scene.ExpandedPanel.GetValue(HavenProperties.Visibility));
        Assert.Equal(HavenVisibility.Collapsed, scene.CollapsedPromptButton.GetValue(HavenProperties.Visibility));

        scene.SetCollapsed(true);
        Assert.Equal(HavenVisibility.Collapsed, scene.ExpandedPanel.GetValue(HavenProperties.Visibility));
        Assert.Equal(HavenVisibility.Visible, scene.CollapsedPromptButton.GetValue(HavenProperties.Visibility));
        Assert.Contains("Ask Haven", scene.CollapsedPromptButton.Content?.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Screen", scene.CollapsedPromptButton.Content?.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Scene_owns_compact_suggestions_and_composer_submission()
    {
        using var scene = new OverlayShellHavenScene();
        var labels = new[] { "Continue Code Refactor", "Review today's tasks", "Summarise this file", "Research a question" };
        string? submitted = null;
        scene.SubmitRequested += (_, text) => submitted = text;

        scene.SetSuggestions(labels);
        var buttons = scene.SuggestionsPanel.Children.OfType<HavenButton>().ToArray();
        Assert.Equal(4, buttons.Length);
        Assert.Equal(labels, buttons.Select(button => button.Content?.ToString()).ToArray());

        scene.ComposerInput.Text = "  explain this screen  ";
        scene.SubmitComposer();
        Assert.Equal("explain this screen", submitted);
        Assert.Equal(string.Empty, scene.ComposerInput.Text);
    }
}
