// Agentic safety: checkpoint policy, checkpoint records and restore plans.

namespace Haven.Core;

/// <summary>When Haven records recoverable checkpoints before agentic file changes.</summary>
public enum CheckpointMode
{
    Off = 0,
    RiskyChangesOnly = 1,
    AgenticTasks = 2,
    BeforeFileChanges = 3,
    Always = 4
}

/// <summary>A named recoverable point recorded before agentic file mutations.</summary>
public sealed record CheckpointInfo(
    Guid Id,
    Guid? ConversationId,
    Guid? ContainerId,
    string WorkspaceRoot,
    string Label,
    CheckpointMode Mode,
    long StartSequence,
    DateTimeOffset CreatedAt);

/// <summary>One recorded file mutation that a restore replays or reverses.</summary>
public sealed record WorkspaceRestoreEntry(
    long Sequence,
    string RelativePath,
    int Kind,
    string BeforeContent,
    string AfterContent);

/// <summary>The concrete plan to bring files back to a checkpoint's state.</summary>
public sealed record CheckpointRestorePlan(
    Guid CheckpointId,
    IReadOnlyDictionary<string, string> PathToBeforeContent)
{
    public bool IsEmpty => PathToBeforeContent.Count == 0;
}
