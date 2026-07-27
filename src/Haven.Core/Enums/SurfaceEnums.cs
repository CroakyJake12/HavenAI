namespace Haven.Core;

/// <summary>
/// Surface kind values.
/// </summary>
public enum SurfaceKind { Chat = 0, Go = 1, Do = Go, Teach = 2, Studio = 3, Browse = 4, Plan = 5, Phone = 6, Dashboard = 7, Training = 8 }
/// <summary>
/// Intent classification values.
/// </summary>
public enum IntentClassification { DirectTool = 0, ModeSwitch = 1, Clarify = 2, Compose = 3, Inspect = 4 }
/// <summary>
/// Activity event kind values.
/// </summary>
public enum ActivityEventKind { ToolRun = 0, Turn = 1, BrowserCompletion = 2, FilesystemAction = 3, CommandRun = 4, ModeSwitch = 5, ConversationMove = 6, PermissionChange = 7 }
