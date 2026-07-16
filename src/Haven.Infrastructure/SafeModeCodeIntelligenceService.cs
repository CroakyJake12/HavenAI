using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

public sealed class SafeModeCodeIntelligenceService(
    ProductionCodeIntelligenceService inner,
    IProductionDiagnostics diagnostics) : ICodeIntelligenceService
{
    public async Task<CodeIntelligenceStatus> GetStatusAsync(
        string workspaceRoot,
        string relativePath,
        CancellationToken cancellationToken)
    {
        var status = await inner.GetStatusAsync(workspaceRoot, relativePath, cancellationToken).ConfigureAwait(false);
        return !RuntimeSafetyState.IsSafeMode
            ? status
            : status with
            {
                HasCliDiagnosticsFallback = false,
                CanFormat = false,
                Message = "Code intelligence processes and formatting are disabled in crash-loop recovery safe mode. " + RuntimeSafetyState.Reason
            };
    }

    public Task<IReadOnlyList<CodeDiagnostic>> GetDiagnosticsAsync(
        string workspaceRoot,
        string relativePath,
        CancellationToken cancellationToken) => RuntimeSafetyState.IsSafeMode
        ? DeniedAsync<IReadOnlyList<CodeDiagnostic>>("diagnostics", cancellationToken)
        : inner.GetDiagnosticsAsync(workspaceRoot, relativePath, cancellationToken);

    public Task<IReadOnlyList<CodeSymbol>> SearchSymbolsAsync(
        string workspaceRoot,
        string query,
        CancellationToken cancellationToken) => RuntimeSafetyState.IsSafeMode
        ? DeniedAsync<IReadOnlyList<CodeSymbol>>("symbol search", cancellationToken)
        : inner.SearchSymbolsAsync(workspaceRoot, query, cancellationToken);

    public Task<CodeFormatPreview> PreviewFormatAsync(
        string workspaceRoot,
        string relativePath,
        int tabSize,
        bool insertSpaces,
        CancellationToken cancellationToken) => RuntimeSafetyState.IsSafeMode
        ? DeniedAsync<CodeFormatPreview>("format preview", cancellationToken)
        : inner.PreviewFormatAsync(workspaceRoot, relativePath, tabSize, insertSpaces, cancellationToken);

    public Task<CodeFormatApplyResult> ApplyFormatAsync(
        string workspaceRoot,
        CodeFormatPreview preview,
        CancellationToken cancellationToken) => RuntimeSafetyState.IsSafeMode
        ? DeniedAsync<CodeFormatApplyResult>("format apply", cancellationToken)
        : inner.ApplyFormatAsync(workspaceRoot, preview, cancellationToken);

    private async Task<T> DeniedAsync<T>(string operation, CancellationToken cancellationToken)
    {
        await diagnostics.WriteAsync(
            ReliabilitySeverity.Warning,
            "safe-mode",
            "code-intelligence-blocked",
            $"Code intelligence {operation} was blocked by crash-loop recovery safe mode.",
            new Dictionary<string, string> { ["operation"] = operation },
            cancellationToken: cancellationToken).ConfigureAwait(false);
        throw new InvalidOperationException($"Code intelligence {operation} is disabled in crash-loop recovery safe mode. {RuntimeSafetyState.Reason}");
    }
}
