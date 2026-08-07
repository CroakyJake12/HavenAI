namespace Haven.Core;

/// <summary>
/// Surface kind values.
/// </summary>
public enum SurfaceKind
{
    Chat = 0,
    Tasks = 1,
    Study = 2,
    Studio = 3,
    Browse = 4,
    Plan = 5,
    Phone = 6,
    Dashboard = 7,
    Training = 8,
    Go = 9,
    Imagine = 10,
    Present = 11,
    Data = 12,
    Vision = 13,
    Play = 14,
    Translate = 15,
    Launcher = 16,

    // Persistence aliases for runs recorded before the product rename.
    Do = Tasks,
    Teach = Study
}
/// <summary>
/// Intent classification values.
/// </summary>
public enum IntentClassification { DirectTool = 0, ModeSwitch = 1, Clarify = 2, Compose = 3, Inspect = 4 }
/// <summary>
/// Activity event kind values.
/// </summary>
public enum ActivityEventKind { ToolRun = 0, Turn = 1, BrowserCompletion = 2, FilesystemAction = 3, CommandRun = 4, ModeSwitch = 5, ConversationMove = 6, PermissionChange = 7 }
