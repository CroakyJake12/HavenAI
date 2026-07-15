using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

public sealed class IntentRouter : IModeIntentRouter
{
    private readonly IModeRegistry _modes;

    public IntentRouter(IModeRegistry modes)
    {
        _modes = modes;
    }

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
