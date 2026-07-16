using System.Text.Json;
using Haven.Application;

namespace Haven.Infrastructure;

public sealed class RecoverySafetyProbe(IAppPaths paths) : IRecoverySafetyProbe
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };
    private static readonly TimeSpan CrashWindow = TimeSpan.FromMinutes(15);
    private const int SafeModeThreshold = 3;
    private readonly string _statePath = Path.Combine(paths.DataDirectory, "startup-recovery.json");

    public async Task<RecoverySafetyAssessment> AssessAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        if (!File.Exists(_statePath))
            return new RecoverySafetyAssessment(false, true, 0, "No crash-loop recovery state is present.", now);

        try
        {
            StartupState? state = null;
            Exception? lastError = null;
            for (var attempt = 0; attempt < 3 && state is null; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    await using var stream = new FileStream(
                        _statePath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete,
                        16 * 1024,
                        FileOptions.Asynchronous | FileOptions.SequentialScan);
                    state = await JsonSerializer.DeserializeAsync<StartupState>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
                {
                    lastError = ex;
                    if (attempt < 2) await Task.Delay(TimeSpan.FromMilliseconds(75 * (attempt + 1)), cancellationToken).ConfigureAwait(false);
                }
            }

            if (state is null)
                return new RecoverySafetyAssessment(true, false, SafeModeThreshold, "The crash-loop recovery state could not be read safely: " + lastError?.GetType().Name + ". Background side effects are blocked.", now);

            var failures = state.RecentUncleanStarts
                .Where(value => value <= now && now - value <= CrashWindow)
                .ToHashSet();
            if (state.CurrentRun is { CleanShutdown: false } current && current.StartedAt <= now && now - current.StartedAt <= CrashWindow)
                failures.Add(current.StartedAt);
            var safeMode = failures.Count >= SafeModeThreshold;
            return new RecoverySafetyAssessment(
                safeMode,
                true,
                failures.Count,
                safeMode
                    ? $"Haven detected {failures.Count} recent unclean desktop starts. Background automation is blocked until Haven completes a clean desktop shutdown."
                    : "Crash-loop threshold not reached.",
                now);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or InvalidDataException)
        {
            return new RecoverySafetyAssessment(true, false, SafeModeThreshold, "The crash-loop recovery state is invalid: " + ex.GetType().Name + ". Background side effects are blocked.", now);
        }
    }

    private sealed record StartupState(int Version, StartupRun? CurrentRun, IReadOnlyList<DateTimeOffset> RecentUncleanStarts);
    private sealed record StartupRun(string Id, DateTimeOffset StartedAt, bool StartupCompleted, bool CleanShutdown);
}
