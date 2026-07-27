namespace Haven.Core;

/// <summary>
/// Coarse-grained stages that can be shown to a user while Haven is preparing or producing a response.
/// Stages are intentionally task-level rather than line-level so the UI remains useful without becoming noisy.
/// </summary>
public enum ChatExecutionStage
{
    Preparing,
    LoadingModel,
    LoadingContext,
    SelectingCapabilities,
    Thinking,
    InspectingCode,
    Searching,
    Browsing,
    RunningTool,
    RunningCommand,
    EditingFiles,
    Testing,
    Generating,
    Speaking,
    WaitingForApproval,
    Recovering,
    Completed,
    Failed,
    Cancelled
}

/// <summary>
/// One readable item in the expandable execution detail log.
/// </summary>
public sealed record ChatExecutionLogEntry(
    DateTimeOffset Timestamp,
    ChatExecutionStage Stage,
    string Summary,
    string? Detail = null,
    bool Succeeded = true);

/// <summary>
/// Current response progress. The status is hidden for fast responses and becomes visible after two seconds.
/// </summary>
public sealed record ChatExecutionSnapshot(
    Guid OperationId,
    ChatExecutionStage Stage,
    string StatusText,
    DateTimeOffset StartedAt,
    DateTimeOffset UpdatedAt,
    bool IsVisible,
    TimeSpan? EstimatedRemaining,
    IReadOnlyList<ChatExecutionLogEntry> Log)
{
    public string DisplayText => EstimatedRemaining is { } eta
        ? $"{StatusText}. ETA for task: {ChatEtaFormatter.Format(eta)}"
        : StatusText;
}

/// <summary>
/// Formatting and strict parsing helpers for model-produced task estimates.
/// </summary>
public static class ChatEtaFormatter
{
    public static string Format(TimeSpan value)
    {
        var rounded = value < TimeSpan.FromMinutes(1)
            ? TimeSpan.FromMinutes(1)
            : TimeSpan.FromMinutes(Math.Ceiling(value.TotalMinutes));

        if (rounded.TotalHours >= 1)
        {
            var hours = (int)Math.Floor(rounded.TotalHours);
            var minutes = rounded.Minutes;
            return minutes == 0
                ? $"{hours} {(hours == 1 ? "hour" : "hours")}"
                : $"{hours} {(hours == 1 ? "hour" : "hours")} {minutes} minutes";
        }

        var totalMinutes = Math.Max(1, (int)rounded.TotalMinutes);
        return $"{totalMinutes} {(totalMinutes == 1 ? "minute" : "minutes")}";
    }

    /// <summary>
    /// Accepts only a clear duration such as "45 minutes", "about 2 hours", or "1 hour 20 minutes".
    /// Explanations, refusals, ranges, and unknown estimates are rejected.
    /// </summary>
    public static bool TryParseClearEstimate(string? value, out TimeSpan estimate)
    {
        estimate = default;
        if (string.IsNullOrWhiteSpace(value)) return false;

        var normalized = value.Trim().ToLowerInvariant();
        if (normalized.Contains("hard to estimate", StringComparison.Ordinal) ||
            normalized.Contains("cannot estimate", StringComparison.Ordinal) ||
            normalized.Contains("can't estimate", StringComparison.Ordinal) ||
            normalized.Contains("unknown", StringComparison.Ordinal) ||
            normalized.Contains("depends", StringComparison.Ordinal) ||
            normalized.Contains('–') || normalized.Contains('-'))
        {
            return false;
        }

        normalized = normalized
            .Replace("approximately", string.Empty, StringComparison.Ordinal)
            .Replace("approx.", string.Empty, StringComparison.Ordinal)
            .Replace("about", string.Empty, StringComparison.Ordinal)
            .Replace("eta:", string.Empty, StringComparison.Ordinal)
            .Trim();

        var tokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var total = TimeSpan.Zero;
        var found = false;

        for (var index = 0; index + 1 < tokens.Length; index++)
        {
            if (!double.TryParse(tokens[index], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var amount) || amount <= 0)
            {
                continue;
            }

            var unit = tokens[index + 1].TrimEnd('.', ',', ';', ':');
            if (unit.StartsWith("hour", StringComparison.Ordinal))
            {
                total += TimeSpan.FromHours(amount);
                found = true;
                index++;
            }
            else if (unit.StartsWith("minute", StringComparison.Ordinal) || unit == "min" || unit == "mins")
            {
                total += TimeSpan.FromMinutes(amount);
                found = true;
                index++;
            }
        }

        if (!found || total <= TimeSpan.Zero || total > TimeSpan.FromDays(30)) return false;
        estimate = total;
        return true;
    }
}

/// <summary>
/// Maps internal stages to stable, user-facing labels.
/// </summary>
public static class ChatExecutionStageText
{
    public static string Get(ChatExecutionStage stage) => stage switch
    {
        ChatExecutionStage.Preparing => "Preparing",
        ChatExecutionStage.LoadingModel => "Loading model",
        ChatExecutionStage.LoadingContext => "Loading context",
        ChatExecutionStage.SelectingCapabilities => "Selecting capabilities",
        ChatExecutionStage.Thinking => "Thinking",
        ChatExecutionStage.InspectingCode => "Inspecting code",
        ChatExecutionStage.Searching => "Searching",
        ChatExecutionStage.Browsing => "Browsing",
        ChatExecutionStage.RunningTool => "Using a tool",
        ChatExecutionStage.RunningCommand => "Running a command",
        ChatExecutionStage.EditingFiles => "Editing files",
        ChatExecutionStage.Testing => "Testing",
        ChatExecutionStage.Generating => "Generating response",
        ChatExecutionStage.Speaking => "Speaking",
        ChatExecutionStage.WaitingForApproval => "Waiting for approval",
        ChatExecutionStage.Recovering => "Recovering",
        ChatExecutionStage.Completed => "Completed",
        ChatExecutionStage.Failed => "Failed",
        ChatExecutionStage.Cancelled => "Cancelled",
        _ => "Working"
    };
}
