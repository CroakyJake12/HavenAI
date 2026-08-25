// Where:    src/Haven.Android/HavenKeyboardAiController.cs
// What:     AI text actions (Rewrite / Fix grammar / Shorten / Change tone) for the
//           Haven Keyboard suggestion strip, plus the offline contextual nudge.
// How:      The controller is a thin static shell around a pluggable
//           IKeyboardAiExecutor. The parent bootstrap wires a real executor via
//           Configure(...) once Haven's services exist (see OllamaKeyboardAiExecutor).
//           Every call is cancellable, capped at 20 seconds, reports honest inline
//           status strings, and NEVER blocks typing.
// Why:      The IME can start before Haven's app services exist, so direct DI
//           injection is not reliable; a late-bound executor hook keeps the IME
//           self-sufficient while still routing through shared Haven model plumbing.
//
// PRIVACY RULE (MANDATORY):
//   - Field content is NEVER logged. No Debug/Log call anywhere in the IME may
//     receive keystrokes, composing text, selections or AI source/result text.
//   - Text leaves the device ONLY through RunAsync -> IKeyboardAiExecutor when the
//     user explicitly taps an AI action on a non-secure field with AI enabled in
//     settings AND an active network. Secure fields never reach this file.
//   - CloudAiAllowed must be honoured by any future cloud-backed executor: refuse
//     to run when it is false. The current Ollama adapter documents this too.

using Haven.Application;
using Haven.Core;
using System.Text.RegularExpressions;

namespace Haven.Android;

/// <summary>The AI text transformations offered by the keyboard strip.</summary>
internal enum HavenKeyboardAiAction
{
    /// <summary>Rewrite for clarity while keeping meaning.</summary>
    Rewrite = 0,

    /// <summary>Correct spelling and grammar only.</summary>
    FixGrammar = 1,

    /// <summary>Shorten to roughly half length.</summary>
    Shorten = 2,

    /// <summary>Formal, professional tone.</summary>
    ToneFormal = 3,

    /// <summary>Warm, friendly tone.</summary>
    ToneFriendly = 4,
}

/// <summary>
/// Abstraction over whatever completion backend Haven wires up at bootstrap.
/// Implementations receive fully built prompts and return plain text.
/// </summary>
internal interface IKeyboardAiExecutor
{
    /// <summary>Completes a prompt, returning null when nothing usable came back.</summary>
    Task<string?> CompleteAsync(string prompt, CancellationToken cancellationToken);
}

/// <summary>
/// Executor adapter over Haven's shared <see cref="IOllamaClient"/> model routing.
/// The parent bootstrap constructs this once services are available, e.g.:
/// <code>
/// HavenKeyboardAiController.Configure(new OllamaKeyboardAiExecutor(
///     services.GetRequiredService&lt;IOllamaClient&gt;(), "default-model-id"));
/// </code>
/// </summary>
/// <param name="client">Shared model client owned by Haven's DI container.</param>
/// <param name="model">Model identifier to route completions to.</param>
internal sealed class OllamaKeyboardAiExecutor(IOllamaClient client, string model) : IKeyboardAiExecutor
{
    /// <inheritdoc/>
    public async Task<string?> CompleteAsync(string prompt, CancellationToken cancellationToken)
    {
        var request = new OllamaChatRequest(
            Model: model,
            Messages: [new OllamaMessage("user", prompt)],
            Effort: EffortLevel.Medium);
        return await client.CompleteAsync(request, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Central dispatcher for keyboard AI actions. Holds no user text beyond the
/// lifetime of one action; never persists, logs or transmits anything on its own.
/// </summary>
internal static class HavenKeyboardAiController
{
    private const int TimeoutSeconds = 20;

    private static volatile IKeyboardAiExecutor? _executor;

    /// <summary>
    /// Wires the real completion backend. Called by the parent bootstrap after
    /// Haven's service provider exists; passing null disconnects AI actions.
    /// </summary>
    internal static void Configure(IKeyboardAiExecutor? executor)
    {
        _executor = executor;
    }

    /// <summary>True when an executor has been wired at bootstrap.</summary>
    internal static bool IsConfigured => _executor is not null;

    /// <summary>
    /// Runs one AI transformation. Status updates are reported through
    /// <paramref name="reportStatus"/>; failures produce an honest status string
    /// ("AI unavailable" etc.) and a null result — typing is never blocked.
    /// </summary>
    /// <returns>The transformed text, or null when the action did not succeed.</returns>
    internal static async Task<string?> RunAsync(
        HavenKeyboardAiAction action,
        string sourceText,
        Action<string>? reportStatus,
        CancellationToken cancellationToken)
    {
        var executor = _executor;
        if (executor is null)
        {
            reportStatus?.Invoke("AI unavailable");
            return null;
        }

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(TimeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        try
        {
            var result = await executor
                .CompleteAsync(BuildPrompt(action, sourceText), linkedCts.Token)
                .ConfigureAwait(false);
            var cleaned = CleanResult(result);
            if (cleaned.Length == 0)
            {
                reportStatus?.Invoke("AI returned nothing");
                return null;
            }
            return cleaned;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            reportStatus?.Invoke("AI timed out");
            return null;
        }
        catch (OperationCanceledException)
        {
            reportStatus?.Invoke("AI cancelled");
            return null;
        }
        catch (Exception)
        {
            // Deliberately swallowed: exception details could carry field content.
            reportStatus?.Invoke("AI unavailable");
            return null;
        }
    }

    /// <summary>
    /// Builds the instruction prompt for an action. Prompts demand plain-text-only
    /// replies so results can be inserted directly into the field.
    /// </summary>
    internal static string BuildPrompt(HavenKeyboardAiAction action, string sourceText) => action switch
    {
        HavenKeyboardAiAction.Rewrite =>
            "Rewrite the following text so it is clear and natural while keeping its meaning."
            + " Reply with ONLY the rewritten text.\n\nTEXT:\n" + sourceText,
        HavenKeyboardAiAction.FixGrammar =>
            "Correct the spelling and grammar of the following text, keeping wording as close as possible."
            + " Reply with ONLY the corrected text.\n\nTEXT:\n" + sourceText,
        HavenKeyboardAiAction.Shorten =>
            "Shorten the following text to roughly half its length while preserving its meaning."
            + " Reply with ONLY the shortened text.\n\nTEXT:\n" + sourceText,
        HavenKeyboardAiAction.ToneFormal =>
            "Rewrite the following text in a formal, professional tone. Reply with ONLY the rewritten text."
            + "\n\nTEXT:\n" + sourceText,
        _ =>
            "Rewrite the following text in a warm, friendly tone. Reply with ONLY the rewritten text."
            + "\n\nTEXT:\n" + sourceText,
    };

    /// <summary>Trims model output and strips wrapping quotes/code fences.</summary>
    private static string CleanResult(string? result)
    {
        if (string.IsNullOrWhiteSpace(result))
        {
            return string.Empty;
        }
        var trimmed = result.Trim().Trim('"');
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = trimmed.IndexOf('\n');
            if (firstNewline >= 0 && trimmed.EndsWith("```", StringComparison.Ordinal))
            {
                trimmed = trimmed[(firstNewline + 1)..^3].Trim();
            }
        }
        return trimmed;
    }
}

/// <summary>A detected calendar-worthy phrase from locally typed text.</summary>
/// <param name="Title">Suggested event title shown on the chip.</param>
/// <param name="BeginTime">Next occurrence of the mentioned local time.</param>
internal sealed record HavenCalendarNudge(string Title, DateTimeOffset BeginTime);

/// <summary>
/// Offline pattern scanner that powers the "Add to calendar?" nudge chip. It runs
/// entirely on-device against recent composed text, never sends that text
/// anywhere and never records a match.
/// </summary>
internal static class HavenKeyboardNudgeDetector
{
    private static readonly Regex Pattern = new(
        @"\b(?:(?<verb>meeting|call|lunch|dinner)\s+(?:at|on)\s+(?<time>\d{1,2}(?::\d{2})?\s*(?:am|pm)?))"
        + @"|(?<day>today|tomorrow)\s+at\s+(?<time>\d{1,2}(?::\d{2})?\s*(?:am|pm)?)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(250));

    /// <summary>
    /// Scans recent typed text for phrases like "meeting at 3pm" or
    /// "tomorrow at 14:30". Returns null when nothing matches.
    /// </summary>
    internal static HavenCalendarNudge? Detect(string recentText)
    {
        if (string.IsNullOrWhiteSpace(recentText))
        {
            return null;
        }

        Match match;
        try
        {
            match = Pattern.Match(recentText);
        }
        catch (RegexMatchTimeoutException)
        {
            return null;
        }
        if (!match.Success)
        {
            return null;
        }

        var timeText = match.Groups["time"].Value.Trim();
        if (!TryResolveBeginTime(timeText, out var beginTime))
        {
            return null;
        }

        var phrase = match.Value.Trim();
        var title = char.ToUpperInvariant(phrase[0]) + phrase[1..];
        return new HavenCalendarNudge(title, beginTime);
    }

    /// <summary>
    /// Parses "h", "hh:mm", optional am/pm into the next future local time.
    /// Heuristic without meridiem: hours 1-7 are treated as afternoon/evening.
    /// </summary>
    private static bool TryResolveBeginTime(string timeText, out DateTimeOffset beginTime)
    {
        beginTime = default;
        if (timeText.Length == 0)
        {
            return false;
        }

        var meridiem = 0; // 0 none, -1 am, +1 pm
        var lowered = timeText.ToLowerInvariant();
        if (lowered.EndsWith("pm", StringComparison.Ordinal))
        {
            meridiem = 1;
            lowered = lowered[..^2].Trim();
        }
        else if (lowered.EndsWith("am", StringComparison.Ordinal))
        {
            meridiem = -1;
            lowered = lowered[..^2].Trim();
        }

        var separator = lowered.IndexOf(':');
        int hour;
        int minute;
        if (separator >= 0)
        {
            if (!int.TryParse(lowered[..separator], out hour) || !int.TryParse(lowered[(separator + 1)..], out minute))
            {
                return false;
            }
        }
        else if (!int.TryParse(lowered, out hour))
        {
            return false;
        }
        else
        {
            minute = 0;
        }

        if (hour is < 0 or > 23 || minute is < 0 or > 59 || (meridiem != 0 && hour > 12))
        {
            return false;
        }
        if (meridiem == 1 && hour < 12)
        {
            hour += 12;
        }
        else if (meridiem == -1 && hour == 12)
        {
            hour = 0;
        }
        else if (meridiem == 0 && hour is >= 1 and <= 7)
        {
            hour += 12;
        }

        var candidate = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, hour, minute, 0, DateTimeKind.Local);
        if (candidate <= DateTime.Now)
        {
            candidate = candidate.AddDays(1);
        }
        beginTime = new DateTimeOffset(candidate);
        return true;
    }
}
