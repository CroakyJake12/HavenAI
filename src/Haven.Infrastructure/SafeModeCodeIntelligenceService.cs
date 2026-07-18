/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Infrastructure/SafeModeCodeIntelligenceService.cs, in the Infrastructure layer, where persistence, providers, Windows integration, and external I/O are implemented.
 * What: This file owns SafeModeCodeIntelligenceService. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Platform and persistence details are contained here so higher layers do not acquire external-system coupling.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

/// <summary>
/// Represents safe mode code intelligence service and keeps its related state and behavior together.
/// </summary>
public sealed class SafeModeCodeIntelligenceService(
    ProductionCodeIntelligenceService inner,
    IProductionDiagnostics diagnostics) : ICodeIntelligenceService
{
    /// <summary>
    /// Retrieves status async for the current operation.
    /// </summary>
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

    /// <summary>
    /// Retrieves diagnostics async for the current operation.
    /// </summary>
    public Task<IReadOnlyList<CodeDiagnostic>> GetDiagnosticsAsync(
        string workspaceRoot,
        string relativePath,
        CancellationToken cancellationToken) => RuntimeSafetyState.IsSafeMode
        ? DeniedAsync<IReadOnlyList<CodeDiagnostic>>("diagnostics", cancellationToken)
        : inner.GetDiagnosticsAsync(workspaceRoot, relativePath, cancellationToken);

    /// <summary>
    /// Performs search symbols async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task<IReadOnlyList<CodeSymbol>> SearchSymbolsAsync(
        string workspaceRoot,
        string query,
        CancellationToken cancellationToken) => RuntimeSafetyState.IsSafeMode
        ? DeniedAsync<IReadOnlyList<CodeSymbol>>("symbol search", cancellationToken)
        : inner.SearchSymbolsAsync(workspaceRoot, query, cancellationToken);

    /// <summary>
    /// Performs preview format async asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public Task<CodeFormatPreview> PreviewFormatAsync(
        string workspaceRoot,
        string relativePath,
        int tabSize,
        bool insertSpaces,
        CancellationToken cancellationToken) => RuntimeSafetyState.IsSafeMode
        ? DeniedAsync<CodeFormatPreview>("format preview", cancellationToken)
        : inner.PreviewFormatAsync(workspaceRoot, relativePath, tabSize, insertSpaces, cancellationToken);

    /// <summary>
    /// Performs apply format async asynchronously so I/O does not block the caller's thread.
    /// </summary>
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
