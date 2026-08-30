using Haven.Application;
using Haven.Desktop.HavenUI.Creative;
using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;

namespace Haven.Desktop.Views.Pages.Canvas;

// Retains support contracts used by the older board-rename and pen-preset partials
// after the duplicate release scene was removed in favour of the active recovery scene.
internal sealed partial class CanvasHavenScene
{
    private IReadOnlyList<string> _boardTitles = [];
    private int _renameBoardIndex = -1;
    private Container? _renameBoardPanel;
    private Input _renameBoardInput = null!;
    private readonly Container _penPresets = new() { Name = "Canvas.Compatibility.PenPresets", Layout = HavenLayout.Horizontal };
    private int _customPenPresetCount;

    private static Container FloatingCard(string name)
    {
        var card = new Container { Name = name, Layout = HavenLayout.Horizontal };
        card.SetValue(HavenProperties.Background, "Surface");
        card.SetValue(HavenProperties.Padding, HavenThickness.Parse("10px"));
        card.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(10)));
        return card;
    }

    private static HavenButton CompactButton(string name, string label, string accessible)
    {
        var button = new HavenButton { Name = name, Content = label, Variant = ButtonVariant.Tertiary };
        button.Accessibility.AccessibleName = accessible;
        return button;
    }

    private static void HidePanels() { }

    private HavenButton PresetButton(string label, string colour, double width, double opacity, string effect, bool highlighter)
    {
        var button = CompactButton("Canvas.Compatibility.Preset." + label, label, label + " pen preset");
        button.Invoked += (_, _) =>
        {
            var controller = UnifiedSurface.Controller;
            controller.PenColour = colour;
            controller.PenWidth = width;
            controller.PenOpacity = opacity;
            controller.PenEffect = effect;
            UnifiedSurface.SetTool(highlighter ? UnifiedCanvasTool.Highlighter : UnifiedCanvasTool.Pen);
            ToolRequested?.Invoke(highlighter ? CanvasTool.Highlighter : CanvasTool.Pen);
            RefreshSurfaceMetadata(controller);
        };
        return button;
    }
}
