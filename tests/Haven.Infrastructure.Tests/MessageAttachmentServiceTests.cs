/*
 * FILE DOCUMENTATION
 * Where: tests/Haven.Infrastructure.Tests/MessageAttachmentServiceTests.cs, in the automated test suite, where executable examples protect behavior against regressions.
 * What: This file owns MessageAttachmentServiceTests, NoTools, TestPaths. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The test is intentionally close to the public behavior it protects, making failures describe a user-visible or architectural contract.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.IO.Compression;
using System.Text;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

/// <summary>
/// Represents message attachment service tests and keeps its related state and behavior together.
/// </summary>
public sealed class MessageAttachmentServiceTests : IDisposable
{
    /// <summary>
    /// Stores paths locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly TestPaths _paths = new();

    /// <summary>
    /// Performs the text and image attachments persist and build prompt context step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the open xml document text is extracted without executing document content step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the video without local media tools reports metadata only rather than inventing analysis step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the size limits are enforced before copying step owned by this component.
    /// </summary>
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
        var service = new SafeMessageAttachmentService(innerAttachments);
        return (conversations, production, service);
    }

    /// <summary>
    /// Performs the conversation at step owned by this component.
    /// </summary>
    private static Conversation ConversationAt(DateTimeOffset now) => new(
        Guid.NewGuid(), HavenMode.Chat, ConversationKind.Chat, "Attachments", null, null, false, false, now, now);

    /// <summary>
    /// Performs the dispose step owned by this component.
    /// </summary>
    public void Dispose() => _paths.Dispose();

    /// <summary>
    /// Represents no tools and keeps its related state and behavior together.
    /// </summary>
    private sealed class NoTools : ILocalMediaToolLocator
    {
        /// <summary>
        /// Performs the find executable step owned by this component.
        /// </summary>
        public string? FindExecutable(string name) => null;
    }

    /// <summary>
    /// Represents test paths and keeps its related state and behavior together.
    /// </summary>
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

        /// <summary>
        /// Gets or updates data directory, the bindable or domain state represented by this property.
        /// </summary>
        public string DataDirectory { get; }
        /// <summary>
        /// Gets or updates database path, the bindable or domain state represented by this property.
        /// </summary>
        public string DatabasePath { get; }
        /// <summary>
        /// Gets or updates browser profile directory, the bindable or domain state represented by this property.
        /// </summary>
        public string BrowserProfileDirectory { get; }
        /// <summary>
        /// Gets or updates attachments directory, the bindable or domain state represented by this property.
        /// </summary>
        public string AttachmentsDirectory { get; }
        /// <summary>
        /// Gets or updates logs directory, the bindable or domain state represented by this property.
        /// </summary>
        public string LogsDirectory { get; }
        /// <summary>
        /// Gets or updates legacy state path, the bindable or domain state represented by this property.
        /// </summary>
        public string LegacyStatePath { get; }
        /// <summary>
        /// Performs the dispose step owned by this component.
        /// </summary>
        public void Dispose() { try { Directory.Delete(DataDirectory, true); } catch (IOException) { } }
    }
}
