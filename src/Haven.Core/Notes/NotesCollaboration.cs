namespace Haven.Core;

/// <summary>
/// Collaboration state for a document.
/// </summary>
public sealed class NotesCollaborationState
{
    /// <summary>
    /// Gets or sets the owner id.
    /// </summary>
    public string OwnerId { get; set; } = Environment.UserName;
    /// <summary>
    /// Gets or sets the collaborators.
    /// </summary>
    public List<NotesCollaborator> Collaborators { get; set; } = [];
    /// <summary>
    /// Gets or sets the sync revision.
    /// </summary>
    public string SyncRevision { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the remote ETag.
    /// </summary>
    public string RemoteEtag { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the conflict state.
    /// </summary>
    public NotesConflictState ConflictState { get; set; }
    /// <summary>
    /// Gets or sets the last synced timestamp.
    /// </summary>
    public DateTimeOffset? LastSyncedAt { get; set; }
    /// <summary>
    /// Gets or sets the conflicts.
    /// </summary>
    public List<NotesConflict> Conflicts { get; set; } = [];
}

/// <summary>
/// A collaborator on a document.
/// </summary>
public sealed class NotesCollaborator
{
    /// <summary>
    /// Gets or sets the collaborator id.
    /// </summary>
    public string Id { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the display name.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the role.
    /// </summary>
    public string Role { get; set; } = "Viewer";
    /// <summary>
    /// Gets or sets the last seen timestamp.
    /// </summary>
    public DateTimeOffset? LastSeenAt { get; set; }
}

/// <summary>
/// A merge conflict in a document.
/// </summary>
public sealed class NotesConflict
{
    /// <summary>
    /// Gets or sets the conflict id.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>
    /// Gets or sets the target block id.
    /// </summary>
    public Guid? BlockId { get; set; }
    /// <summary>
    /// Gets or sets the local value.
    /// </summary>
    public string LocalValue { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the remote value.
    /// </summary>
    public string RemoteValue { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the resolution.
    /// </summary>
    public string Resolution { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the detection timestamp.
    /// </summary>
    public DateTimeOffset DetectedAt { get; set; } = DateTimeOffset.UtcNow;
    /// <summary>
    /// Gets or sets the resolved timestamp.
    /// </summary>
    public DateTimeOffset? ResolvedAt { get; set; }
}

/// <summary>
/// Recovery state for a document.
/// </summary>
public sealed class NotesRecoveryState
{
    /// <summary>
    /// Gets or sets the last autosave timestamp.
    /// </summary>
    public DateTimeOffset? LastAutosaveAt { get; set; }
    /// <summary>
    /// Gets or sets the last backup timestamp.
    /// </summary>
    public DateTimeOffset? LastBackupAt { get; set; }
    /// <summary>
    /// Gets or sets the last recovery timestamp.
    /// </summary>
    public DateTimeOffset? LastRecoveredAt { get; set; }
    /// <summary>
    /// Gets or sets the last valid SHA-256 hash.
    /// </summary>
    public string LastValidSha256 { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets whether unsaved recovery exists.
    /// </summary>
    public bool HasUnsavedRecovery { get; set; }
    /// <summary>
    /// Gets or sets the recovery reason.
    /// </summary>
    public string RecoveryReason { get; set; } = string.Empty;
}
