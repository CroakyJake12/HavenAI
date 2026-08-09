using System.Collections.Concurrent;
using System.Text.Json;
using Haven.Core;

namespace Haven.Application;

/// <summary>Destination adapter; capability adapters must call Haven's normal capability/permission runtime.</summary>
public interface IGenUiEventHandler
{
    GenUiRouteKind RouteKind { get; }
    bool CanHandle(string targetKey);
    Task<GenUiActionResult> HandleAsync(
        GenUiEvent semanticEvent,
        GenUiActionBinding binding,
        CancellationToken cancellationToken);
}

public interface IGenUiEventAuditSink
{
    ValueTask RecordAsync(GenUiEvent semanticEvent, GenUiActionResult result, CancellationToken cancellationToken);
}

/// <summary>
/// Routes meaningful UI interaction to the cheapest authoritative destination.
/// It never treats a click as permission and never converts a structured event
/// into a synthetic natural-language user message.
/// </summary>
public sealed class GenerativeUiEventRouter(
    IEnumerable<IGenUiEventHandler> handlers,
    IGenUiEventAuditSink audit,
    GenUiInstanceStore instances)
{
    private readonly IReadOnlyList<IGenUiEventHandler> _handlers = handlers.ToArray();

    public async Task<GenUiActionResult> RouteAsync(
        GenUiEvent semanticEvent,
        GenUiActionBinding binding,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(semanticEvent);
        ArgumentNullException.ThrowIfNull(binding);
        var errors = GenerativeUiContractValidator.Validate(semanticEvent);
        if (errors.Count > 0) throw new InvalidOperationException(string.Join(" ", errors));
        if (!binding.ActionId.Equals(semanticEvent.ActionId, StringComparison.Ordinal))
            throw new InvalidOperationException("Event action ID does not match its registered binding.");

        var handler = _handlers.FirstOrDefault(candidate =>
            candidate.RouteKind == binding.Route && candidate.CanHandle(binding.TargetKey));
        var result = handler is null
            ? Result(semanticEvent, GenUiActionStatus.Unavailable,
                $"No {binding.Route} handler is registered for '{binding.TargetKey}'.")
            : await handler.HandleAsync(semanticEvent, binding, cancellationToken).ConfigureAwait(false);

        if (result.EventId != semanticEvent.EventId || result.Origin != semanticEvent.Origin)
            throw new InvalidOperationException("Action result lost the originating event or instance identity.");

        await instances.ApplyResultAsync(result, cancellationToken).ConfigureAwait(false);
        await audit.RecordAsync(semanticEvent, result, cancellationToken).ConfigureAwait(false);
        return result;
    }

    public static GenUiActionResult Result(
        GenUiEvent semanticEvent,
        GenUiActionStatus status,
        string summary,
        JsonElement? structuredResult = null,
        IReadOnlyList<GenUiStatePatch>? patches = null) => new(
        Guid.NewGuid(),
        semanticEvent.EventId,
        semanticEvent.Origin,
        semanticEvent.ComponentId,
        semanticEvent.ActionId,
        status,
        summary,
        structuredResult ?? JsonSerializer.SerializeToElement(new { }),
        patches ?? [],
        DateTimeOffset.UtcNow);
}

/// <summary>Registers deterministic handlers without involving a model.</summary>
public sealed class GenUiLocalActionRegistry : IGenUiEventHandler
{
    private readonly ConcurrentDictionary<string, Func<GenUiEvent, CancellationToken, Task<GenUiActionResult>>> _handlers =
        new(StringComparer.OrdinalIgnoreCase);

    public GenUiRouteKind RouteKind => GenUiRouteKind.Local;

    public bool CanHandle(string targetKey) => _handlers.ContainsKey(targetKey);

    public void Register(
        string targetKey,
        Func<GenUiEvent, CancellationToken, Task<GenUiActionResult>> handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetKey);
        ArgumentNullException.ThrowIfNull(handler);
        if (!_handlers.TryAdd(targetKey, handler))
            throw new InvalidOperationException($"Local GenUI handler '{targetKey}' is already registered.");
    }

    public void RegisterOrReplace(
        string targetKey,
        Func<GenUiEvent, CancellationToken, Task<GenUiActionResult>> handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetKey);
        ArgumentNullException.ThrowIfNull(handler);
        _handlers[targetKey] = handler;
    }

    public Task<GenUiActionResult> HandleAsync(
        GenUiEvent semanticEvent,
        GenUiActionBinding binding,
        CancellationToken cancellationToken) =>
        _handlers.TryGetValue(binding.TargetKey, out var handler)
            ? handler(semanticEvent, cancellationToken)
            : Task.FromResult(GenerativeUiEventRouter.Result(
                semanticEvent, GenUiActionStatus.Unavailable, $"Local handler '{binding.TargetKey}' is unavailable."));
}

/// <summary>Bounded semantic audit; it deliberately stores no raw conversation transcript.</summary>
public sealed class BoundedGenUiEventAuditSink : IGenUiEventAuditSink
{
    private const int MaximumEntries = 500;
    private readonly object _gate = new();
    private readonly Queue<GenUiAuditEntry> _entries = new();

    public IReadOnlyList<GenUiAuditEntry> Snapshot()
    {
        lock (_gate) return _entries.ToArray();
    }

    public ValueTask RecordAsync(GenUiEvent semanticEvent, GenUiActionResult result, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var context = semanticEvent.InteractionContext.Length <= 256
            ? semanticEvent.InteractionContext
            : semanticEvent.InteractionContext[..256];
        lock (_gate)
        {
            _entries.Enqueue(new GenUiAuditEntry(
                semanticEvent.EventId,
                semanticEvent.EventType,
                semanticEvent.Origin,
                semanticEvent.ComponentId,
                semanticEvent.ActionId,
                semanticEvent.Source,
                context,
                result.Status,
                result.Summary,
                result.Timestamp));
            while (_entries.Count > MaximumEntries) _entries.Dequeue();
        }
        return ValueTask.CompletedTask;
    }
}

public sealed record GenUiAuditEntry(
    Guid EventId,
    GenUiEventType EventType,
    GenUiOrigin Origin,
    string ComponentId,
    string ActionId,
    GenUiEventSource Source,
    string InteractionContext,
    GenUiActionStatus Status,
    string Summary,
    DateTimeOffset Timestamp);
