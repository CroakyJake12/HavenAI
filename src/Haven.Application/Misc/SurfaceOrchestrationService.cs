/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Application/SurfaceOrchestrationService.cs, in the Application layer, which coordinates use cases through abstractions without owning platform details.
 * What: This file owns SurfaceOrchestrationService, SurfaceResolutionResult. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The implementation depends on interfaces so policy remains testable and platform-specific details can be replaced.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Core;

namespace Haven.Application;

/// <summary>
/// Represents surface orchestration service and keeps its related state and behavior together.
/// </summary>
public sealed class SurfaceOrchestrationService
{
    /// <summary>
    /// Stores surface router locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly ISurfaceRouter _surfaceRouter;
    /// <summary>
    /// Stores modes locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IModeRegistry _modes;
    /// <summary>
    /// Stores docked locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly ICompanionDockService _docked;
    /// <summary>
    /// Stores activity log locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
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

    /// <summary>
    /// Performs resolve asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Performs classify intent asynchronously so I/O does not block the caller's thread.
    /// </summary>
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

    /// <summary>
    /// Performs the mode to surface step owned by this component.
    /// </summary>
    private static SurfaceKind ModeToSurface(HavenMode mode) => mode switch
    {
        HavenMode.Chat => SurfaceKind.Chat,
        HavenMode.Study => SurfaceKind.Study,
        HavenMode.Tasks => SurfaceKind.Tasks,
        HavenMode.Studio => SurfaceKind.Studio,
        _ => SurfaceKind.Chat
    };

    /// <summary>
    /// Performs the escape json step owned by this component.
    /// </summary>
    private static string EscapeJson(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
}

/// <summary>
/// Represents surface resolution result and keeps its related state and behavior together.
/// </summary>
public sealed record SurfaceResolutionResult(
    IntentClassification Classification,
    SurfaceKind TargetSurface,
    bool IsCrossSurface,
    string? SwitchNotice);
