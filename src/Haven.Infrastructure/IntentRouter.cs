/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Infrastructure/IntentRouter.cs, in the Infrastructure layer, where persistence, providers, Windows integration, and external I/O are implemented.
 * What: This file owns IntentRouter. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: Platform and persistence details are contained here so higher layers do not acquire external-system coupling.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

/// <summary>
/// Represents intent router and keeps its related state and behavior together.
/// </summary>
public sealed class IntentRouter : IModeIntentRouter
{
    /// <summary>
    /// Stores modes locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly IModeRegistry _modes;

    public IntentRouter(IModeRegistry modes)
    {
        _modes = modes;
    }

    /// <summary>
    /// Performs classify asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task<IntentClassification> ClassifyAsync(string prompt, HavenMode currentMode, string? workspaceRoot, CancellationToken cancellationToken)
    {
        var lower = prompt.ToLowerInvariant();

        if (lower.StartsWith("open ") || lower.StartsWith("launch ") || lower.StartsWith("click ") ||
            lower.StartsWith("type ") || lower.StartsWith("press ") || lower.StartsWith("run "))
            return IntentClassification.DirectTool;

        if (lower.Contains("switch to") || lower.Contains("go to") || lower.Contains("open the"))
            return IntentClassification.ModeSwitch;

        if (lower.Contains("what") || lower.Contains("how") || lower.Contains("explain") ||
            lower.Contains("help") || lower.Contains("?"))
            return IntentClassification.Inspect;

        if (!string.IsNullOrWhiteSpace(workspaceRoot))
        {
            if (lower.Contains("edit") || lower.Contains("write") || lower.Contains("create") ||
                lower.Contains("build") || lower.Contains("fix") || lower.Contains("test"))
                return IntentClassification.Compose;
        }

        return IntentClassification.DirectTool;
    }

    /// <summary>
    /// Performs resolve mode asynchronously so I/O does not block the caller's thread.
    /// </summary>
    public async Task<ModeSlot?> ResolveModeAsync(string prompt, HavenMode currentMode, string? workspaceRoot, CancellationToken cancellationToken)
    {
        var modes = await _modes.GetModesAsync(cancellationToken).ConfigureAwait(false);
        var lower = prompt.ToLowerInvariant();

        foreach (var mode in modes.Where(m => m.IsEnabled))
        {
            if (lower.Contains(mode.Key.ToLowerInvariant()) ||
                lower.Contains(mode.Name.ToLowerInvariant()))
            {
                return new ModeSlot(
                    mode.Id,
                    mode.Key,
                    mode.Name,
                    mode.IconKey,
                    mode.BaseMode,
                    0,
                    false,
                    mode.Source == ModeSource.BuiltIn);
            }
        }

        return null;
    }
}
