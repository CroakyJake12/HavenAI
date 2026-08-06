/*
 * FILE DOCUMENTATION
 * Where: src/Haven.OldHaven/Services/CallTranscriptExportFormatter.cs, in the Desktop services layer, adapting application behavior to Windows and Avalonia concerns.
 * What: This file owns CallTranscriptExportFormatter. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The file keeps one cohesive responsibility in a predictable location so callers can find and replace it without unrelated changes.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Text;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.ViewModels;

namespace Haven.Desktop.Services;

/// <summary>
/// Represents call transcript export formatter and keeps its related state and behavior together.
/// </summary>
public static class CallTranscriptExportFormatter
{
    /// <summary>
    /// Performs the to markdown step owned by this component.
    /// </summary>
    public static string ToMarkdown(
        IEnumerable<CallTranscriptItemViewModel> transcript,
        DateTimeOffset exportedAt)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        var items = transcript.ToArray();
        var builder = new StringBuilder();
        builder.AppendLine("# Haven Call Transcript");
        builder.AppendLine();
        builder.AppendLine($"Exported: {exportedAt.ToLocalTime():f}");
        builder.AppendLine($"Turns: {items.Length}");
        builder.AppendLine();

        foreach (var item in items)
        {
            builder.AppendLine($"## {item.Speaker} · {item.TimeLabel}");
            builder.AppendLine();
            builder.AppendLine(item.Text.Trim());
            if (item.WasInterrupted) builder.AppendLine("\n> Response was interrupted.");
            if (item.IsPartial) builder.AppendLine("\n> Transcript was still partial when exported.");
            builder.AppendLine();
        }
        return builder.ToString().TrimEnd() + Environment.NewLine;
    }

    /// <summary>
    /// Performs the to markdown step owned by this component.
    /// </summary>
    public static string ToMarkdown(
        IEnumerable<TranscriptExportEntry> transcript,
        DateTimeOffset exportedAt)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        var items = transcript.ToArray();
        var builder = new StringBuilder();
        builder.AppendLine("# Haven Call Transcript");
        builder.AppendLine();
        builder.AppendLine($"Exported: {exportedAt.ToLocalTime():f}");
        builder.AppendLine($"Turns: {items.Length}");
        builder.AppendLine();

        foreach (var item in items)
        {
            var speaker = item.Role == MessageRole.User ? "You" : "Haven";
            var timeLabel = item.Timestamp.ToString("HH:mm");
            builder.AppendLine($"## {speaker} · {timeLabel}");
            builder.AppendLine();
            builder.AppendLine(item.Text.Trim());
            builder.AppendLine();
        }
        return builder.ToString().TrimEnd() + Environment.NewLine;
    }
}

/// <summary>
/// Simple transcript entry for export without ViewModel dependency.
/// </summary>
public sealed record TranscriptExportEntry(MessageRole Role, string Text, DateTimeOffset Timestamp);
