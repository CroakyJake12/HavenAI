using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;
using Xunit;

namespace Haven.Desktop.Tests;

public sealed class FileMailDraftStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "HavenMailDraftStoreTests", Guid.NewGuid().ToString("N"));
    private readonly FileMailDraftStore _store;

    public FileMailDraftStoreTests()
    {
        Directory.CreateDirectory(_root);
        _store = new FileMailDraftStore(new TestAppPaths(_root));
    }

    [Fact]
    public async Task UpsertAndGet_RoundTripsRichSemanticDraft()
    {
        var accountId = Guid.NewGuid();
        var localId = Guid.NewGuid();
        var attachmentId = Guid.NewGuid();
        var updatedAt = new DateTimeOffset(2026, 8, 23, 16, 0, 0, TimeSpan.Zero);
        var draft = new MailDraft(
            accountId, "provider-draft-42", MailResponseKind.ReplyAll, "source-message-7", "thread-9",
            [new MailAddress("Ada", "ada@example.com")],
            [new MailAddress("Grace", "grace@example.com")],
            [new MailAddress("Linus", "linus@example.com")],
            "Rich draft", "<p>Hello <strong>world</strong></p>", true,
            [new MailDraftAttachment("notes.txt", "text/plain", [1, 2, 3, 4], attachmentId)],
            LocalId: localId, Provider: CalendarProviderKind.Google, UpdatedAt: updatedAt,
            PersistenceState: MailDraftPersistenceState.Saved, LastSafeError: "safe marker");

        await _store.UpsertAsync(draft, CancellationToken.None);
        var restored = await _store.GetAsync(localId, CancellationToken.None);

        Assert.NotNull(restored);
        Assert.Equal(localId, restored.LocalId);
        Assert.Equal(accountId, restored.AccountId);
        Assert.Equal("provider-draft-42", restored.DraftId);
        Assert.Equal(MailResponseKind.ReplyAll, restored.ResponseKind);
        Assert.Equal("<p>Hello <strong>world</strong></p>", restored.Body);
        Assert.True(restored.IsHtml);
        Assert.Equal(CalendarProviderKind.Google, restored.Provider);
        Assert.Equal(MailDraftPersistenceState.Saved, restored.PersistenceState);
        Assert.Equal("safe marker", restored.LastSafeError);
        var attachment = Assert.Single(restored.Attachments);
        Assert.Equal(attachmentId, attachment.LocalId);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, attachment.Content);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class TestAppPaths(string root) : IAppPaths
    {
        public string DataDirectory { get; } = root;
        public string DatabasePath => Path.Combine(DataDirectory, "haven.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
    }
}
