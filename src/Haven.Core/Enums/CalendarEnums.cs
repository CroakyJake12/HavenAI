namespace Haven.Core;

/// <summary>
/// Calendar provider kind values.
/// </summary>
public enum CalendarProviderKind { Local = 0, Google = 1, Microsoft = 2 }
/// <summary>
/// Calendar permission values.
/// </summary>
public enum CalendarPermission { Owner = 0, Writer = 1, Reader = 2 }
/// <summary>
/// Calendar sync status values.
/// </summary>
public enum CalendarSyncStatus { NotConfigured = 0, Disconnected = 1, Ready = 2, Syncing = 3, Offline = 4, Error = 5 }
/// <summary>
/// Calendar conflict resolution values.
/// </summary>
public enum CalendarConflictResolution { KeepHaven = 0, KeepProvider = 1, Duplicate = 2 }
