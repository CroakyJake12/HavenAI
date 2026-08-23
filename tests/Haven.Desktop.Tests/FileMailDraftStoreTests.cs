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
       