using Haven.Core;
using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;
using HavenContainer = Haven.UI.Components.Container;
using HavenText = Haven.UI.Components.Text;

namespace Haven.Desktop.Views.Pages.Imagine;

internal sealed partial class ImagineWorkspaceScene
{
    public HavenContainer LayerPanel { get; }
    public DynamicUIRuntime Layers { get; }
    public HavenText LayersEmpty { get; }

    public event Action<Guid>? LayerSelectRequested;
    public event Action<Guid>? LayerVisibilityRequested;
    public event Action<Guid>? LayerLockRequested;
    public event Action<Guid, int>? LayerMoveRequested;

    private void SyncLayers(ImagineProject project)
    {
        foreach (var layer in project.Objects
                     .OrderByDescending(item => item.ZIndex)
                     .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            var row = _dynamic.CreateItem(
                "ImagineLayerRow",
                Layers.Name!,
                "layer-" + layer.Id.ToString("N"),
                new Dictionary<string, object?>
                {
                    ["TITLE"] = layer.Name,
                    ["VISIBILITY"] = layer.IsVisible ? "Hide" : "Show",
                    ["LOCK"] = layer.IsLocked ? "Unlock" : "Lock"
                });

            var select = row.GetComponent<HavenButton>("Select");
            select.Variant = project.Selection.Kind == ImagineSelectionKind.Object && project.Selection.TargetId == layer.Id
                ? ButtonVariant.Tertiary
                : ButtonVariant.Navigation;
            select.Invoked += (_, _) => LayerSelectRequested?.Invoke(layer.Id);
            row.GetComponent<HavenButton>("Visibility").Invoked += (_, _) => LayerVisibilityRequested?.Invoke(layer.Id);
            row.GetComponent<HavenButton>("Lock").Invoked += (_, _) => LayerLockRequested?.Invoke(layer.Id);
            row.GetComponent<HavenButton>("Lower").Invoked += (_, _) => LayerMoveRequested?.Invoke(layer.Id, -1);
            row.GetComponent<HavenButton>("Raise").Invoked += (_, _) => LayerMoveRequested?.Invoke(layer.Id, 1);
        }

        LayersEmpty.SetValue(HavenProperties.Visibility, project.Objects.Length == 0 ? HavenVisibility.Visible : HavenVisibility.Collapsed);
    }
}
