using System.IO.Compression;
using System.Text;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

public sealed class MessageAttachmentServiceTests : IDisposable
{
    private readonly TestPaths _paths = new();

    [Fact]
    public async Task TextAndImageAttachmentsPersistAndBuildPromptContext()
    {
        var (conversations, production, service) = await CreateServicesAsync();
        var now = DateTimeOffset.UtcNow;
        var conversation = ConversationAt(now);
        await conversations.UpsertConversationAsync(conversation, CancellationToken.None);
        var branch = await production.EnsureRootBranchAsync(conversation.Id, CancellationToken.None);
        var textPath = Path.Combine(_paths.DataDirectory, "notes.txt");
        var imagePath = Path.Combine(_paths.DataDirectory, "image.png");
        await File.WriteAllTextAsync(textPath, "A persistent attachment sentence.");
        await File.WriteAllBytesAsync(imagePath, [137, 80, 78, 71, 13, 10, 26, 10]);

        var text = await service.ImportAsync(conversation.Id, null, branch.Id, textPath, null, CancellationToken.None);
        var image = await service.ImportAsync(conversation.Id, null, branch.Id, imagePath, null, CancellationToken.None);
        var context = await service.BuildPromptContextAsync(conversation.Id, [text.Id, image.Id], null, CancellationToken.None);

        Assert.Equal(AttachmentProcessingState.Ready, text.ProcessingState);
        Assert.Equal(AttachmentAnalysisMethod.TextExtracted, text.AnalysisMethod);
        Assert.Contains("persistent attachment sentence", context.ExtractedText, StringComparison.OrdinalIgnoreCase);
        Assert.Single(context.ImageBase64);
        Assert.Contains(context.Notices, item => item.Contains("sent directly", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task OpenXmlDocumentTextIsExtractedWithoutExecutingDocumentContent()
    {
        var (conversations, production, service) = await CreateServicesAsync();
        var conversation = ConversationAt(DateTimeOffset.UtcNow);
        await conversations.UpsertConversationAsync(conversation, CancellationToken.None);
        var branch = await production.EnsureRootBranchAsync(conversation.Id, CancellationToken.None);
        var path = Path.Combine(_paths.DataDirectory, "document.docx");
        await using (var stream = File.Create(path))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("word/document.xml");
            await using var output = entry.Open();
            var xml = "<w:document xmlns:w=\"urn:test\"><w:body><w:p><w:r><w:t>Hello from the document</w:t></w:r></w:p></w:body></w:document>";
            await output.WriteAsync(Encoding.UTF8.GetBytes(xml));
        }

        var attachment = await service.ImportAsync(conversation.Id, null, branch.Id, path, null, CancellationToken.None);

        Assert.Equal(MessageAttachmentKind.Word, attachment.Kind);
        Assert.Equal(AttachmentAnalysisMethod.TextExtracted, attachment.AnalysisMethod);
        Assert.Contains("Hello from the document", attachment.ExtractedText);
    }

    [Fact]
    public async Task VideoWithoutLocalMediaToolsReportsMetadataOnlyRatherThanInventingAnalysis()
    {
        var (conversations, production, service) = await CreateServicesAsync();
        var conversation = ConversationAt(DateTimeOffset.UtcNow);
        await conversations.UpsertConversationAsync(conversation, CancellationToken.None);
        var branch = await production.EnsureRootBranchAsync(conversation.Id, CancellationToken.None);
        var path = Path.Combine(_paths.DataDirectory, "clip.mp4");
        await File.WriteAllBytesAsync(path, new byte[128]);

        var attachment = await service.ImportAsync(conversation.Id, null, branch.Id, path, null, CancellationToken.None);

        Assert.Equal(MessageAttachmentKind.Video, attachment.Kind);
        Assert.Equal(AttachmentAnalysisMethod.InferredFromMetadata, attachment.AnalysisMethod);
        Assert.Empty(attachment.ExtractedText);
        Assert.Contains("No frames or audio were analysed", attachment.MetadataJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SizeLimitsAreEnforcedBeforeCopying()
    {
        var (conversations, production, service) = await CreateServicesAsync();
        var conversation = ConversationAt(DateTimeOffset.UtcNow);
        await conversations.UpsertConversationAsync(conversation, CancellationToken.None);
        var branch = await production.EnsureRootBranchAsync(conversation.Id, CancellationToken.None);
        var path = Path.Combine(_paths.DataDirectory, "large.txt");
        await File.WriteAllBytesAsync(path, new byte[64]);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ImportAsync(
            conversation.Id, null, branch.Id, path, new AttachmentProcessingOptions(MaxDocumentBytes: 32), CancellationToken.None));
    }

    private async Task<(ConversationRepository Conversations, IConversationProductionRepository Production, IMessageAttachmentService Service)> CreateServicesAsync()
    {
        var database = new SqliteDatabase(_paths);
        await new ConversationProductionDatabase(database).InitializeAsync(CancellationToken.None);
        var conversations = new ConversationRepository(database);
        var innerProduction = new ConversationProductionRepository(database, conversations);
        var production = new SafeConversationProductionRepository(database, conversations, innerProduction);
        var innerAttachments = new MessageAttachmentService(_paths, production, new NoTools());
        var service = new SafeMessageAttachmentService(innerAttachments, _paths);
        return (conversations, production, service);
    }

    private static Conversation ConversationAt(DateTimeOffset now) => new(
        Guid.NewGuid(), HavenMode.Chat, ConversationKind.Chat, "Attachments", null, null, false, false, now, now);

    public void Dispose() => _paths.Dispose();

    private sealed class NoTools : ILocalMediaToolLocator
    {
        public string? FindExecutable(string name) => null;
    }

    private sealed class TestPaths : IAppPaths, IDisposable
    {
        public TestPaths()
        {
            DataDirectory = Path.Combine(Path.GetTempPath(), "haven-attachment-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DataDirectory);
            DatabasePath = Path.Combine(DataDirectory, "test.db");
            BrowserProfileDirectory = Path.Combine(DataDirectory, "browser");
            AttachmentsDirectory = Path.Combine(DataDirectory, "attachments");
            LogsDirectory = Path.Combine(DataDirectory, "logs");
            LegacyStatePath = Path.Combine(DataDirectory, "missing.json");
        }

        public string DataDirectory { get; }
        public string DatabasePath { get; }
        public string BrowserProfileDirectory { get; }
        public string AttachmentsDirectory { get; }
        public string LogsDirectory { get; }
        public string LegacyStatePath { get; }
        public void Dispose() { try { Directory.Delete(DataDirectory, true); } catch (IOException) { } }
    }
}
