/*
 * FILE DOCUMENTATION
 * Where: tests/Haven.Infrastructure.Tests/TeachAndChatGroupRepositoryTests.cs, in the automated test suite, where executable examples protect behavior against regressions.
 * What: This file owns TeachAndChatGroupRepositoryTests, TestPaths. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The test is intentionally close to the public behavior it protects, making failures describe a user-visible or architectural contract.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;
using System.IO.Compression;

namespace Haven.Infrastructure.Tests;

/// <summary>
/// Represents teach and chat group repository tests and keeps its related state and behavior together.
/// </summary>
public sealed class TeachAndChatGroupRepositoryTests : IDisposable
{
    /// <summary>
    /// Stores paths locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly TestPaths _paths = new();

    /// <summary>
    /// Performs the scoped queries keep general groups quick chats and lessons separate step owned by this component.
    /// </summary>
    [Fact]
    public async Task ScopedQueriesKeepGeneralGroupsQuickChatsAndLessonsSeparate()
    {
        var database = await CreateDatabaseAsync();
        var conversations = new ConversationRepository(database);
        var containers = new ContainerRepository(database);
        var now = DateTimeOffset.UtcNow;
        var group = new ContainerDefinition(Guid.NewGuid(), HavenMode.Chat, "Group", null, "", "", now, now);
        var subject = new ContainerDefinition(Guid.NewGuid(), HavenMode.Teach, "Maths", null, "", "", now, now);
        await containers.UpsertAsync(group, CancellationToken.None);
        var generalLesson = await containers.CreateSubjectAsync(subject, CancellationToken.None);

        var general = NewConversation(HavenMode.Chat, ConversationKind.Chat, null, null, now.AddMinutes(1));
        var grouped = NewConversation(HavenMode.Chat, ConversationKind.Chat, group.Id, null, now.AddMinutes(2));
        var quick = NewConversation(HavenMode.Teach, ConversationKind.QuickChat, null, null, now.AddMinutes(3));
        var lesson = NewConversation(HavenMode.Teach, ConversationKind.LessonChat, subject.Id, generalLesson.Id, now.AddMinutes(4));
        var call = NewConversation(HavenMode.Chat, ConversationKind.Call, null, null, now.AddMinutes(5));
        foreach (var item in new[] { general, grouped, quick, lesson, call }) await conversations.UpsertConversationAsync(item, CancellationToken.None);

        Assert.Equal(general.Id, Assert.Single(await conversations.GetRecentInScopeAsync(ConversationScope.GeneralChat, 20, CancellationToken.None)).Id);
        Assert.Equal(grouped.Id, Assert.Single(await conversations.GetRecentInScopeAsync(ConversationScope.ForChatGroup(group.Id), 20, CancellationToken.None)).Id);
        Assert.Equal(quick.Id, Assert.Single(await conversations.GetRecentInScopeAsync(ConversationScope.TeachQuickChat, 20, CancellationToken.None)).Id);
        Assert.Equal(lesson.Id, Assert.Single(await conversations.GetRecentInScopeAsync(ConversationScope.ForTeachLesson(subject.Id, generalLesson.Id), 20, CancellationToken.None)).Id);
    }

    /// <summary>
    /// Performs the subject creation is atomic and creates general lesson step owned by this component.
    /// </summary>
    [Fact]
    public async Task SubjectCreationIsAtomicAndCreatesGeneralLesson()
    {
        var database = await CreateDatabaseAsync();
        var containers = new ContainerRepository(database);
        var now = DateTimeOffset.UtcNow;
        var subject = new ContainerDefinition(Guid.NewGuid(), HavenMode.Teach, "Physics", null, "Shared context", "Teach carefully", now, now);

        var lesson = await containers.CreateSubjectAsync(subject, CancellationToken.None);

        Assert.Equal("General", lesson.Name);
        Assert.Equal(subject.Id, lesson.SubjectId);
        Assert.Equal(subject.Id, Assert.Single(await containers.GetByModeAsync(HavenMode.Teach, CancellationToken.None)).Id);
        Assert.Equal(lesson.Id, Assert.Single(await containers.GetLessonsAsync(subject.Id, CancellationToken.None)).Id);
    }

    /// <summary>
    /// Performs the permanent group and lesson deletion detach but preserve conversations step owned by this component.
    /// </summary>
    [Fact]
    public async Task PermanentGroupAndLessonDeletionDetachButPreserveConversations()
    {
        var database = await CreateDatabaseAsync();
        var conversations = new ConversationRepository(database);
        var containers = new ContainerRepository(database, _paths);
        var resources = new ContainerResourceRepository(_paths, database);
        var now = DateTimeOffset.UtcNow;
        var group = new ContainerDefinition(Guid.NewGuid(), HavenMode.Chat, "Group", null, "", "", now, now);
        var subject = new ContainerDefinition(Guid.NewGuid(), HavenMode.Teach, "Subject", null, "", "", now, now);
        await containers.UpsertAsync(group, CancellationToken.None);
        var lesson = await containers.CreateSubjectAsync(subject, CancellationToken.None);
        var groupChat = NewConversation(HavenMode.Chat, ConversationKind.Chat, group.Id, null, now);
        var lessonChat = NewConversation(HavenMode.Teach, ConversationKind.LessonChat, subject.Id, lesson.Id, now);
        await conversations.UpsertConversationAsync(groupChat, CancellationToken.None);
        await conversations.UpsertConversationAsync(lessonChat, CancellationToken.None);
        await conversations.AddMessageAsync(new ChatMessage(Guid.NewGuid(), groupChat.Id, MessageRole.User, "Keep me", null, null, null, now), CancellationToken.None);
        var source = Path.Combine(_paths.DataDirectory, "group-reference.txt");
        await File.WriteAllTextAsync(source, "Preserved until the group is permanently deleted.");
        var resource = await resources.AddAsync(group.Id, source, CancellationToken.None);
        var storedResourcePath = resources.GetStoredPath(resource);

        await containers.DeleteAndDetachConversationsAsync(group.Id, CancellationToken.None);
        await containers.DeleteLessonAsync(lesson.Id, CancellationToken.None);

        var detachedGroup = await conversations.GetAsync(groupChat.Id, CancellationToken.None);
        var detachedLesson = await conversations.GetAsync(lessonChat.Id, CancellationToken.None);
        Assert.Null(detachedGroup?.ContainerId);
        Assert.Equal(ConversationKind.Chat, detachedGroup?.Kind);
        Assert.Equal("Keep me", Assert.Single(await conversations.GetMessagesAsync(groupChat.Id, CancellationToken.None)).Content);
        Assert.False(File.Exists(storedResourcePath));
        Assert.Null(detachedLesson?.ContainerId);
        Assert.Null(detachedLesson?.LessonId);
        Assert.Equal(ConversationKind.QuickChat, detachedLesson?.Kind);
    }

    /// <summary>
    /// Performs the reference files are validated copied deduplicated and indexed step owned by this component.
    /// </summary>
    [Fact]
    public async Task ReferenceFilesAreValidatedCopiedDeduplicatedAndIndexed()
    {
        var database = await CreateDatabaseAsync();
        var containers = new ContainerRepository(database);
        var resources = new ContainerResourceRepository(_paths, database);
        var now = DateTimeOffset.UtcNow;
        var group = new ContainerDefinition(Guid.NewGuid(), HavenMode.Chat, "Research", null, "", "", now, now);
        await containers.UpsertAsync(group, CancellationToken.None);
        var source = Path.Combine(_paths.DataDirectory, "source.md");
        await File.WriteAllTextAsync(source, "# Durable reference\nImportant fact.");

        var first = await resources.AddAsync(group.Id, source, CancellationToken.None);
        var duplicate = await resources.AddAsync(group.Id, source, CancellationToken.None);

        Assert.Equal(first.Id, duplicate.Id);
        Assert.True(File.Exists(resources.GetStoredPath(first)));
        Assert.Single(await resources.GetByContainerAsync(group.Id, CancellationToken.None));
        var docxPath = Path.Combine(_paths.DataDirectory, "notes.docx");
        using (var archive = ZipFile.Open(docxPath, ZipArchiveMode.Create))
        await using (var writer = new StreamWriter(archive.CreateEntry("word/document.xml").Open()))
            await writer.WriteAsync("<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:p><w:r><w:t>Extracted DOCX context</w:t></w:r></w:p></w:body></w:document>");
        var docx = await resources.AddAsync(group.Id, docxPath, CancellationToken.None);
        var promptContext = await resources.BuildPromptContextAsync(group.Id, CancellationToken.None);
        Assert.Contains("Important fact", promptContext);
        Assert.Contains("Extracted DOCX context", promptContext);
        var unsupported = Path.Combine(_paths.DataDirectory, "unsafe.exe");
        await File.WriteAllTextAsync(unsupported, "not executable in this test");
        await Assert.ThrowsAsync<InvalidOperationException>(() => resources.AddAsync(group.Id, unsupported, CancellationToken.None));

        await resources.DeleteAsync(first.Id, CancellationToken.None);
        await resources.DeleteAsync(docx.Id, CancellationToken.None);
        Assert.False(File.Exists(resources.GetStoredPath(first)));
        Assert.Empty(await resources.GetByContainerAsync(group.Id, CancellationToken.None));
    }

    /// <summary>
    /// Creates database async with the invariants required by its callers.
    /// </summary>
    private async Task<SqliteDatabase> CreateDatabaseAsync()
    {
        var database = new SqliteDatabase(_paths);
        await database.InitializeAsync(CancellationToken.None);
        return database;
    }

    /// <summary>
    /// Performs the new conversation step owned by this component.
    /// </summary>
    private static Conversation NewConversation(HavenMode mode, ConversationKind kind, Guid? containerId, Guid? lessonId, DateTimeOffset now) =>
        new(Guid.NewGuid(), mode, kind, kind.ToString(), containerId, lessonId, false, false, now, now);

    /// <summary>
    /// Performs the dispose step owned by this component.
    /// </summary>
    public void Dispose() => _paths.Dispose();

    /// <summary>
    /// Represents test paths and keeps its related state and behavior together.
    /// </summary>
    private sealed class TestPaths : IAppPaths, IDisposable
    {
        public TestPaths()
        {
            DataDirectory = Path.Combine(Path.GetTempPath(), "haven-teach-group-tests-" + Guid.NewGuid().ToString("N"));
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
