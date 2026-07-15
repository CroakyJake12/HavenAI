namespace Haven.Core;

public enum HavenMode { Chat = 0, Teach = 1, Do = 2, Studio = 3 }
public enum DuoMode { Solo, PingPong, Collaborate, Supervise }
public enum MessageRole { System, User, Assistant, Tool }
public enum ConversationKind { Chat = 0, QuickChat = 1, LessonChat = 2, Task = 3, StudioChat = 4, AutomationRun = 5, Training = 6, Call = 7 }
public enum ConversationScopeKind { GeneralChat = 0, ChatGroup = 1, TeachQuickChat = 2, TeachLesson = 3 }
public enum ContainerResourceKind { Text = 0, Document = 1, Image = 2 }
public enum EffortLevel { Low, Medium, High, Max }
public enum PermissionMode { Ask, AutoSafe, FullAccess }
public enum AutomationScheduleKind { Once, Hourly, Daily, Weekly, ConditionWatch }
public enum AutomationRunStatus { Pending, Running, Succeeded, Failed, Cancelled, SkippedDuplicate }
public enum ToolCapability
{
    Text = 0,
    Vision = 1,
    Tools = 2,
    Browser = 3,
    ComputerUse = 4,
    WebSearch = 5,
    Embeddings = 6,
    Streaming = 7,
    StructuredOutput = 8,
    Reranking = 9,
    PromptCaching = 10,
    UsageReporting = 11,
    AudioInput = 12,
    AudioOutput = 13
}
public enum ContextEntryKind { Registered, CompactSummary, Decision, ErrorPattern, HandoffEvidence }
public enum WorkspaceVersionKind { Edit, Undo, Redo, Rollback, Rollforward }
public enum BrowserTabPrivacy { Standard, Private }

public enum PlannerPriority { None = 0, Low = 1, Medium = 2, High = 3, Urgent = 4 }
public enum PlannerTaskStatus { Inbox = 0, Planned = 1, InProgress = 2, Completed = 3, Cancelled = 4 }
public enum PlannerViewKind { Today = 0, Inbox = 1, Upcoming = 2, List = 3, Board = 4, Day = 5, Week = 6, Month = 7, Agenda = 8 }
public enum CalendarProviderKind { Local = 0, Google = 1, Microsoft = 2 }
public enum CalendarPermission { Owner = 0, Writer = 1, Reader = 2 }
public enum CalendarSyncStatus { NotConfigured = 0, Disconnected = 1, Ready = 2, Syncing = 3, Offline = 4, Error = 5 }
public enum PlannerChangeKind { CreateTask = 0, UpdateTask = 1, CompleteTask = 2, DeleteTask = 3, CreateEvent = 4, UpdateEvent = 5, DeleteEvent = 6 }
public enum CalendarConflictResolution { KeepHaven = 0, KeepProvider = 1, Duplicate = 2 }
public enum PlannerReminderKind { Task = 0, Event = 1 }
public enum ModeSource { BuiltIn = 0, Community = 1, Created = 2 }
public enum ModeInstallState { BuiltIn = 0, Installed = 1, Pinned = 2, InstalledByUser = 3 }
public enum ConversationPlacement { Auto = 0, Dock = 1, Floating = 2, Background = 3 }
public enum SurfaceKind { Chat = 0, Do = 1, Teach = 2, Studio = 3, Browse = 4, Plan = 5, Phone = 6, Dashboard = 7, Training = 8 }
public enum IntentClassification { DirectTool = 0, ModeSwitch = 1, Clarify = 2, Compose = 3, Inspect = 4 }
public enum ActivityEventKind { ToolRun = 0, Turn = 1, BrowserCompletion = 2, FilesystemAction = 3, CommandRun = 4, ModeSwitch = 5, ConversationMove = 6, PermissionChange = 7 }
