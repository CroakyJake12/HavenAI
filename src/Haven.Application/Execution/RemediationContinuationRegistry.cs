using System.Collections.Concurrent;
using Haven.Core;

namespace Haven.Application;

public sealed record RemediationResolution(bool Approved = false, string? CredentialReference = null);

public sealed record RemediationContinuationResult(bool Succeeded, string SafeSummary, string? SafeError = null);

/// <summary>
/// Keeps resumable actions in memory without persisting raw tool arguments. Continuations are one-shot and can survive
/// the remediation idle timeout for as long as the Haven process remains alive.
/// </summary>
public sealed class RemediationContinuationRegistry
{
    private readonly ConcurrentDictionary<Guid, Func<RemediationResolution, CancellationToken, Task<RemediationContinuationResult>>> _continuations = new();

    public bool Contains(Guid remediationId) => _continuations.ContainsKey(remediationId);

    public void Register(
        Guid remediationId,
        Func<RemediationResolution, CancellationToken, Task<RemediationContinuationResult>> continuation)
    {
        ArgumentNullException.ThrowIfNull(continuation);
        if (!_continuations.TryAdd(remediationId, continuation))
            throw new InvalidOperationException("A continuation is already registered for this remediation.");
    }

    public void Remove(Guid remediationId) => _continuations.TryRemove(remediationId, out _);

    public async Task<RemediationContinuationResult?> TryResumeAsync(
        Guid remediationId,
        RemediationResolution resolution,
        CancellationToken cancellationToken)
    {
        if (!_continuations.TryRemove(remediationId, out var continuation)) return null;
        try
        {
            return await continuation(resolution, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _continuations.TryAdd(remediationId, continuation);
            throw;
        }
        catch (Exception ex)
        {
            return new RemediationContinuationResult(false, "The blocked action could not be resumed.", SensitiveTextRedactor.Redact(ex.Message, 1_000));
        }
    }
}
