using System.Text.Json;
using Haven.Core;

namespace Haven.Application;

public interface ILanguageServerConfigurationStore
{
    Task<IReadOnlyList<LanguageServerDefinition>> GetAllAsync(CancellationToken cancellationToken);
    Task<LanguageServerDefinition?> FindForPathAsync(string path, CancellationToken cancellationToken);
    Task UpsertAsync(LanguageServerDefinition definition, CancellationToken cancellationToken);
    Task DeleteAsync(string id, CancellationToken cancellationToken);
}

public interface ILanguageServerClientFactory
{
    Task<ILanguageServerClient> StartAsync(
        LanguageServerDefinition definition,
        string workspaceRoot,
        CancellationToken cancellationToken);
}

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

public sealed record LanguageServerTextEdit(CodeRange Range, string NewText);

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
