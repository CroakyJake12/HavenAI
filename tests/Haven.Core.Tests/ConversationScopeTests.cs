/*
 * FILE DOCUMENTATION
 * Where: tests/Haven.Core.Tests/ConversationScopeTests.cs, in the automated test suite, where executable examples protect behavior against regressions.
 * What: This file owns ConversationScopeTests. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The test is intentionally close to the public behavior it protects, making failures describe a user-visible or architectural contract.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Core;

namespace Haven.Core.Tests;

/// <summary>
/// Represents conversation scope tests and keeps its related state and behavior together.
/// </summary>
public sealed class ConversationScopeTests
{
    /// <summary>
    /// Stores now locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-07-15T12:00:00Z");

    /// <summary>
    /// Performs the scopes match only their exact conversation history step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the scope factory preserves chat and teach boundaries step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the conversation step owned by this component.
    /// </summary>
    private static Conversation Conversation(HavenMode mode, ConversationKind kind, Guid? containerId, Guid? lessonId) =>
        new(Guid.NewGuid(), mode, kind, "Test", containerId, lessonId, false, false, Now, Now);
}
