using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

public sealed class AdvancedCodeIntelligenceService(
    IWorkspaceToolService workspaceTools,
    IWorkspaceTransactionService transactions,
    ILanguageServerConfigurationStore configurations,
    ILanguageServerClientFactory languageServers) : IAdvancedCodeIntelligenceService
{
    public async Task<LanguageServerCapabilities> GetCapabilitiesAsync(string workspaceRoot, string relativePath, CancellationToken cancellationToken)
    {
        var (root, path) = Resolve(workspaceRoot, relativePath);
        var text = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        return await WithServerAsync(root, path, text, static (server, _, _) => Task.FromResult(server.AdvancedCapabilities), cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CodeLocation>> GetDefinitionAsync(string workspaceRoot, string relativePath, string documentText, CodePosition position, CancellationToken cancellationToken)
    {
        var (root, path) = Resolve(workspaceRoot, relativePath);
        return await WithServerAsync(root, path, documentText, (server, uri, token) => server.GetDefinitionAsync(uri, position, root, token), cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CodeLocation>> FindReferencesAsync(string workspaceRoot, string relativePath, string documentText, CodePosition position, CancellationToken cancellationToken)
    {
        var (root, path) = Resolve(workspaceRoot, relativePath);
        return await WithServerAsync(root, path, documentText, (server, uri, token) => server.FindReferencesAsync(uri, position, root, true, token), cancellationToken).ConfigureAwait(false);
    }

    public async Task<CodeWorkspaceMutationResult> RenameSymbolAsync(string workspaceRoot, string relativePath, string documentText, CodePosition position, string newName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);
        var trimmed = newName.Trim();
        if (trimmed.Length > 256 || trimmed.Any(char.IsControl)) throw new ArgumentException("The new symbol name is invalid.", nameof(newName));
        var (root, path) = Resolve(workspaceRoot, relativePath);
        await RequireSavedDocumentAsync(path, documentText, cancellationToken).ConfigureAwait(false);
        var workspaceEdit = await WithServerAsync(root, path, documentText, (server, uri, token) => server.RenameSymbolAsync(uri, position, trimmed, root, token), cancellationToken).ConfigureAwait(false);
        var prepared = await PrepareWorkspaceEditAsync(root, workspaceEdit, cancellationToken).ConfigureAwait(false);
        return await ApplyPreparedAsync(root, $"Rename symbol to {trimmed}", prepared, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<LanguageServerCompletion>> GetCompletionsAsync(string workspaceRoot, string relativePath, string documentText, CodePosition position, CancellationToken cancellationToken)
    {
        var (root, path) = Resolve(workspaceRoot, relativePath);
        return await WithServerAsync(root, path, documentText, (server, uri, token) => server.GetCompletionsAsync(uri, position, token), cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CodeActionProposal>> GetCodeActionsAsync(string workspaceRoot, string relativePath, string documentText, CodeRange range, CancellationToken cancellationToken)
    {
        var (root, path) = Resolve(workspaceRoot, relativePath);
        await RequireSavedDocumentAsync(path, documentText, cancellationToken).ConfigureAwait(false);
        var actions = await WithServerAsync(root, path, documentText, (server, uri, token) => server.GetCodeActionsAsync(uri, range, root, token), cancellationToken).ConfigureAwait(false);
        var proposals = new List<CodeActionProposal>(actions.Count);
        foreach (var action in actions)
        {
            if (action.Edit is null || action.Edit.Documents.Count == 0)
            {
                proposals.Add(new CodeActionProposal(Guid.NewGuid(), action.Title, action.Kind, action.IsPreferred, [], action.ServerCommand is null ? "The language server did not provide an applicable edit." : $"This action requires server command '{action.ServerCommand}', which Haven does not execute outside the safe workspace transaction path."));
                continue;
            }
            try
            {
                var prepared = await PrepareWorkspaceEditAsync(root, action.Edit, cancellationToken).ConfigureAwait(false);
                proposals.Add(new CodeActionProposal(Guid.NewGuid(), action.Title, action.Kind, action.IsPreferred, prepared, prepared.Count == 0 ? "The action produced no file changes." : null));
            }
            catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                proposals.Add(new CodeActionProposal(Guid.NewGuid(), action.Title, action.Kind, action.IsPreferred, [], exception.Message));
            }
        }
        return proposals;
    }

    public Task<CodeWorkspaceMutationResult> ApplyCodeActionAsync(string workspaceRoot, CodeActionProposal action, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (!action.IsApplicable) throw new NotSupportedException(action.UnavailableReason ?? "The selected code action is not safely applicable.");
        var root = ResolveRoot(workspaceRoot);
        return ApplyPreparedAsync(root, action.Title, action.Files, cancellationToken);
    }

    public async Task<IReadOnlyList<CodeSemanticToken>> GetSemanticTokensAsync(string workspaceRoot, string relativePath, string documentText, CancellationToken cancellationToken)
    {
        var (root, path) = Resolve(workspaceRoot, relativePath);
        return await WithServerAsync(root, path, documentText, (server, uri, token) => server.GetSemanticTokensAsync(uri, token), cancellationToken).ConfigureAwait(false);
    }

    private async Task<T> WithServerAsync<T>(string root, string path, string documentText, Func<IAdvancedLanguageServerClient, string, CancellationToken, Task<T>> action, CancellationToken cancellationToken)
    {
        if (RuntimeSafetyState.IsSafeMode) throw new InvalidOperationException($"Advanced language-server features are disabled in crash-loop recovery safe mode. {RuntimeSafetyState.Reason}");
        var definition = await configurations.FindForPathAsync(path, cancellationToken).ConfigureAwait(false) ?? throw new NotSupportedException("No enabled language server is configured for this file type.");
        if (!ExecutableLocator.IsAvailable(definition.Command)) throw new InvalidOperationException($"{definition.DisplayName} is configured, but '{definition.Command}' is unavailable.");
        await using var server = await languageServers.StartAsync(definition, root, cancellationToken).ConfigureAwait(false);
        if (server is not IAdvancedLanguageServerClient advanced) throw new NotSupportedException("The configured language-server client does not expose Haven's advanced protocol features.");
        var uri = StdioLanguageServerClient.ToDocumentUri(path);
        await server.OpenDocumentAsync(uri, definition.LanguageId, documentText, 1, cancellationToken).ConfigureAwait(false);
        return await action(advanced, uri, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<CodeFileMutation>> PrepareWorkspaceEditAsync(string root, LanguageServerWorkspaceEdit workspaceEdit, CancellationToken cancellationToken)
    {
        var output = new List<CodeFileMutation>();
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var seen = new HashSet<string>(comparer);
        foreach (var document in workspaceEdit.Documents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!seen.Add(document.RelativePath)) throw new InvalidOperationException($"The language server returned duplicate edits for {document.RelativePath}.");
            var path = workspaceTools.ResolveWorkspacePath(root, document.RelativePath);
            if (!File.Exists(path)) throw new InvalidOperationException($"The language server attempted to edit missing file {document.RelativePath}.");
            var before = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            var after = LanguageServerTextEditApplicator.Apply(before, document.Edits);
            if (!string.Equals(before, after, StringComparison.Ordinal)) output.Add(new CodeFileMutation(document.RelativePath, before, after));
        }
        return output;
    }

    private async Task<CodeWorkspaceMutationResult> ApplyPreparedAsync(string root, string summary, IReadOnlyList<CodeFileMutation> files, CancellationToken cancellationToken)
    {
        if (files.Count == 0) throw new InvalidOperationException("The language server produced no file changes to apply.");
        foreach (var file in files)
        {
            var path = workspaceTools.ResolveWorkspacePath(root, file.RelativePath);
            var current = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(current, file.BeforeContent, StringComparison.Ordinal)) throw new InvalidOperationException($"{file.RelativePath} changed after the language-server edit was prepared. Refresh and try again.");
        }
        var transaction = await transactions.ApplyAsync(root, files.Select(file => new WorkspaceFileMutation(file.RelativePath, file.AfterContent)).ToArray(), cancellationToken).ConfigureAwait(false);
        return new CodeWorkspaceMutationResult(transaction.TransactionId, summary, files);
    }

    private async Task RequireSavedDocumentAsync(string path, string documentText, CancellationToken cancellationToken)
    {
        var disk = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(disk, documentText, StringComparison.Ordinal)) throw new InvalidOperationException("Save this file before applying a multi-file language-server edit so ranges are validated against the current project state.");
    }

    private (string Root, string Path) Resolve(string workspaceRoot, string relativePath)
    {
        var root = ResolveRoot(workspaceRoot);
        var path = workspaceTools.ResolveWorkspacePath(root, relativePath);
        if (!File.Exists(path)) throw new FileNotFoundException("The requested source file does not exist.", path);
        return (root, path);
    }

    private static string ResolveRoot(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        var root = Path.GetFullPath(workspaceRoot.Trim());
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
        return root;
    }
}
