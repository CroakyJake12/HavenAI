namespace Haven.Core;

/// <summary>
/// Haven mode values.
/// </summary>
public enum HavenMode
{
    Chat = 0,
    Study = 1,
    Tasks = 2,
    Studio = 3,

    // Persistence aliases. Keep these indefinitely so databases and imported
    // manifests written by earlier Haven builds retain their numeric meaning.
    Teach = Study,
    Do = Tasks
}
/// <summary>
/// Duo mode values.
/// </summary>
public enum DuoMode { Solo, PingPong, Collaborate, Supervise }
/// <summary>
/// Message role values.
/// </summary>
public enum MessageRole { System, User, Assistant, Tool }
/// <summary>
/// Conversation kind values.
/// </summary>
public enum ConversationKind { Chat = 0, QuickChat = 1, LessonChat = 2, Task = 3, StudioChat = 4, AutomationRun = 5, Training = 6, Call = 7 }
/// <summary>
/// Conversation scope kind values.
/// </summary>
public enum ConversationScopeKind
{
    GeneralChat = 0,
    ChatGroup = 1,
    StudyQuickChat = 2,
    StudyLesson = 3,

    // Persistence aliases for data and extensions created before Study replaced Teach.
    TeachQuickChat = StudyQuickChat,
    TeachLesson = StudyLesson
}
/// <summary>
/// Conversation placement values.
/// </summary>
public enum ConversationPlacement { Auto = 0, Dock = 1, Floating = 2, Background = 3 }
