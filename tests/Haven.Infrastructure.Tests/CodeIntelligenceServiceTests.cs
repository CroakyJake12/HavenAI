/*
 * FILE DOCUMENTATION
 * Where: tests/Haven.Infrastructure.Tests/CodeIntelligenceServiceTests.cs, in the automated test suite, where executable examples protect behavior against regressions.
 * What: This file owns CodeIntelligenceServiceTests, FakeConfigurationStore, FakeLanguageServerClientFactory, FakeLanguageServerClient. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The test is intentionally close to the public behavior it protects, making failures describe a user-visible or architectural contract.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

/// <summary>
/// Represents code intelligence service tests and keeps its related state and behavior together.
/// </summary>
public sealed class CodeIntelligenceServiceTests : IDisposable
{
    /// <summary>
    /// Stores root locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly string _root = Path.Combine(Path.GetTempPath(), "haven-code-intelligence-tests-" + Guid.NewGuid().ToString("N"));
    /// <summary>
    /// Stores configurations locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly FakeConfigurationStore _configurations;
    /// <summary>
    /// Stores servers locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly FakeLanguageServerClientFactory _servers = new();
    /// <summary>
    /// Stores service locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
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

    /// <summary>
    /// Performs the diagnostics use configured language server through public service step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the symbol search combines language server and bounded lexical fallback step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the formatting is previewed then applied through workspace transaction step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the formatting rejects a stale preview without overwriting external changes step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the dispose step owned by this component.
    /// </summary>
    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    /// <summary>
    /// Represents fake configuration store and keeps its related state and behavior together.
    /// </summary>
    private sealed class FakeConfigurationStore(LanguageServerDefinition definition) : ILanguageServerConfigurationStore
    {
        /// <summary>
        /// Retrieves all async for the current operation.
        /// </summary>
        public Task<IReadOnlyList<LanguageServerDefinition>> GetAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<LanguageServerDefinition>>([definition]);

        /// <summary>
        /// Performs find for path asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task<LanguageServerDefinition?> FindForPathAsync(string path, CancellationToken cancellationToken) =>
            Task.FromResult<LanguageServerDefinition?>(Path.GetExtension(path).Equals(".cs", StringComparison.OrdinalIgnoreCase) ? definition : null);

        /// <summary>
        /// Performs upsert asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task UpsertAsync(LanguageServerDefinition value, CancellationToken cancellationToken) => throw new NotSupportedException();
        /// <summary>
        /// Performs delete asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task DeleteAsync(string id, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    /// <summary>
    /// Represents fake language server client factory and keeps its related state and behavior together.
    /// </summary>
    private sealed class FakeLanguageServerClientFactory : ILanguageServerClientFactory
    {
        /// <summary>
        /// Gets or updates diagnostics, the bindable or domain state represented by this property.
        /// </summary>
        public IReadOnlyList<CodeDiagnostic> Diagnostics { get; set; } = [];
        /// <summary>
        /// Gets or updates symbols, the bindable or domain state represented by this property.
        /// </summary>
        public IReadOnlyList<CodeSymbol> Symbols { get; set; } = [];
        /// <summary>
        /// Gets or updates format edits, the bindable or domain state represented by this property.
        /// </summary>
        public IReadOnlyList<LanguageServerTextEdit> FormatEdits { get; set; } = [];
        /// <summary>
        /// Gets or updates start count, the bindable or domain state represented by this property.
        /// </summary>
        public int StartCount { get; private set; }
        /// <summary>
        /// Gets or updates last client, the bindable or domain state represented by this property.
        /// </summary>
        public FakeLanguageServerClient? LastClient { get; private set; }

        /// <summary>
        /// Performs start asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task<ILanguageServerClient> StartAsync(LanguageServerDefinition definition, string workspaceRoot, CancellationToken cancellationToken)
        {
            StartCount++;
            LastClient = new FakeLanguageServerClient(definition.Id, Diagnostics, Symbols, FormatEdits);
            return Task.FromResult<ILanguageServerClient>(LastClient);
        }
    }

    /// <summary>
    /// Represents fake language server client and keeps its related state and behavior together.
    /// </summary>
    private sealed class FakeLanguageServerClient(
        string serverId,
        IReadOnlyList<CodeDiagnostic> diagnostics,
        IReadOnlyList<CodeSymbol> symbols,
        IReadOnlyList<LanguageServerTextEdit> formatEdits) : ILanguageServerClient
    {
        /// <summary>
        /// Gets or updates server id, the bindable or domain state represented by this property.
        /// </summary>
        public string ServerId => serverId;
        /// <summary>
        /// Gets or updates open count, the bindable or domain state represented by this property.
        /// </summary>
        public int OpenCount { get; private set; }
        /// <summary>
        /// Gets or updates shutdown called, the bindable or domain state represented by this property.
        /// </summary>
        public bool ShutdownCalled { get; private set; }

        /// <summary>
        /// Performs open document asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task OpenDocumentAsync(string documentUri, string languageId, string text, int version, CancellationToken cancellationToken)
        {
            OpenCount++;
            return Task.CompletedTask;
        }

        /// <summary>
        /// Retrieves diagnostics async for the current operation.
        /// </summary>
        public Task<IReadOnlyList<CodeDiagnostic>> GetDiagnosticsAsync(string documentUri, string workspaceRoot, TimeSpan timeout, CancellationToken cancellationToken) =>
            Task.FromResult(diagnostics);

        /// <summary>
        /// Performs search workspace symbols asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task<IReadOnlyList<CodeSymbol>> SearchWorkspaceSymbolsAsync(string query, string workspaceRoot, CancellationToken cancellationToken) =>
            Task.FromResult(symbols);

        /// <summary>
        /// Performs format document asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task<IReadOnlyList<LanguageServerTextEdit>> FormatDocumentAsync(string documentUri, int tabSize, bool insertSpaces, CancellationToken cancellationToken) =>
            Task.FromResult(formatEdits);

        /// <summary>
        /// Performs shutdown asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task ShutdownAsync(CancellationToken cancellationToken)
        {
            ShutdownCalled = true;
            return Task.CompletedTask;
        }

        /// <summary>
        /// Performs dispose asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
