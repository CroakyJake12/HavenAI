namespace Haven.Core;

/// <summary>
/// Context entry kind values.
/// </summary>
public enum ContextEntryKind { Registered, CompactSummary, Decision, ErrorPattern, HandoffEvidence }
/// <summary>
/// Workspace version kind values.
/// </summary>
public enum WorkspaceVersionKind { Edit, Undo, Redo, Rollback, Rollforward }
