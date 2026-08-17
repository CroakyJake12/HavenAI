using Haven.Core;
using Haven.Desktop.Overlay;
using Haven.UI.Components;

namespace Haven.Desktop.Tests;

public sealed class OverlayShellHavenSceneTests
{
    [Fact]
    public void Scene_projects_sessions_permission_and_dynamic_actions()
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
        scene.SetActions(
        [
            new OverlayContextActionDescriptor("ask-haven", "Ask Haven", "sparkles", true),
            new OverlayContextActionDescriptor(
                "capability:web-search:search", "Web Search · Search", "web-search", true, true,
                "browser.search", CapabilityRiskClass.ReadOnly, CapabilityAvailability.PermissionRequired,
                "haven.browser", "browser.search")
        ]);

        Assert.Equal("Chat", scene.TitleText.Content);
        Assert.Contains("Browser", scene.SourceText.Content);
        Assert.Contains("Capture allowed", scene.PermissionText.Content);
        Assert.Contains("bounded selection", scene.ContextSummary.Content, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, scene.SessionTabs.Items.Count);
        Assert.Equal(ButtonVariant.Primary, scene.SessionTabs.GetItem(activeId.ToString("N")).GetComponent<Button>("Activate").Variant);
        Assert.Equal(ButtonVariant.Secondary, scene.SessionTabs.GetItem(pinnedId.ToString("N")).GetComponent<Button>("Activate").Variant);
        Assert.Equal(ButtonVariant.Primary, scene.Actions.GetItem("ask-haven-0").GetComponent<Button>("Invoke").Variant);
        Assert.Contains("asks", scene.Actions.GetItem("capability-web-search-search-1").GetComponent<Button>("Invoke").Content);
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
