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
        var conversations = new FakeConversations(recordMessages: true);
        var service = new ChatSessionService(
            conversations, ollama, new CapabilityPreflightService(), new PermitSafety(),
            new WorkspaceToolRuntime(new TestWorkspaceTools()), new ComputerToolRuntime(new TestComputerTools()));
        var now = DateTimeOffset.UtcNow;
        var conversation = new Conversation(Guid.NewGuid(), HavenMode.Studio, ConversationKind.StudioChat, "Test", null, null, false, false, now, now);

        var events = new List<ChatStreamEvent>();
        await foreach (var item in service.SendAsync(
                           conversation, "Create the file", model, EffortLevel.Medium, [], "Default", "",
                           DuoMode.Solo, _root, "", "", null, CancellationToken.None))
            events.Add(item);

        Assert.Equal("created", await File.ReadAllTextAsync(Path.Combine(_root, "tool-loop.txt")));
        var toolEvent = Assert.Single(events, item => item.Kind == ChatStreamEventKind.ToolActivity && item.ToolActivity?.Succeeded == true);
        var assistant = Assert.Single(events, item => item.Kind == ChatStreamEventKind.AssistantCompleted && item.Message?.Content == "Created and verified the file.").Message!;
        Assert.Equal(assistant.Id, toolEvent.MessageId);
        Assert.True(assistant.Metadata.TryGetValue("toolActivities", out var toolActivities));
        Assert.Equal(JsonValueKind.Array, toolActivities.ValueKind);
        Assert.Contains(toolActivities.EnumerateArray(), item => item.GetProperty("Id").GetGuid() == toolEvent.ToolActivity!.Id);
        var persistedMessages = await conversations.GetMessagesAsync(conversation.Id, CancellationToken.None);
        var persistedAssistant = Assert.Single(persistedMessages, message => message.Id == assistant.Id);
        Assert.Equal(assistant.MetadataJson, persistedAssistant.MetadataJson);
        Assert.True(persistedAssistant.Metadata.ContainsKey("toolActivities"));
        Assert.Equal(2, ollama.ToolRequests);
    }

    [Fact]
    public async Task McpPermissionBlockerCreatesResumableRemediationAndRetriesOnlyAfterApproval()
    {
        var model = new ModelDescriptor("tools-model", 1, "test", "test", "test", new HashSet<ToolCapability> { ToolCapability.Text, ToolCapability.Tools }, DateTimeOffset.UtcNow);
        var connection = ChatMcpRepository.ReadyConnection();
        var connectionRepository = new ChatMcpRepository(connection);
        var mcpClient = new ChatMcpClient();
        var mcpRuntime = new McpToolRuntime(connectionRepository, mcpClient);
        var localToolName = McpToolRuntime.LocalToolName(connection.Id, "write_item");
        var remediationRepository = new ChatRemediationRepository();
        var eventSink = new ChatRecordingSink();
        var continuations = new RemediationContinuationRegistry();
        var coordinator = new RemediationCoordinator(remediationRepository, new ChatSecretStore(), eventSink, continuations);
        var service = new ChatSessionService(
            new FakeConversations(), new SingleToolOllama(model, localToolName), new CapabilityPreflightService(), new PermitSafety(),
            new WorkspaceToolRuntime(new TestWorkspaceTools()), new ComputerToolRuntime(new TestComputerTools()),
            mcpTools: mcpRuntime, executionEvents: eventSink, recovery: new AutonomousRecoveryService(), remediations: coordinator);
        var now = DateTimeOffset.UtcNow;
        var conversation = new Conversation(Guid.NewGuid(), HavenMode.Chat, ConversationKind.Chat, "Test", null, null, false, true, now, now);
        var active = new ActiveCapability(ExternalConnectionNaming.CapabilityKey(connection.Id), ExternalConnectionNaming.PluginName(connection.Name),
            "connection", "Use connection", "connection.mcp", "haven.connections");

        await foreach (var _ in service.SendAsync(
                           conversation, "Update the attached MCP item", model, EffortLevel.Medium, [active], "Default", "",
                           DuoMode.Solo, null, "", "", null, CancellationToken.None, commandPermission: PermissionMode.Ask))
        {
        }

        Assert.Equal(0, mcpClient.InvocationCount);
        var remediation = Assert.IsType<RemediationRequest>(remediationRepository.Value);
        Assert.Equal(RemediationType.PermissionRequest, remediation.Type);
        Assert.True(remediation.CanResume);
        Assert.True(coordinator.CanResume(remediation.Id));
        Assert.Contains(eventSink.Events, item => item.ActionId == remediation.ActionId && item.Failure?.Code == "MCP_PERMISSION_REQUIRED");

        await coordinator.ApproveAndResolveAsync(remediation.Id, CancellationToken.None);

        Assert.Equal(1, mcpClient.InvocationCount);
        Assert.False(coordinator.CanResume(remediation.Id));
        Assert.Contains(eventSink.Events, item => item.Name == "Blocked action resumed" && item.RecoveryOfActionId == remediation.ActionId);
    }

    /// <summary>
    /// Performs the workspace tools are not exposed by chat mode even when a root is supplied step owned by this component.
    /// </summary>
    [Fact]
    public async Task WorkspaceToolsAreNotExposedByChatModeEvenWhenARootIsSupplied()
    {
        var model = new ModelDescriptor("tools-model", 1, "test", "test", "test", new HashSet<ToolCapability> { ToolCapability.Text, ToolCapability.Tools }, DateTimeOffset.UtcNow);
        var ollama = new FakeOllama(model);
        var conversations = new FakeConversations();
        var service = new ChatSessionService(
            conversations, ollama, new CapabilityPreflightService(), new PermitSafety(),
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
            new FakeConversations(), ollama, new CapabilityPreflightService(), new PermitSafety(),
            new WorkspaceToolRuntime(new TestWorkspaceTools()), new ComputerToolRuntime(computer));
        var now = DateTimeOffset.UtcNow;
        var conversation = new Conversation(Guid.NewGuid(), HavenMode.Chat, ConversationKind.Chat, "Test", null, null, false, true, now, now);

        var events = new List<ChatStreamEvent>();
        await foreach (var item in service.SendAsync(
                           conversation, "open notepad", model, EffortLevel.Medium,
                           [ActiveCapability.FromDefinition(CapabilityRegistryCatalog.BuiltIns.Single(item => item.Key == "computer-device-use"))], "Default", "",
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
            new FakeConversations(), new UnsupportedToolsOllama(model), new CapabilityPreflightService(), new PermitSafety(),
            new WorkspaceToolRuntime(new TestWorkspaceTools()), new ComputerToolRuntime(new TestComputerTools()));
        var now = DateTimeOffset.UtcNow;
        var conversation = new Conversation(Guid.NewGuid(), HavenMode.Chat, ConversationKind.Chat, "Test", null, null, false, true, now, now);

        var events = new List<ChatStreamEvent>();
        await foreach (var item in service.SendAsync(
                           conversation, "click the Save button", model, EffortLevel.Medium,
                           [ActiveCapability.FromDefinition(CapabilityRegistryCatalog.BuiltIns.Single(item => item.Key == "computer-device-use"))], "Default", "",
                           DuoMode.Solo, null, "", "", null, CancellationToken.None))
            events.Add(item);

        Assert.Contains(events, item => item.Kind == ChatStreamEventKind.ToolActivity && item.ToolActivity?.Title == "Inspecting the desktop" && item.ToolActivity.Succeeded);
        Assert.Contains(events, item => item.Kind == ChatStreamEventKind.AssistantCompleted && item.Message?.Content == "Done — the requested tool action completed.");
    }

    /// <summary>
    /// Performs the dispose step owned by this component.
    /// </summary>
    private sealed class PermitSafety : IConversationSafetyService
    {
        public Task<ConversationSafetySnapshot> GetSnapshotAsync(Guid conversationId, CancellationToken cancellationToken) =>
            Task.FromResult(new ConversationSafetySnapshot(conversationId, 0, ConversationSafetyState.Active, null, 0));
        public Task<ConversationSafetyFlagResult> RecordConfirmedFlagAsync(Guid conversationId, ConfirmedSafetyFlag flag, CancellationToken cancellationToken) =>
            Task.FromResult(new ConversationSafetyFlagResult(true, false, new ConversationSafetySnapshot(conversationId, 1, ConversationSafetyState.Active, null, 1)));
        public Task EnsureMayActAsync(Guid conversationId, string operation, CancellationToken cancellationToken) => Task.CompletedTask;
    }

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

    private sealed class SingleToolOllama(ModelDescriptor model, string toolName) : IOllamaClient
    {
        private int _requests;
        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<IReadOnlyList<ModelDescriptor>> GetModelsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ModelDescriptor>>([model]);
        public async IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken) { await Task.CompletedTask; yield break; }
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken cancellationToken) => Task.FromResult(string.Empty);
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken cancellationToken)
        {
            _requests++;
            if (_requests == 1)
                return Task.FromResult(new OllamaToolResponse(string.Empty, [new OllamaToolCall(toolName, new Dictionary<string, JsonElement>())]));
            return Task.FromResult(new OllamaToolResponse("Waiting for the required approval.", []));
        }
    }

    private sealed class ChatMcpRepository(ExternalConnection connection) : IExternalConnectionRepository
    {
        private ExternalConnection? _connection = connection;
        public Task<IReadOnlyList<ExternalConnection>> GetAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ExternalConnection>>(_connection is null ? [] : [_connection]);
        public Task<ExternalConnection?> GetAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(_connection?.Id == id ? _connection : null);
        public Task UpsertAsync(ExternalConnection value, CancellationToken cancellationToken) { _connection = value; return Task.CompletedTask; }
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken) { if (_connection?.Id == id) _connection = null; return Task.CompletedTask; }

        public static ExternalConnection ReadyConnection()
        {
            var now = DateTimeOffset.UtcNow;
            return new ExternalConnection(Guid.NewGuid(), "Test MCP", "mcp.test", ExternalConnectionKind.Mcp, "custom-mcp", true, ExternalConnectionState.Ready, "Connected",
                JsonSerializer.Serialize(new McpConnectionConfiguration(McpTransportKind.StreamableHttp, "http://127.0.0.1:8765/mcp", LocalOnly: true)),
                "test-mcp", "1", "2026-07-28", now, now);
        }
    }

    private sealed class ChatMcpClient : IMcpConnectionClient
    {
        public int InvocationCount { get; private set; }
        public Task<(McpServerIdentity Identity, IReadOnlyList<McpExternalTool> Tools)> DiscoverAsync(ExternalConnection connection, CancellationToken cancellationToken)
        {
            using var document = JsonDocument.Parse("{\"type\":\"object\",\"properties\":{}}");
            IReadOnlyList<McpExternalTool> tools = [new McpExternalTool("write_item", "Write an external item", document.RootElement.Clone())];
            return Task.FromResult((new McpServerIdentity("test-mcp", "1", "2026-07-28", "{}"), tools));
        }
        public Task<McpToolInvocationResult> InvokeAsync(ExternalConnection connection, string toolName, IReadOnlyDictionary<string, JsonElement> arguments, CancellationToken cancellationToken)
        {
            InvocationCount++;
            return Task.FromResult(new McpToolInvocationResult(true, "Updated external item.", null, "[]"));
        }
    }

    private sealed class ChatRemediationRepository : IRemediationRepository
    {
        public RemediationRequest? Value { get; private set; }
        public Task UpsertAsync(RemediationRequest request, CancellationToken cancellationToken) { Value = request; return Task.CompletedTask; }
        public Task<RemediationRequest?> GetAsync(Guid remediationId, CancellationToken cancellationToken) => Task.FromResult(Value?.Id == remediationId ? Value : null);
        public Task<IReadOnlyList<RemediationRequest>> GetWaitingAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RemediationRequest>>(Value is { State: RemediationState.Waiting or RemediationState.InProgress or RemediationState.Suspended } value ? [value] : []);
    }

    private sealed class ChatRecordingSink : IExecutionEventSink
    {
        public List<ExecutionEvent> Events { get; } = [];
        public bool TryPublish(ExecutionEvent executionEvent) { Events.Add(executionEvent); return true; }
    }

    private sealed class ChatSecretStore : IProviderSecretStore
    {
        private readonly Dictionary<(string Provider, string Name), string> _values = [];
        public Task SetAsync(string providerId, string secretName, string secret, CancellationToken cancellationToken) { _values[(providerId, secretName)] = secret; return Task.CompletedTask; }
        public Task<string?> GetAsync(string providerId, string secretName, CancellationToken cancellationToken) =>
            Task.FromResult(_values.TryGetValue((providerId, secretName), out var value) ? value : null);
        public Task DeleteAsync(string providerId, string secretName, CancellationToken cancellationToken) { _values.Remove((providerId, secretName)); return Task.CompletedTask; }
    }

    /// <summary>
    /// Represents fake conversations and keeps its related state and behavior together.
    /// </summary>
    private sealed class FakeConversations : IConversationRepository
    {
        private readonly bool _recordMessages;
        public FakeConversations(bool recordMessages = false) => _recordMessages = recordMessages;
        public List<ChatMessage> Messages { get; } = [];
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
        public Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(Guid conversationId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ChatMessage>>(_recordMessages ? Messages.Where(message => message.ConversationId == conversationId).ToArray() : []);
        /// <summary>
        /// Performs upsert conversation asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task UpsertConversationAsync(Conversation conversation, CancellationToken cancellationToken) => Task.CompletedTask;
        /// <summary>
        /// Performs add message asynchronously so I/O does not block the caller's thread.
        /// </summary>
        public Task AddMessageAsync(ChatMessage message, CancellationToken cancellationToken)
        {
            if (_recordMessages) Messages.Add(message);
            return Task.CompletedTask;
        }
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
