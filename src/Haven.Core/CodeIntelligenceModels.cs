namespace Haven.Core;

public enum CodeDiagnosticSeverity
{
    Error = 1,
    Warning = 2,
    Information = 3,
    Hint = 4
}

public sealed record CodePosition(int Line, int Character);

public sealed record CodeRange(CodePosition Start, CodePosition End);

public sealed record CodeDiagnostic(
    string RelativePath,
    CodeRange Range,
    CodeDiagnosticSeverity Severity,
    string? Code,
    string? Source,
    string Message);

public sealed record CodeSymbol(
    string Name,
    string Kind,
    string RelativePath,
    CodeRange Range,
    string? ContainerName,
    string Source);

public sealed record LanguageServerDefinition(
    string Id,
    string DisplayName,
    string Command,
    string Arguments,
    string LanguageId,
    IReadOnlyList<string> Extensions,
    bool IsEnabled,
    int RequestTimeoutSeconds = 20,
    string InitializationOptionsJson = "{}");

public sealed record LanguageServerHealth(
    string ServerId,
    bool IsConfigured,
    bool IsExecutableAvailable,
    string Message,
    DateTimeOffset CheckedAt);

public sealed record CodeIntelligenceStatus(
    string RelativePath,
    string LanguageId,
    LanguageServerHealth? LanguageServer,
    bool HasCliDiagnosticsFallback,
    bool CanFormat,
    string Message);

public sealed record CodeFormatPreview(
    Guid PreviewId,
    string RelativePath,
    string OriginalContentHash,
    string OriginalContent,
    string FormattedContent,
    string UnifiedDiff,
    string Formatter,
    DateTimeOffset CreatedAt)
{
    public bool HasChanges => !string.Equals(OriginalContent, FormattedContent, StringComparison.Ordinal);
}

public sealed record CodeFormatApplyResult(
    Guid PreviewId,
    Guid? TransactionId,
    string RelativePath,
    bool Changed,
    string Message);
