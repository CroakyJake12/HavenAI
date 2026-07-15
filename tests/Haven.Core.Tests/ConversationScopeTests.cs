using Haven.Core;

namespace Haven.Core.Tests;

public sealed class ConversationScopeTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-07-15T12:00:00Z");

    [Fact]
    public void ScopesMatchOnlyTheirExactConversationHistory()
    {
        var groupId = Guid.NewGuid();
        var subjectId = Guid.NewGuid();
        var lessonId = Guid.NewGuid();
        var general = Conversation(HavenMode.Chat, ConversationKind.Chat, null, null);
        var grouped = Conversation(HavenMode.Chat, ConversationKind.Chat, groupId, null);
        var quick = Conversation(HavenMode.Teach, ConversationKind.QuickChat, null, null);
        var lesson = Conversation(HavenMode.Teach, ConversationKind.LessonChat, subjectId, lessonId);
        var call = Conversation(HavenMode.Chat, ConversationKind.Call, null, null);

        Assert.True(ConversationScope.GeneralChat.Matches(general));
        Assert.False(ConversationScope.GeneralChat.Matches(grouped));
        Assert.False(ConversationScope.GeneralChat.Matches(call));
        Assert.True(ConversationScope.ForChatGroup(groupId).Matches(grouped));
        Assert.False(ConversationScope.ForChatGroup(groupId).Matches(general));
        Assert.True(ConversationScope.TeachQuickChat.Matches(quick));
        Assert.False(ConversationScope.TeachQuickChat.Matches(lesson));
        Assert.True(ConversationScope.ForTeachLesson(subjectId, lessonId).Matches(lesson));
        Assert.False(ConversationScope.ForTeachLesson(subjectId, Guid.NewGuid()).Matches(lesson));
    }

    [Fact]
    public void ScopeFactoryPreservesChatAndTeachBoundaries()
    {
        var groupId = Guid.NewGuid();
        var subjectId = Guid.NewGuid();
        var lessonId = Guid.NewGuid();
        Assert.Equal(ConversationScopeKind.GeneralChat, ConversationScope.From(Conversation(HavenMode.Chat, ConversationKind.Chat, null, null)).Kind);
        Assert.Equal(groupId, ConversationScope.From(Conversation(HavenMode.Chat, ConversationKind.Chat, groupId, null)).ContainerId);
        Assert.Equal(ConversationScopeKind.TeachQuickChat, ConversationScope.From(Conversation(HavenMode.Teach, ConversationKind.QuickChat, null, null)).Kind);
        Assert.Equal(lessonId, ConversationScope.From(Conversation(HavenMode.Teach, ConversationKind.LessonChat, subjectId, lessonId)).LessonId);
        Assert.Throws<ArgumentException>(() => ConversationScope.ForChatGroup(Guid.Empty));
    }

    private static Conversation Conversation(HavenMode mode, ConversationKind kind, Guid? containerId, Guid? lessonId) =>
        new(Guid.NewGuid(), mode, kind, "Test", containerId, lessonId, false, false, Now, Now);
}
