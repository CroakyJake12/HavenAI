namespace Haven.Core;

/// <summary>
/// Represents a macro definition.
/// </summary>
public sealed record ReusableTaskDefinition(
    Guid Id,
    string Name,
    string Description,
    string Instruction,
    Guid? ContainerId,
    bool IsEnabled,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Represents a workspace version.
/// </summary>
public sealed record WorkspaceVersion(
    Guid Id,
    Guid? ConversationId,
    Guid? ContainerId,
    string WorkspaceRoot,
    string RelativePath,
    WorkspaceVersionKind Kind,
    string BeforeContent,
    string AfterContent,
    string Summary,
    int LinesAdded,
    int LinesRemoved,
    DateTimeOffset CreatedAt);

/// <summary>
/// Represents a decision record.
/// </summary>
public sealed record DecisionRecord(
    Guid Id,
    Guid ContainerId,
    string Title,
    string Decision,
    string Alternatives,
    string Reasoning,
    string Evidence,
    string Consequences,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Represents a project state snapshot.
/// </summary>
public sealed record ProjectStateSnapshot(
    string RootPath,
    string Branch,
    bool HasUncommittedWork,
    int Ahead,
    int Behind,
    string LastCommit,
    string LastBuildResult,
    string MostRecentError,
    string RecommendedAction,
    DateTimeOffset CapturedAt);

/// <summary>
/// Represents a release risk report.
/// </summary>
public sealed record ReleaseRiskReport(
    int Score,
    string Level,
    IReadOnlyList<string> ChangedFiles,
    IReadOnlyList<string> RiskAreas,
    IReadOnlyList<string> RecommendedTests,
    IReadOnlyList<string> CriticalFindings);
