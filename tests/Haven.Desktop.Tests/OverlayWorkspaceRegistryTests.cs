using Haven.Application;
using Haven.Desktop.Overlay;

namespace Haven.Desktop.Tests;

public sealed class OverlayWorkspaceRegistryTests
{
    [Fact]
    public async Task Registry_restores_only_pinned_sessions_without_captured_context_payload()
    {
        var now = new DateTimeOffset(2026, 8, 16, 19, 0, 0, TimeSpan.Zero);
        var settings = new InMemorySettingsStore();
        var registry = new OverlayWorkspaceRegistry(settings, () => now);
        await registry.InitializeAsync(CancellationToken.None);

        var chat = await registry.OpenSessionAsync("chat", "Chat", Guid.NewGuid(), "Microsoft Word", CancellationToken.None);
        var go = await registry.OpenSessionAsync("go", "Go", null, null, CancellationToken.None);
        var context = TextContext(now, "Explain this paragraph");
        await registry.SetContextAsync(chat.Id, context, CancellationToken.None);
        await registry.UpdateGeometryAsync(chat.Id, new OverlaySurfaceGeometry(640, 480, -220, 90), CancellationToken.None);
        await registry.SetPinnedAsync(chat.Id, true, CancellationToken.None);
        await registry.CloseSessionAsync(chat.Id, CancellationToken.None);

        var closedPinned = Assert.Single(registry.Snapshot.Sessions, session => session.Id == chat.Id);
        Assert.True(closedPinned.IsPinned);
        Assert.False(closedPinned.IsVisible);
        Assert.Contains(registry.Snapshot.Sessions, session => session.Id == go.Id);

        var restoredRegistry = new OverlayWorkspaceRegistry(settings, () => now.AddMinutes(1));
        await restoredRegistry.InitializeAsync(CancellationToken.None);
        var restored = Assert.Single(restoredRegistry.Snapshot.Sessions);

        Assert.Equal(chat.Id, restored.Id);
        Assert.Equal("chat", restored.AppKey);
        Assert.True(restored.IsPinned);
        Assert.True(restored.IsVisible);
        Assert.Null(restored.Context);
        Assert.Equal(640, restored.Geometry.Width);
        Assert.Equal(480, restored.Geometry.Height);
        Assert.Equal(-220, restored.Geometry.X);
        Assert.Equal("Microsoft Word", restored.SourceAssociation);
    }

    [Fact]
    public async Task Registry_bounds_context_and_unpin_preserves_the_live_session()
    {
        var now = new DateTimeOffset(2026, 8, 16, 19, 0, 0, TimeSpan.Zero);
        var settings = new InMemorySettingsStore();
        var registry = new OverlayWorkspaceRegistry(settings, () => now);
        await registry.InitializeAsync(CancellationToken.None);
        var session = await registry.OpenSessionAsync("study", "Study", null, "Browser", CancellationToken.None);

        var attachments = Enumerable.Range(0, 10)
            .Select(index => new OverlayContextAttachmentReference($"a-{index}", "image", "image/png", $"capture-{index}", null))
            .ToList();
        var context = new OverlayContextEnvelope(
            OverlayContextKind.Mixed,
            new string('x', 40_000),
            attachments,
            "screen-region",
            new OverlayContextProvenance(
                "Browser",
                "Revision notes",
                new OverlaySelectionBounds(10, 20, 500, 280),
                now,
                now.AddMinutes(5),
                OverlayContextPermissionState.Granted,
                "User selected a bounded screen region."));

        await registry.SetContextAsync(session.Id, context, CancellationToken.None);
        var bounded = Assert.Single(registry.Snapshot.Sessions).Context;
        Assert.NotNull(bounded);
        Assert.Equal(32_768, bounded.SelectedText!.Length);
        Assert.Equal(8, bounded.Attachments.Count);
        Assert.True(bounded.WasTruncated);

        await registry.SetPinnedAsync(session.Id, true, CancellationToken.None);
        await registry.SetPinnedAsync(session.Id, false, CancellationToken.None);
        var live = Assert.Single(registry.Snapshot.Sessions);
        Assert.False(live.IsPinned);
        Assert.True(live.IsVisible);
        Assert.NotNull(live.Context);

        var restarted = new OverlayWorkspaceRegistry(settings, () => now.AddMinutes(1));
        await restarted.InitializeAsync(CancellationToken.None);
        Assert.Empty(restarted.Snapshot.Sessions);
    }

    [Fact]
    public async Task Registry_rejects_context_outside_its_retention_window()
    {
        var now = new DateTimeOffset(2026, 8, 16, 19, 0, 0, TimeSpan.Zero);
        var registry = new OverlayWorkspaceRegistry(new InMemorySettingsStore(), () => now);
        await registry.InitializeAsync(CancellationToken.None);
        var session = await registry.OpenSessionAsync("chat", "Chat", null, null, CancellationToken.None);
        var expired = TextContext(now.AddMinutes(-10), "Old selection") with
        {
            Provenance = TextContext(now.AddMinutes(-10), "Old selection").Provenance with { ExpiresAt = now.AddMinutes(-1) }
        };

        await Assert.ThrowsAsync<ArgumentException>(() => registry.SetContextAsync(session.Id, expired, CancellationToken.None));
    }

    [Fact]
    public void Fixed_action_catalog_changes_with_text_and_visual_context()
    {
        var now = DateTimeOffset.UtcNow;
        var text = TextContext(now, "Selected text");
        var visual = text with { Kind = OverlayContextKind.Region, SelectedText = null, MediaReference = "region" };

        var textActions = OverlayContextActionCatalog.BuildFixed(text);
        Assert.Contains(textActions, action => action.Id == "ask-haven");
        Assert.Contains(textActions, action => action.Id == "summarise");
        Assert.Contains(textActions, action => action.Id == "send-study");
        Assert.DoesNotContain(textActions, action => action.Id == "ocr-copy");

        var visualActions = OverlayContextActionCatalog.BuildFixed(visual);
        Assert.Contains(visualActions, action => action.Id == "ask-haven");
        Assert.Contains(visualActions, action => action.Id == "analyse");
        Assert.Contains(visualActions, action => action.Id == "ocr-copy");
        Assert.DoesNotContain(visualActions, action => action.Id == "summarise");
    }

    private static OverlayContextEnvelope TextContext(DateTimeOffset capturedAt, string text) => new(
        OverlayContextKind.Text,
        text,
        [],
        null,
        new OverlayContextProvenance(
            "Editor",
            "Document",
            null,
            capturedAt,
            capturedAt.AddMinutes(5),
            OverlayContextPermissionState.Granted,
            "Explicit user selection."));

    private sealed class InMemorySettingsStore : IVersionedSettingsStore
    {
        private readonly Dictionary<string, object> _values = new(StringComparer.OrdinalIgnoreCase);

        public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken) where T : class
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_values.TryGetValue(key, out var value) ? value as T : null);
        }

        public Task SetAsync<T>(string key, T value, CancellationToken cancellationToken) where T : class
        {
            cancellationToken.ThrowIfCancellationRequested();
            _values[key] = value;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _values.Remove(key);
            return Task.CompletedTask;
        }

        public Task<SettingsExportManifest> ExportAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new SettingsExportManifest());
        }

        public Task<SettingsImportResult> ImportAsync(SettingsExportManifest manifest, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new SettingsImportResult(true, manifest.Settings, "Imported"));
        }
    }
}
