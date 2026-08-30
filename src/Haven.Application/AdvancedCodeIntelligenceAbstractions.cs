using Haven.Core;

namespace Haven.Application;

public sealed record LanguageServerCapabilities(
    bool Definition,
    bool References,
    bool Rename,
    bool Completion,
    bool CodeActions,
    bool CodeActionResolve,
    bool SemanticTokens,
    IReadOnlyList<string> SemanticTokenTypes,
    IReadOnlyList<string> SemanticTokenModifiers)
{
    public static LanguageServerCapabilities None { get; } = new(false, false, false, false, false, false, false, [], []);
}

public sealed record CodeLocation(string Uri, string DisplayPath, CodeRange Range, bool IsInWorkspace);
public sealed record LanguageServerDocumentEdit(string RelativePath, IReadOnlyList<LanguageServerTextEdit> Edits);
public sealed record LanguageServerWorkspaceEdit(IReadOnlyList<LanguageServerDocumentEdit> Documents);
public sealed record LanguageServerCompletion(
    string Label,
    string? Detail,
    string InsertText,
    CodeRange? InsertRange,
    CodeRange? ReplaceRange,
    IReadOnlyList<LanguageServerTextEdit> AdditionalTextEdits);
public sealed record LanguageServerCodeAction(
    string Title,
    string? Kind,
    bool IsPreferred,
    LanguageServerWorkspaceEdit? Edit,
    string? ServerCommand);
public sealed record CodeSemanticToken(CodeRange Range, string TokenType, IReadOnlyList<string> Modifiers);
public sealed record CodeFileMutation(string RelativePath, string BeforeContent, string AfterContent);
public sealed record CodeWorkspaceMutationResult(Guid TransactionId, string Summary, IReadOnlyList<CodeFileMutation> Files);
public sealed record CodeActionProposal(
    Guid Id,
    string Title,
    string? Kind,
    bool IsPreferred,
    IReadOnlyList<CodeFileMutation> Files,
    string? UnavailableReason)
{
    public bool IsApplicable => Files.Count > 0 && string.IsNullOrWhiteSpace(UnavailableReason);
}

public interface IAdvancedLanguageServerClient
{
    LanguageServerCapabilities AdvancedCapabilities { get; }
    Task<IReadOnlyList<CodeLocation>> GetDefinitionAsync(string documentUri, CodePosition position, string workspaceRoot, CancellationToken cancellationToken);
    Task<IReadOnlyList<CodeLocation>> FindReferencesAsync(string documentUri, CodePosition position, string workspaceRoot, bool includeDeclaration, CancellationToken cancellationToken);
    Task<LanguageServerWorkspaceEdit> RenameSymbolAsync(string documentUri, CodePosition position, string newName, string workspaceRoot, CancellationToken cancellationToken);
    Task<IReadOnlyList<LanguageServerCompletion>> GetCompletionsAsync(string documentUri, CodePosition position, CancellationToken cancellationToken);
    Task<IReadOnlyList<LanguageServerCodeAction>> GetCodeActionsAsync(string documentUri, CodeRange range, string workspaceRoot, CancellationToken cancellationToken);
    Task<IReadOnlyList<CodeSemanticToken>> GetSemanticTokensAsync(string documentUri, CancellationToken cancellationToken);
}

public interface IAdvancedCodeIntelligenceService
{
    Task<LanguageServerCapabilities> GetCapabilitiesAsync(string workspaceRoot, string relativePath, CancellationToken cancellationToken);
    Task<IReadOnlyList<CodeLocation>> GetDefinitionAsync(string workspaceRoot, string relativePath, string documentText, CodePosition position, CancellationToken cancellationToken);
    Task<IReadOnlyList<CodeLocation>> FindReferencesAsync(string workspaceRoot, string relativePath, string documentText, CodePosition position, CancellationToken cancellationToken);
    Task<CodeWorkspaceMutationResult> RenameSymbolAsync(string workspaceRoot, string relativePath, string documentText, CodePosition position, string newName, CancellationToken cancellationToken);
    Task<IReadOnlyList<LanguageServerCompletion>> GetCompletionsAsync(string workspaceRoot, string relativePath, string documentText, CodePosition position, CancellationToken cancellationToken);
    Task<IReadOnlyList<CodeActionProposal>> GetCodeActionsAsync(string workspaceRoot, string relativePath, string documentText, CodeRange range, CancellationToken cancellationToken);
    Task<CodeWorkspaceMutationResult> ApplyCodeActionAsync(string workspaceRoot, CodeActionProposal action, CancellationToken cancellationToken);
    Task<IReadOnlyList<CodeSemanticToken>> GetSemanticTokensAsync(string workspaceRoot, string relativePath, string documentText, CancellationToken cancellationToken);
}
