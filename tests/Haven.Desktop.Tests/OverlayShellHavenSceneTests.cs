using Haven.Core;
using Haven.Desktop.Overlay;
using Haven.UI;
using Haven.UI.Components;

namespace Haven.Desktop.Tests;

public sealed class OverlayShellHavenSceneTests
{
    [Fact]
    public void Scene_projects_sessions_permission_dynamic_actions_and_compact_composer()
    {
        var now = new DateTimeOffset(2026, 8, 17, 7, 0, 0, TimeSpan.Zero);
        var activeId = Guid.NewGuid();
        var pinnedId = Guid.NewGuid();
        var context = new OverlayContextEnvelope(
            OverlayContextKind.Text,
            "A bounded selection from another application.",
            [],
            null,
            new OverlayContextProvenance(
                "Browser",
                "Revision notes",
                new OverlaySelectionBounds(12, 24, 640, 280),
                now,
                now.AddMinutes(5),
                OverlayContextPermissionState.Granted,
                "Explicit selection."));

        var snapshot = new OverlayWorkspaceSnapshot(
            activeId,
            [
                Session(activeId, "Chat", false, context, now),
                Session(pinnedId, "Pinned chat", true, null, now.AddSeconds(1))
            ]);

        using var scene = new OverlayShellHavenScene();
        scene.ApplySnapshot(snapshot, activeId);
        scene.SetSuggestions(
        [
            new OverlayCompactSuggestion("Continue recent chat", "Continue my most recent chat."),
            new OverlayCompactSuggestion("Study next topic", "Continue studying my next topic.")
        ]);
        scene.SetActions(
        [
            new OverlayContextActionDescriptor("ask-haven", "Ask Haven", "sparkles", true),
            new OverlayContextActionDescriptor(
                "capability:web-search:search", "Web Search · Search", "web-search", true, true,
                "browser.search", CapabilityRiskClass.ReadOnly, CapabilityAvailability.PermissionRequired,
                "haven.browser", "browser.search")
        ]);

        Assert.StartsWith("Chat", scene.TitleText.Content);
        Assert.Equal("How can I help?", scene.PromptText.Content);
        Assert.Equal("Ask Haven about your screen", scene.ComposerInput.Placeholder);
        Assert.Contains("Browser", scene.SourceText.Content);
        Assert.Contains("Capture allowed", scene.PermissionText.Content);
        Assert.Contains("bounded selection", scene.ContextSummary.Content, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, scene.SessionTabs.Items.Count);
        Assert.Equal(HavenVisibility.Collapsed, scene.SessionTabs.GetValue(HavenProperties.Visibility));
        Assert.Equal(ButtonVariant.Primary, scene.SessionTabs.GetItem(activeId.ToString("N")).GetComponent<Button>("Activate").Variant);
        Assert.Equal(ButtonVariant.Secondary, scene.SessionTabs.GetItem(pinnedId.ToString("N")).GetComponent<Button>("Activate").Variant);
        Assert.Equal(2, scene.SuggestedActions.Items.Count);
        Assert.Equal("Continue recent chat", scene.SuggestedActions.GetItem("suggestion-0").GetComponent<Button>("Invoke").Content);
        Assert.Equal(ButtonVariant.Primary, scene.Actions.GetItem("ask-haven-0").GetComponent<Button>("Invoke").Variant);
        Assert.Contains("asks", scene.Actions.GetItem("capability-web-search-search-1").GetComponent<Button>("Invoke").Content);
    }

    [Fact]
    public void Scene_summarises_universal_selection_items_in_context_panel()
    {
        var now = new DateTimeOffset(2026, 8, 18, 8, 0, 0, TimeSpan.Zero);
        var sessionId = Guid.NewGuid();
        var context = new OverlayContextEnvelope(
            OverlayContextKind.UiComponent,
            null,
            [],
            null,
            new OverlayContextProvenance("Browser", "Checkout", new OverlaySelectionBounds(20, 40, 100, 42), now, now.AddMinutes(5), OverlayContextPermissionState.Granted, "Explicit selection."),
            false,
            [new OverlaySelectionItem(
                "submit",
                OverlaySelectionKind.UiComponent,
                new OverlaySelectionBounds(20, 40, 100, 42),
                null,
                null,
                null,
                new OverlaySelectionSemanticMetadata("button", "Submit", "submit-button", "Button", true, false, null, null),
                "Submit button")]);

        using var scene = new OverlayShellHavenScene();
        scene.ApplySnapshot(new OverlayWorkspaceSnapshot(sessionId, [Session(sessionId, "Chat", false, context, now)]), sessionId);

        Assert.Equal("Selected UI component · Submit button · Button", scene.ContextSummary.Content);
    }

    [Fact]
    public void Scene_projects_collapsed_state_as_compact_ask_haven_surface()
    {
        var now = new DateTimeOffset(2026, 8, 17, 16, 0, 0, TimeSpan.Zero);
        var sessionId = Guid.NewGuid();
        var collapsed = Session(sessionId, "Chat", false, null, now) with { IsCollapsed = true };

        using var scene = new OverlayShellHavenScene();
        scene.ApplySnapshot(new OverlayWorkspaceSnapshot(sessionId, [collapsed]), sessionId);

        Assert.Equal("Ask Haven about your Screen", scene.TitleText.Content);
        Assert.Equal("Expand", scene.CollapseButton.Content);
        Assert.Equal(ButtonVariant.Secondary, scene.CollapseButton.Variant);
        Assert.Equal(HavenVisibility.Collapsed, scene.PromptText.GetValue(HavenProperties.Visibility));
        Assert.Equal(HavenVisibility.Collapsed, scene.ContextPanel.GetValue(HavenProperties.Visibility));
        Assert.Equal(HavenVisibility.Collapsed, scene.Composer.GetValue(HavenProperties.Visibility));
        Assert.Equal(HavenVisibility.Collapsed, scene.CaptureButton.GetValue(HavenProperties.Visibility));
    }

    [Fact]
    public void Composer_emits_trimmed_instruction_without_using_a_full_chat_page()
    {
        using var scene = new OverlayShellHavenScene();
        string? submitted = null;
        scene.SubmitRequested += (_, instruction) => submitted = instruction;
        scene.ComposerInput.Text = "  Explain what is on this screen.  ";

        scene.SubmitComposer();

        Assert.Equal("Explain what is on this screen.", submitted);
    }

    private static OverlaySessionState Session(
        Guid id,
        string title,
        bool pinned,
        OverlayContextEnvelope? context,
        DateTimeOffset now) =>
        new(id, "chat", title, Guid.NewGuid(), pinned, true, OverlaySurfaceGeometry.Default, context, now, now,
            context?.Provenance.SourceApplication);
}
