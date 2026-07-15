using Haven.Core;

namespace Haven.Application;

public interface IConversationRepository
{
    Task<IReadOnlyList<Conversation>> GetRecentAsync(HavenMode? mode, int limit, CancellationToken cancellationToken);
    async Task<IReadOnlyList<Conversation>> GetRecentInScopeAsync(ConversationScope scope, int limit, CancellationToken cancellationToken)
    {
        var rows = await GetRecentAsync(scope.Mode, limit, cancellationToken).ConfigureAwait(false);
        return rows.Where(scope.Matches).Take(limit).ToArray();
    }
    Task<IReadOnlyList<Conversation>> GetArchivedAsync(HavenMode? mode, int limit, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Conversation>>([]);
    Task<Conversation?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(Guid conversationId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ChatMessage>> GetContextMessagesAsync(Guid conversationId, CancellationToken cancellationToken) => GetMessagesAsync(conversationId, cancellationToken);
    Task UpsertConversationAsync(Conversation conversation, CancellationToken cancellationToken);
    Task AddMessageAsync(ChatMessage message, CancellationToken cancellationToken);
    Task MarkMessagesCompactedAsync(Guid conversationId, IReadOnlyCollection<Guid> messageIds, CancellationToken cancellationToken) => Task.CompletedTask;
    Task<IReadOnlyList<ConversationContextEntry>> GetContextEntriesAsync(Guid conversationId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ConversationContextEntry>>([]);
    Task AddContextEntryAsync(ConversationContextEntry entry, CancellationToken cancellationToken) => Task.CompletedTask;
    Task DeleteConversationAsync(Guid id, CancellationToken cancellationToken);
}

public interface IContainerRepository
{
    Task<IReadOnlyList<ContainerDefinition>> GetByModeAsync(HavenMode mode, CancellationToken cancellationToken);
    Task<IReadOnlyList<ContainerDefinition>> GetArchivedByModeAsync(HavenMode mode, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ContainerDefinition>>([]);
    Task UpsertAsync(ContainerDefinition item, CancellationToken cancellationToken);
    Task<Lesson> CreateSubjectAsync(ContainerDefinition subject, CancellationToken cancellationToken);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
    Task DeleteAndDetachConversationsAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<Lesson>> GetLessonsAsync(Guid subjectId, CancellationToken cancellationToken);
    Task UpsertLessonAsync(Lesson lesson, CancellationToken cancellationToken);
    Task DeleteLessonAsync(Guid id, CancellationToken cancellationToken);
}

public interface IContainerResourceRepository
{
    Task<IReadOnlyList<ContainerResource>> GetByContainerAsync(Guid containerId, CancellationToken cancellationToken);
    Task<ContainerResource> AddAsync(Guid containerId, string sourcePath, CancellationToken cancellationToken);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
    string GetStoredPath(ContainerResource resource);
    Task<string> BuildPromptContextAsync(Guid containerId, CancellationToken cancellationToken);
}

public interface ICatalogRepository
{
    Task<IReadOnlyList<AgentDefinition>> GetAgentsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<PluginDefinition>> GetPluginsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<PromptDefinition>> GetPromptsAsync(CancellationToken cancellationToken);
    Task UpsertAgentAsync(AgentDefinition agent, CancellationToken cancellationToken);
    Task UpsertPluginAsync(PluginDefinition plugin, CancellationToken cancellationToken);
    Task UpsertPromptAsync(PromptDefinition prompt, CancellationToken cancellationToken);
    Task SetAgentEnabledAsync(Guid id, bool enabled, CancellationToken cancellationToken);
    Task SetPluginEnabledAsync(Guid id, bool enabled, CancellationToken cancellationToken);
    Task SetPromptEnabledAsync(Guid id, bool enabled, CancellationToken cancellationToken);
    Task DeleteCustomAgentAsync(Guid id, CancellationToken cancellationToken);
    Task DeleteCustomPluginAsync(Guid id, CancellationToken cancellationToken);
    Task DeleteCustomPromptAsync(Guid id, CancellationToken cancellationToken);
}

public interface IWorkspaceStateRepository
{
    Task<IReadOnlyList<MacroDefinition>> GetMacrosAsync(Guid? containerId, CancellationToken cancellationToken);
    Task UpsertMacroAsync(MacroDefinition macro, CancellationToken cancellationToken);
    Task DeleteMacroAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<WorkspaceVersion>> GetVersionsAsync(Guid? containerId, string? relativePath, int limit, CancellationToken cancellationToken);
    Task AddVersionAsync(WorkspaceVersion version, CancellationToken cancellationToken);
    Task<IReadOnlyList<DecisionRecord>> GetDecisionsAsync(Guid containerId, CancellationToken cancellationToken);
    Task UpsertDecisionAsync(DecisionRecord decision, CancellationToken cancellationToken);
    Task DeleteDecisionAsync(Guid id, CancellationToken cancellationToken);
}

public interface IProjectIntelligenceService
{
    Task<IReadOnlyList<ProjectDiscoveryItem>> ScanAsync(string root, CancellationToken cancellationToken);
    Task<ProjectStateSnapshot> GetStateAsync(string root, CancellationToken cancellationToken);
    Task<ReleaseRiskReport> ForecastReleaseRiskAsync(string root, CancellationToken cancellationToken);
    Task<string> FindIntentMatchesAsync(string root, string intent, CancellationToken cancellationToken);
    Task<ProcessResult> RunBuildAsync(string root, CancellationToken cancellationToken);
    Task<ProcessResult> RunTestsAsync(string root, CancellationToken cancellationToken);
    Task<ProcessResult> InitializeGitAsync(string root, CancellationToken cancellationToken);
    Task<ProcessResult> ConnectGitRemoteAsync(string root, string remoteUrl, CancellationToken cancellationToken);
    Task<ProcessResult> RunBugTimeMachineAsync(string root, string reproductionCommand, CancellationToken cancellationToken);
    Task LaunchEditorAsync(string root, CancellationToken cancellationToken);
    Task LaunchTerminalAsync(string root, CancellationToken cancellationToken);
    Task LaunchLocalServerAsync(string root, CancellationToken cancellationToken);
}

public sealed record ProjectDiscoveryItem(string Name, string RootPath, string EntryPath, string Kind, string Category);

public interface IAutomationRepository
{
    Task<IReadOnlyList<AutomationDefinition>> GetAllAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<AutomationDefinition>> GetDueAsync(DateTimeOffset now, CancellationToken cancellationToken);
    Task UpsertAsync(AutomationDefinition automation, CancellationToken cancellationToken);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
    Task<bool> TryAcquireLeaseAsync(Guid automationId, string leaseToken, DateTimeOffset leaseUntil, CancellationToken cancellationToken);
    Task CompleteRunAsync(AutomationRun run, DateTimeOffset? nextRunAt, CancellationToken cancellationToken);
    Task<IReadOnlyList<AutomationRun>> GetRunsAsync(Guid automationId, int limit, CancellationToken cancellationToken);
}

public interface ITrainingRepository
{
    Task UpsertRunAsync(TrainingRun run, CancellationToken cancellationToken);
    Task<TrainingRun?> GetRunAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<TrainingRun>> GetRecentRunsAsync(int limit, CancellationToken cancellationToken);
    Task DeleteRunAsync(Guid id, CancellationToken cancellationToken);
    Task UpsertAttemptAsync(TrainingAttempt attempt, CancellationToken cancellationToken);
    Task<IReadOnlyList<TrainingAttempt>> GetAttemptsAsync(Guid runId, CancellationToken cancellationToken);
}

public interface IOllamaClient
{
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<ModelDescriptor>> GetModelsAsync(CancellationToken cancellationToken);
    IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request, CancellationToken cancellationToken);
    Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken cancellationToken);
    Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken cancellationToken);
    Task PullModelAsync(string model, IProgress<double>? progress, CancellationToken cancellationToken) => Task.FromException(new NotSupportedException("This model provider does not support model installation."));
    Task DeleteModelAsync(string model, CancellationToken cancellationToken) => Task.FromException(new NotSupportedException("This model provider does not support model removal."));
}

public sealed record GenerationOptions(double Temperature = 0.7, int ContextLimit = 32768, int ActionLimit = 24);

public sealed record OllamaChatRequest(
    string Model,
    IReadOnlyList<OllamaMessage> Messages,
    EffortLevel Effort,
    string? SystemPrompt = null,
    bool EnableTools = false,
    GenerationOptions? Options = null);

public sealed record OllamaMessage(string Role, string Content, IReadOnlyList<string>? Images = null);

public sealed record OllamaToolDefinition(
    string Name,
    string Description,
    IReadOnlyDictionary<string, object> Properties,
    IReadOnlyList<string> Required);

public sealed record OllamaToolCall(string Name, IReadOnlyDictionary<string, System.Text.Json.JsonElement> Arguments);

public sealed record OllamaToolTurn(
    string Role,
    string Content,
    IReadOnlyList<OllamaToolCall>? ToolCalls = null,
    string? ToolName = null,
    IReadOnlyList<string>? Images = null);

public sealed record OllamaToolRequest(
    string Model,
    IReadOnlyList<OllamaToolTurn> Messages,
    IReadOnlyList<OllamaToolDefinition> Tools,
    EffortLevel Effort,
    string? SystemPrompt = null,
    GenerationOptions? Options = null);

public sealed record OllamaToolResponse(string Content, IReadOnlyList<OllamaToolCall> ToolCalls);

public interface IWorkspaceToolService
{
    string ResolveWorkspacePath(string workspaceRoot, string relativePath);
    Task<string> ReadTextAsync(string workspaceRoot, string relativePath, CancellationToken cancellationToken);
    Task WriteTextAtomicAsync(string workspaceRoot, string relativePath, string content, CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> SearchFilesAsync(string workspaceRoot, string searchPattern, CancellationToken cancellationToken);
    Task<ProcessResult> RunProcessAsync(ProcessRequest request, CancellationToken cancellationToken);
}

public interface IComputerToolService
{
    Task<string> SnapshotAsync(CancellationToken cancellationToken);
    Task<string> ListWindowsAsync(CancellationToken cancellationToken);
    Task<string> LaunchAppAsync(string name, CancellationToken cancellationToken);
    Task<string> FocusWindowAsync(string title, CancellationToken cancellationToken);
    Task<string> InvokeAsync(string windowTitle, string name, string automationId, CancellationToken cancellationToken);
    Task<string> ClickAsync(string windowTitle, int x, int y, string button, CancellationToken cancellationToken);
    Task<string> TypeAsync(string windowTitle, string text, CancellationToken cancellationToken);
    Task<string> PressAsync(string windowTitle, string keys, CancellationToken cancellationToken);
    Task<string> CloseWindowAsync(string title, CancellationToken cancellationToken);
}

public interface IBrowserToolService
{
    bool IsInteractiveAvailable => false;
    Task<string> NavigateAsync(string address, CancellationToken cancellationToken);
    Task<string> BackAsync(CancellationToken cancellationToken);
    Task<string> ForwardAsync(CancellationToken cancellationToken);
    Task<string> ReloadAsync(bool clearSiteCache, CancellationToken cancellationToken);
    Task<string> ReadVisibleTextAsync(CancellationToken cancellationToken);
    Task<string> ClickAsync(string selector, CancellationToken cancellationToken);
    Task<string> ClickTextAsync(string text, CancellationToken cancellationToken);
    Task<string> FillAsync(string selector, string value, CancellationToken cancellationToken);
    Task<string> ScrollAsync(double x, double y, CancellationToken cancellationToken);
}

public sealed record ProcessRequest(string FileName, string Arguments, string WorkingDirectory, TimeSpan Timeout, IReadOnlyDictionary<string, string>? Environment = null, bool DetachGui = false);
public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError, TimeSpan Duration, bool TimedOut);

public interface ILegacyStateMigrator
{
    Task<LegacyMigrationResult> MigrateIfNeededAsync(CancellationToken cancellationToken);
}

public sealed record LegacyMigrationResult(bool Attempted, bool Imported, int ConversationCount, int MessageCount, string? Note);

public interface IModeRegistry
{
    Task<IReadOnlyList<ModeDefinition>> GetModesAsync(CancellationToken cancellationToken);
    Task<ModeDefinition?> GetModeByKeyAsync(string key, CancellationToken cancellationToken);
    Task<ModeDefinition?> GetModeByIdAsync(Guid id, CancellationToken cancellationToken);
    Task UpsertModeAsync(ModeDefinition mode, CancellationToken cancellationToken);
    Task<IReadOnlyList<ModeVersion>> GetVersionsAsync(Guid modeId, CancellationToken cancellationToken);
    Task AddVersionAsync(ModeVersion version, CancellationToken cancellationToken);
    Task<IReadOnlyList<ModePermissionGrant>> GetGrantsAsync(Guid modeId, CancellationToken cancellationToken);
    Task UpsertGrantAsync(ModePermissionGrant grant, CancellationToken cancellationToken);
}

public interface IModeUsageRepository
{
    Task RecordUsageAsync(Guid modeId, DateOnly date, CancellationToken cancellationToken);
    Task<IReadOnlyList<ModeUsage>> GetRecentUsageAsync(int days, CancellationToken cancellationToken);
    Task<int> GetTotalUseCountAsync(Guid modeId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ModeUsage>> GetUsageByModeAsync(Guid modeId, int limit, CancellationToken cancellationToken);
}

public interface IPinRepository
{
    Task<IReadOnlyList<ModePin>> GetPinsAsync(CancellationToken cancellationToken);
    Task UpsertPinAsync(ModePin pin, CancellationToken cancellationToken);
    Task DeletePinAsync(Guid modeId, CancellationToken cancellationToken);
}

public interface ISurfaceRouter
{
    Task<SurfaceKind> ResolveSurfaceAsync(string intent, HavenMode currentMode, CancellationToken cancellationToken);
    Task<IReadOnlyList<SurfaceRun>> GetRecentRunsAsync(int limit, CancellationToken cancellationToken);
    Task RecordRunAsync(SurfaceRun run, CancellationToken cancellationToken);
}

public interface IModeIntentRouter
{
    Task<IntentClassification> ClassifyAsync(string prompt, HavenMode currentMode, string? workspaceRoot, CancellationToken cancellationToken);
    Task<ModeSlot?> ResolveModeAsync(string prompt, HavenMode currentMode, string? workspaceRoot, CancellationToken cancellationToken);
}

public interface IActivityLogRepository
{
    Task<IReadOnlyList<ActivityEvent>> GetRecentAsync(int limit, CancellationToken cancellationToken);
    Task AddEventAsync(ActivityEvent activityEvent, CancellationToken cancellationToken);
}

public interface IConversationMoveRepository
{
    Task RecordMoveAsync(ConversationMove move, CancellationToken cancellationToken);
    Task<IReadOnlyList<ConversationMove>> GetMovesAsync(Guid conversationId, CancellationToken cancellationToken);
}

public interface ICompanionDockService
{
    Task<bool> IsDockedAsync(Guid conversationId, CancellationToken cancellationToken);
    Task DockAsync(Guid conversationId, SurfaceKind surface, CancellationToken cancellationToken);
    Task UndockAsync(Guid conversationId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Guid>> GetDockedConversationsAsync(SurfaceKind surface, CancellationToken cancellationToken);
}

public interface IBrowserTabHostManager
{
    Task<int> GetActiveTabCountAsync(CancellationToken cancellationToken);
    Task<bool> CanCompleteAsync(CancellationToken cancellationToken);
    Task<string> GetCompletionSummaryAsync(CancellationToken cancellationToken);
}

public interface IPlatformShellService
{
    Task OpenExternalAsync(string url, CancellationToken cancellationToken);
    Task<string> GetClipboardTextAsync(CancellationToken cancellationToken);
    Task SetClipboardTextAsync(string text, CancellationToken cancellationToken);
}

public interface IAppDiagnostics
{
    Task<IReadOnlyDictionary<string, string>> GetDiagnosticsAsync(CancellationToken cancellationToken);
    Task RecordErrorAsync(string component, string error, string? detail, CancellationToken cancellationToken);
    Task<IReadOnlyList<(string Component, string Error, DateTimeOffset Timestamp)>> GetRecentErrorsAsync(int limit, CancellationToken cancellationToken);
}

public interface IAppCommandRegistry
{
    void Register(string key, string label, string description, Action execute);
    IReadOnlyList<(string Key, string Label, string Description)> GetAll();
}

public interface IAppDatabase
{
    Task InitializeAsync(CancellationToken cancellationToken);
}

public interface IAppPaths
{
    string DataDirectory { get; }
    string DatabasePath { get; }
    string BrowserProfileDirectory { get; }
    string AttachmentsDirectory { get; }
    string LogsDirectory { get; }
    string LegacyStatePath { get; }
}
