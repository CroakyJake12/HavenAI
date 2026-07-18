/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Core/Enums.cs, in the dependency-free Core layer, where shared domain models and rules live.
 * What: This file owns HavenMode, DuoMode, MessageRole, ConversationKind, ConversationScopeKind, ContainerResourceKind, EffortLevel, PermissionMode, AutomationScheduleKind, AutomationRunStatus, ToolCapability, ContextEntryKind, WorkspaceVersionKind, BrowserTabPrivacy, PlannerPriority, PlannerTaskStatus, PlannerViewKind, CalendarProviderKind, CalendarPermission, CalendarSyncStatus, PlannerChangeKind, CalendarConflictResolution, PlannerReminderKind, ModeSource, ModeInstallState, ConversationPlacement, SurfaceKind, IntentClassification, ActivityEventKind. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: This code stays free of UI and storage dependencies so the same rule or data shape can be reused and tested everywhere.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

namespace Haven.Core;

/// <summary>
/// Lists the supported haven mode values used to make state explicit and type-safe.
/// </summary>
public enum HavenMode { Chat = 0, Teach = 1, Do = 2, Studio = 3 }
/// <summary>
/// Lists the supported duo mode values used to make state explicit and type-safe.
/// </summary>
public enum DuoMode { Solo, PingPong, Collaborate, Supervise }
/// <summary>
/// Lists the supported message role values used to make state explicit and type-safe.
/// </summary>
public enum MessageRole { System, User, Assistant, Tool }
/// <summary>
/// Lists the supported conversation kind values used to make state explicit and type-safe.
/// </summary>
public enum ConversationKind { Chat = 0, QuickChat = 1, LessonChat = 2, Task = 3, StudioChat = 4, AutomationRun = 5, Training = 6, Call = 7 }
/// <summary>
/// Lists the supported conversation scope kind values used to make state explicit and type-safe.
/// </summary>
public enum ConversationScopeKind { GeneralChat = 0, ChatGroup = 1, TeachQuickChat = 2, TeachLesson = 3 }
/// <summary>
/// Lists the supported container resource kind values used to make state explicit and type-safe.
/// </summary>
public enum ContainerResourceKind { Text = 0, Document = 1, Image = 2 }
/// <summary>
/// Lists the supported effort level values used to make state explicit and type-safe.
/// </summary>
public enum EffortLevel { Low, Medium, High, Max }
/// <summary>
/// Lists the supported permission mode values used to make state explicit and type-safe.
/// </summary>
public enum PermissionMode { Ask, AutoSafe, FullAccess }
/// <summary>
/// Lists the supported automation schedule kind values used to make state explicit and type-safe.
/// </summary>
public enum AutomationScheduleKind { Once, Hourly, Daily, Weekly, ConditionWatch }
/// <summary>
/// Lists the supported automation run status values used to make state explicit and type-safe.
/// </summary>
public enum AutomationRunStatus { Pending, Running, Succeeded, Failed, Cancelled, SkippedDuplicate }
/// <summary>
/// Lists the supported tool capability values used to make state explicit and type-safe.
/// </summary>
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
/// <summary>
/// Lists the supported context entry kind values used to make state explicit and type-safe.
/// </summary>
public enum ContextEntryKind { Registered, CompactSummary, Decision, ErrorPattern, HandoffEvidence }
/// <summary>
/// Lists the supported workspace version kind values used to make state explicit and type-safe.
/// </summary>
public enum WorkspaceVersionKind { Edit, Undo, Redo, Rollback, Rollforward }
/// <summary>
/// Lists the supported browser tab privacy values used to make state explicit and type-safe.
/// </summary>
public enum BrowserTabPrivacy { Standard, Private }

/// <summary>
/// Lists the supported planner priority values used to make state explicit and type-safe.
/// </summary>
public enum PlannerPriority { None = 0, Low = 1, Medium = 2, High = 3, Urgent = 4 }
/// <summary>
/// Lists the supported planner task status values used to make state explicit and type-safe.
/// </summary>
public enum PlannerTaskStatus { Inbox = 0, Planned = 1, InProgress = 2, Completed = 3, Cancelled = 4 }
/// <summary>
/// Lists the supported planner view kind values used to make state explicit and type-safe.
/// </summary>
public enum PlannerViewKind { Today = 0, Inbox = 1, Upcoming = 2, List = 3, Board = 4, Day = 5, Week = 6, Month = 7, Agenda = 8 }
/// <summary>
/// Lists the supported calendar provider kind values used to make state explicit and type-safe.
/// </summary>
public enum CalendarProviderKind { Local = 0, Google = 1, Microsoft = 2 }
/// <summary>
/// Lists the supported calendar permission values used to make state explicit and type-safe.
/// </summary>
public enum CalendarPermission { Owner = 0, Writer = 1, Reader = 2 }
/// <summary>
/// Lists the supported calendar sync status values used to make state explicit and type-safe.
/// </summary>
public enum CalendarSyncStatus { NotConfigured = 0, Disconnected = 1, Ready = 2, Syncing = 3, Offline = 4, Error = 5 }
/// <summary>
/// Lists the supported planner change kind values used to make state explicit and type-safe.
/// </summary>
public enum PlannerChangeKind { CreateTask = 0, UpdateTask = 1, CompleteTask = 2, DeleteTask = 3, CreateEvent = 4, UpdateEvent = 5, DeleteEvent = 6 }
/// <summary>
/// Lists the supported calendar conflict resolution values used to make state explicit and type-safe.
/// </summary>
public enum CalendarConflictResolution { KeepHaven = 0, KeepProvider = 1, Duplicate = 2 }
/// <summary>
/// Lists the supported planner reminder kind values used to make state explicit and type-safe.
/// </summary>
public enum PlannerReminderKind { Task = 0, Event = 1 }
/// <summary>
/// Lists the supported mode source values used to make state explicit and type-safe.
/// </summary>
public enum ModeSource { BuiltIn = 0, Community = 1, Created = 2 }
/// <summary>
/// Lists the supported mode install state values used to make state explicit and type-safe.
/// </summary>
public enum ModeInstallState { BuiltIn = 0, Installed = 1, Pinned = 2, InstalledByUser = 3 }
/// <summary>
/// Lists the supported conversation placement values used to make state explicit and type-safe.
/// </summary>
public enum ConversationPlacement { Auto = 0, Dock = 1, Floating = 2, Background = 3 }
/// <summary>
/// Lists the supported surface kind values used to make state explicit and type-safe.
/// </summary>
public enum SurfaceKind { Chat = 0, Do = 1, Teach = 2, Studio = 3, Browse = 4, Plan = 5, Phone = 6, Dashboard = 7, Training = 8 }
/// <summary>
/// Lists the supported intent classification values used to make state explicit and type-safe.
/// </summary>
public enum IntentClassification { DirectTool = 0, ModeSwitch = 1, Clarify = 2, Compose = 3, Inspect = 4 }
/// <summary>
/// Lists the supported activity event kind values used to make state explicit and type-safe.
/// </summary>
public enum ActivityEventKind { ToolRun = 0, Turn = 1, BrowserCompletion = 2, FilesystemAction = 3, CommandRun = 4, ModeSwitch = 5, ConversationMove = 6, PermissionChange = 7 }
