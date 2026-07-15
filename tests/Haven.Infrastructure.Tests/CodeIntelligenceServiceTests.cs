using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

public sealed class CodeIntelligenceServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "haven-code-intelligence-tests-" + Guid.NewGuid().ToString("N"));
    private readonly FakeConfigurationStore _configurations;
    private readonly FakeLanguageServerClientFactory _servers = new();
    private readonly ProductionCodeIntelligenceService _service;

    public CodeIntelligenceServiceTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "src"));
        _configurations = new FakeConfigurationStore(new LanguageServerDefinition(
            "test-csharp",
            "Test C# Server",
            "dotnet",
            string.Empty,
            "csharp",
            [".cs"],
            true,
            10,
            "{}"));
        var workspaceTools = new WorkspaceToolService();
        _service = new ProductionCodeIntelligenceService(
            workspaceTools,
            new WorkspaceTransactionService(workspaceTools),
            _configurations,
            _servers);
    }

    [Fact]
    public async Task DiagnosticsUseConfiguredLanguageServerThroughPublicService()
    {
        var path = Path.Combine(_root, "src", "Broken.cs");
        await File.WriteAllTextAsync(path, "class Broken { }");
        _servers.Diagnostics =
        [
            new CodeDiagnostic(
                Path.Combine("src", "Broken.cs"),
                new CodeRange(new CodePosition(0, 6), new CodePosition(0, 12)),
                CodeDiagnosticSeverity.Warning,
                "TEST001",
                "fake-lsp",
                "Demonstration diagnostic")
        ];

        var diagnostics = await _service.GetDiagnosticsAsync(_root, Path.Combine("src", "Broken.cs"), CancellationToken.None);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("TEST001", diagnostic.Code);
        Assert.Equal(1, _servers.StartCount);
        Assert.Equal(1, _servers.LastClient!.OpenCount);
        Assert.True(_servers.LastClient.ShutdownCalled);
    }

    [Fact]
    public async Task SymbolSearchCombinesLanguageServerAndBoundedLexicalFallback()
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "src", "Symbols.cs"), "namespace Demo; public sealed class FallbackThing { }");
        _servers.Symbols =
        [
            new CodeSymbol(
                "ServerThing",
                "Class",
                Path.Combine("src", "Server.cs"),
                new CodeRange(new CodePosition(3, 4), new CodePosition(3, 15)),
                "Demo",
                "fake-lsp")
        ];

        var symbols = await _service.SearchSymbolsAsync(_root, "Thing", CancellationToken.None);

        Assert.Contains(symbols, item => item.Name == "ServerThing" && item.Source == "fake-lsp");
        Assert.Contains(symbols, item => item.Name == "FallbackThing" && item.Source == "Haven lexical fallback");
    }

    [Fact]
    public async Task FormattingIsPreviewedThenAppliedThroughWorkspaceTransaction()
    {
        var relative = Path.Combine("src", "Format.cs");
        var path = Path.Combine(_root, relative);
        await File.WriteAllTextAsync(path, "class C{ }\n");
        _servers.FormatEdits =
        [
            new LanguageServerTextEdit(
                new CodeRange(new CodePosition(0, 0), new CodePosition(1, 0)),
                "class C\n{\n}\n")
        ];

        var preview = await _service.PreviewFormatAsync(_root, relative, 4, true, CancellationToken.None);

        Assert.True(preview.HasChanges);
        Assert.Contains("--- a/", preview.UnifiedDiff);
        Assert.Equal("class C{ }\n", await File.ReadAllTextAsync(path));

        var applied = await _service.ApplyFormatAsync(_root, preview, CancellationToken.None);

        Assert.True(applied.Changed);
        Assert.NotNull(applied.TransactionId);
        Assert.Equal("class C\n{\n}\n", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task FormattingRejectsAStalePreviewWithoutOverwritingExternalChanges()
    {
        var relative = Path.Combine("src", "Stale.cs");
        var path = Path.Combine(_root, relative);
        await File.WriteAllTextAsync(path, "class C{ }\n");
        _servers.FormatEdits =
        [
            new LanguageServerTextEdit(
                new CodeRange(new CodePosition(0, 0), new CodePosition(1, 0)),
                "class C\n{\n}\n")
        ];
        var preview = await _service.PreviewFormatAsync(_root, relative, 4, true, CancellationToken.None);
        await File.WriteAllTextAsync(path, "// external edit\nclass C{ }\n");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.ApplyFormatAsync(_root, preview, CancellationToken.None));

        Assert.Contains("changed after", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("// external edit\nclass C{ }\n", await File.ReadAllTextAsync(path));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private sealed class FakeConfigurationStore(LanguageServerDefinition definition) : ILanguageServerConfigurationStore
    {
        public Task<IReadOnlyList<LanguageServerDefinition>> GetAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<LanguageServerDefinition>>([definition]);

        public Task<LanguageServerDefinition?> FindForPathAsync(string path, CancellationToken cancellationToken) =>
            Task.FromResult<LanguageServerDefinition?>(Path.GetExtension(path).Equals(".cs", StringComparison.OrdinalIgnoreCase) ? definition : null);

        public Task UpsertAsync(LanguageServerDefinition value, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteAsync(string id, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeLanguageServerClientFactory : ILanguageServerClientFactory
    {
        public IReadOnlyList<CodeDiagnostic> Diagnostics { get; set; } = [];
        public IReadOnlyList<CodeSymbol> Symbols { get; set; } = [];
        public IReadOnlyList<LanguageServerTextEdit> FormatEdits { get; set; } = [];
        public int StartCount { get; private set; }
        public FakeLanguageServerClient? LastClient { get; private set; }

        public Task<ILanguageServerClient> StartAsync(LanguageServerDefinition definition, string workspaceRoot, CancellationToken cancellationToken)
        {
            StartCount++;
            LastClient = new FakeLanguageServerClient(definition.Id, Diagnostics, Symbols, FormatEdits);
            return Task.FromResult<ILanguageServerClient>(LastClient);
        }
    }

    private sealed class FakeLanguageServerClient(
        string serverId,
        IReadOnlyList<CodeDiagnostic> diagnostics,
        IReadOnlyList<CodeSymbol> symbols,
        IReadOnlyList<LanguageServerTextEdit> formatEdits) : ILanguageServerClient
    {
        public string ServerId => serverId;
        public int OpenCount { get; private set; }
        public bool ShutdownCalled { get; private set; }

        public Task OpenDocumentAsync(string documentUri, string languageId, string text, int version, CancellationToken cancellationToken)
        {
            OpenCount++;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<CodeDiagnostic>> GetDiagnosticsAsync(string documentUri, string workspaceRoot, TimeSpan timeout, CancellationToken cancellationToken) =>
            Task.FromResult(diagnostics);

        public Task<IReadOnlyList<CodeSymbol>> SearchWorkspaceSymbolsAsync(string query, string workspaceRoot, CancellationToken cancellationToken) =>
            Task.FromResult(symbols);

        public Task<IReadOnlyList<LanguageServerTextEdit>> FormatDocumentAsync(string documentUri, int tabSize, bool insertSpaces, CancellationToken cancellationToken) =>
            Task.FromResult(formatEdits);

        public Task ShutdownAsync(CancellationToken cancellationToken)
        {
            ShutdownCalled = true;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
