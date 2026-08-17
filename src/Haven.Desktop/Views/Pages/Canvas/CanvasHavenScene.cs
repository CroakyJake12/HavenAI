using Haven.Application;
using Haven.Core;
using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;
using HavenCanvas = Haven.UI.Components.Canvas;
using HavenText = Haven.UI.Components.Text;

namespace Haven.Desktop.Views.Pages.Canvas;

internal sealed class CanvasHavenScene : IDisposable
{
    private bool _suppressChanges;
    private string _title = string.Empty;
    private string _objectText = string.Empty;
    private string _x = string.Empty;
    private string _y = string.Empty;
    private string _width = string.Empty;
    private string _height = string.Empty;
    private string _rotation = string.Empty;
    private bool _disposed;

    public CanvasHavenScene()
    {
        Root = new Page { Name = "Canvas.Root", Layout = HavenLayout.Grid, Columns = "1fr", Rows = "Auto Auto 1fr Auto" };
        Root.SetValue(HavenProperties.Padding, HavenThickness.Parse("18px 22px"));
        Root.SetValue(HavenProperties.Gap, HavenLength.Px(10));
        Root.SetValue(HavenProperties.Background, "Surface");
        Root.SetValue(HavenProperties.Overflow, HavenOverflow.Clip);

        Header = new Container { Name = "Canvas.Header", Layout = HavenLayout.Grid, Columns = "1fr Auto", Rows = "Auto" };
        Header.SetValue(HavenProperties.Row, 0);
        Header.SetValue(HavenProperties.Gap, HavenLength.Px(12));
        TitleInput = NewInput("Canvas.Title", "Canvas title", "Untitled canvas");
        TitleInput.SetValue(HavenProperties.Column, 0);
        TitleInput.SetValue(HavenProperties.FontSize, 24d);
        TitleInput.SetValue(HavenProperties.FontWeight, 700);
        PositionText = Caption(string.Empty);
        PositionText.Name = "Canvas.Position";
        PositionText.SetValue(HavenProperties.Column, 1);
        PositionText.SetValue(HavenProperties.VerticalAlignment, HavenVerticalAlignment.Center);
        Header.Add(TitleInput); Header.Add(PositionText); Root.Add(Header);

        Toolbar = NewToolbar("Canvas.Toolbar", 1);
        PreviousButton = NewButton("Canvas.Previous", "Previous"); NextButton = NewButton("Canvas.Next", "Next"); NewCanvasButton = NewButton("Canvas.New", "New canvas"); SaveButton = NewButton("Canvas.Save", "Save"); ImportButton = NewButton("Canvas.Import", "Import board"); ExportButton = NewButton("Canvas.Export", "Export board"); UndoButton = NewButton("Canvas.Undo", "Undo"); RedoButton = NewButton("Canvas.Redo", "Redo");
        foreach (var button in new[] { PreviousButton, NextButton, NewCanvasButton, SaveButton, ImportButton, ExportButton, UndoButton, RedoButton }) Toolbar.Add(button);
        Root.Add(Toolbar);

        Workspace = new Container { Name = "Canvas.Workspace", Layout = HavenLayout.Grid, Columns = "1fr 310px", Rows = "1fr" };
        Workspace.SetValue(HavenProperties.Row, 2); Workspace.SetValue(HavenProperties.Gap, HavenLength.Px(10)); Workspace.SetValue(HavenProperties.Overflow, HavenOverflow.Clip);
        BoardSurface = new HavenCanvas { Name = "Canvas.Board" };
        BoardSurface.Accessibility.Role = HavenAccessibleRole.Group; BoardSurface.Accessibility.AccessibleName = "Canvas board";
        BoardSurface.SetValue(HavenProperties.Column, 0); BoardSurface.SetValue(HavenProperties.Width, HavenLength.Percent(100)); BoardSurface.SetValue(HavenProperties.Height, HavenLength.Percent(100)); BoardSurface.SetValue(HavenProperties.MinHeight, HavenLength.Px(560)); BoardSurface.SetValue(HavenProperties.Background, "SurfaceRaised"); BoardSurface.SetValue(HavenProperties.BorderColor, "Border"); BoardSurface.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1)); BoardSurface.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(16))); BoardSurface.SetValue(HavenProperties.Overflow, HavenOverflow.Clip);
        Workspace.Add(BoardSurface);

        Inspector = NewCard("Canvas.Inspector"); Inspector.SetValue(HavenProperties.Column, 1); Inspector.SetValue(HavenProperties.Overflow, HavenOverflow.Scroll); BuildInspector(); Workspace.Add(Inspector); Root.Add(Workspace);
        StatusText = Caption("Opening local canvases…"); StatusText.Name = "Canvas.Status"; StatusText.SetValue(HavenProperties.Row, 3); Root.Add(StatusText);

        TitleInput.Invalidated += OnTitleInvalidated; ObjectTextInput.Invalidated += OnObjectTextInvalidated; XInput.Invalidated += (_, _) => EmitNumber(XInput, ref _x, XChanged); YInput.Invalidated += (_, _) => EmitNumber(YInput, ref _y, YChanged); WidthInput.Invalidated += (_, _) => EmitNumber(WidthInput, ref _width, WidthChanged); HeightInput.Invalidated += (_, _) => EmitNumber(HeightInput, ref _height, HeightChanged); RotationInput.Invalidated += (_, _) => EmitNumber(RotationInput, ref _rotation, RotationChanged);
        LockToggle.CheckedChanged += (_, _) => { if (!_suppressChanges) LockChanged?.Invoke(LockToggle.IsChecked); }; InfiniteToggle.CheckedChanged += (_, _) => { if (!_suppressChanges) InfiniteChanged?.Invoke(InfiniteToggle.IsChecked); }; ZoomSlider.ValueChanged += (_, _) => { if (!_suppressChanges) ZoomChanged?.Invoke(ZoomSlider.Value); }; PenWidthSlider.ValueChanged += (_, _) => { if (!_suppressChanges) PenWidthChanged?.Invoke(PenWidthSlider.Value); };
        PreviousButton.Invoked += (_, _) => PreviousRequested?.Invoke(this, EventArgs.Empty); NextButton.Invoked += (_, _) => NextRequested?.Invoke(this, EventArgs.Empty); NewCanvasButton.Invoked += (_, _) => NewRequested?.Invoke(this, EventArgs.Empty); SaveButton.Invoked += (_, _) => SaveRequested?.Invoke(this, EventArgs.Empty); ImportButton.Invoked += (_, _) => ImportRequested?.Invoke(this, EventArgs.Empty); ExportButton.Invoked += (_, _) => ExportRequested?.Invoke(this, EventArgs.Empty); UndoButton.Invoked += (_, _) => UndoRequested?.Invoke(this, EventArgs.Empty); RedoButton.Invoked += (_, _) => RedoRequested?.Invoke(this, EventArgs.Empty);
        SelectToolButton.Invoked += (_, _) => ToolRequested?.Invoke(CanvasTool.Select); PenToolButton.Invoked += (_, _) => ToolRequested?.Invoke(CanvasTool.Pen); HighlighterToolButton.Invoked += (_, _) => ToolRequested?.Invoke(CanvasTool.Highlighter); EraserToolButton.Invoked += (_, _) => ToolRequested?.Invoke(CanvasTool.Eraser); PanToolButton.Invoked += (_, _) => ToolRequested?.Invoke(CanvasTool.Pan);
        AddTextButton.Invoked += (_, _) => AddObjectRequested?.Invoke(NotesCanvasObjectKind.Text); AddShapeButton.Invoked += (_, _) => AddObjectRequested?.Invoke(NotesCanvasObjectKind.Shape); AddFrameButton.Invoked += (_, _) => AddObjectRequested?.Invoke(NotesCanvasObjectKind.Frame); DeleteButton.Invoked += (_, _) => DeleteRequested?.Invoke(this, EventArgs.Empty); BringFrontButton.Invoked += (_, _) => BringFrontRequested?.Invoke(this, EventArgs.Empty); SendBackButton.Invoked += (_, _) => SendBackRequested?.Invoke(this, EventArgs.Empty); ConnectButton.Invoked += (_, _) => ConnectRequested?.Invoke(this, EventArgs.Empty); GroupButton.Invoked += (_, _) => GroupRequested?.Invoke(this, EventArgs.Empty); UngroupButton.Invoked += (_, _) => UngroupRequested?.Invoke(this, EventArgs.Empty); ResetViewButton.Invoked += (_, _) => ResetViewRequested?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? PreviousRequested; public event EventHandler? NextRequested; public event EventHandler? NewRequested; public event EventHandler? SaveRequested; public event EventHandler? ImportRequested; public event EventHandler? ExportRequested; public event EventHandler? UndoRequested; public event EventHandler? RedoRequested;
    public event Action<CanvasTool>? ToolRequested; public event Action<NotesCanvasObjectKind>? AddObjectRequested; public event Action<Guid>? ObjectSelected; public event EventHandler? DeleteRequested; public event EventHandler? BringFrontRequested; public event EventHandler? SendBackRequested; public event EventHandler? ConnectRequested; public event EventHandler? GroupRequested; public event EventHandler? UngroupRequested; public event EventHandler? ResetViewRequested;
    public event Action<string>? TitleChanged; public event Action<string>? ObjectTextChanged; public event Action<double>? XChanged; public event Action<double>? YChanged; public event Action<double>? WidthChanged; public event Action<double>? HeightChanged; public event Action<double>? RotationChanged; public event Action<bool>? LockChanged; public event Action<bool>? InfiniteChanged; public event Action<double>? ZoomChanged; public event Action<double>? PenWidthChanged;

    public Page Root { get; } public Container Header { get; } public Container Toolbar { get; } public Container Workspace { get; } public HavenCanvas BoardSurface { get; } public Container Inspector { get; }
    public Input TitleInput { get; } public HavenText PositionText { get; } public HavenText StatusText { get; } public HavenText BoardSummaryText { get; private set; } = null!; public HavenText SelectionText { get; private set; } = null!;
    public HavenButton PreviousButton { get; } public HavenButton NextButton { get; } public HavenButton NewCanvasButton { get; } public HavenButton SaveButton { get; } public HavenButton ImportButton { get; } public HavenButton ExportButton { get; } public HavenButton UndoButton { get; } public HavenButton RedoButton { get; }
    public HavenButton SelectToolButton { get; private set; } = null!; public HavenButton PenToolButton { get; private set; } = null!; public HavenButton HighlighterToolButton { get; private set; } = null!; public HavenButton EraserToolButton { get; private set; } = null!; public HavenButton PanToolButton { get; private set; } = null!; public HavenButton AddTextButton { get; private set; } = null!; public HavenButton AddShapeButton { get; private set; } = null!; public HavenButton AddFrameButton { get; private set; } = null!; public HavenButton DeleteButton { get; private set; } = null!; public HavenButton BringFrontButton { get; private set; } = null!; public HavenButton SendBackButton { get; private set; } = null!; public HavenButton ConnectButton { get; private set; } = null!; public HavenButton GroupButton { get; private set; } = null!; public HavenButton UngroupButton { get; private set; } = null!; public HavenButton ResetViewButton { get; private set; } = null!;
    public Input ObjectTextInput { get; private set; } = null!; public Input XInput { get; private set; } = null!; public Input YInput { get; private set; } = null!; public Input WidthInput { get; private set; } = null!; public Input HeightInput { get; private set; } = null!; public Input RotationInput { get; private set; } = null!; public Toggle LockToggle { get; private set; } = null!; public Toggle InfiniteToggle { get; private set; } = null!; public Slider ZoomSlider { get; private set; } = null!; public Slider PenWidthSlider { get; private set; } = null!;

    public void SetDocument(NotesDocument document, NotesCanvasData board, int documentIndex, int documentCount, Guid? selectedObjectId, CanvasTool tool, bool canUndo, bool canRedo)
    {
        ArgumentNullException.ThrowIfNull(document); ArgumentNullException.ThrowIfNull(board); _suppressChanges = true;
        try
        {
            _title = document.Title; TitleInput.Text = document.Title; PositionText.Content = $"Canvas {documentIndex + 1} of {Math.Max(1, documentCount)} · v{document.Version}"; BoardSummaryText.Content = $"{board.Objects.Count} objects · {board.Strokes.Count} strokes · {Math.Round(board.Zoom * 100)}% zoom";
            BoardSurface.PanX = board.OffsetX; BoardSurface.PanY = board.OffsetY; BoardSurface.Zoom = board.Zoom; InfiniteToggle.IsChecked = board.Infinite; ZoomSlider.Value = board.Zoom;
            UndoButton.SetValue(HavenProperties.Enabled, canUndo); RedoButton.SetValue(HavenProperties.Enabled, canRedo); SetToolVisual(tool); RebuildBoard(board, selectedObjectId); SetSelection(board.Objects.FirstOrDefault(value => value.Id == selectedObjectId));
        }
        finally { _suppressChanges = false; }
    }

    public void SetStatus(string text) => StatusText.Content = text ?? string.Empty;
    public void SetBusy(bool busy)
    {
        var enabled = !busy; foreach (var button in new[] { PreviousButton, NextButton, NewCanvasButton, SaveButton, ImportButton, ExportButton, UndoButton, RedoButton, SelectToolButton, PenToolButton, HighlighterToolButton, EraserToolButton, PanToolButton, AddTextButton, AddShapeButton, AddFrameButton, DeleteButton, BringFrontButton, SendBackButton, ConnectButton, GroupButton, UngroupButton, ResetViewButton }) button.SetValue(HavenProperties.Enabled, enabled);
        foreach (var input in new[] { TitleInput, ObjectTextInput, XInput, YInput, WidthInput, HeightInput, RotationInput }) input.SetValue(HavenProperties.Enabled, enabled); ZoomSlider.SetValue(HavenProperties.Enabled, enabled); PenWidthSlider.SetValue(HavenProperties.Enabled, enabled); LockToggle.SetValue(HavenProperties.Enabled, enabled); InfiniteToggle.SetValue(HavenProperties.Enabled, enabled);
    }

    private void BuildInspector()
    {
        Inspector.Add(new HavenText("Tools") { Level = TextLevel.H2 }); var tools = NewToolbar("Canvas.Tools", 0); SelectToolButton = NewButton("Canvas.Tool.Select", "Select"); PenToolButton = NewButton("Canvas.Tool.Pen", "Pen"); HighlighterToolButton = NewButton("Canvas.Tool.Highlighter", "Highlight"); EraserToolButton = NewButton("Canvas.Tool.Eraser", "Erase"); PanToolButton = NewButton("Canvas.Tool.Pan", "Pan"); foreach (var button in new[] { SelectToolButton, PenToolButton, HighlighterToolButton, EraserToolButton, PanToolButton }) tools.Add(button); Inspector.Add(tools);
        Inspector.Add(Caption("Pen width")); PenWidthSlider = new Slider { Name = "Canvas.PenWidth", Minimum = 1, Maximum = 24, Step = 1, Value = 3 }; PenWidthSlider.Accessibility.AccessibleName = "Pen width"; Inspector.Add(PenWidthSlider);
        Inspector.Add(Caption("Add object")); var add = NewToolbar("Canvas.Add", 0); AddTextButton = NewButton("Canvas.Add.Text", "Text"); AddShapeButton = NewButton("Canvas.Add.Shape", "Shape"); AddFrameButton = NewButton("Canvas.Add.Frame", "Frame"); add.Add(AddTextButton); add.Add(AddShapeButton); add.Add(AddFrameButton); Inspector.Add(add);
        BoardSummaryText = Caption("0 objects · 0 strokes"); BoardSummaryText.Name = "Canvas.Board.Summary"; BoardSummaryText.Accessibility.Description = "Semantic summary of the visual canvas board."; Inspector.Add(BoardSummaryText);
        var view = NewCard("Canvas.View"); view.Add(Caption("Zoom")); ZoomSlider = new Slider { Name = "Canvas.Zoom", Minimum = 0.05, Maximum = 8, Step = 0.05, Value = 1 }; ZoomSlider.Accessibility.AccessibleName = "Canvas zoom"; view.Add(ZoomSlider); view.Add(Caption("Infinite canvas")); InfiniteToggle = new Toggle { Name = "Canvas.Infinite" }; InfiniteToggle.Accessibility.AccessibleName = "Infinite canvas"; view.Add(InfiniteToggle); ResetViewButton = NewButton("Canvas.ResetView", "Reset view"); view.Add(ResetViewButton); Inspector.Add(view);
        Inspector.Add(new HavenText("Selection") { Level = TextLevel.H2 }); SelectionText = Caption("No object selected"); SelectionText.Name = "Canvas.Selection"; Inspector.Add(SelectionText); ObjectTextInput = NewInput("Canvas.Object.Text", "Selected object text", "Object label"); Inspector.Add(ObjectTextInput);
        var geometry = new Container { Name = "Canvas.Geometry", Layout = HavenLayout.Grid, Columns = "1fr 1fr", Rows = "Auto Auto Auto" }; geometry.SetValue(HavenProperties.Gap, HavenLength.Px(6)); XInput = NewInput("Canvas.Object.X", "Selected object X position", "X"); YInput = NewInput("Canvas.Object.Y", "Selected object Y position", "Y"); WidthInput = NewInput("Canvas.Object.Width", "Selected object width", "Width"); HeightInput = NewInput("Canvas.Object.Height", "Selected object height", "Height"); RotationInput = NewInput("Canvas.Object.Rotation", "Selected object rotation", "Rotation"); XInput.SetValue(HavenProperties.Row, 0); XInput.SetValue(HavenProperties.Column, 0); YInput.SetValue(HavenProperties.Row, 0); YInput.SetValue(HavenProperties.Column, 1); WidthInput.SetValue(HavenProperties.Row, 1); WidthInput.SetValue(HavenProperties.Column, 0); HeightInput.SetValue(HavenProperties.Row, 1); HeightInput.SetValue(HavenProperties.Column, 1); RotationInput.SetValue(HavenProperties.Row, 2); RotationInput.SetValue(HavenProperties.ColumnSpan, 2); foreach (var input in new[] { XInput, YInput, WidthInput, HeightInput, RotationInput }) geometry.Add(input); Inspector.Add(geometry);
        Inspector.Add(Caption("Locked")); LockToggle = new Toggle { Name = "Canvas.Object.Locked" }; LockToggle.Accessibility.AccessibleName = "Lock selected object"; Inspector.Add(LockToggle); var actions = NewToolbar("Canvas.Object.Actions", 0); BringFrontButton = NewButton("Canvas.Object.Front", "Front"); SendBackButton = NewButton("Canvas.Object.Back", "Back"); ConnectButton = NewButton("Canvas.Object.Connect", "Connect"); GroupButton = NewButton("Canvas.Object.Group", "Group"); UngroupButton = NewButton("Canvas.Object.Ungroup", "Ungroup"); DeleteButton = NewButton("Canvas.Object.Delete", "Delete"); DeleteButton.Variant = ButtonVariant.Danger; foreach (var button in new[] { BringFrontButton, SendBackButton, ConnectButton, GroupButton, UngroupButton, DeleteButton }) actions.Add(button); Inspector.Add(actions);
    }

    private void RebuildBoard(NotesCanvasData board, Guid? selectedId)
    {
        BoardSurface.Children.ToList().ForEach(child => child.Parent?.Remove(child));
        var zoom = Math.Max(board.Zoom, 0.05);
        var visibleObjects = board.Objects
            .Where(value => CanvasGhostVisibility.IsObjectVisible(board, value))
            .ToDictionary(value => value.Id);

        for (var x = 0; x <= Math.Min(board.Width, 4000); x += 80) { var line = new Container { Name = $"Canvas.Grid.V.{x}" }; Position(line, board.OffsetX + x * zoom, board.OffsetY, 1, Math.Min(board.Height * zoom, 4000)); line.SetValue(HavenProperties.Background, "Border"); line.SetValue(HavenProperties.Opacity, .22); line.SetValue(HavenProperties.PointerEvents, HavenPointerEvents.None); BoardSurface.Add(line); }
        for (var y = 0; y <= Math.Min(board.Height, 4000); y += 80) { var line = new Container { Name = $"Canvas.Grid.H.{y}" }; Position(line, board.OffsetX, board.OffsetY + y * zoom, Math.Min(board.Width * zoom, 4000), 1); line.SetValue(HavenProperties.Background, "Border"); line.SetValue(HavenProperties.Opacity, .22); line.SetValue(HavenProperties.PointerEvents, HavenPointerEvents.None); BoardSurface.Add(line); }

        foreach (var stroke in board.Strokes.Where(value => CanvasGhostVisibility.IsStrokeVisible(board, value)))
            for (var index = 1; index < stroke.Points.Count; index++)
            {
                var a = stroke.Points[index - 1];
                var b = stroke.Points[index];
                AddLine($"Canvas.Stroke.{stroke.Id:N}.{index}", board.OffsetX + a.X * zoom, board.OffsetY + a.Y * zoom, board.OffsetX + b.X * zoom, board.OffsetY + b.Y * zoom, Math.Max(1, stroke.BaseWidth * zoom * (stroke.Tool.Equals("highlighter", StringComparison.OrdinalIgnoreCase) ? .65 : 1)), stroke.Tool.Equals("highlighter", StringComparison.OrdinalIgnoreCase) ? .32 : stroke.Opacity, "AccentSecondary", -20 + index);
            }

        foreach (var value in visibleObjects.Values.OrderBy(item => item.ZIndex))
        {
            if (value.Kind == NotesCanvasObjectKind.Connector
                && value.FromObjectId is { } fromId
                && value.ToObjectId is { } toId
                && visibleObjects.TryGetValue(fromId, out var from)
                && visibleObjects.TryGetValue(toId, out var to))
            {
                AddLine($"Canvas.Object.{value.Id:N}", board.OffsetX + (from.X + from.Width / 2) * zoom, board.OffsetY + (from.Y + from.Height / 2) * zoom, board.OffsetX + (to.X + to.Width / 2) * zoom, board.OffsetY + (to.Y + to.Height / 2) * zoom, 3, .9, value.Id == selectedId ? "Accent" : "TextSecondary", value.ZIndex);
                continue;
            }

            if (value.Kind == NotesCanvasObjectKind.Connector) continue;
            var button = NewButton($"Canvas.Object.{value.Id:N}", string.IsNullOrWhiteSpace(value.Text) ? value.Kind.ToString() : value.Text); button.Variant = value.Id == selectedId ? ButtonVariant.Primary : ButtonVariant.Ghost; button.Accessibility.AccessibleName = $"{value.Kind}: {(string.IsNullOrWhiteSpace(value.Text) ? "unlabelled" : value.Text)}{(value.Locked ? ", locked" : string.Empty)}"; Position(button, board.OffsetX + value.X * zoom, board.OffsetY + value.Y * zoom, Math.Max(32, value.Width * zoom), Math.Max(28, value.Height * zoom)); button.SetValue(HavenProperties.Rotation, value.Rotation); button.SetValue(HavenProperties.ZIndex, value.ZIndex); var id = value.Id; button.Invoked += (_, _) => ObjectSelected?.Invoke(id); BoardSurface.Add(button);
        }
    }

    private void AddLine(string name, double x1, double y1, double x2, double y2, double thickness, double opacity, string token, int z)
    {
        var layout = CanvasLineGeometry.Compute(x1, y1, x2, y2, thickness);
        if (layout.Width < .1) return;
        var line = new Container { Name = name };
        Position(line, layout.Left, layout.Top, layout.Width, layout.Height);
        line.SetValue(HavenProperties.Background, token);
        line.SetValue(HavenProperties.Opacity, Math.Clamp(opacity, 0, 1));
        line.SetValue(HavenProperties.Rotation, layout.RotationDegrees);
        line.SetValue(HavenProperties.ZIndex, z);
        line.SetValue(HavenProperties.PointerEvents, HavenPointerEvents.None);
        BoardSurface.Add(line);
    }

    private void SetSelection(NotesCanvasObject? value)
    {
        var enabled = value is not null; SelectionText.Content = value is null ? "No object selected" : $"{value.Kind} · z{value.ZIndex}{(value.Locked ? " · locked" : string.Empty)}"; _objectText = value?.Text ?? string.Empty; _x = Number(value?.X); _y = Number(value?.Y); _width = Number(value?.Width); _height = Number(value?.Height); _rotation = Number(value?.Rotation); ObjectTextInput.Text = _objectText; XInput.Text = _x; YInput.Text = _y; WidthInput.Text = _width; HeightInput.Text = _height; RotationInput.Text = _rotation; LockToggle.IsChecked = value?.Locked == true;
        foreach (var element in new HavenElement[] { ObjectTextInput, XInput, YInput, WidthInput, HeightInput, RotationInput, LockToggle, BringFrontButton, SendBackButton, ConnectButton, GroupButton, UngroupButton, DeleteButton }) element.SetValue(HavenProperties.Enabled, enabled);
    }

    private void SetToolVisual(CanvasTool tool) { SelectToolButton.Variant = tool == CanvasTool.Select ? ButtonVariant.Primary : ButtonVariant.Tertiary; PenToolButton.Variant = tool == CanvasTool.Pen ? ButtonVariant.Primary : ButtonVariant.Tertiary; HighlighterToolButton.Variant = tool == CanvasTool.Highlighter ? ButtonVariant.Primary : ButtonVariant.Tertiary; EraserToolButton.Variant = tool == CanvasTool.Eraser ? ButtonVariant.Primary : ButtonVariant.Tertiary; PanToolButton.Variant = tool == CanvasTool.Pan ? ButtonVariant.Primary : ButtonVariant.Tertiary; }
    private void OnTitleInvalidated(object? sender, EventArgs e) { if (_suppressChanges || TitleInput.Text == _title) return; _title = TitleInput.Text; TitleChanged?.Invoke(_title); }
    private void OnObjectTextInvalidated(object? sender, EventArgs e) { if (_suppressChanges || ObjectTextInput.Text == _objectText) return; _objectText = ObjectTextInput.Text; ObjectTextChanged?.Invoke(_objectText); }
    private void EmitNumber(Input input, ref string cache, Action<double>? callback) { if (_suppressChanges || input.Text == cache) return; cache = input.Text; if (double.TryParse(cache, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value)) callback?.Invoke(value); }
    private static string Number(double? value) => value?.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
    private static void Position(HavenElement element, double left, double top, double width, double height) { element.SetValue(HavenProperties.Left, HavenLength.Px(left)); element.SetValue(HavenProperties.Top, HavenLength.Px(top)); element.SetValue(HavenProperties.Width, HavenLength.Px(width)); element.SetValue(HavenProperties.Height, HavenLength.Px(height)); }
    private static Container NewCard(string name) { var card = new Container { Name = name, Layout = HavenLayout.Vertical }; card.SetValue(HavenProperties.Background, "SurfaceRaised"); card.SetValue(HavenProperties.BorderColor, "Border"); card.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1)); card.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(16))); card.SetValue(HavenProperties.Padding, HavenThickness.Parse("14px")); card.SetValue(HavenProperties.Gap, HavenLength.Px(8)); return card; }
    private static Container NewToolbar(string name, int row) { var toolbar = new Container { Name = name, Layout = HavenLayout.Horizontal }; toolbar.SetValue(HavenProperties.Row, row); toolbar.SetValue(HavenProperties.Gap, HavenLength.Px(6)); toolbar.SetValue(HavenProperties.Overflow, HavenOverflow.Scroll); return toolbar; }
    private static HavenButton NewButton(string name, string label) { var button = new HavenButton { Name = name, Content = label, Variant = ButtonVariant.Tertiary }; button.Accessibility.AccessibleName = label; button.SetValue(HavenProperties.MinHeight, HavenLength.Px(38)); return button; }
    private static Input NewInput(string name, string accessibleName, string placeholder) { var input = new Input { Name = name, Placeholder = placeholder }; input.Accessibility.AccessibleName = accessibleName; input.SetValue(HavenProperties.Width, HavenLength.Percent(100)); return input; }
    private static HavenText Caption(string text) { var value = new HavenText(text) { Level = TextLevel.Caption }; value.SetValue(HavenProperties.Foreground, "TextSecondary"); return value; }
    public void Dispose() { if (_disposed) return; _disposed = true; TitleInput.Invalidated -= OnTitleInvalidated; ObjectTextInput.Invalidated -= OnObjectTextInvalidated; }
}
