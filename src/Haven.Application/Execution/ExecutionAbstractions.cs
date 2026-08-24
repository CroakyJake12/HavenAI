using Haven.Core;

namespace Haven.Application;

public interface IExecutionEventSink
{
    bool TryPublish(ExecutionEvent executionEvent);
}

public interface IExecutionEventRepository
{
    Task AppendAsync(IReadOnlyList<ExecutionEvent> events, CancellationToken cancellationToken);
    Task<IReadOnlyList<ExecutionEvent>> GetExecutionAsync(Guid executionId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ExecutionSummary>> SearchExecutionsAsync(string? query, int limit, CancellationToken cancellationToken);
}

public interface IActionFeedbackRepository
{
    Task UpsertAsync(ActionFeedback feedback, CancellationToken cancellationToken);
    Task<ActionFeedback?> GetAsync(Guid executionId, Guid actionId, CancellationToken cancellationToken);
    Task DeleteAsync(Guid feedbackId, CancellationToken cancellationToken);
}

public interface IRemediationRepository
{
    Task UpsertAsync(RemediationRequest request, CancellationToken cancellationToken);
    Task<RemediationRequest?> GetAsync(Guid remediationId, CancellationToken cancellationToken);
    Task<IReadOnlyList<RemediationRequest>> GetWaitingAsync(CancellationToken cancellationToken);
}

public interface IExternalAgentTaskRepository
{
    Task CreateAsync(ExternalAgentTask task, CancellationToken cancellationToken);
    Task<ExternalAgentTask?> GetByLocatorAsync(HavenTaskLocator locator, CancellationToken cancellationToken);
    Task<ExternalAgentTask?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<ExternalAgentTask>> GetRecentAsync(int limit, CancellationToken cancellationToken);
    Task<ExternalTaskClaim?> TryClaimAsync(
        Guid taskId,
        string claimant,
        string leaseTokenHash,
        string returnedLeaseToken,
        DateTimeOffset now,
        DateTimeOffset leaseExpiresAt,
        CancellationToken cancellationToken);
    Task<bool> TryUpdateClaimedAsync(
        Guid taskId,
        string leaseTokenHash,
        HavenTaskStatus status,
        string? safeProgress,
        string? safeResult,
        string? safeError,
        string? idempotencyKey,
        DateTimeOffset now,
        CancellationToken cancellationToken);
    Task<bool> TryCancelAsync(Guid taskId, Guid ownerUserId, DateTimeOffset now, CancellationToken cancellationToken);
}

public interface IHavenNotificationRepository
{
    Task UpsertAsync(HavenNotification notification, CancellationToken cancellationToken);
    Task<IReadOnlyList<HavenNotification>> GetRecentAsync(int limit, bool includeDismissed, CancellationToken cancellationToken);
    Task SetReadAsync(Guid id, bool isRead, CancellationToken cancellationToken);
    Task DismissAsync(Guid id, CancellationToken cancellationToken);
}

public interface IWorkspaceSessionRepository
{
    Task<WorkspaceSessionSnapshot?> LoadAsync(CancellationToken cancellationToken);
    Task SaveAsync(WorkspaceSessionSnapshot snapshot, CancellationToken cancellationToken);
}

public interface IExtensionRepository
{
    Task<IReadOnlyList<ExtensionSource>> GetSourcesAsync(CancellationToken cancellationToken);
    Task UpsertSourceAsync(ExtensionSource source, CancellationToken cancellationToken);
    Task DeleteSourceAsync(Guid sourceId, CancellationToken cancellationToken);
    Task<IReadOnlyList<InstalledExtensionPackage>> GetInstalledAsync(CancellationToken cancellationToken);
    Task UpsertInstalledAsync(InstalledExtensionPackage package, CancellationToken cancellationToken);
    Task DeleteInstalledAsync(Guid packageId, CancellationToken cancellationToken);
}

public interface IExtensionSourceTransport
{
    Task<string> MaterializeAsync(ExtensionSource source, string destination, CancellationToken cancellationToken);
}

public interface INativePluginProcess : IAsyncDisposable
{
    string PackageId { get; }
    Task StartAsync(CancellationToken cancellationToken);
    Task<string> InvokeAsync(string capabilityId, string redactedArgumentsJson, CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}

public interface INativePluginProcessFactory
{
    INativePluginProcess Create(InstalledExtensionPackage package);
}
