namespace Haven.Desktop.Views.Pages.Chat;

internal static class ChatTranscriptScrollPolicy
{
    internal const double DefaultTailTolerance = 48d;

    public static bool ShouldFollow(double maxScrollY, double scrollY, double tolerance = DefaultTailTolerance)
    {
        if (!double.IsFinite(maxScrollY) || !double.IsFinite(scrollY)) return true;
        var max = Math.Max(0d, maxScrollY);
        var current = Math.Clamp(scrollY, 0d, max);
        var threshold = double.IsFinite(tolerance) ? Math.Max(0d, tolerance) : DefaultTailTolerance;
        return max - current <= threshold;
    }
}
