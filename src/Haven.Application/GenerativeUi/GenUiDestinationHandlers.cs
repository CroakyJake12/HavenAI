using System.Collections.Concurrent;
using System.Text.Json;
using Haven.Core;

namespace Haven.Application;

/// <summary>
/// Routes semantic events to the owning Haven App's business logic.
/// App-owned events mutate authoritative App state rather than maintaining
/// disconnected template-local copies.
/// </summary>
public sealed class GenUiAppEventHandler : IGenUiEventHandler
{
    private readonly ConcurrentDictionary<string, Func<GenUiEvent, GenUiActionBinding, CancellationToken, Task<GenUiActionResult>>> _handlers =
        new(StringComparer.OrdinalIgnoreCase);

    public GenUiRouteKind RouteKind => GenUiRouteKind.App;

    public bool CanHandle(string targetKey) => _handlers.ContainsKey(targetKey);

    public void Register(string appKey, Func<GenUiEvent, GenUiActionBinding, CancellationToken, Task<GenUiActionResult>> handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appKey);
        ArgumentNullException.ThrowIfNull(handler);
        _handlers[appKey] = handler;
    }

    public Task<GenUiActionResult> HandleAsync(GenUiEvent semanticEvent, GenUiActionBinding binding, CancellationToken cancellationToken)
    {
        if (_handlers.TryGetValue(binding.TargetKey, out var handler))
            return handler(semanticEvent, binding, cancellationToken);

        return Task.FromResult(GenerativeUiEventRouter.Result(
            semanticEvent, GenUiActionStatus.Unavailable,
            $"App handler '{binding.TargetKey}' is not registered."));
    }
}

/// <summary>
/// Routes semantic events that require agent reasoning, generation, adaptation
/// or interpretation back to the active Haven agent through a structured feedback channel.
/// </summary>
public sealed class GenUiAgentEventHandler : IGenUiEventHandler
{
    private readonly IGenUiAgentFeedbackChannel _feedback;

    public GenUiRouteKind RouteKind => GenUiRouteKind.Agent;

    public bool CanHandle(string targetKey) => true;

    public GenUiAgentEventHandler(IGenUiAgentFeedbackChannel feedback) => _feedback = feedback;

    public async Task<GenUiActionResult> HandleAsync(GenUiEvent semanticEvent, GenUiActionBinding binding, CancellationToken cancellationToken)
    {
        var feedbackResult = await _feedback.SubmitEventAsync(semanticEvent, binding, cancellationToken).ConfigureAwait(false);
        return feedbackResult;
    }
}

/// <summary>
/// Routes semantic events to registered Haven capabilities through the Capability Registry.
/// Capability events use the normal capability/permission architecture.
/// </summary>
public sealed class GenUiCapabilityEventHandler : IGenUiEventHandler
{
    private readonly ConcurrentDictionary<string, Func<GenUiEvent, GenUiActionBinding, CancellationToken, Task<GenUiActionResult>>> _handlers =
        new(StringComparer.OrdinalIgnoreCase);

    public GenUiRouteKind RouteKind => GenUiRouteKind.Capability;

    public bool CanHandle(string targetKey) => _handlers.ContainsKey(targetKey);

    public void Register(string capabilityKey, Func<GenUiEvent, GenUiActionBinding, CancellationToken, Task<GenUiActionResult>> handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(capabilityKey);
        ArgumentNullException.ThrowIfNull(handler);
        _handlers[capabilityKey] = handler;
    }

    public Task<GenUiActionResult> HandleAsync(GenUiEvent semanticEvent, GenUiActionBinding binding, CancellationToken cancellationToken)
    {
        if (_handlers.TryGetValue(binding.TargetKey, out var handler))
            return handler(semanticEvent, binding, cancellationToken);

        return Task.FromResult(GenerativeUiEventRouter.Result(
            semanticEvent, GenUiActionStatus.Unavailable,
            $"Capability handler '{binding.TargetKey}' is not registered."));
    }
}

/// <summary>
/// Routes semantic events to authorised external integrations.
/// External events pass through Haven's normal permission/sandbox architecture.
/// </summary>
public sealed class GenUiExternalEventHandler : IGenUiEventHandler
{
    private readonly ConcurrentDictionary<string, Func<GenUiEvent, GenUiActionBinding, CancellationToken, Task<GenUiActionResult>>> _handlers =
        new(StringComparer.OrdinalIgnoreCase);

    public GenUiRouteKind RouteKind => GenUiRouteKind.External;

    public bool CanHandle(string targetKey) => _handlers.ContainsKey(targetKey);

    public void Register(string externalKey, Func<GenUiEvent, GenUiActionBinding, CancellationToken, Task<GenUiActionResult>> handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(externalKey);
        ArgumentNullException.ThrowIfNull(handler);
        _handlers[externalKey] = handler;
    }

    public Task<GenUiActionResult> HandleAsync(GenUiEvent semanticEvent, GenUiActionBinding binding, CancellationToken cancellationToken)
    {
        if (_handlers.TryGetValue(binding.TargetKey, out var handler))
            return handler(semanticEvent, binding, cancellationToken);

        return Task.FromResult(GenerativeUiEventRouter.Result(
            semanticEvent, GenUiActionStatus.Unavailable,
            $"External handler '{binding.TargetKey}' is not registered."));
    }
}
