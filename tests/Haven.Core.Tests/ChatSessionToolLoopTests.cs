using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace Haven.Core.Tests;

public sealed class ChatSessionToolLoopTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "haven-chat-loop-tests", Guid.NewGuid().ToString("N"));

    public ChatSessionToolLoopTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task ToolCapableWorkspaceChatExecutesAndReportsRealToolResult()
    {
        var model = new ModelDescriptor("tools-model", 1, "test", "test", "test", new HashSet<ToolCapability> { ToolCapability.Text, ToolCapability.Tools }, DateTimeOffset.UtcNow);
        var ollama = new FakeOllama(model);
        var service = new ChatSessionService(
            new FakeConversations(), ollama, new CapabilityPreflightService(),
            new WorkspaceToolRuntime(new TestWorkspaceTools()), new ComputerToolRuntime(new TestComputerTools()));
        var now = DateTimeOffset.UtcNow;
        var conversation = new Conversation(Guid.NewGuid(), HavenMode.Studio, ConversationKind.StudioChat, "Test", null, null, false, true, now, now);

        var events = new List<ChatStreamEvent>();
        await foreach (var item in service.SendAsync(
                           conversation, "Create the file", model, EffortLevel.Medium, [], "Default", "",
                           DuoMode.Solo, _root, "", "", null, CancellationToken.None))
            events.Add(item);

        Assert.Equal("created", await File.ReadAllTextAsync(Path.Combine(_root, "tool-loop.txt")));
        Assert.Contains(events, item => item.Kind == ChatStreamEventKind.ToolActivity && item.ToolActivity?.Succeeded == true);
        Assert.Contains(events, item => item.Kind == ChatStreamEventKind.AssistantCompleted && item.Message?.Content == "Created and verified the file.");
        Assert.Equal(2, ollama.ToolRequests);
    }

    [Fact]
    public async Task WorkspaceToolsAreNotExposedByChatModeEvenWhenARootIsSupplied()
    {
        var model = new ModelDescriptor("tools-model", 1, "test", "test", "test", new HashSet<ToolCapability> { ToolCapability.Text, ToolCapability.Tools }, DateTimeOffset.UtcNow);
        var ollama = new FakeOllama(model);
        var service = new ChatSessionService(
            new FakeConversations(), ollama, new CapabilityPreflightService(),
            new WorkspaceToolRuntime(new TestWorkspaceTools()), new ComputerToolRuntime(new TestComputerTools()));
        var now = DateTimeOffset.UtcNow;
        var conversation = new Conversation(Guid.NewGuid(), HavenMode.Chat, ConversationKind.Chat, "Test", null, null, false, true, now, now);

        await foreach (var _ in service.SendAsync(
                           conversation, "Create the file", model, EffortLevel.Medium, [], "Default", "",
                           DuoMode.Solo, _root, "", "", null, CancellationToken.None))
        {
        }

        Assert.False(File.Exists(Path.Combine(_root, "tool-loop.txt")));
        Assert.Equal(0, ollama.ToolRequests);
    }

    [Fact]
    public async Task DirectComputerLaunchCompletesWithoutSendingUnsupportedToolSchemaToModel()
    {
        var model = new ModelDescriptor("desktop-model", 1, "test", "test", "test", new HashSet<ToolCapability> { ToolCapability.Text, ToolCapability.ComputerUse }, DateTimeOffset.UtcNow);
        var ollama = new FakeOllama(model);
        var computer = new TestComputerTools();
        var service = new ChatSessionService(
            new FakeConversations(), ollama, new CapabilityPreflightService(),
            new WorkspaceToolRuntime(new TestWorkspaceTools()), new ComputerToolRuntime(computer));
        var now = DateTimeOffset.UtcNow;
        var conversation = new Conversation(Guid.NewGuid(), HavenMode.Chat, ConversationKind.Chat, "Test", null, null, false, true, now, now);

        var events = new List<ChatStreamEvent>();
        await foreach (var item in service.SendAsync(
                           conversation, "open notepad", model, EffortLevel.Medium,
                           [new ActivePlugin("ComputerUse", "computer-use", false)], "Default", "",
                           DuoMode.Solo, null, "", "", null, CancellationToken.None))
            events.Add(item);

        Assert.Equal("notepad", computer.LaunchedName, ignoreCase: true);
        Assert.Equal(0, ollama.ToolRequests);
        Assert.Contains(events, item => item.Kind == ChatStreamEventKind.AssistantCompleted && item.Message?.Content == "Done — opened notepad.");
    }

    [Fact]
    public async Task UnsupportedNativeToolSchemaFallsBackToCompatibilityRouter()
    {
        var model = new ModelDescriptor("legacy-model", 1, "test", "test", "test", new HashSet<ToolCapability> { ToolCapability.Text, ToolCapability.ComputerUse }, DateTimeOffset.UtcNow);
        var service = new ChatSessionService(
            new FakeConversations(), new UnsupportedToolsOllama(model), new CapabilityPreflightService(),
            new WorkspaceToolRuntime(new TestWorkspaceTools()), new ComputerToolRuntime(new TestComputerTools()));
        var now = DateTimeOffset.UtcNow;
        var conversation = new Conversation(Guid.NewGuid(), HavenMode.Chat, ConversationKind.Chat, "Test", null, null, false, true, now, now);

        var events = new List<ChatStreamEvent>();
        await foreach (var item in service.SendAsync(
                           conversation, "click the Save button", model, EffortLevel.Medium,
                           [new ActivePlugin("ComputerUse", "computer-use", false)], "Default", "",
                           DuoMode.Solo, null, "", "", null, CancellationToken.None))
            events.Add(item);

        Assert.Contains(events, item => item.Kind == ChatStreamEventKind.ToolActivity && item.ToolActivity?.Title == "Inspecting the desktop" && item.ToolActivity.Succeeded);
        Assert.Contains(events, item => item.Kind == ChatStreamEventKind.AssistantCompleted && item.Message?.Content == "Done — the requested tool action completed.");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private sealed class FakeOllama(ModelDescriptor model) : IOllamaClient
    {
        public int ToolRequests { get; private set; }
        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<IReadOnlyList<ModelDescriptor>> GetModelsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ModelDescriptor>>([model]);
        public async IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken) { await Task.CompletedTask; yield break; }
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken cancellationToken) => Task.FromResult(string.Empty);
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken cancellationToken)
        {
            ToolRequests++;
            return Task.FromResult(ToolRequests == 1
                ? new OllamaToolResponse("", [Call("write_file", new { path = "tool-loop.txt", content = "created" })])
                : new OllamaToolResponse("Created and verified the file.", []));
        }

        private static OllamaToolCall Call(string name, object arguments)
        {
            using var document = JsonDocument.Parse(JsonSerializer.Serialize(arguments));
            return new OllamaToolCall(name, document.RootElement.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.Clone()));
        }
    }

    private sealed class UnsupportedToolsOllama(ModelDescriptor model) : IOllamaClient
    {
        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<IReadOnlyList<ModelDescriptor>> GetModelsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ModelDescriptor>>([model]);
        public async IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken) { await Task.CompletedTask; yield break; }
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken cancellationToken) =>
            Task.FromResult("{\"name\":\"computer_snapshot\",\"arguments\":{}}");
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken cancellationToken) =>
            Task.FromException<OllamaToolResponse>(new HttpRequestException(
                "Ollama returned 400: model does not support tools", null, System.Net.HttpStatusCode.BadRequest));
    }

    private sealed class FakeConversations : IConversationRepository
    {
        public Task<IReadOnlyList<Conversation>> GetRecentAsync(HavenMode? mode, int limit, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Conversation>>([]);
        public Task<Conversation?> GetAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<Conversation?>(null);
        public Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(Guid conversationId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ChatMessage>>([]);
        public Task UpsertConversationAsync(Conversation conversation, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task AddMessageAsync(ChatMessage message, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task DeleteConversationAsync(Guid id, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class TestWorkspaceTools : IWorkspaceToolService
    {
        public string ResolveWorkspacePath(string workspaceRoot, string relativePath)
        {
            var root = Path.GetFullPath(workspaceRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var result = Path.GetFullPath(Path.Combine(root, relativePath));
            if (!result.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new UnauthorizedAccessException("outside workspace");
            return result;
        }

        public Task<string> ReadTextAsync(string workspaceRoot, string relativePath, CancellationToken cancellationToken) => File.ReadAllTextAsync(ResolveWorkspacePath(workspaceRoot, relativePath), cancellationToken);
        public async Task WriteTextAtomicAsync(string workspaceRoot, string relativePath, string content, CancellationToken cancellationToken)
        {
            var path = ResolveWorkspacePath(workspaceRoot, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, content, cancellationToken);
        }
        public Task<IReadOnlyList<string>> SearchFilesAsync(string workspaceRoot, string searchPattern, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<ProcessResult> RunProcessAsync(ProcessRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class TestComputerTools : IComputerToolService
    {
        public string? LaunchedName { get; private set; }
        public Task<string> SnapshotAsync(CancellationToken cancellationToken) => Task.FromResult("snapshot");
        public Task<string> ListWindowsAsync(CancellationToken cancellationToken) => Task.FromResult("[]");
        public Task<string> LaunchAppAsync(string name, CancellationToken cancellationToken) { LaunchedName = name; return Task.FromResult($"opened {name}"); }
        public Task<string> FocusWindowAsync(string title, CancellationToken cancellationToken) => Task.FromResult($"focused {title}");
        public Task<string> InvokeAsync(string windowTitle, string name, string automationId, CancellationToken cancellationToken) => Task.FromResult("invoked");
        public Task<string> ClickAsync(string windowTitle, int x, int y, string button, CancellationToken cancellationToken) => Task.FromResult("clicked");
        public Task<string> TypeAsync(string windowTitle, string text, CancellationToken cancellationToken) => Task.FromResult("typed");
        public Task<string> PressAsync(string windowTitle, string keys, CancellationToken cancellationToken) => Task.FromResult("pressed");
        public Task<string> CloseWindowAsync(string title, CancellationToken cancellationToken) => Task.FromResult("closed");
    }
}
