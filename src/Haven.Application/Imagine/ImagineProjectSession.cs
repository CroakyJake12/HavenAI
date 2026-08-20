using System.Text.Json;
using Haven.Core;

namespace Haven.Application;

/// <summary>
/// Owns one Imagine editing session. Project state is immutable at the boundary;
/// pointer previews stay in the UI and each committed operation becomes one undo step.
/// </summary>
public sealed partial class ImagineProjectSession
{
    private static readonly JsonSerializerOptions SnapshotOptions = new(JsonSerializerDefaults.Web);
    private readonly Stack<ImagineProject> _undo = new();
    private readonly Stack<ImagineProject> _redo = new();

    public ImagineProjectSession(ImagineProject project)
    {
        Project = project ?? throw new ArgumentNullException(nameof(project));
    }

    public ImagineProject Project { get; private set; }
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public event EventHandler? Changed;

    public static ImagineProject CreateProject(string name, double canvasWidth = 1600, double canvasHeight = 1000)
    {
        var now = DateTimeOffset.UtcNow;
        return new ImagineProject(
            Guid.NewGuid(),
            string.IsNullOrWhiteSpace(name) ? "Untitled Imagine Project" : name.Trim(),
            Math.Max(320, canvasWidth),
            Math.Max(240, canvasHeight),
            [],
            [],
            [],
            [],
            new ImagineSelectionScope(ImagineSelectionKind.None),
            [],
            now,
            now);
    }

    public void AddImportedAsset(ImagineMediaAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        Apply(
            "import-asset",
            new ImagineSelectionScope(ImagineSelectionKind.Media, asset.Id),
            "user-import",
            null,
            project =>
            {
                if (project.Assets.Any(item => item.Id == asset.Id)) return project;
                var assets = project.Assets.Append(asset).ToArray();
                if (asset.Kind == ImagineMediaKind.Image)
                {
                    var width = Math.Min(720, project.CanvasWidth * .68);
                    var height = Math.Min(540, project.CanvasHeight * .68);
                    var image = new ImagineEditableObject(
                        Guid.NewGuid(),
                        ImagineObjectKind.Image,
                        asset.Name,
                        asset.Id,
                        new ImagineTransform(
                            Math.Max(0, (project.CanvasWidth - width) / 2),
                            Math.Max(0, (project.CanvasHeight - height) / 2),
                            width,
                            height),
                        NextZ(project),
                        string.Empty,
                        "#FFFFFF",
                        true,
                        false);
                    return project with
                    {
                        Assets = assets,
                        Objects = project.Objects.Append(image).ToArray(),
                        Selection = new ImagineSelectionScope(ImagineSelectionKind.Object, image.Id)
                    };
                }

                var trackKind = asset.Kind == ImagineMediaKind.Audio ? ImagineTrackKind.Audio : ImagineTrackKind.Video;
                var track = new ImagineTrack(
                    Guid.NewGuid(),
                    trackKind,
                    asset.Name,
                    project.Tracks.Length,
                    false,
                    1,
                    [new ImagineClip(Guid.NewGuid(), asset.Id, asset.Name, 0, 0, AssetDurationSeconds(asset) ?? 0)]);
                return project with
                {
                    Assets = assets,
                    Tracks = project.Tracks.Append(track).ToArray(),
                    Selection = new ImagineSelectionScope(ImagineSelectionKind.Track, track.Id)
                };
            });
    }

    public Guid AddRectangle(double x = 120, double y = 120, double width = 260, double height = 180)
    {
        var id = Guid.NewGuid();
        var item = new ImagineEditableObject(
            id,
            ImagineObjectKind.Rectangle,
            "Rectangle",
            null,
            new ImagineTransform(x, y, Math.Max(12, width), Math.Max(12, height)),
            NextZ(Project),
            string.Empty,
            "#00A7B3",
            true,
            false);
        Apply(
            "add-rectangle",
            new ImagineSelectionScope(ImagineSelectionKind.Object, id),
            "user",
            null,
            project => project with
            {
                Objects = project.Objects.Append(item).ToArray(),
                Selection = new ImagineSelectionScope(ImagineSelectionKind.Object, id)
            });
        return id;
    }

    public Guid AddText(string text, double x = 140, double y = 140)
    {
        var value = string.IsNullOrWhiteSpace(text) ? "Text" : text.Trim();
        var id = Guid.NewGuid();
        var item = new ImagineEditableObject(
            id,
            ImagineObjectKind.Text,
            value,
            null,
            new ImagineTransform(x, y, 360, 72),
            NextZ(Project),
            value,
            "#111111",
            true,
            false);
        Apply(
            "add-text",
            new ImagineSelectionScope(ImagineSelectionKind.Object, id),
            "user",
            null,
            project => project with
            {
                Objects = project.Objects.Append(item).ToArray(),
                Selection = new ImagineSelectionScope(ImagineSelectionKind.Object, id)
            });
        return id;
    }

    public bool SelectObject(Guid id)
    {
        if (Project.Objects.All(item => item.Id != id)) return false;
        SetSelection(new ImagineSelectionScope(ImagineSelectionKind.Object, id));
        return true;
    }

    public bool SelectSemanticComponent(Guid id)
    {
        if (Project.SemanticComponents.All(item => item.Id != id)) return false;
        SetSelection(new ImagineSelectionScope(ImagineSelectionKind.SemanticComponent, id));
        return true;
    }

    public void ClearSelection() => SetSelection(new ImagineSelectionScope(ImagineSelectionKind.None));

    public bool CommitObjectTransform(Guid objectId, ImagineTransform transform, string operation = "transform")
    {
        var current = Project.Objects.FirstOrDefault(item => item.Id == objectId);
        if (current is null || current.IsLocked) return false;
        var normalized = NormalizeTransform(transform);
        if (current.Transform == normalized) return false;
        Apply(
            operation,
            new ImagineSelectionScope(ImagineSelectionKind.Object, objectId),
            "user",
            null,
            project => project with
            {
                Objects = project.Objects
                    .Select(item => item.Id == objectId ? item with { Transform = normalized } : item)
                    .ToArray(),
                Selection = new ImagineSelectionScope(ImagineSelectionKind.Object, objectId)
            });
        return true;
    }

    public bool ApplyAssistantObjectEdit(ImagineAssistantEditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (!result.Succeeded || result.ObjectId is not Guid objectId) return false;
        var current = Project.Objects.FirstOrDefault(item => item.Id == objectId);
        if (current is null || current.IsLocked) return false;
        var transform = NormalizeTransform(result.Transform ?? current.Transform);
        var text = current.Kind == ImagineObjectKind.Text && !string.IsNullOrWhiteSpace(result.Text) ? result.Text.Trim() : current.Text;
        var fill = result.Fill is { Length: 7 } value && value[0] == '#' && value.Skip(1).All(Uri.IsHexDigit) ? value.ToUpperInvariant() : current.Fill;
        if (current.Transform == transform && current.Text == text && current.Fill == fill) return false;
        Apply(
            "ai-structural-edit",
            new ImagineSelectionScope(ImagineSelectionKind.Object, objectId),
            "ai-structural",
            result.Model,
            project => project with
            {
                Objects = project.Objects.Select(item => item.Id == objectId ? item with { Transform = transform, Text = text, Fill = fill } : item).ToArray(),
                Selection = new ImagineSelectionScope(ImagineSelectionKind.Object, objectId)
            });
        return true;
    }

    public bool MoveSelected(double deltaX, double deltaY)
    {
        var item = SelectedObject();
        return item is not null && CommitObjectTransform(
            item.Id,
            item.Transform with { X = item.Transform.X + deltaX, Y = item.Transform.Y + deltaY },
            "move");
    }

    public bool ResizeSelected(double width, double height)
    {
        var item = SelectedObject();
        return item is not null && CommitObjectTransform(
            item.Id,
            item.Transform with { Width = Math.Max(12, width), Height = Math.Max(12, height) },
            "resize");
    }

    public bool RotateSelected(double deltaDegrees)
    {
        var item = SelectedObject();
        return item is not null && CommitObjectTransform(
            item.Id,
            item.Transform with { RotationDegrees = NormalizeDegrees(item.Transform.RotationDegrees + deltaDegrees) },
            "rotate");
    }

    public bool AlignSelectedHorizontal(double position)
    {
        var item = SelectedObject();
        if (item is null || item.IsLocked || !double.IsFinite(position)) return false;
        var factor = Math.Clamp(position, 0, 1);
        var radians = item.Transform.RotationDegrees * Math.PI / 180d;
        var visibleWidth = Math.Abs(item.Transform.Width * Math.Cos(radians)) + Math.Abs(item.Transform.Height * Math.Sin(radians));
        var visibleLeft = Math.Max(0, Project.CanvasWidth - visibleWidth) * factor;
        var targetCenterX = visibleLeft + visibleWidth / 2;
        return CommitObjectTransform(
            item.Id,
            item.Transform with { X = targetCenterX - item.Transform.Width / 2 },
            "align-horizontal");
    }

    public bool AlignSelectedVertical(double position)
    {
        var item = SelectedObject();
        if (item is null || item.IsLocked || !double.IsFinite(position)) return false;
        var factor = Math.Clamp(position, 0, 1);
        var radians = item.Transform.RotationDegrees * Math.PI / 180d;
        var visibleHeight = Math.Abs(item.Transform.Width * Math.Sin(radians)) + Math.Abs(item.Transform.Height * Math.Cos(radians));
        var visibleTop = Math.Max(0, Project.CanvasHeight - visibleHeight) * factor;
        var targetCenterY = visibleTop + visibleHeight / 2;
        return CommitObjectTransform(
            item.Id,
            item.Transform with { Y = targetCenterY - item.Transform.Height / 2 },
            "align-vertical");
    }

    public bool CropCanvasToSelection()
    {
        var item = SelectedObject();
        if (item is null || !item.IsVisible) return false;
        var radians = item.Transform.RotationDegrees * Math.PI / 180d;
        var visibleWidth = Math.Abs(item.Transform.Width * Math.Cos(radians)) + Math.Abs(item.Transform.Height * Math.Sin(radians));
        var visibleHeight = Math.Abs(item.Transform.Width * Math.Sin(radians)) + Math.Abs(item.Transform.Height * Math.Cos(radians));
        var left = item.Transform.X + item.Transform.Width / 2 - visibleWidth / 2;
        var top = item.Transform.Y + item.Transform.Height / 2 - visibleHeight / 2;
        var cropLeft = Math.Clamp(left, 0, Project.CanvasWidth);
        var cropTop = Math.Clamp(top, 0, Project.CanvasHeight);
        var cropRight = Math.Clamp(left + visibleWidth, cropLeft, Project.CanvasWidth);
        var cropBottom = Math.Clamp(top + visibleHeight, cropTop, Project.CanvasHeight);
        var width = cropRight - cropLeft;
        var height = cropBottom - cropTop;
        if (width < 1 || height < 1) return false;
        if (Math.Abs(cropLeft) < .001 && Math.Abs(cropTop) < .001 &&
            Math.Abs(width - Project.CanvasWidth) < .001 && Math.Abs(height - Project.CanvasHeight) < .001) return false;

        Apply(
            "crop-canvas-to-selection",
            new ImagineSelectionScope(ImagineSelectionKind.Object, item.Id),
            "user",
            null,
            project => project with
            {
                CanvasWidth = width,
                CanvasHeight = height,
                Objects = project.Objects.Select(value => value with
                {
                    Transform = value.Transform with { X = value.Transform.X - cropLeft, Y = value.Transform.Y - cropTop }
                }).ToArray(),
                Selection = new ImagineSelectionScope(ImagineSelectionKind.Object, item.Id)
            });
        return true;
    }

    public bool DuplicateSelected()
    {
        var item = SelectedObject();
        if (item is null) return false;
        var copy = item with
        {
            Id = Guid.NewGuid(),
            Name = item.Name + " copy",
            Transform = item.Transform with { X = item.Transform.X + 24, Y = item.Transform.Y + 24 },
            ZIndex = NextZ(Project)
        };
        Apply(
            "duplicate",
            new ImagineSelectionScope(ImagineSelectionKind.Object, item.Id),
            "user",
            null,
            project => project with
            {
                Objects = project.Objects.Append(copy).ToArray(),
                Selection = new ImagineSelectionScope(ImagineSelectionKind.Object, copy.Id)
            });
        return true;
    }

    public bool DeleteSelected()
    {
        if (Project.Selection.Kind == ImagineSelectionKind.Object && Project.Selection.TargetId is Guid objectId)
        {
            if (Project.Objects.All(item => item.Id != objectId)) return false;
            Apply(
                "delete-object",
                Project.Selection,
                "user",
                null,
                project => project with
                {
                    Objects = project.Objects.Where(item => item.Id != objectId).ToArray(),
                    Selection = new ImagineSelectionScope(ImagineSelectionKind.None)
                });
            return true;
        }

        if (Project.Selection.Kind == ImagineSelectionKind.SemanticComponent && Project.Selection.TargetId is Guid componentId)
        {
            var ids = DescendantIds(componentId);
            if (ids.Count == 0) return false;
            Apply(
                "delete-semantic-component",
                Project.Selection,
                "user",
                null,
                project => project with
                {
                    SemanticComponents = project.SemanticComponents.Where(item => !ids.Contains(item.Id)).ToArray(),
                    Selection = new ImagineSelectionScope(ImagineSelectionKind.None)
                });
            return true;
        }

        return false;
    }

    public void ReplaceSemanticComponents(
        Guid assetId,
        IReadOnlyList<ImagineSemanticComponent> components,
        string? model)
    {
        ArgumentNullException.ThrowIfNull(components);
        if (Project.Assets.All(item => item.Id != assetId))
            throw new KeyNotFoundException($"Imagine asset '{assetId}' does not exist.");

        var validated = components
            .Where(item => item.AssetId == assetId)
            .Select(item => item with { Bounds = NormalizeRegion(item.Bounds) })
            .ToArray();
        Apply(
            "semantic-decomposition",
            new ImagineSelectionScope(ImagineSelectionKind.Media, assetId),
            "vision-bounds",
            model,
            project => project with
            {
                SemanticComponents = project.SemanticComponents
                    .Where(item => item.AssetId != assetId)
                    .Concat(validated)
                    .ToArray(),
                Selection = validated.Length == 0
                    ? new ImagineSelectionScope(ImagineSelectionKind.Media, assetId)
                    : new ImagineSelectionScope(ImagineSelectionKind.SemanticComponent, validated[0].Id)
            });
    }

    public ImagineAiEditRequest CreateAiEditRequest(string instruction)
    {
        if (string.IsNullOrWhiteSpace(instruction))
            throw new ArgumentException("An Imagine AI edit instruction is required.", nameof(instruction));
        if (Project.Selection.Kind == ImagineSelectionKind.None)
            throw new InvalidOperationException("Select a project target before requesting an AI edit.");

        var assets = Project.Selection.Kind switch
        {
            ImagineSelectionKind.Media when Project.Selection.TargetId is Guid mediaId => [mediaId],
            ImagineSelectionKind.Object when SelectedObject()?.AssetId is Guid assetId => [assetId],
            ImagineSelectionKind.SemanticComponent when Project.Selection.TargetId is Guid componentId
                => Project.SemanticComponents.Where(item => item.Id == componentId).Select(item => item.AssetId).ToArray(),
            _ => Array.Empty<Guid>()
        };
        return new ImagineAiEditRequest(
            Project.Id,
            instruction.Trim(),
            Project.Selection,
            assets,
            DateTimeOffset.UtcNow);
    }

    public bool Undo()
    {
        if (_undo.Count == 0) return false;
        var current = Project;
        var previous = _undo.Pop();
        _redo.Push(current);
        Project = previous with
        {
            History = current.History.Append(Audit("undo", current.Selection, "user", null, current, previous)).ToArray(),
            UpdatedAt = DateTimeOffset.UtcNow
        };
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public bool Redo()
    {
        if (_redo.Count == 0) return false;
        var current = Project;
        var next = _redo.Pop();
        _undo.Push(current);
        Project = next with
        {
            History = current.History.Append(Audit("redo", next.Selection, "user", null, current, next)).ToArray(),
            UpdatedAt = DateTimeOffset.UtcNow
        };
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private void SetSelection(ImagineSelectionScope selection)
    {
        if (Project.Selection == selection) return;
        Project = Project with { Selection = selection, UpdatedAt = DateTimeOffset.UtcNow };
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Apply(
        string operation,
        ImagineSelectionScope scope,
        string provenance,
        string? model,
        Func<ImagineProject, ImagineProject> update)
    {
        var before = Project;
        var changed = update(before);
        if (ReferenceEquals(before, changed) || before == changed) return;
        _undo.Push(before);
        _redo.Clear();
        var now = DateTimeOffset.UtcNow;
        changed = changed with { UpdatedAt = now };
        var audit = Audit(operation, scope, provenance, model, before, changed, now);
        Project = changed with { History = before.History.Append(audit).ToArray() };
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private ImagineEditableObject? SelectedObject() =>
        Project.Selection.Kind == ImagineSelectionKind.Object && Project.Selection.TargetId is Guid id
            ? Project.Objects.FirstOrDefault(item => item.Id == id)
            : null;

    private HashSet<Guid> DescendantIds(Guid rootId)
    {
        var result = new HashSet<Guid>();
        var pending = new Queue<Guid>();
        pending.Enqueue(rootId);
        while (pending.TryDequeue(out var id))
        {
            if (!result.Add(id)) continue;
            foreach (var child in Project.SemanticComponents.Where(item => item.ParentId == id))
                pending.Enqueue(child.Id);
        }
        return result;
    }

    private static ImagineTransform NormalizeTransform(ImagineTransform value) => value with
    {
        Width = Math.Max(12, value.Width),
        Height = Math.Max(12, value.Height),
        RotationDegrees = NormalizeDegrees(value.RotationDegrees)
    };

    private static ImagineRegion NormalizeRegion(ImagineRegion value)
    {
        var x = Math.Clamp(value.X, 0, 1);
        var y = Math.Clamp(value.Y, 0, 1);
        var width = Math.Clamp(value.Width, 0, 1 - x);
        var height = Math.Clamp(value.Height, 0, 1 - y);
        return new ImagineRegion(x, y, width, height);
    }

    private static double NormalizeDegrees(double value)
    {
        var normalized = value % 360;
        return normalized < -180 ? normalized + 360 : normalized > 180 ? normalized - 360 : normalized;
    }

    private static int NextZ(ImagineProject project) =>
        project.Objects.Length == 0 ? 0 : project.Objects.Max(item => item.ZIndex) + 1;

    private static ImagineEditRecord Audit(
        string operation,
        ImagineSelectionScope scope,
        string provenance,
        string? model,
        ImagineProject before,
        ImagineProject after,
        DateTimeOffset? createdAt = null) =>
        new(
            Guid.NewGuid(),
            operation,
            scope,
            provenance,
            model,
            Snapshot(before),
            Snapshot(after),
            createdAt ?? DateTimeOffset.UtcNow);

    private static string Snapshot(ImagineProject project) =>
        JsonSerializer.Serialize(project with { History = [] }, SnapshotOptions);
}
