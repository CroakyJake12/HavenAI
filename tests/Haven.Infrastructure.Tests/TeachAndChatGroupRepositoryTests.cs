using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;
using System.IO.Compression;

namespace Haven.Infrastructure.Tests;

public sealed class TeachAndChatGroupRepositoryTests : IDisposable
{
    private readonly TestPaths _paths = new();

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

    private async Task<SqliteDatabase> CreateDatabaseAsync()
    {
        var database = new SqliteDatabase(_paths);
        await database.InitializeAsync(CancellationToken.None);
        return database;
    }

    private static Conversation NewConversation(HavenMode mode, ConversationKind kind, Guid? containerId, Guid? lessonId, DateTimeOffset now) =>
        new(Guid.NewGuid(), mode, kind, kind.ToString(), containerId, lessonId, false, false, now, now);

    public void Dispose() => _paths.Dispose();

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
        public string DataDirectory { get; }
        public string DatabasePath { get; }
        public string BrowserProfileDirectory { get; }
        public string AttachmentsDirectory { get; }
        public string LogsDirectory { get; }
        public string LegacyStatePath { get; }
        public void Dispose() { try { Directory.Delete(DataDirectory, true); } catch (IOException) { } }
    }
}
