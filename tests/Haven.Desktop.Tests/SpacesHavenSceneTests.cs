using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Haven.Application;
using Haven.Desktop.HavenUI.Backend;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Pages.Spaces;
using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;

namespace Haven.Desktop.Tests;

public sealed class SpacesHavenSceneTests
{
    [AvaloniaFact]
    public void Picker_and_editor_expose_real_space_configuration_and_save_draft()
    {
        using var scene = new SpacesHavenScene();
        var space = SampleSpace(false);
        SpaceEditorDraft? saved = null;
        scene.SaveRequested += (_, draft) => saved = draft;
        scene.SetSpaces([space], space.Id);
        scene.SetSpace(space);

        Assert.Equal("Research workspace", scene.Name.Text);
        Assert.Equal("local-model", scene.Model.Text);
        Assert.Equal(3, scene.Thinking.SelectedIndex);
        Assert.False(scene.ManageLayout.GetValue(HavenProperties.Enabled));
        Assert.Contains("shared NodeEditor", scene.LayoutState.Content, StringComparison.Ordinal);
        Assert.Contains(scene.Files.DescendantsAndSelf().OfType<HavenButton>(), button => button.Content == "Remove");

        scene.Name.Text = "Research lab";
        scene.ExampleUser.Text = "Summarise this source";
        scene.ExampleAssistant.Text = "Here are the sourced findings.";
        scene.AddExampleFromInputs();
        scene.SurfaceTemplate.SelectedIndex = 1;
        scene.SurfaceInputs.Text = "{\"title\":\"Evidence\"}";
        scene.SaveCurrentDraft();

        Assert.NotNull(saved);
        Assert.Equal("Research lab", saved!.Name);
        Assert.Equal(SpaceThinkingMode.Deep, saved.ThinkingMode);
        Assert.Equal(2, saved.ExamplePairs.Count);
        Assert.Equal("checklist", saved.GeneratedSurface?.TemplateKey);
        Assert.Equal("{\"title\":\"Evidence\"}", saved.GeneratedSurface?.InputsJson);
    }

    [AvaloniaFact]
    public void Delete_uses_anchored_popup_and_built_ins_cannot_be_deleted()
    {
        using var scene = new SpacesHavenScene();
        var custom = SampleSpace(false);
        Guid? deleted = null;
        scene.DeleteRequested += (_, id) => deleted = id;
        scene.SetSpace(custom);

        scene.ShowDeleteConfirmation();
        var popup = Assert.Single(scene.Root.Children.OfType<PopupMenu>());
        Assert.Equal($"Delete {custom.Name}", popup.Card.Accessibility.AccessibleName);
        var confirm = popup.Card.Children.OfType<HavenButton>().Single(button => button.Content == "Delete permanently");
        Assert.Equal(ButtonVariant.Danger, confirm.Variant);
        popup.Dismiss();
        scene.ConfirmDelete(custom.Id);
        Assert.Equal(custom.Id, deleted);
        Assert.Empty(scene.Root.Children.OfType<PopupMenu>());

        scene.SetSpace(custom with { IsBuiltIn = true, Kind = SpaceKind.Study });
        Assert.False(scene.Delete.GetValue(HavenProperties.Enabled));
        Assert.Equal("Built-in Space", scene.Delete.Content);
    }

    [AvaloniaFact]
    public void Compact_layout_stacks_picker_and_editor_and_can_restore_desktop_layout()
    {
        using var scene = new SpacesHavenScene();

        scene.SetCompactLayout(true);

        Assert.True(scene.IsCompactLayout);
        Assert.Equal("1fr", scene.Body.Columns);
        Assert.Equal("220px 1fr", scene.Body.Rows);
        Assert.Equal(1, scene.EditorPanel.GetValue(HavenProperties.Row));
        Assert.Equal(0, scene.EditorPanel.GetValue(HavenProperties.Column));
        Assert.Equal("1fr 1fr", scene.EditorActions.Columns);
        Assert.Equal(2, scene.SelectedHeading.GetValue(HavenProperties.ColumnSpan));

        scene.SetCompactLayout(false);

        Assert.False(scene.IsCompactLayout);
        Assert.Equal("280px 1fr", scene.Body.Columns);
        Assert.Equal("1fr", scene.Body.Rows);
        Assert.Equal(0, scene.EditorPanel.GetValue(HavenProperties.Row));
        Assert.Equal(1, scene.EditorPanel.GetValue(HavenProperties.Column));
        Assert.Equal("1fr Auto Auto Auto", scene.EditorActions.Columns);
        Assert.Equal(1, scene.SelectedHeading.GetValue(HavenProperties.ColumnSpan));
    }

    [AvaloniaFact]
    public void Suggested_space_edits_change_the_draft_but_do_not_save_until_explicitly_requested()
    {
        using var scene = new SpacesHavenScene();
        var space = SampleSpace(false);
        SpaceEditorDraft? saved = null;
        scene.SaveRequested += (_, draft) => saved = draft;
        scene.SetSpace(space);
        scene.SetEditWithHavenAvailable(true);

        scene.ApplyEditPatch(new SpaceEditPatch(
            "Evidence lab",
            "Review evidence carefully",
            "research-model",
            "Separate facts from inference.",
            SpaceThinkingMode.Balanced,
            "checklist",
            "{\"title\":\"Evidence\"}"));

        Assert.Null(saved);
        Assert.Equal("Evidence lab", scene.Name.Text);
        Assert.Equal("research-model", scene.Model.Text);
        Assert.Equal((int)SpaceThinkingMode.Balanced, scene.Thinking.SelectedIndex);
        Assert.Equal(1, scene.SurfaceTemplate.SelectedIndex);
        Assert.Contains("Review them", scene.Status.Content, StringComparison.Ordinal);

        scene.SaveCurrentDraft();

        Assert.NotNull(saved);
        Assert.Equal("Evidence lab", saved!.Name);
        Assert.Equal("research-model", saved.ModelName);
        Assert.Equal(SpaceThinkingMode.Balanced, saved.ThinkingMode);
        Assert.Equal("checklist", saved.GeneratedSurface?.TemplateKey);
    }

    private static SpaceDefinition SampleSpace(bool builtIn) => new(
        Guid.NewGuid(),
        "Research workspace",
        "Investigate carefully",
        "search",
        SpaceKind.Research,
        builtIn,
        false,
        "local-model",
        "Separate sourced facts from inference.",
        SpaceThinkingMode.Deep,
        [new SpaceExamplePair("Question", "Answer")],
        [new SpaceFileReference(Path.Combine(Path.GetTempPath(), "source.txt"), "source.txt", SpaceFilePermission.ReadOnly, DateTimeOffset.UtcNow)],
        null,
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow);

}
