/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Application/CallOptimizedOllamaClient.cs, in the Application layer.
 * What: Wraps the normal local Ollama client with call-specific latency limits and safe instant replies.
 * How: Call requests use a smaller context window, common social turns bypass inference, and selected models can be warmed before use.
 * Why: A 32K context and a cold model make even greetings take several seconds in live voice.
 * Maintenance: Keep instant replies narrow and deterministic; factual or substantive requests must always reach the selected model.
 */

using System.Runtime.CompilerServices;
using Haven.Core;

namespace Haven.Application;

/// <summary>
/// Keeps normal Chat and Studio model behaviour unchanged while making the Call
/// surface responsive enough for spoken conversation.
/// </summary>
public sealed class CallOptimizedOllamaClient(IOllamaClient inner) : IOllamaClient
{
    private const int CallContextLimit = 4096;

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken) =>
        inner.IsAvailableAsync(cancellationToken);

    public Task<IReadOnlyList<ModelDescriptor>> GetModelsAsync(CancellationToken cancellationToken) =>
        inner.GetModelsAsync(cancellationToken);

    public async IAsyncEnumerable<string> StreamChatAsync(
        OllamaChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (TryGetInstantReply(request, out var instantReply))
        {
            yield return instantReply;
            yield break;
        }

        var optimized = new OllamaChatRequest(
            request.Model,
            request.Messages,
            request.Effort,
            request.SystemPrompt,
            Options: new GenerationOptions(
                Temperature: Math.Min(request.Options?.Temperature ?? 0.65, 0.65),
                ContextLimit: Math.Min(request.Options?.ContextLimit ?? CallContextLimit, CallContextLimit),
                ActionLimit: 0));

        await foreach (var delta in inner.StreamChatAsync(optimized, cancellationToken).ConfigureAwait(false))
            yield return delta;
    }

    public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken cancellationToken) =>
        inner.CompleteAsync(request, cancellationToken);

    public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken cancellationToken) =>
        inner.ChatWithToolsAsync(request, cancellationToken);

    /// <summary>
    /// Loads the selected model and its call-sized KV cache before the first real
    /// turn. Only the first streamed token is needed to complete the warm-up.
    /// </summary>
    public async Task WarmAsync(ModelDescriptor model, CancellationToken cancellationToken)
    {
        var request = new OllamaChatRequest(
            model.Name,
            [new OllamaMessage("user", "Reply with OK.")],
            EffortLevel.Low,
            "Reply with exactly OK.",
            Options: new GenerationOptions(0, CallContextLimit, 0));

        await foreach (var _ in inner.StreamChatAsync(request, cancellationToken).ConfigureAwait(false))
            break;
    }

    private static bool TryGetInstantReply(OllamaChatRequest request, out string reply)
    {
        reply = string.Empty;
        var message = request.Messages.LastOrDefault(item =>
            string.Equals(item.Role, "user", StringComparison.OrdinalIgnoreCase));
        if (message is null || message.Images is { Count: > 0 }) return false;

        var value = Normalize(message.Content);
        reply = value switch
        {
            "hi" or "hi there" or "hello" or "hello there" or "hey" or "hiya" or
            "hello haven" or "hey haven" =>
                "Hi! It’s good to hear you. How are you doing?",
            "good morning" => "Good morning! How are you doing?",
            "good afternoon" => "Good afternoon! How’s your day going?",
            "good evening" => "Good evening! How are you doing?",
            "how are you" or "how are you doing" or "hows it going" =>
                "I’m doing well, thanks. How are you?",
            "thanks" or "thank you" or "cheers" => "You’re very welcome!",
            "bye" or "goodbye" or "see you" or "see you later" => "Bye! Speak soon.",
            _ => string.Empty
        };
        return reply.Length > 0;
    }

    private static string Normalize(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        normalized = normalized.TrimEnd('.', '!', '?', ',', '…');
        return string.Join(' ', normalized.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}
