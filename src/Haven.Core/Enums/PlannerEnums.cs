namespace Haven.Core;

/// <summary>
/// Planner priority values.
/// </summary>
public enum PlannerPriority { None = 0, Low = 1, Medium = 2, High = 3, Urgent = 4 }
/// <summary>
/// Planner task status values.
/// </summary>
public enum PlannerTaskStatus { Inbox = 0, Planned = 1, InProgress = 2, Completed = 3, Cancelled = 4 }
/// <summary>
/// Planner view kind values.
/// </summary>
public enum PlannerViewKind { Today = 0, Inbox = 1, Upcoming = 2, List = 3, Board = 4, Day = 5, Week = 6, Month = 7, Agenda = 8 }
/// <summary>
/// Planner change kind values.
/// </summary>
public enum PlannerChangeKind { CreateTask = 0, UpdateTask = 1, CompleteTask = 2, DeleteTask = 3, CreateEvent = 4, UpdateEvent = 5, DeleteEvent = 6 }
/// <summary>
/// Planner reminder kind values.
/// </summary>
public enum PlannerReminderKind { Task = 0, Event = 1 }
