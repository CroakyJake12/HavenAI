using Haven.Application;
using Haven.Core;
using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;
using HavenContainer = Haven.UI.Components.Container;
using HavenPage = Haven.UI.Components.Page;
using HavenText = Haven.UI.Components.Text;

namespace Haven.Desktop.Views.Pages.Imagine;

/// <summary>Visible release workspace for Imagine. Image, Audio, and Video share project/media state but not editor chrome.</summary>
internal sealed class ImagineWorkspaceScene : IDisposable
{
    private readonly HavenPrefabCatalog _prefabs;
    private readonly DynamicUI _dynamic;
    private string _signature = string.Empty;
    private double _viewportWidth = 1280;

    public ImagineWorkspaceScene()
    {
        _prefabs = HavenPrefabCatalog.FromAssembly(typeof(ImagineWorkspaceScene).Assembly);
        Root = new HavenPage { Name = "Imagine.Root", Layout = HavenLayout.Vertical };
        Root.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        Root.SetValue(HavenProperties.Height, HavenLength.Percent(100));
        Root.SetValue(HavenProperties.Gap, HavenLength.Px(10));
        Root.SetValue(HavenProperties.Padding, HavenThickness.Parse("14px 16px"));
        Root.Accessibility.AccessibleName = "Imagine creative workspace";

        var global = Wrap("Imagine.GlobalToolbar", 6);
        foreach (var button in new[]
        {
            Action("Import", "Import", "plus"), Action("Save", "Save", "file"),
            Action("Export", "Export", "archive"), Action("Undo", "Undo", "chevron-left"),
            Action("Redo", "Redo", "chevron-right")
        }) global.Add(button);
        Root.Add(global);

        var modes = Wrap("Imagine.ModeBar", 6);
        ImageMode = Action("Imagine.Mode.Image", "Image", "image");
        AudioMode = Action("Imagine.Mode.Audio", "Audio", "music");
        VideoMode = Action("Imagine.Mode.Video", "Video", "video");
        modes.Add(ImageMode); modes.Add(AudioMode); modes.Add(VideoMode);
        ModeHint = Muted("Imagine.ModeHint", string.Empty); modes.Add(ModeHint);
        Root.Add(modes);

        ImageTools = Wrap("Imagine.ImageTools", 6);
        foreach (var button in new[]
        {
            Action("AddRectangle", "Rectangle", "window"), Action("Duplicate", "Duplicate", "plus"),
            Action("Delete", "Delete", "delete"), Action("Decompose", "Semantic parts", "sparkles"),
            Action("Fit", "Fit canvas", "search"), Action("InspectVision", "Inspect in Vision", "vision")
        }) ImageTools.Add(button);
        TextInput = new Input { Name = "Imagine.TextInput", Placeholder = "Text to add", SubmitOnEnter = true };
        TextInput.SetValue(HavenProperties.MinWidth, HavenLength.Px(160));
        ImageTools.Add(TextInput); ImageTools.Add(Action("AddText", "Add text", "edit"));
        Root.Add(ImageTools);

        TimelineTools = Wrap("Imagine.TimelineTools", 6);
        TimelineTools.Add(Action("TimelineAddTrack", "Add track", "plus"));
        var addAudioTrack = Action("TimelineAddAudioTrack", "Add audio track", "music"); addAudioTrack.Invoked += (_, _) => AddTimelineAudioTrack(); TimelineTools.Add(addAudioTrack);
        TimelineTools.Add(Action("TimelineSplit", "Split at playhead", "edit"));
        TimelineTools.Add(Action("TimelineDelete", "Delete", "delete"));
        TimelineTools.Add(Action("TimelineMute", "Mute / unmute", "volume"));
        TimelineTools.Add(Action("TimelineGainDown", "Gain −", "minus"));
        TimelineTools.Add(Action("TimelineGainUp", "Gain +", "plus"));
        TimelineTools.Add(Action("TimelineZoomOut", "Zoom −", "minus"));
        TimelineTools.Add(Action("TimelineZoomIn", "Zoom +", "plus"));
        TimelineTools.Add(Action("TimelineFit", "Fit timeline", "search"));
        Root.Add(TimelineTools);

        Body = Grid("Imagine.Body", "240px 1fr 300px", "1fr");
        Body.SetValue(HavenProperties.MinHeight, HavenLength.Px(520));
        Root.Add(Body);

        Left = Vertical("Imagine.Left", 8); Left.SetValue(HavenProperties.Column, 0); Left.SetValue(HavenProperties.Overflow, HavenOverflow.Scroll);
        Body.Add(Left); Left.Add(Heading("Projects")); RecentProjects = Runtime("Imagine.RecentProjects"); Left.Add(RecentProjects);
        Left.Add(Heading("Media")); Assets = Runtime("Imagine.Assets"); Left.Add(Assets);
        TrackSummary = Muted("Imagine.TrackSummary", "No audio/video tracks."); Left.Add(TrackSummary);

        Canvas = new ImagineMediaCanvasElement { Name = "Imagine.Canvas" };
        Canvas.SetValue(HavenProperties.Column, 1); Canvas.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        Canvas.SetValue(HavenProperties.Height, HavenLength.Percent(100)); Canvas.SetValue(HavenProperties.MinHeight, HavenLength.Px(500));
        Body.Add(Canvas);

        Right = Vertical("Imagine.Right", 8); Right.SetValue(HavenProperties.Column, 2); Right.SetValue(HavenProperties.Overflow, HavenOverflow.Scroll);
        Body.Add(Right); ProjectTitle = Heading("Untitled Imagine Project"); Right.Add(ProjectTitle);
        Selection = Muted("Imagine.Selection", "Nothing selected."); Right.Add(Selection);
        SemanticPanel = Vertical("Imagine.SemanticPanel", 8); SemanticPanel.Add(Heading("Semantic components"));
        Components = Runtime("Imagine.Components"); SemanticPanel.Add(Components);
        ComponentsEmpty = Muted("Imagine.ComponentsEmpty", "No semantic decomposition yet. Select an image and choose Semantic parts.");
        SemanticPanel.Add(ComponentsEmpty); Right.Add(SemanticPanel);
        Status = Muted("Imagine.Status", "Loading…"); Right.Add(Status);

        Assistant = _prefabs.Create("Chatbox", "Imagine-Chatbox");
        AssistantInput = Assistant.GetComponent<Input>("Instruction"); AssistantInput.Placeholder = "Ask Haven to edit the selected object";
        AssistantInput.Multiline = true; AssistantInput.SubmitOnEnter = true;
        Assistant.GetComponent<HavenButton>("AddMenu").SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
        AssistantSend = Assistant.GetComponent<HavenButton>("Send"); AssistantSend.Accessibility.AccessibleName = "Apply AI structural edit to selection";
        Root.Add(Assistant);

        _dynamic = new DynamicUI(Root, HavenDynamicUITemplateCatalog.FromAssembly(typeof(ImagineWorkspaceScene).Assembly), _prefabs);
        Wire("Import", () => ImportRequested?.Invoke(this, EventArgs.Empty)); Wire("Save", () => SaveRequested?.Invoke(this, EventArgs.Empty));
        Wire("Export", () => ExportRequested?.Invoke(this, EventArgs.Empty)); Wire("Undo", () => UndoRequested?.Invoke(this, EventArgs.Empty));
        Wire("Redo", () => RedoRequested?.Invoke(this, EventArgs.Empty)); Wire("AddRectangle", () => AddRectangleRequested?.Invoke(this, EventArgs.Empty));
        Wire("AddText", () => AddTextRequested?.Invoke(this, EventArgs.Empty)); Wire("Duplicate", () => DuplicateRequested?.Invoke(this, EventArgs.Empty));
        Wire("Delete", () => DeleteRequested?.Invoke(this, EventArgs.Empty)); Wire("Decompose", () => DecomposeRequested?.Invoke(this, EventArgs.Empty));
        Wire("Fit", () => FitRequested?.Invoke(this, EventArgs.Empty)); Wire("InspectVision", () => InspectVisionRequested?.Invoke(this, EventArgs.Empty));
        Wire("Imagine.Mode.Image", () => SetMode(ImagineMediaKind.Image)); Wire("Imagine.Mode.Audio", () => SetMode(ImagineMediaKind.Audio));
        Wire("Imagine.Mode.Video", () => SetMode(ImagineMediaKind.Video));
        Wire("TimelineAddTrack", AddTimelineTrack); Wire("TimelineSplit", () => TimelineAction(Canvas.Timeline.SplitSelected(), "Split clip at playhead."));
        Wire("TimelineDelete", () => TimelineAction(Canvas.Timeline.DeleteSelected(), "Deleted timeline selection."));
        Wire("TimelineMute", () => TimelineAction(Canvas.Timeline.ToggleMuteSelected(), "Updated mute state."));
        Wire("TimelineGainDown", () => TimelineAction(Canvas.Timeline.AdjustGainSelected(-.1), "Reduced gain."));
        Wire("TimelineGainUp", () => TimelineAction(Canvas.Timeline.AdjustGainSelected(.1), "Increased gain."));
        Wire("TimelineZoomOut", () => Canvas.Timeline.ZoomBy(.8)); Wire("TimelineZoomIn", () => Canvas.Timeline.ZoomBy(1.25));
        Wire("TimelineFit", () => Canvas.Timeline.Fit());
        AssistantSend.Invoked += (_, _) => SubmitAssistant();
        SetMode(ImagineMediaKind.Image); SetViewportWidth(1280);
    }

    public HavenPage Root { get; }
    public HavenContainer Body { get; }
    public HavenContainer Left { get; }
    public HavenContainer Right { get; }
    public HavenContainer ImageTools { get; }
    public HavenContainer TimelineTools { get; }
    public HavenContainer SemanticPanel { get; }
    public DynamicUIRuntime RecentProjects { get; }
    public DynamicUIRuntime Assets { get; }
    public DynamicUIRuntime Components { get; }
    public ImagineMediaCanvasElement Canvas { get; }
    public HavenText ProjectTitle { get; }
    public HavenText Selection { get; }
    public HavenText TrackSummary { get; }
    public HavenText ComponentsEmpty { get; }
    public HavenText Status { get; }
    public HavenText ModeHint { get; }
    public HavenButton ImageMode { get; }
    public HavenButton AudioMode { get; }
    public HavenButton VideoMode { get; }
    public Prefab Assistant { get; }
    public Input AssistantInput { get; }
    public HavenButton AssistantSend { get; }
    public Input TextInput { get; }
    public ImagineMediaKind Mode => Canvas.Mode;

    public event EventHandler? ImportRequested; public event EventHandler? SaveRequested; public event EventHandler? ExportRequested;
    public event EventHandler? UndoRequested; public event EventHandler? RedoRequested; public event EventHandler? AddRectangleRequested;
    public event EventHandler? AddTextRequested; public event EventHandler? DuplicateRequested; public event EventHandler? DeleteRequested;
    public event EventHandler? DecomposeRequested; public event EventHandler? FitRequested; public event EventHandler? InspectVisionRequested;
    public event Action<ImagineProject>? ProjectRequested; public event Action<ImagineMediaAsset>? AssetRequested;
    public event Action<ImagineSemanticComponent>? ComponentRequested; public event Action<string>? AssistantRequested;

    public void SetSession(ImagineProjectSession session)
    {
        Canvas.SetSession(session); Canvas.SetMode(Mode); _signature = string.Empty;
    }

    public void SetMode(ImagineMediaKind mode)
    {
        Canvas.SetMode(mode);
        var image = mode == ImagineMediaKind.Image;
        ImageTools.SetValue(HavenProperties.Visibility, image ? HavenVisibility.Visible : HavenVisibility.Collapsed);
        TimelineTools.SetValue(HavenProperties.Visibility, image ? HavenVisibility.Collapsed : HavenVisibility.Visible);
        Find<HavenButton>("TimelineAddAudioTrack").SetValue(HavenProperties.Visibility, mode == ImagineMediaKind.Video ? HavenVisibility.Visible : HavenVisibility.Collapsed);
        SemanticPanel.SetValue(HavenProperties.Visibility, image ? HavenVisibility.Visible : HavenVisibility.Collapsed);
        Assistant.SetValue(HavenProperties.Visibility, image ? HavenVisibility.Visible : HavenVisibility.Collapsed);
        ModeHint.Content = mode switch
        {
            ImagineMediaKind.Audio => "Multitrack audio · real clip timing/edit state; playback appears only when a media host is available.",
            ImagineMediaKind.Video => "Video + audio timeline · real clip timing/edit state; preview appears only when a native video host is available.",
            _ => "Direct image canvas · select, move, resize, rotate, snap and inspect."
        };
        ImageMode.Variant = image ? ButtonVariant.Tertiary : ButtonVariant.Ghost;
        AudioMode.Variant = mode == ImagineMediaKind.Audio ? ButtonVariant.Tertiary : ButtonVariant.Ghost;
        VideoMode.Variant = mode == ImagineMediaKind.Video ? ButtonVariant.Tertiary : ButtonVariant.Ghost;
        SetViewportWidth(_viewportWidth);
    }

    public void Sync(ImagineProject project, IReadOnlyList<ImagineProject> recent)
    {
        ProjectTitle.Content = project.Name; Selection.Content = SelectionLabel(project);
        TrackSummary.Content = project.Tracks.Length == 0 ? "No audio/video tracks." : string.Join(" · ", project.Tracks.GroupBy(track => track.Kind).Select(group => $"{group.Count()} {group.Key.ToString().ToLowerInvariant()} track{(group.Count() == 1 ? string.Empty : "s")}"));
        Canvas.InvalidateCanvas(); Canvas.Timeline.InvalidateTimeline();
        var signature = project.UpdatedAt.UtcDateTime.Ticks + "|" + string.Join('|', recent.Select(item => item.Id + ":" + item.UpdatedAt.UtcDateTime.Ticks));
        if (signature == _signature) return; _signature = signature; RecentProjects.ClearItems(); Assets.ClearItems(); Components.ClearItems();
        foreach (var item in recent.Take(20))
        {
            var row = _dynamic.CreateItem("ImagineProjectRow", RecentProjects.Name!, "project-" + item.Id.ToString("N"), new Dictionary<string, object?> { ["TITLE"] = item.Name, ["DETAIL"] = item.UpdatedAt.LocalDateTime.ToString("d MMM HH:mm") });
            row.GetComponent<HavenButton>("Open").Invoked += (_, _) => ProjectRequested?.Invoke(item);
        }
        foreach (var asset in project.Assets.OrderByDescending(item => item.CreatedAt))
        {
            var row = _dynamic.CreateItem("ImagineAssetRow", Assets.Name!, "asset-" + asset.Id.ToString("N"), new Dictionary<string, object?> { ["TITLE"] = asset.Name, ["DETAIL"] = asset.Kind + " · " + FormatBytes(asset.SizeBytes) });
            row.GetComponent<HavenButton>("Open").Invoked += (_, _) => AssetRequested?.Invoke(asset);
        }
        foreach (var component in project.SemanticComponents.OrderBy(item => item.Order))
        {
            var depth = Depth(component, project.SemanticComponents);
            var row = _dynamic.CreateItem("ImagineComponentRow", Components.Name!, "component-" + component.Id.ToString("N"), new Dictionary<string, object?> { ["TITLE"] = new string('·', depth) + (depth == 0 ? string.Empty : " ") + component.Label, ["DETAIL"] = component.Confidence is double confidence ? $"{component.Type} · {confidence:P0}" : component.Type });
            row.GetComponent<HavenButton>("Open").Invoked += (_, _) => ComponentRequested?.Invoke(component);
        }
        ComponentsEmpty.SetValue(HavenProperties.Visibility, project.SemanticComponents.Length == 0 ? HavenVisibility.Visible : HavenVisibility.Collapsed);
    }

    public void SetStatus(string value) => Status.Content = value;
    public void SetUnavailable(string value) { Status.Content = value; RecentProjects.ClearItems(); Assets.ClearItems(); Components.ClearItems(); }

    public void SetViewportWidth(double width)
    {
        if (!double.IsFinite(width) || width <= 0) return; _viewportWidth = width;
        Body.Columns = width < 760 ? "1fr" : width < 1100 ? "220px 1fr" : "240px 1fr 300px";
        Left.SetValue(HavenProperties.Visibility, width < 760 ? HavenVisibility.Collapsed : HavenVisibility.Visible);
        Right.SetValue(HavenProperties.Visibility, width < 1100 ? HavenVisibility.Collapsed : HavenVisibility.Visible);
        Canvas.SetValue(HavenProperties.Column, width < 760 ? 0 : 1);
    }

    private void AddTimelineTrack()
    {
        var id = Canvas.Timeline.AddTrack();
        SetStatus(id == Guid.Empty ? "Open an Imagine project before adding a track." : $"Added {Mode.ToString().ToLowerInvariant()} track.");
    }

    private void AddTimelineAudioTrack()
    {
        var id = Canvas.Timeline.AddTrack(ImagineTrackKind.Audio);
        SetStatus(id == Guid.Empty ? "Switch to Video before adding a separate audio track." : "Added audio track to the video timeline.");
    }

    private void TimelineAction(bool changed, string success) => SetStatus(changed ? success : "Select a compatible clip or track first.");
    private void SubmitAssistant() { var prompt = AssistantInput.Text.Trim(); if (!string.IsNullOrWhiteSpace(prompt)) AssistantRequested?.Invoke(prompt); }

    private static string SelectionLabel(ImagineProject project)
    {
        var selection = project.Selection;
        if (selection.Kind == ImagineSelectionKind.Object && selection.TargetId is Guid objectId && project.Objects.FirstOrDefault(item => item.Id == objectId) is { } obj) return $"Selected: {obj.Name} · x {obj.Transform.X:0} y {obj.Transform.Y:0} · {obj.Transform.Width:0}×{obj.Transform.Height:0} · {obj.Transform.RotationDegrees:0.#}°";
        if (selection.Kind == ImagineSelectionKind.SemanticComponent && selection.TargetId is Guid componentId && project.SemanticComponents.FirstOrDefault(item => item.Id == componentId) is { } component) return $"Semantic selection: {component.Label} · {component.Type}";
        if (selection.Kind == ImagineSelectionKind.Clip && selection.TargetId is Guid clipId)
        {
            foreach (var track in project.Tracks) if (track.Clips.FirstOrDefault(clip => clip.Id == clipId) is { } clip) return $"Clip: {clip.Name} · {clip.TimelineStartSeconds:0.##}s · {(clip.DurationSeconds > 0 ? clip.DurationSeconds.ToString("0.##") + "s" : "duration unknown")} · {track.Name}";
        }
        if (selection.Kind == ImagineSelectionKind.Track && selection.TargetId is Guid trackId && project.Tracks.FirstOrDefault(track => track.Id == trackId) is { } selectedTrack) return $"Track: {selectedTrack.Name} · gain {selectedTrack.Gain:0.##}{(selectedTrack.IsMuted ? " · muted" : string.Empty)}";
        return "Nothing selected.";
    }

    private static int Depth(ImagineSemanticComponent component, IReadOnlyList<ImagineSemanticComponent> all) { var depth = 0; var parent = component.ParentId; var guard = 0; while (parent is Guid id && guard++ < 12) { depth++; parent = all.FirstOrDefault(item => item.Id == id)?.ParentId; } return depth; }
    private static string FormatBytes(long bytes) => bytes >= 1048576 ? $"{bytes / 1048576d:0.#} MB" : bytes >= 1024 ? $"{bytes / 1024d:0.#} KB" : $"{bytes} B";
    private T Find<T>(string name) where T : HavenElement => Root.DescendantsAndSelf().OfType<T>().Single(item => item.Name == name);
    private void Wire(string name, Action action) => Find<HavenButton>(name).Invoked += (_, _) => action();
    private static HavenButton Action(string name, string content, string icon) => new() { Name = name, Content = content, IconKey = icon, Variant = ButtonVariant.Ghost };
    private static HavenContainer Grid(string name, string columns, string rows) { var value = new HavenContainer { Name = name, Layout = HavenLayout.Grid, Columns = columns, Rows = rows }; value.SetValue(HavenProperties.Width, HavenLength.Percent(100)); value.SetValue(HavenProperties.Gap, HavenLength.Px(10)); return value; }
    private static HavenContainer Vertical(string name, double gap) { var value = new HavenContainer { Name = name, Layout = HavenLayout.Vertical }; value.SetValue(HavenProperties.Width, HavenLength.Percent(100)); value.SetValue(HavenProperties.Gap, HavenLength.Px(gap)); return value; }
    private static HavenContainer Wrap(string name, double gap) { var value = new HavenContainer { Name = name, Layout = HavenLayout.Wrap }; value.SetValue(HavenProperties.Width, HavenLength.Percent(100)); value.SetValue(HavenProperties.Gap, HavenLength.Px(gap)); return value; }
    private static DynamicUIRuntime Runtime(string name) { var value = new DynamicUIRuntime { Name = name }; value.SetValue(HavenProperties.Width, HavenLength.Percent(100)); return value; }
    private static HavenText Heading(string content) { var value = new HavenText { Content = content, Level = TextLevel.H3 }; value.SetValue(HavenProperties.FontWeight, 750); return value; }
    private static HavenText Muted(string name, string content) { var value = new HavenText { Name = name, Content = content }; value.SetValue(HavenProperties.Foreground, "TextSecondary"); value.SetValue(HavenProperties.FontSize, 11d); return value; }
    public void Dispose() { RecentProjects.ClearItems(); Assets.ClearItems(); Components.ClearItems(); Canvas.Dispose(); }
}
