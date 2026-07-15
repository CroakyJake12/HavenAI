namespace Haven.Core;

public enum BrowserActionKind
{
    SubmitElement = 0,
    Download = 1
}

public enum BrowserActionState
{
    Pending = 0,
    Approved = 1,
    Rejected = 2,
    Executed = 3,
    Expired = 4,
    Failed = 5
}

public sealed record BrowserNavigationAssessment(
    Uri Address,
    bool IsAllowed,
    string Reason,
    IReadOnlyList<string> ResolvedAddresses);

public sealed record BrowserPageElement(
    string Reference,
    string Kind,
    string Text,
    string? Address,
    string? Name,
    string? InputType,
    bool IsSensitive,
    bool SubmitsForm);

public sealed record BrowserPageSnapshot(
    Uri? Address,
    string Title,
    string Text,
    IReadOnlyList<string> Headings,
    IReadOnlyList<BrowserPageElement> Elements,
    DateTimeOffset CapturedAt,
    bool IsInteractive,
    bool WasTruncated);

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

public sealed record BrowserAuditEntry(
    Guid Id,
    BrowserActionKind? Kind,
    string Operation,
    string Origin,
    string Detail,
    bool Succeeded,
    DateTimeOffset RecordedAt);

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

public sealed record BrowserActionExecutionResult(
    Guid ActionId,
    BrowserActionState State,
    string Message,
    BrowserDownloadRecord? Download = null);
