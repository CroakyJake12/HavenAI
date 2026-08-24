using Haven.Core;

namespace Haven.Application;

public sealed partial class ImagineProjectSession
{
    private ImagineEditableObject? _clipboardObject;

    public bool CopySelected()
    {
        var item = SelectedObject();
        if (item is null) return false;
        _clipboardObject = item;
        return true;
    }

    public bool CutSelected()
    {
        if (!CopySelected()) return false;
        return DeleteSelected();
    }

    public bool PasteClipboard()
    {
        if (_clipboardObject is not { } source) return false;
        if (source.AssetId is Guid assetId && Project.Assets.All(asset => asset.Id != assetId)) return false;

        var copy = source with
        {
            Id = Guid.NewGuid(),
            Name = source.Name + " copy",
            Transform = source.Transform with { X = source.Transform.X + 24, Y = source.Transform.Y + 24 },
            ZIndex = NextZ(Project),
            IsLocked = false
        };
        Apply(
            "paste-object",
            new ImagineSelectionScope(ImagineSelectionKind.Object, copy.Id),
            "user",
            null,
            project => project with
            {
                Objects = project.Objects.Append(copy).ToArray(),
                Selection = new ImagineSelectionScope(ImagineSelectionKind.Object, copy.Id)
            });
        return true;
    }
}
