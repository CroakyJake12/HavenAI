using Haven.Application;

namespace Haven.Desktop.Views.Pages.Canvas;

internal sealed partial class CanvasHavenScene
{
    private void SaveCurrentPenPreset()
    {
        var controller = UnifiedSurface.Controller;
        var highlighter = controller.Tool == CanvasTool.Highlighter;
        var label = $"Custom {++_customPenPresetCount}";
        _penPresets.Add(PresetButton(
            label,
            controller.PenColour,
            controller.PenWidth,
            controller.PenOpacity,
            controller.PenEffect,
            highlighter));
        SetStatus($"Saved {label} for this Canvas session.");
    }
}
