using System.Globalization;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.HavenUI.Backend;
using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;
using HavenContainer = Haven.UI.Components.Container;
using HavenPage = Haven.UI.Components.Page;
using HavenText = Haven.UI.Components.Text;

namespace Haven.Desktop.Views.Pages.Imagine;

/// <summary>Avalonia is only the platform host/file-picker boundary; all visible Imagine product UI is Haven.UI.</summary>
public sealed partial class ImaginePage : UserControl, IDisposable
{
    private readonly IImagineProjectRepository _projects; private readonly IImagineSemanticService _semantic; private readonly IImagineAssistantService _assistant;
    private readonly ImagineWorkspaceScene _scene; private readonly HavenSceneControl _host; private ImagineProjectSession? _session; private IReadOnlyList<ImagineProject> _recent = [];
    private CancellationTokenSource? _operationCancellation; private CancellationTokenSource? _autosaveCancellation; private bool _initialised; private bool _disposed;
    public ImaginePage(IImagineProjectRepository projects, IImagineSemanticService semantic, IImagineAssistantService assistant, ImagineGenerationCommand generationCommand)
    {
        _projects = projects; _semantic = semantic; _assistant = assistant; _generationCommand = generationCommand; _scene = new ImagineWorkspaceScene(); _host = new HavenSceneControl { Root = _scene.Root }; Content = _host;
        _host.InputSubmitted += input => { if (ReferenceEquals(input, _scene.AssistantInput)) _ = ApplyAssistantAsync(_scene.AssistantInput.Text); else if (ReferenceEquals(input, _scene.TextInput)) AddText(); };
        WireScene(); WireGenerationScene(); WireVideoExport(); AttachedToVisualTree += (_, _) => _ = InitialiseAsync(); SizeChanged += (_, e) => _scene.SetViewportWidth(e.NewSize.Width);
    }
    public Guid? ProjectId => _session?.Project.Id; public event Action<string>? InspectInVisionRequested;
    public async Task ImportPathAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return; var kind = MediaKind(path); if (kind is null) { SetStatus("Imagine supports common image, audio and video files."); return; }
        await RunOperationAsync("Importing media…", async token => { var session = _session; if (session is null) { var project = await _projects.CreateAsync("Untitled Imagine Project", 1600, 1000, token); session = new ImagineProjectSession(project); AttachSession(session); } var asset = await _projects.ImportAssetAsync(session.Project.Id, path, kind.Value, token); session.AddImportedAsset(asset); _scene.SetMode(asset.Kind); await _projects.SaveAsync(session.Project, token); SetStatus($"Imported {asset.Name}. The source file was left unchanged."); });
    }
    private void WireScene()
    {
        foreach (var action in new[]
        {
            (Name: "AlignLeft", Label: "Align left", Horizontal: true, Position: 0d),
            (Name: "AlignCentre", Label: "Align centre", Horizontal: true, Position: .5d),
            (Name: "AlignRight", Label: "Align right", Horizontal: true, Position: 1d),
            (Name: "AlignTop", Label: "Align top", Horizontal: false, Position: 0d),
            (Name: "AlignMiddle", Label: "Align middle", Horizontal: false, Position: .5d),
            (Name: "AlignBottom", Label: "Align bottom", Horizontal: false, Position: 1d)
        })
        {
            var button = new HavenButton { Name = action.Name, Content = action.Label, Variant = ButtonVariant.Ghost };
            button.Invoked += (_, _) =>
            {
                var changed = action.Horizontal
                    ? _session?.AlignSelectedHorizontal(action.Position) == true
                    : _session?.AlignSelectedVertical(action.Position) == true;
                SetStatus(changed ? action.Label + "." : "Select an unlocked image object before aligning.");
            };
            _scene.ImageTools.Add(button);
        }

        var crop = new HavenButton { Name = "CropCanvasToSelection", Content = "Crop canvas to selection", Variant = ButtonVariant.Ghost };
        crop.Invoked += (_, _) =>
        {
            var changed = _session?.CropCanvasToSelection() == true;
            if (changed) _scene.Canvas.Fit();
            SetStatus(changed ? "Cropped canvas to the selected object." : "Select a visible image object before cropping the canvas.");
        };
        _scene.ImageTools.Add(crop);

        var ellipse = new HavenButton { Name = "AddEllipse", Content = "Ellipse", IconKey = "target", Variant = ButtonVariant.Ghost };
        ellipse.Invoked += (_, _) =>
        {
            _session?.AddEllipse();
            SetStatus("Ellipse added.");
        };
        _scene.ImageTools.Add(ellipse);

        var fillInput = new Input { Name = "Imagine.FillInput", Placeholder = "#RRGGBB" };
        fillInput.SetValue(HavenProperties.MinWidth, HavenLength.Px(100));
        _scene.ImageTools.Add(fillInput);
        var applyFill = new HavenButton { Name = "Imagine.ApplyFill", Content = "Apply fill", IconKey = "palette", Variant = ButtonVariant.Ghost };
        applyFill.Invoked += (_, _) =>
        {
            var changed = _session?.SetSelectedFill(fillInput.Text.Trim()) == true;
            SetStatus(changed ? "Updated selection fill." : "Select an unlocked object and enter a colour as #RRGGBB.");
        };
        _scene.ImageTools.Add(applyFill);

        _scene.ImportRequested += async (_, _) => await PickImportAsync(); _scene.SaveRequested += async (_, _) => await SaveAsync(); _scene.ExportRequested += async (_, _) => await ExportAsync();
        _scene.UndoRequested += (_, _) => { if (_session?.Undo() == true) SetStatus("Undo"); }; _scene.RedoRequested += (_, _) => { if (_session?.Redo() == true) SetStatus("Redo"); };
        _scene.AddRectangleRequested += (_, _) => { _session?.AddRectangle(); SetStatus("Rectangle added."); }; _scene.AddTextRequested += (_, _) => AddText(); _scene.DuplicateRequested += (_, _) => { if (_session?.DuplicateSelected() == true) SetStatus("Selection duplicated."); };
        _scene.CopyRequested += (_, _) => SetStatus(_session?.CopySelected() == true ? "Copied semantic object." : "Select an image object to copy."); _scene.CutRequested += (_, _) => SetStatus(_session?.CutSelected() == true ? "Cut semantic object." : "Select an unlocked image object to cut."); _scene.PasteRequested += (_, _) => SetStatus(_session?.PasteClipboard() == true ? "Pasted semantic object." : "There is no compatible Imagine object to paste.");
        _scene.DeleteRequested += (_, _) => { if (_session?.DeleteSelected() == true) SetStatus("Selection deleted."); };
        _scene.DecomposeRequested += async (_, _) => await DecomposeAsync(); _scene.FitRequested += (_, _) => _scene.Canvas.Fit(); _scene.ProjectRequested += project => _ = OpenProjectAsync(project.Id); _scene.AssetRequested += SelectAsset; _scene.ComponentRequested += component => _session?.SelectSemanticComponent(component.Id); _scene.AssistantRequested += prompt => _ = ApplyAssistantAsync(prompt); _scene.InspectVisionRequested += (_, _) => InspectSelectedInVision();
        _scene.LayerSelectRequested += id => _session?.SelectObject(id);
        _scene.LayerVisibilityRequested += id =>
        {
            if (_session?.ToggleObjectVisibility(id) != true) return;
            var layer = _session.Project.Objects.First(item => item.Id == id);
            SetStatus(layer.IsVisible ? $"Shown {layer.Name}." : $"Hidden {layer.Name}.");
        };
        _scene.LayerLockRequested += id =>
        {
            if (_session?.ToggleObjectLock(id) != true) return;
            var layer = _session.Project.Objects.First(item => item.Id == id);
            SetStatus(layer.IsLocked ? $"Locked {layer.Name}." : $"Unlocked {layer.Name}.");
        };
        _scene.LayerMoveRequested += (id, direction) =>
        {
            var changed = _session?.MoveObjectLayer(id, direction) == true;
            SetStatus(changed
                ? (direction > 0 ? "Raised layer." : "Lowered layer.")
                : "The layer is locked or already at that edge of the stack.");
        };
    }
    private async Task InitialiseAsync()
    {
        if (_initialised || _disposed) return; _initialised = true; SetStatus("Loading Imagine projects…");
        try { _recent = await _projects.GetRecentAsync(20, CancellationToken.None); _scene.ShowHome(_recent); SetStatus("Ready to create."); }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or JsonException) { _scene.ShowHome([]); SetStatus("Imagine could not load its project workspace: " + exception.Message); }
    }
    private async Task OpenProjectAsync(Guid id) => await RunOperationAsync("Opening project…", async token => { var project = await _projects.GetAsync(id, token); if (project is null) { SetStatus("That Imagine project no longer exists."); return; } AttachSession(new ImagineProjectSession(project)); SetStatus($"Opened {project.Name}."); });
    private void AttachSession(ImagineProjectSession session) { if (_session is not null) _session.Changed -= OnSessionChanged; _session = session; _session.Changed += OnSessionChanged; _scene.SetSession(session); _scene.ShowEditor(); RefreshScene(); }
    private void OnSessionChanged(object? sender, EventArgs e) { Dispatcher.UIThread.Post(RefreshScene); QueueAutosave(); }
    private void QueueAutosave() { _autosaveCancellation?.Cancel(); _autosaveCancellation?.Dispose(); var cancellation = new CancellationTokenSource(); _autosaveCancellation = cancellation; _ = AutosaveAsync(cancellation); }
    private async Task AutosaveAsync(CancellationTokenSource cancellation) { try { await Task.Delay(180, cancellation.Token); if (_session is { } session) await _projects.SaveAsync(session.Project, cancellation.Token); } catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException) { await Dispatcher.UIThread.InvokeAsync(() => SetStatus("Autosave failed: " + exception.Message)); } }
    private async Task PickImportAsync() { var storage = TopLevel.GetTopLevel(this)?.StorageProvider; if (storage is null) { SetStatus("The platform file picker is unavailable."); return; } var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Import media into Imagine", AllowMultiple = true }); foreach (var path in files.Select(file => file.TryGetLocalPath()).OfType<string>()) await ImportPathAsync(path); }
    private async Task SaveAsync() { if (_session is null) return; await RunOperationAsync("Saving project…", async token => { await _projects.SaveAsync(_session.Project, token); await RefreshRecentAsync(token); SetStatus("Project saved."); }); }
    private async Task ExportAsync()
    {
        if (_session is null) return; var storage = TopLevel.GetTopLevel(this)?.StorageProvider; if (storage is null) { SetStatus("The platform save picker is unavailable."); return; } if (_scene.Mode == ImagineMediaKind.Image) { var imageDestination = await storage.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Export Imagine image", SuggestedFileName = SafeFileName(_session.Project.Name) + ".png" }); var imagePath = imageDestination?.TryGetLocalPath(); if (string.IsNullOrWhiteSpace(imagePath)) return; await RunOperationAsync("Exporting image…", async token => { await _projects.SaveAsync(_session.Project, token); var exportedImage = await ImagineRasterExporter.ExportAsync(_session.Project, imagePath, token); SetStatus("Exported image to " + exportedImage); }); return; } var destination = await storage.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Export Imagine project bundle", SuggestedFileName = SafeFileName(_session.Project.Name) + ".haven-imagine" }); var path = destination?.TryGetLocalPath(); if (string.IsNullOrWhiteSpace(path)) return;
        await RunOperationAsync("Exporting project…", async token => { await _projects.SaveAsync(_session.Project, token); var exported = await _projects.ExportBundleAsync(_session.Project, path, token); SetStatus("Exported project bundle to " + exported); });
    }
    private async Task DecomposeAsync() { if (_session is null) return; var assetId = SelectedImageAssetId(); if (assetId is null) { SetStatus("Select an imported image object before semantic decomposition."); return; } await RunOperationAsync("Analysing semantic components…", async token => { var result = await _semantic.DecomposeImageAsync(_session.Project, assetId.Value, token); if (result.Succeeded) { _session.ReplaceSemanticComponents(assetId.Value, result.Components, result.Model); await _projects.SaveAsync(_session.Project, token); } SetStatus(result.Status); }); }
    private async Task ApplyAssistantAsync(string instruction)
    {
        if (_session is null || string.IsNullOrWhiteSpace(instruction)) return; ImagineAiEditRequest request; try { request = _session.CreateAiEditRequest(instruction); } catch (Exception exception) when (exception is ArgumentException or InvalidOperationException) { SetStatus(exception.Message); return; } _scene.AssistantInput.Text = string.Empty;
        await RunOperationAsync("Haven is editing the selected object…", async token => { var result = await _assistant.ProposeEditAsync(_session.Project, request, token); if (result.Succeeded && _session.ApplyAssistantObjectEdit(result)) await _projects.SaveAsync(_session.Project, token); SetStatus(result.Status); });
    }
    private void AddText() { if (_session is null) return; var value = _scene.TextInput.Text.Trim(); _session.AddText(string.IsNullOrWhiteSpace(value) ? "Text" : value); _scene.TextInput.Text = string.Empty; SetStatus("Text added."); }
    private void SelectAsset(ImagineMediaAsset asset)
    {
        if (_session is null) return;
        _scene.SetMode(asset.Kind);
        if (asset.Kind == ImagineMediaKind.Image)
        {
            var match = _session.Project.Objects.LastOrDefault(item => item.AssetId == asset.Id);
            if (match is not null) _session.SelectObject(match.Id);
            else SetStatus($"{asset.Name} is in the project, but no editable image object currently references it.");
            return;
        }

        var clip = _session.Project.Tracks.SelectMany(track => track.Clips).LastOrDefault(item => item.AssetId == asset.Id);
        if (clip is not null)
        {
            _session.SelectClip(clip.Id);
            SetStatus($"Selected {asset.Name} in the {asset.Kind.ToString().ToLowerInvariant()} timeline.");
        }
        else SetStatus($"{asset.Name} is in the project, but no timeline clip currently references it.");
    }
    private void InspectSelectedInVision() { if (_session is null) return; var assetId = SelectedImageAssetId(); var path = assetId is Guid id ? _session.Project.Assets.FirstOrDefault(item => item.Id == id)?.ManagedPath : null; if (string.IsNullOrWhiteSpace(path)) { SetStatus("Select an image before opening it in Vision."); return; } InspectInVisionRequested?.Invoke(path); }
    private Guid? SelectedImageAssetId() { if (_session is null) return null; var selection = _session.Project.Selection; if (selection.Kind == ImagineSelectionKind.Object && selection.TargetId is Guid objectId && _session.Project.Objects.FirstOrDefault(item => item.Id == objectId)?.AssetId is Guid assetId && _session.Project.Assets.Any(item => item.Id == assetId && item.Kind == ImagineMediaKind.Image)) return assetId; if (selection.Kind == ImagineSelectionKind.SemanticComponent && selection.TargetId is Guid componentId) return _session.Project.SemanticComponents.FirstOrDefault(item => item.Id == componentId)?.AssetId; return null; }
    private async Task RefreshRecentAsync(CancellationToken token) { _recent = await _projects.GetRecentAsync(20, token); await Dispatcher.UIThread.InvokeAsync(RefreshScene); } private void RefreshScene() { if (_session is null) { _scene.SetUnavailable("No Imagine project is open."); return; } _scene.Sync(_session.Project, _recent); }
    private async Task RunOperationAsync(string status, Func<CancellationToken, Task> operation) { _operationCancellation?.Cancel(); _operationCancellation?.Dispose(); var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(5)); _operationCancellation = cancellation; _scene.SetBusy(true); SetStatus(status); try { await operation(cancellation.Token); } catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { SetStatus("Operation cancelled or timed out. No incomplete result was committed."); } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or JsonException or HttpRequestException) { SetStatus("Imagine operation failed: " + exception.Message); } finally { if (ReferenceEquals(_operationCancellation, cancellation)) _operationCancellation = null; _scene.SetBusy(false); cancellation.Dispose(); } }
    private void SetStatus(string value) => Dispatcher.UIThread.Post(() => _scene.SetStatus(value));
    private static ImagineMediaKind? MediaKind(string path) => Path.GetExtension(path).ToLowerInvariant() switch { ".png" or ".jpg" or ".jpeg" or ".webp" or ".gif" or ".bmp" => ImagineMediaKind.Image, ".wav" or ".mp3" or ".m4a" or ".aac" or ".flac" or ".ogg" => ImagineMediaKind.Audio, ".mp4" or ".mov" or ".mkv" or ".webm" or ".avi" => ImagineMediaKind.Video, _ => null };
    private static string SafeFileName(string value) { var invalid = Path.GetInvalidFileNameChars().ToHashSet(); var safe = new string(value.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim(); return string.IsNullOrWhiteSpace(safe) ? "Imagine Project" : safe; }
    public void Dispose() { if (_disposed) return; _disposed = true; if (_session is not null) _session.Changed -= OnSessionChanged; _operationCancellation?.Cancel(); _operationCancellation?.Dispose(); _autosaveCancellation?.Cancel(); _autosaveCancellation?.Dispose(); _scene.Dispose(); }
}

internal class ImagineCanvasElement : HavenContainer, IHavenDrawCommandSource, IHavenPointerInputTarget, IHavenScrollInputTarget, IDisposable
{
    private enum Interaction { None, Move, Resize, Rotate, Pan }
    private ImagineProjectSession? _session;
    protected ImagineProjectSession? Session => _session;
    private Interaction _interaction;
    private ImagineResizeHandle _resizeHandle;
    private ImagineEditableObject? _original;
    private ImagineTransform? _preview;
    private HavenPoint _pointerStart;
    private HavenPoint _offset;
    private double _zoom = 1;
    private double? _guideX;
    private double? _guideY;
    private bool _fitPending = true;

    public ImagineCanvasElement()
    {
        Accessibility.Role = HavenAccessibleRole.Image;
        Accessibility.Focusable = true;
        Accessibility.AccessibleName = "Imagine editable image canvas";
        SetValue(HavenProperties.Background, "SurfaceRaised");
        SetValue(HavenProperties.BorderColor, "Border");
        SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
        SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(16)));
        SetValue(HavenProperties.Clip, true);
    }

    public void SetSession(ImagineProjectSession session)
    {
        if (ReferenceEquals(_session, session)) return;
        if (_session is not null) _session.Changed -= OnSessionChanged;
        _session = session;
        _session.Changed += OnSessionChanged;
        _fitPending = true;
        Invalidate();
    }

    public void Fit() { _fitPending = true; Invalidate(); }
    public void InvalidateCanvas() => Invalidate();

    public bool PointerPressed(HavenPointerInput input)
    {
        if (_session is null) return false;
        EnsureFit();
        _pointerStart = input.LocalPosition;
        _guideX = _guideY = null;
        var selected = SelectedObject();
        if (selected is not null)
        {
            var screen = ToScreen(selected.Transform);
            var absolute = new HavenPoint(Bounds.X + input.LocalPosition.X, Bounds.Y + input.LocalPosition.Y);
            if (ImagineCanvasGeometry.HitRotateHandle(screen, selected.Transform.RotationDegrees, absolute))
            {
                Begin(Interaction.Rotate, selected);
                return true;
            }

            var handle = ImagineCanvasGeometry.HitResizeHandle(screen, selected.Transform.RotationDegrees, absolute);
            if (handle != ImagineResizeHandle.None)
            {
                _resizeHandle = handle;
                Begin(Interaction.Resize, selected);
                return true;
            }
        }

        var board = ToBoard(input.LocalPosition);
        var hit = _session.Project.Objects
            .Where(item => item.IsVisible)
            .OrderByDescending(item => item.ZIndex)
            .FirstOrDefault(item => ImagineCanvasGeometry.Contains(item.Transform, board));
        if (hit is not null)
        {
            _session.SelectObject(hit.Id);
            Begin(Interaction.Move, hit);
            return true;
        }

        _session.ClearSelection();
        _interaction = Interaction.Pan;
        return true;
    }

    public bool PointerMoved(HavenPointerInput input)
    {
        if (_session is null) return false;
        var delta = new HavenPoint((input.LocalPosition.X - _pointerStart.X) / Math.Max(.05, _zoom), (input.LocalPosition.Y - _pointerStart.Y) / Math.Max(.05, _zoom));
        if (_original is not null && _preview is not null)
        {
            if (_interaction == Interaction.Move)
            {
                var raw = _original.Transform with { X = _original.Transform.X + delta.X, Y = _original.Transform.Y + delta.Y };
                var snapped = ImagineCanvasGeometry.SnapMove(_session.Project, _original.Id, raw, _zoom);
                _preview = snapped.Transform;
                _guideX = snapped.GuideX;
                _guideY = snapped.GuideY;
            }
            else if (_interaction == Interaction.Resize)
            {
                _preview = ImagineCanvasGeometry.ResizeFromCorner(_original.Transform, _resizeHandle, ToBoard(input.LocalPosition));
                _guideX = _guideY = null;
            }
            else if (_interaction == Interaction.Rotate)
            {
                _preview = _original.Transform with { RotationDegrees = RotationFor(_original.Transform, input.LocalPosition) };
                _guideX = _guideY = null;
            }
        }
        else if (_interaction == Interaction.Pan)
        {
            _offset = new HavenPoint(_offset.X + input.LocalPosition.X - _pointerStart.X, _offset.Y + input.LocalPosition.Y - _pointerStart.Y);
            _pointerStart = input.LocalPosition;
        }

        Invalidate();
        return true;
    }

    public bool PointerReleased(HavenPointerInput input)
    {
        if (_session is null) return false;
        if (_original is not null && _preview is not null && _interaction is Interaction.Move or Interaction.Resize or Interaction.Rotate)
        {
            var operation = _interaction == Interaction.Move ? "move" : _interaction == Interaction.Resize ? "resize" : "rotate";
            _session.CommitObjectTransform(_original.Id, _preview, operation);
        }

        _interaction = Interaction.None;
        _resizeHandle = ImagineResizeHandle.None;
        _original = null;
        _preview = null;
        _guideX = _guideY = null;
        Invalidate();
        return true;
    }

    public bool PointerWheel(HavenPoint localPosition, double deltaX, double deltaY)
    {
        if (_session is null || Math.Abs(deltaY) < .001) return false;
        EnsureFit();
        var before = ToBoard(localPosition);
        _zoom = Math.Clamp(_zoom * (deltaY < 0 ? 1.1 : .9), .05, 8);
        _offset = new HavenPoint(localPosition.X - before.X * _zoom, localPosition.Y - before.Y * _zoom);
        Invalidate();
        return true;
    }

    public void Draw(HavenDrawingContext context, double opacity)
    {
        if (_session is null || Bounds.Width <= 2 || Bounds.Height <= 2) return;
        EnsureFit();
        context.Add(new HavenFillRoundedRectCommand(Bounds, new HavenTokenBrush("SurfaceRaised"), 16, opacity));
        var project = _session.Project;
        var canvas = ToScreen(new ImagineTransform(0, 0, project.CanvasWidth, project.CanvasHeight));
        context.Add(new HavenFillRoundedRectCommand(canvas, new HavenSolidBrush(255, 255, 255, 255), 4, opacity));
        context.Add(new HavenStrokeRoundedRectCommand(canvas, new HavenPen(new HavenTokenBrush("Border"), 1), 4, opacity));
        foreach (var item in project.Objects.Where(item => item.IsVisible).OrderBy(item => item.ZIndex)) DrawObject(context, item, opacity);
        DrawSemanticBounds(context, opacity);
        if (SelectedObject() is { } selected) DrawSelection(context, selected, opacity);
        DrawGuides(context, opacity);
    }

    private void DrawObject(HavenDrawingContext context, ImagineEditableObject item, double opacity)
    {
        var transform = _preview is not null && _original?.Id == item.Id ? _preview : item.Transform;
        var rect = ToScreen(transform);
        if (Math.Abs(transform.RotationDegrees) > .01)
            context.Add(new HavenPushTransformCommand(rect, new HavenTransform(RotationDegrees: transform.RotationDegrees), new HavenPoint(rect.X + rect.Width / 2, rect.Y + rect.Height / 2)));
        switch (item.Kind)
        {
            case ImagineObjectKind.Image:
                var source = item.AssetId is Guid assetId ? _session?.Project.Assets.FirstOrDefault(asset => asset.Id == assetId)?.ManagedPath : null;
                if (!string.IsNullOrWhiteSpace(source)) context.Add(new HavenImageCommand(rect, new HavenImage(source), HavenImageLayout.Contain, opacity));
                break;
            case ImagineObjectKind.Ellipse:
                context.Add(new HavenEllipseCommand(rect, Brush(item.Fill), null, opacity));
                break;
            case ImagineObjectKind.Text:
                context.Add(new HavenTextCommand(rect, new HavenTextLayout(item.Text, "Segoe UI", Math.Max(14, 26 * _zoom), 600, rect.Width, true), Brush(item.Fill), opacity));
                break;
            default:
                context.Add(new HavenFillRoundedRectCommand(rect, Brush(item.Fill), 8, opacity));
                break;
        }
        if (Math.Abs(transform.RotationDegrees) > .01) context.Add(new HavenPopTransformCommand(rect));
    }

    private void DrawSemanticBounds(HavenDrawingContext context, double opacity)
    {
        if (_session is null) return;
        foreach (var component in _session.Project.SemanticComponents)
        {
            var image = _session.Project.Objects.LastOrDefault(item => item.AssetId == component.AssetId && item.Kind == ImagineObjectKind.Image);
            if (image is null) continue;
            var b = component.Bounds;
            var logical = new ImagineTransform(image.Transform.X + b.X * image.Transform.Width, image.Transform.Y + b.Y * image.Transform.Height, b.Width * image.Transform.Width, b.Height * image.Transform.Height);
            var screen = ToScreen(logical);
            var selected = _session.Project.Selection.Kind == ImagineSelectionKind.SemanticComponent && _session.Project.Selection.TargetId == component.Id;
            context.Add(new HavenStrokeRoundedRectCommand(screen, new HavenPen(new HavenSolidBrush((byte)(selected ? 255 : 150), 142, 36, 170), selected ? 2.5 : 1.2), 4, opacity));
        }
    }

    private void DrawSelection(HavenDrawingContext context, ImagineEditableObject item, double opacity)
    {
        var transform = _preview is not null && _original?.Id == item.Id ? _preview : item.Transform;
        var rect = ToScreen(transform);
        var pen = new HavenPen(new HavenSolidBrush(255, 30, 136, 229), 2);
        var rotated = Math.Abs(transform.RotationDegrees) > .01;
        if (rotated) context.Add(new HavenPushTransformCommand(rect, new HavenTransform(RotationDegrees: transform.RotationDegrees), new HavenPoint(rect.X + rect.Width / 2, rect.Y + rect.Height / 2)));
        context.Add(new HavenStrokeRoundedRectCommand(rect, pen, 5, opacity));
        foreach (var handle in ImagineCanvasGeometry.CornerHandles(rect))
            context.Add(new HavenFillRoundedRectCommand(handle, new HavenSolidBrush(255, 30, 136, 229), 3, opacity));
        var rotate = ImagineCanvasGeometry.RotateHandle(rect);
        context.Add(new HavenLineCommand(new HavenPoint(rect.X + rect.Width / 2, rect.Y), new HavenPoint(rotate.X + rotate.Width / 2, rotate.Bottom), pen, opacity));
        context.Add(new HavenEllipseCommand(rotate, new HavenSolidBrush(255, 255, 255, 255), pen, opacity));
        if (rotated) context.Add(new HavenPopTransformCommand(rect));
    }

    private void DrawGuides(HavenDrawingContext context, double opacity)
    {
        if (_session is null || (_guideX is null && _guideY is null)) return;
        var canvas = ToScreen(new ImagineTransform(0, 0, _session.Project.CanvasWidth, _session.Project.CanvasHeight));
        var pen = new HavenPen(new HavenSolidBrush(220, 230, 70, 190), 1);
        if (_guideX is double x)
        {
            var screenX = Bounds.X + _offset.X + x * _zoom;
            context.Add(new HavenLineCommand(new HavenPoint(screenX, canvas.Y), new HavenPoint(screenX, canvas.Bottom), pen, opacity));
        }
        if (_guideY is double y)
        {
            var screenY = Bounds.Y + _offset.Y + y * _zoom;
            context.Add(new HavenLineCommand(new HavenPoint(canvas.X, screenY), new HavenPoint(canvas.Right, screenY), pen, opacity));
        }
    }

    private void Begin(Interaction interaction, ImagineEditableObject item)
    {
        _interaction = interaction;
        _original = item;
        _preview = item.Transform;
    }

    private ImagineEditableObject? SelectedObject() => _session?.Project.Selection is { Kind: ImagineSelectionKind.Object, TargetId: Guid id } ? _session.Project.Objects.FirstOrDefault(item => item.Id == id) : null;

    private void EnsureFit()
    {
        if (!_fitPending || _session is null || Bounds.Width <= 2 || Bounds.Height <= 2) return;
        var project = _session.Project;
        _zoom = Math.Clamp(Math.Min((Bounds.Width - 56) / project.CanvasWidth, (Bounds.Height - 56) / project.CanvasHeight), .05, 4);
        _offset = new HavenPoint((Bounds.Width - project.CanvasWidth * _zoom) / 2, (Bounds.Height - project.CanvasHeight * _zoom) / 2);
        _fitPending = false;
    }

    private HavenPoint ToBoard(HavenPoint local) => new((local.X - _offset.X) / Math.Max(.05, _zoom), (local.Y - _offset.Y) / Math.Max(.05, _zoom));
    private HavenRect ToScreen(ImagineTransform value) => new(Bounds.X + _offset.X + value.X * _zoom, Bounds.Y + _offset.Y + value.Y * _zoom, value.Width * _zoom, value.Height * _zoom);

    private double RotationFor(ImagineTransform value, HavenPoint local)
    {
        var center = new HavenPoint(_offset.X + (value.X + value.Width / 2) * _zoom, _offset.Y + (value.Y + value.Height / 2) * _zoom);
        return Math.Atan2(local.Y - center.Y, local.X - center.X) * 180 / Math.PI + 90;
    }

    private static HavenBrush Brush(string value)
    {
        if (value is not { Length: 7 } || value[0] != '#' || !value.Skip(1).All(Uri.IsHexDigit)) return new HavenSolidBrush(255, 17, 17, 17);
        return new HavenSolidBrush(255, byte.Parse(value.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture), byte.Parse(value.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture), byte.Parse(value.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
    }

    private void OnSessionChanged(object? sender, EventArgs e) => Invalidate();
    public void Dispose() { if (_session is not null) _session.Changed -= OnSessionChanged; }
}
