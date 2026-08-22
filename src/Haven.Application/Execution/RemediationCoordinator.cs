using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Haven.Core;

namespace Haven.Application;

/// <summary>Persists resumable blockers while ensuring expensive work is not held open.</summary>
public sealed class RemediationCoordinator(
    IRemediationRepository repository,
    IProviderSecretStore secrets,
    IExecutionEventSink events,
    TimeProvider? timeProvider = null) : IDisposable
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _waiters = new();
    private int _disposed;

    public async Task<RemediationRequest> RequestAsync(RemediationRequest request, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, this);
        Validate(request);
        var now = _time.GetUtcNow();
        var waiting = request with
        {
            State = RemediationState.Waiting,
            LastActivityAt = now,
            ExpiresAt = now + request.IdleTimeout,
            CredentialReference = null
        };
        await repository.UpsertAsync(waiting, cancellationToken).ConfigureAwait(false);
        events.TryPublish(new ExecutionEvent(
            Guid.NewGuid(), waiting.ExecutionId, Guid.NewGuid(), waiting.ActionId, ExecutionOrigin.Haven,
            ExecutionActionType.UserActionRequired, ExecutionActionStatus.UserActionRequired,
            waiting.Title, null, waiting.Explanation, waiting.RequestingComponentId, now,
            RemediationId: waiting.Id));
        StartIdleWatch(waiting);
        return waiting;
    }

    public async Task RecordInteractionAsync(Guid remediationId, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, this);
        var request = await RequireAsync(remediationId, cancellationToken).ConfigureAwait(false);
        if (request.State is not (RemediationState.Waiting or RemediationState.InProgress)) return;
        var now = _time.GetUtcNow();
        var maximum = request.CreatedAt + request.MaximumWait;
        var expires = now + request.IdleTimeout;
        if (expires > maximum) expires = maximum;
        var updated = request with { State = RemediationState.InProgress, LastActivityAt = now, ExpiresAt = expires };
        await repository.UpsertAsync(updated, cancellationToken).ConfigureAwait(false);
        StartIdleWatch(updated);
    }

    public async Task<RemediationRequest> SaveSecretAndResolveAsync(
        Guid remediationId,
        string secretName,
        string secretValue,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, this);
        var request = await RequireAsync(remediationId, cancellationToken).ConfigureAwait(false);
        if (request.Type != RemediationType.SecretInput || request.Sensitivity != RemediationSensitivity.Secret)
            throw new InvalidOperationException("This remediation is not a host-owned secret request.");
        if (string.IsNullOrWhiteSpace(secretValue)) throw new ArgumentException("A secret value is required.", nameof(secretValue));
        if (!request.RequiredInputs.Any(input => input.Key.Equals(secretName, StringComparison.Ordinal)
                                                  && input.Sensitivity == RemediationSensitivity.Secret))
            throw new UnauthorizedAccessException("The requested secret field was not declared by this remediation.");
        var providerKey = $"extension:{request.RequestingComponentId}";
        var previous = await secrets.GetAsync(providerKey, secretName, cancellationToken).ConfigureAwait(false);
        await secrets.SetAsync(providerKey, secretName, secretValue, cancellationToken).ConfigureAwait(false);
        var credentialReference = CreateCredentialReference(providerKey, secretName);
        var now = _time.GetUtcNow();
        var completed = request with
        {
            State = RemediationState.Completed,
            LastActivityAt = now,
            ExpiresAt = null,
            CredentialReference = credentialReference
        };
        try { await repository.UpsertAsync(completed, cancellationToken).ConfigureAwait(false); }
        catch
        {
            if (previous is null) await secrets.DeleteAsync(providerKey, secretName, CancellationToken.None).ConfigureAwait(false);
            else await secrets.SetAsync(providerKey, secretName, previous, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        StopWaiter(remediationId);
        events.TryPublish(new ExecutionEvent(
            Guid.NewGuid(), request.ExecutionId, Guid.NewGuid(), request.ActionId, ExecutionOrigin.Haven,
            ExecutionActionType.AutomaticRepair, ExecutionActionStatus.Completed, "Credential configured",
            "The required credential was stored by Haven's secure credential service.", null,
            request.RequestingComponentId, now, now, now, RecoveryOfActionId: request.ActionId,
            RemediationId: request.Id));
        return completed;
    }

    private void StartIdleWatch(RemediationRequest request)
    {
        StopWaiter(request.Id);
        var waiter = new CancellationTokenSource();
        _waiters[request.Id] = waiter;
        _ = WatchIdleAsync(request.Id, request.ExpiresAt ?? _time.GetUtcNow() + request.IdleTimeout, waiter);
    }

    private async Task WatchIdleAsync(Guid remediationId, DateTimeOffset expiresAt, CancellationTokenSource waiter)
    {
        var cancellationToken = waiter.Token;
        try
        {
            var delay = expiresAt - _time.GetUtcNow();
            if (delay > TimeSpan.Zero) await Task.Delay(delay, _time, cancellationToken).ConfigureAwait(false);
            var current = await repository.GetAsync(remediationId, cancellationToken).ConfigureAwait(false);
            if (current is null || current.State is not (RemediationState.Waiting or RemediationState.InProgress) || current.ExpiresAt > _time.GetUtcNow()) return;
            var suspended = current with { State = RemediationState.Suspended, LastActivityAt = _time.GetUtcNow() };
            await repository.UpsertAsync(suspended, cancellationToken).ConfigureAwait(false);
            events.TryPublish(new ExecutionEvent(
                Guid.NewGuid(), current.ExecutionId, Guid.NewGuid(), current.ActionId, ExecutionOrigin.Haven,
                ExecutionActionType.Warning, ExecutionActionStatus.Suspended, "Waiting timed out", null,
                $"Haven stopped waiting after {(int)current.IdleTimeout.TotalSeconds} seconds. Resolve and resume later.",
                current.RequestingComponentId, _time.GetUtcNow(), RemediationId: current.Id));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        finally
        {
            ((ICollection<KeyValuePair<Guid, CancellationTokenSource>>)_waiters)
                .Remove(new KeyValuePair<Guid, CancellationTokenSource>(remediationId, waiter));
            waiter.Dispose();
        }
    }

    private async Task<RemediationRequest> RequireAsync(Guid id, CancellationToken cancellationToken) =>
        await repository.GetAsync(id, cancellationToken).ConfigureAwait(false)
        ?? throw new KeyNotFoundException("Remediation was not found.");

    private void StopWaiter(Guid id)
    {
        if (!_waiters.TryRemove(id, out var waiter)) return;
        waiter.Cancel();
        waiter.Dispose();
    }

    private static void Validate(RemediationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.IdleTimeout < TimeSpan.FromSeconds(30) || request.IdleTimeout > TimeSpan.FromSeconds(60))
            throw new ArgumentOutOfRangeException(nameof(request), "Interactive idle timeout must be between 30 and 60 seconds.");
        if (request.MaximumWait < request.IdleTimeout || request.MaximumWait > RecoveryPolicyDefaults.MaximumInteractiveWait)
            throw new ArgumentOutOfRangeException(nameof(request), "Maximum wait must be bounded and at least the idle timeout.");
        if (request.Sensitivity == RemediationSensitivity.Secret && request.Type != RemediationType.SecretInput)
            throw new InvalidOperationException("Secret remediation must use the host-owned secret input type.");
    }

    private static string CreateCredentialReference(string providerKey, string secretName)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(providerKey + "\n" + secretName));
        return "credential-ref:" + Convert.ToHexString(hash.AsSpan(0, 12)).ToLowerInvariant();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        foreach (var id in _waiters.Keys.ToArray()) StopWaiter(id);
    }
}
