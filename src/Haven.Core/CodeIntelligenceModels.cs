/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Core/CodeIntelligenceModels.cs, in the dependency-free Core layer, where shared domain models and rules live.
 * What: This file owns CodeDiagnosticSeverity, CodePosition, CodeRange, CodeDiagnostic, CodeSymbol, LanguageServerDefinition, LanguageServerHealth, CodeIntelligenceStatus, CodeFormatPreview, CodeFormatApplyResult. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: This code stays free of UI and storage dependencies so the same rule or data shape can be reused and tested everywhere.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

namespace Haven.Core;

/// <summary>
/// Lists the supported code diagnostic severity values used to make state explicit and type-safe.
/// </summary>
public enum CodeDiagnosticSeverity
{
    Error = 1,
    Warning = 2,
    Information = 3,
    Hint = 4
}

/// <summary>
/// Represents code position and keeps its related state and behavior together.
/// </summary>
public sealed record CodePosition(int Line, int Character);

/// <summary>
/// Represents code range and keeps its related state and behavior together.
/// </summary>
public sealed record CodeRange(CodePosition Start, CodePosition End);

/// <summary>
/// Represents code diagnostic and keeps its related state and behavior together.
/// </summary>
public sealed record CodeDiagnostic(
    string RelativePath,
    CodeRange Range,
    CodeDiagnosticSeverity Severity,
    string? Code,
    string? Source,
    string Message);

/// <summary>
/// Represents code symbol and keeps its related state and behavior together.
/// </summary>
public sealed record CodeSymbol(
    string Name,
    string Kind,
    string RelativePath,
    CodeRange Range,
    string? ContainerName,
    string Source);

/// <summary>
/// Represents language server definition and keeps its related state and behavior together.
/// </summary>
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

/// <summary>
/// Represents language server health and keeps its related state and behavior together.
/// </summary>
public sealed record LanguageServerHealth(
    string ServerId,
    bool IsConfigured,
    bool IsExecutableAvailable,
    string Message,
    DateTimeOffset CheckedAt);

/// <summary>
/// Represents code intelligence status and keeps its related state and behavior together.
/// </summary>
public sealed record CodeIntelligenceStatus(
    string RelativePath,
    string LanguageId,
    LanguageServerHealth? LanguageServer,
    bool HasCliDiagnosticsFallback,
    bool CanFormat,
    string Message);

/// <summary>
/// Represents code format preview and keeps its related state and behavior together.
/// </summary>
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
    /// <summary>
    /// Reports whether changes applies to the current state.
    /// </summary>
    public bool HasChanges => !string.Equals(OriginalContent, FormattedContent, StringComparison.Ordinal);
}

/// <summary>
/// Represents code format apply result and keeps its related state and behavior together.
/// </summary>
public sealed record CodeFormatApplyResult(
    Guid PreviewId,
    Guid? TransactionId,
    string RelativePath,
    bool Changed,
    string Message);
