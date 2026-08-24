using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

public sealed class AdvancedCodeIntelligenceServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "haven-advanced-lsp-tests-" + Guid.NewGuid().ToString("N"));

    public AdvancedCodeIntelligenceServiceTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public async Task Rename_applies_real_multi_file_workspace_edit_through_transaction_service()
    {
        const string aBefore = "class A { B value; }";
        const string bBefore = "class B { }";
        await File.WriteAllTextAsync(Path.Combine(_root, "A.cs"), aBefore);
        await File.WriteAllTextAsync(Path.Combine(_root, "B.cs"), bBefore);
        var client = FakeClient.Create();
        client.Rename = new LanguageServerWorkspaceEdit([
            new LanguageServerDocumentEdit("A.cs", [new LanguageServerTextEdit(new CodeRange(new CodePosition(0, 10), new CodePosition(0, 11)), "Renamed")]),
            new LanguageServerDocumentEdit("B.cs", [new LanguageServerTextEdit(new CodeRange(new CodePosition(0, 6), new CodePosition(0, 7)), "Renamed")])
        ]);
        var service = CreateService(() => client);

        var result = await service.RenameSymbolAsync(_root, "A.cs", aBefore, new CodePosition(0, 10), "Renamed", CancellationToken.None);

        Assert.Equal(2, result.Files.Count);
        Assert.Equal("class A { Renamed value; }", await File.ReadAllTextAsync(Path.Combine(_root, "A.cs")));
        Assert.Equal("class Renamed { }", await File.ReadAllTextAsync(Path.Combine(_root, "B.cs")));
        Assert.NotEqual(Guid.Empty, result.TransactionId);
    }

    [Fact]
    public async Task Rename_rejects_out_of_root_workspace_edit_before_any_mutation()
    {
        const string before = "class A { }";
        await File.WriteAllTextAsync(Path.Combine(_root, "A.cs"), before);
        var client = FakeClient.Create();
        client.Rename = new LanguageServerWorkspaceEdit([new LanguageServerDocumentEdit("../escape.cs", [new LanguageServerTextEdit(new CodeRange(new CodePosition(0, 0), new CodePosition(0, 0)), "x")])]);
        var service = CreateService(() => client);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.RenameSymbolAsync(_root, "A.cs", before, new CodePosition(0, 6), "B", CancellationToken.None));

        Assert.Equal(before, await File.ReadAllTextAsync(Path.Combine(_root, "A.cs")));
    }

    [Fact]
    public async Task Command_only_code_action_is_present_but_not_applicable()
    {
        const string before = "class A { }";
        await File.WriteAllTextAsync(Path.Combine(_root, "A.cs"), before);
        var client = FakeClient.Create();
        client.Actions = [new LanguageServerCodeAction("Organize imports", "source.organizeImports", true, null, "server.organizeImports")];
        var service = CreateService(() => client);

        var actions = await service.GetCodeActionsAsync(_root, "A.cs", before, new CodeRange(new CodePosition(0, 0), new CodePosition(0, 0)), CancellationToken.None);

        var action = Assert.Single(actions);
        Assert.False(action.IsApplicable);
        Assert.Contains("server command", action.UnavailableReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Capability_definition_reference_completion_and_semantic_results_come_from_server_client()
    {
        const string before = "class A { }";
        await File.WriteAllTextAsync(Path.Combine(_root, "A.cs"), before);
        var client = FakeClient.Create();
        client.Definitions = [new CodeLocation("file:///A.cs", "A.cs", new CodeRange(new CodePosition(0, 6), new CodePosition(0, 7)), true)];
        client.References = [new CodeLocation("file:///A.cs", "A.cs", new CodeRange(new CodePosition(0, 6), new CodePosition(0, 7)), true)];
        client.Completions = [new LanguageServerCompletion("A", null, "A", null, new CodeRange(new CodePosition(0, 6), new CodePosition(0, 7)), [])];
        client.SemanticTokens = [new CodeSemanticToken(new CodeRange(new CodePosition(0, 0), new CodePosition(0, 5)), "class", ["declaration"])];
        var service = CreateService(() => client);

        var capabilities = await service.GetCapabilitiesAsync(_root, "A.cs", CancellationToken.None);
        var definitions = await service.GetDefinitionAsync(_root, "A.cs", before, new CodePosition(0, 6), CancellationToken.None);
        var references = await service.FindReferencesAsync(_root, "A.cs", before, new CodePosition(0, 6), CancellationToken.None);
        var completions = await service.GetCompletionsAsync(_root, "A.cs", before, new CodePosition(0, 6), CancellationToken.None);
        var semantics = await service.GetSemanticTokensAsync(_root, "A.cs", before, CancellationToken.None);

        Assert.True(capabilities.Definition && capabilities.References && capabilities.Rename && capabilities.Completion && capabilities.CodeActions && capabilities.SemanticTokens);
        Assert.Single(definitions);
        Assert.Single(references);
        Assert.Single(completions);
        Assert.Equal("class", Assert.Single(semantics).TokenType);
    }

    private AdvancedCodeIntelligenceService CreateService(Func<FakeClient> createClient)
    {
        var tools = new WorkspaceToolService();
        return new AdvancedCodeIntelligenceService(tools, new WorkspaceTransactionService(tools), new FakeConfigurationStore(), new FakeClientFactory(createClient));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    private sealed class FakeConfigurationStore : ILanguageServerConfigurationStore
    {
        private readonly LanguageServerDefinition _definition = new("fake", "Fake LSP", Environment.ProcessPath!, string.Empty, "csharp", [".cs"], true);
        public Task<IReadOnlyList<LanguageServerDefinition>> GetAllAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<LanguageServerDefinition>>([_definition]);
        public Task<LanguageServerDefinition?> FindForPathAsync(string path, CancellationToken cancellationToken) => Task.FromResult<LanguageServerDefinition?>(_definition);
        public Task UpsertAsync(LanguageServerDefinition definition, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task DeleteAsync(string id, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeClientFactory(Func<FakeClient> createClient) : ILanguageServerClientFactory
    {
        public Task<ILanguageServerClient> StartAsync(LanguageServerDefinition definition, string workspaceRoot, CancellationToken cancellationToken) => Task.FromResult<ILanguageServerClient>(createClient());
    }

    private sealed class FakeClient : ILanguageServerClient, IAdvancedLanguageServerClient
    {
        public static FakeClient Create() => new();
        public string ServerId => "fake";
        public LanguageServerCapabilities AdvancedCapabilities { get; } = new(true, true, true, true, true, false, true, ["class"], ["declaration"]);
        public LanguageServerWorkspaceEdit Rename { get; set; } = new([]);
        public IReadOnlyList<LanguageServerCodeAction> Actions { get; set; } = [];
        public IReadOnlyList<CodeLocation> Definitions { get; set; } = [];
        public IReadOnlyList<CodeLocation> References { get; set; } = [];
        public IReadOnlyList<LanguageServerCompletion> Completions { get; set; } = [];
        public IReadOnlyList<CodeSemanticToken> SemanticTokens { get; set; } = [];
        public Task OpenDocumentAsync(string documentUri, string languageId, string text, int version, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyList<CodeDiagnostic>> GetDiagnosticsAsync(string documentUri, string workspaceRoot, TimeSpan timeout, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<CodeDiagnostic>>([]);
        public Task<IReadOnlyList<CodeSymbol>> SearchWorkspaceSymbolsAsync(string query, string workspaceRoot, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<CodeSymbol>>([]);
        public Task<IReadOnlyList<LanguageServerTextEdit>> FormatDocumentAsync(string documentUri, int tabSize, bool insertSpaces, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<LanguageServerTextEdit>>([]);
        public Task ShutdownAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public Task<IReadOnlyList<CodeLocation>> GetDefinitionAsync(string documentUri, CodePosition position, string workspaceRoot, CancellationToken cancellationToken) => Task.FromResult(Definitions);
        public Task<IReadOnlyList<CodeLocation>> FindReferencesAsync(string documentUri, CodePosition position, string workspaceRoot, bool includeDeclaration, CancellationToken cancellationToken) => Task.FromResult(References);
        public Task<LanguageServerWorkspaceEdit> RenameSymbolAsync(string documentUri, CodePosition position, string newName, string workspaceRoot, CancellationToken cancellationToken) => Task.FromResult(Rename);
        public Task<IReadOnlyList<LanguageServerCompletion>> GetCompletionsAsync(string documentUri, CodePosition position, CancellationToken cancellationToken) => Task.FromResult(Completions);
        public Task<IReadOnlyList<LanguageServerCodeAction>> GetCodeActionsAsync(string documentUri, CodeRange range, string workspaceRoot, CancellationToken cancellationToken) => Task.FromResult(Actions);
        public Task<IReadOnlyList<CodeSemanticToken>> GetSemanticTokensAsync(string documentUri, CancellationToken cancellationToken) => Task.FromResult(SemanticTokens);
    }
}
