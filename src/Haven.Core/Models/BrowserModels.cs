// Browser automation actions, page snapshots, audit entries, and downloads.

namespace Haven.Core;

/// <summary>
/// Lists the supported browser action kind values used to make state explicit and type-safe.
/// </summary>
public enum BrowserActionKind
{
    SubmitElement = 0,
    Download = 1
}

/// <summary>
/// Lists the supported browser action state values used to make state explicit and type-safe.
/// </summary>
public enum BrowserActionState
{
    Pending = 0,
    Approved = 1,
    Rejected = 2,
    Executed = 3,
    Expired = 4,
    Failed = 5
}

/// <summary>
/// Represents browser navigation assessment and keeps its related state and behavior together.
/// </summary>
public sealed record BrowserNavigationAssessment(
    Uri Address,
    bool IsAllowed,
    string Reason,
    IReadOnlyList<string> ResolvedAddresses);

/// <summary>
/// Represents browser page element and keeps its related state and behavior together.
/// </summary>
public sealed record BrowserPageElement(
    string Reference,
    string Kind,
    string Text,
    string? Address,
    string? Name,
    string? InputType,
    bool IsSensitive,
    bool SubmitsForm);

/// <summary>
/// Represents browser page snapshot and keeps its related state and behavior together.
/// </summary>
public sealed record BrowserPageSnapshot(
    Uri? Address,
    string Title,
    string Text,
    IReadOnlyList<string> Headings,
    IReadOnlyList<BrowserPageElement> Elements,
    DateTimeOffset CapturedAt,
    bool IsInteractive,
    bool WasTruncated);

/// <summary>
/// Represents browser pending action and keeps its related state and behavior together.
/// </summary>
public sealed record BrowserPendingAction(
    Guid Id,
    BrowserActionKind Kind,
    string Origin,
    string Summary,
    string Target,
    string? SuggestedFileName,
    BrowserActionState State,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset UpdatedAt,
    string? Failure);

/// <summary>
/// Represents browser audit entry and keeps its related state and behavior together.
/// </summary>
public sealed record BrowserAuditEntry(
    Guid Id,
    BrowserActionKind? Kind,
    string Operation,
    string Origin,
    string Detail,
    bool Succeeded,
    DateTimeOffset RecordedAt);

/// <summary>
/// Represents browser download record and keeps its related state and behavior together.
/// </summary>
public sealed record BrowserDownloadRecord(
    Guid Id,
    Guid ActionId,
    string Address,
    string FileName,
    string StoredPath,
    long SizeBytes,
    string Sha256,
    string? ContentType,
    DateTimeOffset CompletedAt);

/// <summary>
/// Represents browser action execution result and keeps its related state and behavior together.
/// </summary>
public sealed record BrowserActionExecutionResult(
    Guid ActionId,
    BrowserActionState State,
    string Message,
    BrowserDownloadRecord? Download = null);
