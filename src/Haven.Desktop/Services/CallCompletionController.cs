/*
 * FILE DOCUMENTATION
 * Where: src/Haven.OldHaven/Services/CallCompletionController.cs, in the Desktop services layer, adapting application behavior to Windows and Avalonia concerns.
 * What: This file owns CallCompletionController. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The file keeps one cohesive responsibility in a predictable location so callers can find and replace it without unrelated changes.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace Haven.Desktop.Services;

/// <summary>
/// Observes the process-wide Call coordinator and adds exactly one durable text summary
/// after a completed call. Raw audio and screen frames never enter this service.
/// </summary>
public sealed class CallCompletionController : IAsyncDisposable
{
    /// <summary>
    /// Stores coordinator locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly ICallCoordinator _coordinator;
    /// <summary>
    /// Stores conversations locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IConversationRepository _conversations;
    /// <summary>
    /// Stores models locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IOllamaClient _models;
    /// <summary>
    /// Stores diagnostics locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IProductionDiagnostics _diagnostics;
    /// <summary>
    /// Stores gate locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly SemaphoreSlim _gate = new(1, 1);
    /// <summary>
    /// Stores completed locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly ConcurrentDictionary<Guid, byte> _completed = new();
    /// <summary>
    /// Stores disposed locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private bool _disposed;

    public CallCompletionController(
        ICallCoordinator coordinator,
        IConversationRepository conversations,
        IOllamaClient models,
        IProductionDiagnostics diagnostics)
    {
        _coordinator = coordinator;
        _conversations = conversations;
        _models = models;
        _diagnostics = diagnostics;
        _coordinator.StateChanged += OnStateChanged;
    }

    /// <summary>
    /// Handles the state changed event raised by the UI or runtime.
    /// </summary>
    private void OnStateChanged(object? sender, CallStateChangedEventArgs e)
    {
        var session = _coordinator.CurrentSession;
        if (_disposed || session is null || session.Status != CallSessionStatus.Completed || session.EndedAt is null) return;
        _ = PersistCompletedSessionAsync(session, _coordinator.CurrentConversation);
    }

    /// <summary>
    /// Performs persist completed session asynchronously so I/O does not block the caller's thread.
    /// </summary>
    internal Task PersistCompletedSessionAsync(CallSession session, Conversation? conversation) =>
        PersistSummarySafelyAsync(session, conversation);

    /// <summary>
    /// Performs persist summary safely asynchronously so I/O does not block the caller's thread.
    /// </summary>
    private async Task PersistSummarySafelyAsync(CallSession session, Conversation? conversation)
    {
        if (conversation is null || !_completed.TryAdd(session.Id, 0)) return;
        var correlationId = Guid.NewGuid().ToString("N");
        await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            var messages = await _conversations.GetMessagesAsync(conversation.Id, CancellationToken.None).ConfigureAwait(false);
            if (messages.Any(IsSummaryMessage)) return;

            var transcriptMessages = messages
                .Where(message => (message.Role is MessageRole.User or MessageRole.Assistant) && !IsSummaryMessage(message))
                .ToArray();
            var summary = transcriptMessages.Length == 0
                ? "The call ended without a completed spoken or typed turn."
                : await CreateSummaryAsync(session.ModelName, transcriptMessages).ConfigureAwait(false);
            var now = DateTimeOffset.UtcNow;
            var metadata = JsonSerializer.Serialize(new
            {
                call = new
                {
                    summary = true,
                    sessionId = session.Id,
                    startedAt = session.StartedAt,
                    endedAt = session.EndedAt,
                    usedScreenShare = session.UsedScreenShare
                }
            });
            await _conversations.AddMessageAsync(new ChatMessage(
                Guid.NewGuid(),
                conversation.Id,
                MessageRole.System,
                "Call summary\n\n" + summary.Trim(),
                "Haven",
                session.ModelName,
                metadata,
                now), CancellationToken.None).ConfigureAwait(false);
            await _conversations.UpsertConversationAsync(
                conversation with { UpdatedAt = now },
                CancellationToken.None).ConfigureAwait(false);
            await _diagnostics.WriteAsync(
                ReliabilitySeverity.Information,
                "call",
                "summary-persisted",
                "A completed Call received one durable text summary.",
                new Dictionary<string, string>
                {
                    ["sessionId"] = session.Id.ToString("D"),
                    ["conversationId"] = conversation.Id.ToString("D"),
                    ["turnCount"] = transcriptMessages.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)
                },
                correlationId,
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _completed.TryRemove(session.Id, out _);
            await _diagnostics.WriteAsync(
                ReliabilitySeverity.Warning,
                "call",
                "summary-failed",
                "The Call ended cleanly, but its durable summary could not be created. A later completion signal may retry it.",
                new Dictionary<string, string>
                {
                    ["sessionId"] = session.Id.ToString("D"),
                    ["exceptionType"] = ex.GetType().FullName ?? ex.GetType().Name
                },
                correlationId,
                CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Creates summary async with the invariants required by its callers.
    /// </summary>
    private async Task<string> CreateSummaryAsync(string modelName, IReadOnlyList<ChatMessage> messages)
    {
        var transcript = new StringBuilder();
        foreach (var message in messages.TakeLast(80))
        {
            transcript.Append(message.Role == MessageRole.User ? "User: " : "Haven: ");
            transcript.AppendLine(message.Content.Trim());
            transcript.AppendLine();
        }
        var value = transcript.ToString();
        if (value.Length > 24_000) value = value[^24_000..];

        try
        {
            return await _models.CompleteAsync(new OllamaChatRequest(
                modelName,
                [new OllamaMessage("user", "Create a concise durable summary of this completed call. Preserve decisions, promises, named items, follow-up actions and unresolved questions. Do not invent details. Use short headings and bullets.\n\n" + value)],
                EffortLevel.Low,
                "You summarize a completed private Haven call using only its text transcript.",
                Options: new GenerationOptions(0.2, 8192, 0)), CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception)
        {
            var fallback = messages.TakeLast(10)
                .Select(message => $"- {(message.Role == MessageRole.User ? "User" : "Haven")}: {Truncate(message.Content.Trim(), 320)}");
            return "Model summary was unavailable. Recent transcript highlights:\n" + string.Join("\n", fallback);
        }
    }

    /// <summary>
    /// Reports whether summary message applies to the current state.
    /// </summary>
    private static bool IsSummaryMessage(ChatMessage message)
    {
        if (string.IsNullOrWhiteSpace(message.MetadataJson)) return false;
        try
        {
            using var document = JsonDocument.Parse(message.MetadataJson);
            return document.RootElement.TryGetProperty("call", out var call)
                   && call.TryGetProperty("summary", out var summary)
                   && summary.ValueKind == JsonValueKind.True;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Performs the truncate step owned by this component.
    /// </summary>
    private static string Truncate(string value, int maximum) =>
        value.Length <= maximum ? value : value[..maximum] + "…";

    /// <summary>
    /// Performs dispose asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _coordinator.StateChanged -= OnStateChanged;
        await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        _gate.Release();
        _gate.Dispose();
        GC.SuppressFinalize(this);
    }
}
