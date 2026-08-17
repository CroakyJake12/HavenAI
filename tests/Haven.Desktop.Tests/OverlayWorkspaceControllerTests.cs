using Haven.Core;
using Haven.Desktop.Overlay;

namespace Haven.Desktop.Tests;

public sealed class OverlayWorkspaceControllerTests
{
    [Fact]
    public void Review_draft_preserves_bounded_context_provenance_and_permission_metadata()
    {
        var now = new DateTimeOffset(2026, 8, 17, 8, 0, 0, TimeSpan.Zero);
        var context = new OverlayContextEnvelope(
            OverlayContextKind.Text,
            "Explain the highlighted paragraph.",
            [],
            null,
            new OverlayContextProvenance(
                "Browser",
                "Revision notes",
                new OverlaySelectionBounds(10, 20, 400, 180),
                now,
                now.AddMinutes(5),
                OverlayContextPermissionState.Granted,
                "Explicit bounded selection."),
            WasTruncated: true);
        var action = new OverlayContextActionDescriptor(
            "capability:web-search:search",
            "Web Search: Search",
            "web-search",
            true,
            true,
            "browser.search",
            CapabilityRiskClass.ReadOnly,
            CapabilityAvailability.PermissionRequired,
            "haven.browser",
            "browser.search");
        var capability = new CapabilityDefinition(
            Guid.NewGuid(),
            "web-search",
            "Web Search",
            "Searches the web.",
            "browse",
            "search",
            string.Empty,
            "browser.search",
            "[\"search\"]",
            CapabilityPlatform.Windows,
            CapabilityRiskClass.ReadOnly,
            CapabilityAvailability.PermissionRequired,
            "[]",
            "haven.browser",
            true,
            true,
            true,
            true,
            now);

        var draft = OverlayWorkspaceController.BuildReviewDraft(action, context, capability);

        Assert.Contains("normal Haven permission flow", draft);
        Assert.Contains("Web Search (ReadOnly, PermissionRequired)", draft);
        Assert.Contains("Explain the highlighted paragraph.", draft);
        Assert.Contains("Browser · Revision notes", draft);
        Assert.Contains("permission Granted", draft);
        Assert.Contains("bounded/truncated", draft);
    }

    [Fact]
    public void Ask_Haven_review_draft_is_user_reviewable_and_does_not_claim_execution()
    {
        var action = new OverlayContextActionDescriptor("ask-haven", "Ask Haven", "sparkles", true);

        var draft = OverlayWorkspaceController.BuildReviewDraft(action, null, null);

        Assert.Equal("Use this selected context to help me.", draft);
        Assert.DoesNotContain("completed", draft, StringComparison.OrdinalIgnoreCase);
    }
}
