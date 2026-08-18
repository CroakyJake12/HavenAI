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

    [Theory]
    [InlineData("inspect-control", "Inspect control", "role, state, accessibility details, and available interactions without activating it")]
    [InlineData("run-automation", "Run automation", "prepare the requested run for my review; do not execute it without the normal Haven permission flow")]
    [InlineData("open-in-app", "Open in app", "prepare an open or navigation action for my review; do not execute it without the normal Haven permission flow")]
    [InlineData("analyse-frame", "Analyse this frame", "selected video frame at the captured media position")]
    [InlineData("summarise-media", "Summarise visible media", "visible media context without assuming content outside the captured selection")]
    public void Universal_selection_actions_use_specific_review_instructions(string id, string label, string expected)
    {
        var action = new OverlayContextActionDescriptor(id, label, "test", true);

        var draft = OverlayWorkspaceController.BuildReviewDraft(action, null, null);

        Assert.Contains(expected, draft, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(label + " the selected context.", draft, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Universal_selection_semantics_reach_the_review_draft()
    {
        var now = new DateTimeOffset(2026, 8, 18, 8, 0, 0, TimeSpan.Zero);
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
                "Submit order",
                "button-preview.png",
                new OverlayContextAttachmentReference("button-detail.json", "semantic", "application/json", "Button details", null),
                new OverlaySelectionSemanticMetadata("button", "Submit", "submit-button", "Button", true, false, null, null),
                "Submit button")]);
        var action = new OverlayContextActionDescriptor("inspect-control", "Inspect control", "info", true);

        var draft = OverlayWorkspaceController.BuildReviewDraft(action, context, null);

        Assert.Contains("Selected items:", draft);
        Assert.Contains("UI component · Submit button · role button · control Button · automation id submit-button · enabled yes · selected no", draft);
        Assert.Contains("bounds 20,40 100×42", draft);
        Assert.Contains("media reference button-preview.png", draft);
        Assert.Contains("attachment Button details", draft);
        Assert.Contains("Text: Submit order", draft);
        Assert.Contains("Browser · Checkout", draft);
    }

    [Fact]
    public void Universal_selection_review_bounds_attachment_labels()
    {
        var now = new DateTimeOffset(2026, 8, 18, 8, 0, 0, TimeSpan.Zero);
        var longLabel = new string('a', 700);
        var context = new OverlayContextEnvelope(
            OverlayContextKind.Image,
            null,
            [],
            null,
            new OverlayContextProvenance(null, null, null, now, now.AddMinutes(5), OverlayContextPermissionState.Granted, null),
            false,
            [new OverlaySelectionItem(
                "image",
                OverlaySelectionKind.Image,
                null,
                null,
                null,
                new OverlayContextAttachmentReference("image.bin", "image", "application/octet-stream", longLabel, null),
                null,
                "Image")]);

        var details = OverlaySelectionPresentation.ReviewDetails(context);

        Assert.Contains("attachment " + new string('a', 511) + "…", details);
        Assert.DoesNotContain(new string('a', 512), details);
    }

    [Fact]
    public void Universal_selection_concrete_files_reach_chat_attachment_handoff()
    {
        var root = Path.Combine(Path.GetTempPath(), "haven-overlay-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var topAttachment = Path.Combine(root, "top.txt");
            var sharedMedia = Path.Combine(root, "shared.png");
            var selectionAttachment = Path.Combine(root, "selection.json");
            var missingMedia = Path.Combine(root, "missing.png");
            File.WriteAllText(topAttachment, "top");
            File.WriteAllBytes(sharedMedia, [1, 2, 3]);
            File.WriteAllText(selectionAttachment, "{}");

            var now = new DateTimeOffset(2026, 8, 18, 9, 0, 0, TimeSpan.Zero);
            var context = new OverlayContextEnvelope(
                OverlayContextKind.Mixed,
                null,
                [new OverlayContextAttachmentReference(topAttachment, "text", "text/plain", "Top", null)],
                sharedMedia,
                new OverlayContextProvenance(null, null, null, now, now.AddMinutes(5), OverlayContextPermissionState.Granted, null),
                false,
                [
                    new OverlaySelectionItem("image", OverlaySelectionKind.Image, null, null, sharedMedia,
                        new OverlayContextAttachmentReference(selectionAttachment, "semantic", "application/json", "Selection", null), null, "Image"),
                    new OverlaySelectionItem("missing", OverlaySelectionKind.Image, null, null, missingMedia, null, null, "Missing image")
                ]);

            var files = OverlayWorkspaceController.ConcreteContextFiles(context);

            Assert.Equal([topAttachment, sharedMedia, selectionAttachment], files);
            Assert.DoesNotContain(missingMedia, files);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Universal_selection_semantics_reach_the_review_draft()
    {
        var now = new DateTimeOffset(2026, 8, 18, 8, 0, 0, TimeSpan.Zero);
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
                "Submit order",
                "button-preview.png",
                new OverlayContextAttachmentReference("button-detail.json", "semantic", "application/json", "Button details", null),
                new OverlaySelectionSemanticMetadata("button", "Submit", "submit-button", "Button", true, false, null, null),
                "Submit button")]);
        var action = new OverlayContextActionDescriptor("inspect-control", "Inspect control", "info", true);

        var draft = OverlayWorkspaceController.BuildReviewDraft(action, context, null);

        Assert.Contains("Selected items:", draft);
        Assert.Contains("UI component · Submit button · role button · control Button · automation id submit-button · enabled yes · selected no", draft);
        Assert.Contains("bounds 20,40 100×42", draft);
        Assert.Contains("media reference button-preview.png", draft);
        Assert.Contains("attachment Button details", draft);
        Assert.Contains("Text: Submit order", draft);
        Assert.Contains("Browser · Checkout", draft);
    }

    [Fact]
    public void Universal_selection_review_bounds_attachment_labels()
    {
        var now = new DateTimeOffset(2026, 8, 18, 8, 0, 0, TimeSpan.Zero);
        var longLabel = new string('a', 700);
        var context = new OverlayContextEnvelope(
            OverlayContextKind.Image,
            null,
            [],
            null,
            new OverlayContextProvenance(null, null, null, now, now.AddMinutes(5), OverlayContextPermissionState.Granted, null),
            false,
            [new OverlaySelectionItem(
                "image",
                OverlaySelectionKind.Image,
                null,
                null,
                null,
                new OverlayContextAttachmentReference("image.bin", "image", "application/octet-stream", longLabel, null),
                null,
                "Image")]);

        var details = OverlaySelectionPresentation.ReviewDetails(context);

        Assert.Contains("attachment " + new string('a', 511) + "…", details);
        Assert.DoesNotContain(new string('a', 512), details);
    }

    [Fact]
    public void Universal_selection_concrete_files_reach_chat_attachment_handoff()
    {
        var root = Path.Combine(Path.GetTempPath(), "haven-overlay-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var topAttachment = Path.Combine(root, "top.txt");
            var sharedMedia = Path.Combine(root, "shared.png");
            var selectionAttachment = Path.Combine(root, "selection.json");
            var missingMedia = Path.Combine(root, "missing.png");
            File.WriteAllText(topAttachment, "top");
            File.WriteAllBytes(sharedMedia, [1, 2, 3]);
            File.WriteAllText(selectionAttachment, "{}");

            var now = new DateTimeOffset(2026, 8, 18, 9, 0, 0, TimeSpan.Zero);
            var context = new OverlayContextEnvelope(
                OverlayContextKind.Mixed,
                null,
                [new OverlayContextAttachmentReference(topAttachment, "text", "text/plain", "Top", null)],
                sharedMedia,
                new OverlayContextProvenance(null, null, null, now, now.AddMinutes(5), OverlayContextPermissionState.Granted, null),
                false,
                [
                    new OverlaySelectionItem("image", OverlaySelectionKind.Image, null, null, sharedMedia,
                        new OverlayContextAttachmentReference(selectionAttachment, "semantic", "application/json", "Selection", null), null, "Image"),
                    new OverlaySelectionItem("missing", OverlaySelectionKind.Image, null, null, missingMedia, null, null, "Missing image")
                ]);

            var files = OverlayWorkspaceController.ConcreteContextFiles(context);

            Assert.Equal([topAttachment, sharedMedia, selectionAttachment], files);
            Assert.DoesNotContain(missingMedia, files);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Collapsed_geometry_preserves_expanded_size_while_updating_position()
    {
        var now = new DateTimeOffset(2026, 8, 17, 16, 0, 0, TimeSpan.Zero);
        var expanded = new OverlaySurfaceGeometry(720, 540, 20, 30);
        var collapsedSession = new OverlaySessionState(
            Guid.NewGuid(),
            "chat",
            "Chat",
            Guid.NewGuid(),
            false,
            true,
            expanded,
            null,
            now,
            now,
            null,
            IsCollapsed: true);
        var liveCollapsed = new OverlaySurfaceGeometry(720, 96, 180, 240);

        var persistedCollapsed = OverlayWorkspaceController.GeometryForPersistence(collapsedSession, liveCollapsed);
        var persistedExpanded = OverlayWorkspaceController.GeometryForPersistence(
            collapsedSession with { IsCollapsed = false },
            liveCollapsed);

        Assert.Equal(720, persistedCollapsed.Width);
        Assert.Equal(540, persistedCollapsed.Height);
        Assert.Equal(180, persistedCollapsed.X);
        Assert.Equal(240, persistedCollapsed.Y);
        Assert.Equal(liveCollapsed, persistedExpanded);
    }
}
