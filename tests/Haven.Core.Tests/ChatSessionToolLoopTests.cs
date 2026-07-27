/*
 * FILE DOCUMENTATION
 * Where: tests/Haven.Core.Tests/ChatSessionToolLoopTests.cs, in the automated test suite, where executable examples protect behavior against regressions.
 * What: This file owns ChatSessionToolLoopTests, FakeOllama, UnsupportedToolsOllama, FakeConversations, TestWorkspaceTools, TestComputerTools. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The test is intentionally close to the public behavior it protects, making failures describe a user-visible or architectural contract.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace Haven.Core.Tests;

/// <summary>
/// Represents chat session tool loop tests and keeps its related state and behavior together.
/// </summary>
public sealed class ChatSessionToolLoopTests : IDisposable
{
    /// <summary>
    /// Stores root locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly string _root = Path.Combine(Path.GetTempPath(), "haven-chat-loop-tests", Guid.NewGuid().ToString("N"));

    public ChatSessionToolLoopTests() => Directory.CreateDirectory(_root);

    /// <summary>
    /// Performs the tool capable workspace chat executes and reports real tool result step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the workspace tools are not exposed by chat mode even when a root is supplied step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the direct computer launch completes without sending unsupported tool schema to model step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the unsupported native tool schema falls back to compatibility router step owned by this component.
    /// </summary>
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

    /// <summary>
    /// Performs the dispose step owned by this component.
    /// </summary>
    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    /// <summary>
    /// Represents fake ollama and keeps its related state and behavior together.
    /// </summary>
    private sealed class FakeOllama(ModelDescriptor model) : IOllamaClient
    {
        /// <summary>
        /// Gets or updates tool requests, the bindable or domain state represented by this property.
        /// </summary>
        public int ToolRequests { get; private set; }
        /// <summary>
        /// Reports whether available async applies to the current state.
        /// </summary>
        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken) => Task.FromResult(true);
        /// <summary>
        /// Retrieves models async for the current operation.
        /// </summary>
        public Task<IReadOnlyList<ModelDescriptor>> GetModelsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ModelDescriptor>>([model]);
        /// <summary>
        /// Performs stream chat asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public async IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken) { await Task.CompletedTask; yield break; }
        /// <summary>
        /// Performs complete asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken cancellationToken) => Task.FromResult(string.Empty);
        /// <summary>
        /// Performs chat with tools asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken cancellationToken)
        {
            ToolRequests++;
            return Task.FromResult(ToolRequests == 1
                ? new OllamaToolResponse("", [Call("write_file", new { path = "tool-loop.txt", content = "created" })])
                : new OllamaToolResponse("Created and verified the file.", []));
        }

        /// <summary>
        /// Performs the call step owned by this component.
        /// </summary>
        private static OllamaToolCall Call(string name, object arguments)
        {
            using var document = JsonDocument.Parse(JsonSerializer.Serialize(arguments));
            return new OllamaToolCall(name, document.RootElement.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.Clone()));
        }
    }

    /// <summary>
    /// Represents unsupported tools ollama and keeps its related state and behavior together.
    /// </summary>
    private sealed class UnsupportedToolsOllama(ModelDescriptor model) : IOllamaClient
    {
        /// <summary>
        /// Reports whether available async applies to the current state.
        /// </summary>
        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken) => Task.FromResult(true);
        /// <summary>
        /// Retrieves models async for the current operation.
        /// </summary>
        public Task<IReadOnlyList<ModelDescriptor>> GetModelsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ModelDescriptor>>([model]);
        /// <summary>
        /// Performs stream chat asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public async IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken) { await Task.CompletedTask; yield break; }
        /// <summary>
        /// Performs complete asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken cancellationToken) =>
            Task.FromResult("{\"name\":\"computer_snapshot\",\"arguments\":{}}");
        /// <summary>
        /// Performs chat with tools asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken cancellationToken) =>
            Task.FromException<OllamaToolResponse>(new HttpRequestException(
                "Ollama returned 400: model does not support tools", null, System.Net.HttpStatusCode.BadRequest));
    }

    /// <summary>
    /// Represents fake conversations and keeps its related state and behavior together.
    /// </summary>
    private sealed class FakeConversations : IConversationRepository
    {
        /// <summary>
        /// Retrieves recent async for the current operation.
        /// </summary>
        public Task<IReadOnlyList<Conversation>> GetRecentAsync(HavenMode? mode, int limit, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Conversation>>([]);
        /// <summary>
        /// Retrieves async for the current operation.
        /// </summary>
        public Task<Conversation?> GetAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<Conversation?>(null);
        /// <summary>
        /// Retrieves messages async for the current operation.
        /// </summary>
        public Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(Guid conversationId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ChatMessage>>([]);
        /// <summary>
        /// Performs upsert conversation asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task UpsertConversationAsync(Conversation conversation, CancellationToken cancellationToken) => Task.CompletedTask;
        /// <summary>
        /// Performs add message asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task AddMessageAsync(ChatMessage message, CancellationToken cancellationToken) => Task.CompletedTask;
        /// <summary>
        /// Performs delete conversation asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task DeleteConversationAsync(Guid id, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    /// <summary>
    /// Represents test workspace tools and keeps its related state and behavior together.
    /// </summary>
    private sealed class TestWorkspaceTools : IWorkspaceToolService
    {
        /// <summary>
        /// Performs the resolve workspace path step owned by this component.
        /// </summary>
        public string ResolveWorkspacePath(string workspaceRoot, string relativePath)
        {
            var root = Path.GetFullPath(workspaceRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var result = Path.GetFullPath(Path.Combine(root, relativePath));
            if (!result.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new UnauthorizedAccessException("outside workspace");
            return result;
        }

        /// <summary>
        /// Performs read text asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task<string> ReadTextAsync(string workspaceRoot, string relativePath, CancellationToken cancellationToken) => File.ReadAllTextAsync(ResolveWorkspacePath(workspaceRoot, relativePath), cancellationToken);
        /// <summary>
        /// Performs write text atomic asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public async Task WriteTextAtomicAsync(string workspaceRoot, string relativePath, string content, CancellationToken cancellationToken)
        {
            var path = ResolveWorkspacePath(workspaceRoot, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, content, cancellationToken);
        }
        /// <summary>
        /// Performs search files asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task<IReadOnlyList<string>> SearchFilesAsync(string workspaceRoot, string searchPattern, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<string>>([]);
        /// <summary>
        /// Runs run process async while preserving the surrounding cancellation and error-handling contract.
        /// </summary>
        public Task<ProcessResult> RunProcessAsync(ProcessRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    /// <summary>
    /// Represents test computer tools and keeps its related state and behavior together.
    /// </summary>
    private sealed class TestComputerTools : IComputerToolService
    {
        /// <summary>
        /// Gets or updates launched name, the bindable or domain state represented by this property.
        /// </summary>
        public string? LaunchedName { get; private set; }
        /// <summary>
        /// Performs snapshot asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task<string> SnapshotAsync(CancellationToken cancellationToken) => Task.FromResult("snapshot");
        /// <summary>
        /// Performs list windows asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task<string> ListWindowsAsync(CancellationToken cancellationToken) => Task.FromResult("[]");
        /// <summary>
        /// Performs launch app asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task<string> LaunchAppAsync(string name, CancellationToken cancellationToken) { LaunchedName = name; return Task.FromResult($"opened {name}"); }
        /// <summary>
        /// Performs focus window asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task<string> FocusWindowAsync(string title, CancellationToken cancellationToken) => Task.FromResult($"focused {title}");
        /// <summary>
        /// Performs invoke asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task<string> InvokeAsync(string windowTitle, string name, string automationId, CancellationToken cancellationToken) => Task.FromResult("invoked");
        /// <summary>
        /// Performs click asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task<string> ClickAsync(string windowTitle, int x, int y, string button, CancellationToken cancellationToken) => Task.FromResult("clicked");
        /// <summary>
        /// Performs type asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task<string> TypeAsync(string windowTitle, string text, CancellationToken cancellationToken) => Task.FromResult("typed");
        /// <summary>
        /// Performs press asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task<string> PressAsync(string windowTitle, string keys, CancellationToken cancellationToken) => Task.FromResult("pressed");
        /// <summary>
        /// Performs close window asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task<string> CloseWindowAsync(string title, CancellationToken cancellationToken) => Task.FromResult("closed");
    }
}
