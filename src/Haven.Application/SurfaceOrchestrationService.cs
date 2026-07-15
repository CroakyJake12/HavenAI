using Haven.Core;

namespace Haven.Application;

public sealed class SurfaceOrchestrationService
{
    private readonly ISurfaceRouter _surfaceRouter;
    private readonly IModeRegistry _modes;
    private readonly ICompanionDockService _docked;
    private readonly IActivityLogRepository _activityLog;

    public SurfaceOrchestrationService(
        ISurfaceRouter surfaceRouter,
        IModeRegistry modes,
        ICompanionDockService docked,
        IActivityLogRepository activityLog)
    {
        _surfaceRouter = surfaceRouter;
        _modes = modes;
        _docked = docked;
        _activityLog = activityLog;
    }

    public async Task<SurfaceResolutionResult> ResolveAsync(
        string prompt,
        HavenMode currentMode,
        string? workspaceRoot,
        CancellationToken cancellationToken)
    {
        var classification = await ClassifyIntentAsync(prompt, currentMode, workspaceRoot, cancellationToken)
            .ConfigureAwait(false);

        var targetSurface = await _surfaceRouter.ResolveSurfaceAsync(prompt, currentMode, cancellationToken)
            .ConfigureAwait(false);

        var isCrossSurface = targetSurface != ModeToSurface(currentMode);

        await _activityLog.AddEventAsync(new ActivityEvent(
            Guid.NewGuid(),
            ActivityEventKind.ModeSwitch,
            null,
            null,
            $"Surface resolution: {classification} -> {targetSurface}",
            $"{{\"prompt\":\"{EscapeJson(prompt)}\",\"classification\":\"{classification}\",\"surface\":\"{targetSurface}\"}}",
            DateTimeOffset.UtcNow), cancellationToken).ConfigureAwait(false);

        return new SurfaceResolutionResult(
            classification,
            targetSurface,
            isCrossSurface,
            isCrossSurface ? $"Switching to {targetSurface} surface" : null);
    }

    private Task<IntentClassification> ClassifyIntentAsync(
        string prompt,
        HavenMode currentMode,
        string? workspaceRoot,
        CancellationToken cancellationToken)
    {
        var lower = prompt.ToLowerInvariant();

        if (lower.StartsWith("open ") || lower.StartsWith("launch ") || lower.StartsWith("click ") ||
            lower.StartsWith("type ") || lower.StartsWith("press ") || lower.StartsWith("run "))
            return Task.FromResult(IntentClassification.DirectTool);

        if (lower.Contains("switch to") || lower.Contains("go to") || lower.Contains("open the"))
            return Task.FromResult(IntentClassification.ModeSwitch);

        if (lower.Contains("what") || lower.Contains("how") || lower.Contains("explain") ||
            lower.Contains("help") || lower.Contains("?"))
            return Task.FromResult(IntentClassification.Inspect);

        if (!string.IsNullOrWhiteSpace(workspaceRoot) &&
            (lower.Contains("edit") || lower.Contains("write") || lower.Contains("create") ||
             lower.Contains("build") || lower.Contains("fix") || lower.Contains("test")))
            return Task.FromResult(IntentClassification.Compose);

        return Task.FromResult(IntentClassification.DirectTool);
    }

    private static SurfaceKind ModeToSurface(HavenMode mode) => mode switch
    {
        HavenMode.Chat => SurfaceKind.Chat,
        HavenMode.Teach => SurfaceKind.Teach,
        HavenMode.Do => SurfaceKind.Do,
        HavenMode.Studio => SurfaceKind.Studio,
        _ => SurfaceKind.Chat
    };

    private static string EscapeJson(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
}

public sealed record SurfaceResolutionResult(
    IntentClassification Classification,
    SurfaceKind TargetSurface,
    bool IsCrossSurface,
    string? SwitchNotice);
