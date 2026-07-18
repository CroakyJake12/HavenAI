/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Infrastructure/RecoverySafetyProbe.cs, in the Infrastructure layer, where persistence, providers, Windows integration, and external I/O are implemented.
 * What: This file owns RecoverySafetyProbe, StartupState, StartupRun. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Platform and persistence details are contained here so higher layers do not acquire external-system coupling.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using System.Text.Json;
using Haven.Application;

namespace Haven.Infrastructure;

/// <summary>
/// Represents recovery safety probe and keeps its related state and behavior together.
/// </summary>
public sealed class RecoverySafetyProbe(IAppPaths paths) : IRecoverySafetyProbe
{
    /// <summary>
    /// Stores json options locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };
    /// <summary>
    /// Stores crash window locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private static readonly TimeSpan CrashWindow = TimeSpan.FromMinutes(15);
    /// <summary>
    /// Stores safe mode threshold locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private const int SafeModeThreshold = 3;
    /// <summary>
    /// Stores state path locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly string _statePath = Path.Combine(paths.DataDirectory, "startup-recovery.json");

    /// <summary>
    /// Performs assess async asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Represents startup state and keeps its related state and behavior together.
    /// </summary>
    private sealed record StartupState(int Version, StartupRun? CurrentRun, IReadOnlyList<DateTimeOffset> RecentUncleanStarts);
    /// <summary>
    /// Represents startup run and keeps its related state and behavior together.
    /// </summary>
    private sealed record StartupRun(string Id, DateTimeOffset StartedAt, bool StartupCompleted, bool CleanShutdown);
}
