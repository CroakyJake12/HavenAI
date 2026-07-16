using System.Text;
using Haven.Desktop.ViewModels;

namespace Haven.Desktop.Services;

public static class CallTranscriptExportFormatter
{
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
}
