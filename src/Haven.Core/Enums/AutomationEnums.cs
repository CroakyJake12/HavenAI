namespace Haven.Core;

/// <summary>
/// Automation schedule kind values.
/// </summary>
public enum AutomationScheduleKind { Once, Hourly, Daily, Weekly, ConditionWatch }
/// <summary>
/// Automation run status values.
/// </summary>
public enum AutomationRunStatus { Pending, Running, Succeeded, Failed, Cancelled, SkippedDuplicate }
