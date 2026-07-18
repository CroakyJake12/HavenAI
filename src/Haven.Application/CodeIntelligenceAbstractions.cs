/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Application/CodeIntelligenceAbstractions.cs, in the Application layer, which coordinates use cases through abstractions without owning platform details.
 * What: This file owns ILanguageServerConfigurationStore, ILanguageServerClientFactory, ILanguageServerClient, LanguageServerTextEdit, ICodeIntelligenceService. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The implementation depends on interfaces so policy remains testable and platform-specific details can be replaced.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Text.Json;
using Haven.Core;

namespace Haven.Application;

/// <summary>
/// Defines the i language server configuration store contract so callers depend on a capability rather than one implementation.
/// </summary>
public interface ILanguageServerConfigurationStore
{
    Task<IReadOnlyList<LanguageServerDefinition>> GetAllAsync(CancellationToken cancellationToken);
    Task<LanguageServerDefinition?> FindForPathAsync(string path, CancellationToken cancellationToken);
    Task UpsertAsync(LanguageServerDefinition definition, CancellationToken cancellationToken);
    Task DeleteAsync(string id, CancellationToken cancellationToken);
}

/// <summary>
/// Defines the i language server client factory contract so callers depend on a capability rather than one implementation.
/// </summary>
public interface ILanguageServerClientFactory
{
    Task<ILanguageServerClient> StartAsync(
        LanguageServerDefinition definition,
        string workspaceRoot,
        CancellationToken cancellationToken);
}

/// <summary>
/// Defines the i language server client contract so callers depend on a capability rather than one implementation.
/// </summary>
public interface ILanguageServerClient : IAsyncDisposable
{
    string ServerId { get; }
    Task OpenDocumentAsync(
        string documentUri,
        string languageId,
        string text,
        int version,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<CodeDiagnostic>> GetDiagnosticsAsync(
        string documentUri,
        string workspaceRoot,
        TimeSpan timeout,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<CodeSymbol>> SearchWorkspaceSymbolsAsync(
        string query,
        string workspaceRoot,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<LanguageServerTextEdit>> FormatDocumentAsync(
        string documentUri,
        int tabSize,
        bool insertSpaces,
        CancellationToken cancellationToken);
    Task ShutdownAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Represents language server text edit and keeps its related state and behavior together.
/// </summary>
public sealed record LanguageServerTextEdit(CodeRange Range, string NewText);

/// <summary>
/// Defines the i code intelligence service contract so callers depend on a capability rather than one implementation.
/// </summary>
public interface ICodeIntelligenceService
{
    Task<CodeIntelligenceStatus> GetStatusAsync(
        string workspaceRoot,
        string relativePath,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<CodeDiagnostic>> GetDiagnosticsAsync(
        string workspaceRoot,
        string relativePath,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<CodeSymbol>> SearchSymbolsAsync(
        string workspaceRoot,
        string query,
        CancellationToken cancellationToken);
    Task<CodeFormatPreview> PreviewFormatAsync(
        string workspaceRoot,
        string relativePath,
        int tabSize,
        bool insertSpaces,
        CancellationToken cancellationToken);
    Task<CodeFormatApplyResult> ApplyFormatAsync(
        string workspaceRoot,
        CodeFormatPreview preview,
        CancellationToken cancellationToken);
}
