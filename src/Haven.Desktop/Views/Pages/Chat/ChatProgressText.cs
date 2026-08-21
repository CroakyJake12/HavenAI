namespace Haven.Desktop.Views.Pages.Chat;

internal static class ChatProgressText
{
    public static string Format(bool isStreaming, long elapsedSeconds, string? detail = null)
    {
        var elapsed = Math.Max(0, elapsedSeconds);
        var duration = elapsed == 1 ? "1 second" : $"{elapsed} seconds";
        var label = isStreaming
            ? elapsed == 0 ? "Working…" : $"Working for {duration}"
            : $"Worked for {duration}";
        return string.IsNullOrWhiteSpace(detail) ? label : $"{label}\n{detail.Trim()}";
    }
}
