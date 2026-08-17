using Haven.Core;

namespace Haven.Application;

/// <summary>Determines whether persisted Canvas study-layer content is currently visible.</summary>
public static class CanvasGhostVisibility
{
    public static bool IsObjectVisible(NotesCanvasData board, NotesCanvasObject value)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(value);
        var layers = board.GhostLayers ?? [];
        return !layers.Any(layer => !layer.IsRevealed && layer.ObjectIds.Contains(value.Id));
    }

    public static bool IsStrokeVisible(NotesCanvasData board, NotesInkStroke stroke)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(stroke);
        var layers = board.GhostLayers ?? [];

        var matchingLayers = layers
            .Where(layer => layer.Id == stroke.GhostLayerId || layer.StrokeIds.Contains(stroke.Id))
            .ToArray();

        if (matchingLayers.Length == 0)
            return !stroke.IsGhost;

        return matchingLayers.All(layer => layer.IsRevealed);
    }
}
