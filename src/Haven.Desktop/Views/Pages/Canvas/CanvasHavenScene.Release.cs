using Haven.Application;
using Haven.Core;
using Haven.Desktop.HavenUI.Creative;
using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;
using HavenText = Haven.UI.Components.Text;

namespace Haven.Desktop.Views.Pages.Canvas;

internal sealed partial class CanvasHavenScene
{
    private Container _releaseChrome = null!;
    private Container _toolDock = null!;
    private Container _operationBar = null!;
    private Container _penPanel = null!;
    private Container _penPresets = null!;
    private int _customPenPresetCount;
    private Container _tablePanel = null!;
    private Container _renameBoardPanel = null!;
    private Container _boardStrip = null!;
    private Container _viewStrip = null!;
    private Haven.UI.Components.TabStrip _boardTabs = null!;
    private HavenButton _addBoardButton = null!;
    private HavenButton _pointerDockButton = null!;
    private HavenButton _penDockButton = null!;
    private HavenButton _eraserDockButton = null!;
    private HavenButton _textDockButton = null!;
    private HavenButton _addDockButton = null!;
    private HavenButton _aiDockButton = null!;
    private HavenButton _zoomOutButton = null!;
    private HavenButton _zoomInButton = null!;
    private HavenButton _fitButton = null!;
    private HavenText _zoomReadout = null!;
    private Input _releaseTitleInput = null!;
    private Input _inlineTextInput = null!;
    private Input _tableRowsInput = null!;
    private Input _tableColumnsInput = null!;
    private Input _renameBoardInput = null!;
    private IReadOnlyList<string> _boardTitles = [];
    private int _renameBoardIndex = -1;
    private Slider _releasePenWidth = null!;
    private Slider _releasePenOpacity = null!;
    private CanvasHueStrip _hueStrip = null!;
    private CanvasPointerOverlay _pointerOverlay = null!;
    private PopupMenu? _openPopup;
    private bool _editingInlineText;

    public event Action<int>? BoardRequested;
    public event EventHandler? AddBoardRequested;
    public event Action<int, string>? BoardRenameRequested;
    public event Action<string>? SpecialInsertRequested;
    public event Action<string>? AiActionRequested;

    private void BuildReleaseChrome()
    {
        Header.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
        Toolbar.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
        Inspector.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
        StatusText.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);

        Root.Rows = "1fr";
        Root.SetValue(HavenProperties.Padding, HavenThickness.Parse("10px"));
        Root.SetValue(HavenProperties.Gap, HavenLength.Px(0));
        Workspace.SetValue(HavenProperties.Row, 0);
        Workspace.Columns = "1fr";
        Workspace.SetValue(HavenProperties.Gap, HavenLength.Px(0));
        BoardSurface.SetValue(HavenProperties.Column, 0);
        BoardSurface.SetValue(HavenProperties.MinHeight, HavenLength.Px(520));

        _pointerOverlay = new CanvasPointerOverlay(UnifiedSurface.Controller);
        _pointerOverlay.SelectionChanged += (_, _) =>
        {
            SurfaceSelectionChanged?.Invoke(this, EventArgs.Empty);
            RefreshSurfaceMetadata(UnifiedSurface.Controller);
        };
        BoardSurface.Add(_pointerOverlay);

        _inlineTextInput = NewInput("Canvas.InlineText", "Edit selected canvas text", "Type on canvas");
        _inlineTextInput.Multiline = true;
        _inlineTextInput.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
        _inlineTextInput.SetValue(HavenProperties.ZIndex, 80);
        _inlineTextInput.TextChanged += (_, _) =>
        {
            if (!_editingInlineText || _suppressChanges) return;
            if (UnifiedSurface.Controller.UpdateSelectedText(_inlineTextInput.Text))
            {
                UnifiedSurface.RefreshSurface();
                SurfaceChanged?.Invoke(this, EventArgs.Empty);
            }
        };
        BoardSurface.Add(_inlineTextInput);

        _releaseChrome = new Container { Name = "Canvas.ReleaseChrome", Layout = HavenLayout.Canvas };
        _releaseChrome.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        _releaseChrome.SetValue(HavenProperties.Height, HavenLength.Percent(100));
        _releaseChrome.SetValue(HavenProperties.LayoutParticipation, HavenLayoutParticipation.Overlay);
        _releaseChrome.SetValue(HavenProperties.PointerEvents, HavenPointerEvents.ChildrenOnly);
        _releaseChrome.SetValue(HavenProperties.ZIndex, 100);
        Root.Add(_releaseChrome);

        BuildReleaseTitle();
        BuildOperationBar();
        BuildToolDock();
        BuildPenPanel();
        BuildTablePanel();
        BuildBoardStrip();
        BuildViewStrip();
    }

    private void BuildReleaseTitle()
    {
        var card = FloatingCard("Canvas.Release.Title");
        card.Layout = HavenLayout.Horizontal;
        card.SetValue(HavenProperties.HorizontalAlignment, HavenHorizontalAlignment.Start);
        card.SetValue(HavenProperties.VerticalAlignment, HavenVerticalAlignment.Start);
        card.SetValue(HavenProperties.Margin, HavenThickness.Parse("8px"));
        card.SetValue(HavenProperties.Width, HavenLength.Px(360));
        _releaseTitleInput = NewInput("Canvas.Release.TitleInput", "Canvas title", "Untitled canvas");
        _releaseTitleInput.SetValue(HavenProperties.MinHeight, HavenLength.Px(38));
        _releaseTitleInput.TextChanged += (_, _) =>
        {
            if (_suppressChanges || _releaseTitleInput.Text == _title) return;
            _title = _releaseTitleInput.Text;
            TitleChanged?.Invoke(_title);
        };
        card.Add(_releaseTitleInput);

        var more = CompactButton("Canvas.Release.More", "⋯", "Canvas file actions");
        more.Invoked += (_, _) => ShowMenu(more, [
            new PopupMenuItem("Canvas home", () => LibraryRequested?.Invoke(this, EventArgs.Empty)),
            new PopupMenuItem("New canvas", () => NewRequested?.Invoke(this, EventArgs.Empty)),
            new PopupMenuItem("Save", () => SaveRequested?.Invoke(this, EventArgs.Empty)),
            new PopupMenuItem("Import board", () => ImportRequested?.Invoke(this, EventArgs.Empty)),
            new PopupMenuItem("Export board", () => ExportRequested?.Invoke(this, EventArgs.Empty)),
            new PopupMenuItem("Previous canvas", () => PreviousRequested?.Invoke(this, EventArgs.Empty)),
            new PopupMenuItem("Next canvas", () => NextRequested?.Invoke(this, EventArgs.Empty))
        ], 220, "Canvas file actions");
        card.Add(more);
        _releaseChrome.Add(card);
    }

    private void BuildToolDock()
    {
        _toolDock = FloatingCard("Canvas.Release.ToolDock");
        _toolDock.Layout = HavenLayout.Horizontal;
        _toolDock.SetValue(HavenProperties.HorizontalAlignment, HavenHorizontalAlignment.Center);
        _toolDock.SetValue(HavenProperties.VerticalAlignment, HavenVerticalAlignment.End);
        _toolDock.SetValue(HavenProperties.Margin, HavenThickness.Parse("0px 0px 0px 12px"));
        _toolDock.SetValue(HavenProperties.Gap, HavenLength.Px(4));

        _pointerDockButton = DockButton("Pointer", "Pointer");
        _penDockButton = DockButton("Pen", "Pen");
        _eraserDockButton = DockButton("Eraser", "Eraser");
        _textDockButton = DockButton("Text", "Text");
        _addDockButton = DockButton("Add", "Add");
        _aiDockButton = DockButton("AI", "AI");
        _aiDockButton.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);

        _pointerDockButton.Invoked += (_, _) => ShowPointerMenu();
        _penDockButton.Invoked += (_, _) =>
        {
            SetPointerMode(CanvasPointerMode.Normal);
            UnifiedSurface.SetTool(UnifiedCanvasTool.Pen);
            ToolRequested?.Invoke(CanvasTool.Pen);
            TogglePanel(_penPanel);
        };
        _eraserDockButton.Invoked += (_, _) => ShowEraserMenu();
        _textDockButton.Invoked += (_, _) =>
        {
            HidePanels();
            SetPointerMode(CanvasPointerMode.Normal);
            UnifiedSurface.SetTool(UnifiedCanvasTool.Text);
        };
        _addDockButton.Invoked += (_, _) => ShowAddMenu();
        _aiDockButton.Invoked += (_, _) => ShowAiMenu();

        foreach (var button in new[] { _pointerDockButton, _penDockButton, _eraserDockButton, _textDockButton, _addDockButton, _aiDockButton })
            _toolDock.Add(button);
        _releaseChrome.Add(_toolDock);
    }

    private void BuildOperationBar()
    {
        _operationBar = FloatingCard("Canvas.Release.Operations");
        _operationBar.Layout = HavenLayout.Horizontal;
        _operationBar.SetValue(HavenProperties.HorizontalAlignment, HavenHorizontalAlignment.Center);
        _operationBar.SetValue(HavenProperties.VerticalAlignment, HavenVerticalAlignment.End);
        _operationBar.SetValue(HavenProperties.Margin, HavenThickness.Parse("0px 0px 0px 70px"));
        _operationBar.SetValue(HavenProperties.Gap, HavenLength.Px(3));

        AddOperation("Undo", () => UndoRequested?.Invoke(this, EventArgs.Empty));
        AddOperation("Redo", () => RedoRequested?.Invoke(this, EventArgs.Empty));
        AddOperation("Save", () => SaveRequested?.Invoke(this, EventArgs.Empty));
        AddOperation("Copy", () => Mutate(c => c.CopySelection(), selectionOnly: true));
        AddOperation("Paste", () => Mutate(c => c.PasteSelection()));
        AddOperation("Duplicate", () => Mutate(c => c.DuplicateSelection()));
        AddOperation("Lock", () =>
        {
            var controller = UnifiedSurface.Controller;
            var lockNext = controller.SelectedObjects.Any(value => !value.Locked);
            Mutate(c => c.SetSelectionLocked(lockNext), selectionOnly: true);
        });
        AddOperation("Group", () => Mutate(c => c.GroupSelection() is not null, selectionOnly: true));
        AddOperation("Delete", () => Mutate(c => c.DeleteSelection(), selectionOnly: true), destructive: true);

        var arrange = CompactButton("Canvas.Release.Arrange", "Arrange", "Arrange selected objects");
        arrange.Invoked += (_, _) => ShowMenu(arrange, [
            new PopupMenuItem("Bring to front", () => Mutate(c => c.BringSelectionToFront(), true)),
            new PopupMenuItem("Send to back", () => Mutate(c => c.SendSelectionToBack(), true)),
            new PopupMenuItem("Align left", () => Mutate(c => c.AlignSelection(CanvasAlignment.Left), true)),
            new PopupMenuItem("Align centre", () => Mutate(c => c.AlignSelection(CanvasAlignment.HorizontalCenter), true)),
            new PopupMenuItem("Align top", () => Mutate(c => c.AlignSelection(CanvasAlignment.Top), true)),
            new PopupMenuItem("Align middle", () => Mutate(c => c.AlignSelection(CanvasAlignment.VerticalCenter), true)),
            new PopupMenuItem("Distribute horizontal", () => Mutate(c => c.DistributeSelection(CanvasDistribution.Horizontal), true)),
            new PopupMenuItem("Distribute vertical", () => Mutate(c => c.DistributeSelection(CanvasDistribution.Vertical), true)),
            new PopupMenuItem("Ungroup", () => Mutate(c => c.UngroupSelection(), true))
        ], 240, "Arrange selection");
        _operationBar.Add(arrange);

        var edit = CompactButton("Canvas.Release.EditText", "Edit text", "Edit selected text directly on canvas");
        edit.Invoked += (_, _) => BeginInlineTextEditing();
        _operationBar.Add(edit);
        _releaseChrome.Add(_operationBar);
    }

    private void BuildPenPanel()
    {
        _penPanel = FloatingCard("Canvas.Release.PenPanel");
        _penPanel.Layout = HavenLayout.Vertical;
        _penPanel.SetValue(HavenProperties.HorizontalAlignment, HavenHorizontalAlignment.Center);
        _penPanel.SetValue(HavenProperties.VerticalAlignment, HavenVerticalAlignment.End);
        _penPanel.SetValue(HavenProperties.Margin, HavenThickness.Parse("0px 0px 0px 126px"));
        _penPanel.SetValue(HavenProperties.Width, HavenLength.Px(300));
        _penPanel.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);

        _penPanel.Add(Caption("Pen presets"));
        _penPresets = new Container { Layout = HavenLayout.Horizontal, Name = "Canvas.Release.PenPresets" };
        _penPresets.SetValue(HavenProperties.Gap, HavenLength.Px(4));
        _penPresets.Add(PresetButton("Blue", "#FF2F80ED", 3, 1, "Pressure", false));
        _penPresets.Add(PresetButton("Black", "#FF202732", 2, 1, "Uniform", false));
        _penPresets.Add(PresetButton("Red", "#FFE04B54", 4, .9, "Pressure", false));
        _penPresets.Add(PresetButton("Highlight", "#FFFFD54F", 12, .28, "Marker", true));
        _penPanel.Add(_penPresets);
        var savePreset = CompactButton("Canvas.Release.Pen.SavePreset", "Save current", "Save current pen as a reusable preset");
        savePreset.Invoked += (_, _) => SaveCurrentPenPreset();
        _penPanel.Add(savePreset);

        _penPanel.Add(Caption("Colour"));
        _hueStrip = new CanvasHueStrip();
        _hueStrip.HueChanged += (_, hex) =>
        {
            UnifiedSurface.Controller.PenColour = hex;
            UnifiedSurface.RefreshSurface();
        };
        _penPanel.Add(_hueStrip);

        _penPanel.Add(Caption("Thickness"));
        _releasePenWidth = new Slider { Minimum = 1, Maximum = 24, Step = 1, Value = 3 };
        _releasePenWidth.Accessibility.AccessibleName = "Pen thickness";
        _releasePenWidth.ValueChanged += (_, _) =>
        {
            UnifiedSurface.Controller.PenWidth = _releasePenWidth.Value;
            PenWidthChanged?.Invoke(_releasePenWidth.Value);
        };
        _penPanel.Add(_releasePenWidth);

        _penPanel.Add(Caption("Transparency"));
        _releasePenOpacity = new Slider { Minimum = .05, Maximum = 1, Step = .05, Value = 1 };
        _releasePenOpacity.Accessibility.AccessibleName = "Pen opacity";
        _releasePenOpacity.ValueChanged += (_, _) => UnifiedSurface.Controller.PenOpacity = _releasePenOpacity.Value;
        _penPanel.Add(_releasePenOpacity);

        var effects = new Container { Layout = HavenLayout.Horizontal, Name = "Canvas.Release.PenEffects" };
        effects.SetValue(HavenProperties.Gap, HavenLength.Px(4));
        foreach (var effect in new[] { "Pressure", "Uniform", "Marker" })
        {
            var button = CompactButton("Canvas.Release.Pen." + effect, effect, effect + " pen effect");
            button.Invoked += (_, _) => UnifiedSurface.Controller.PenEffect = effect;
            effects.Add(button);
        }
        _penPanel.Add(effects);
        _releaseChrome.Add(_penPanel);
    }

    private void BuildTablePanel()
    {
        _tablePanel = FloatingCard("Canvas.Release.TablePanel");
        _tablePanel.Layout = HavenLayout.Vertical;
        _tablePanel.SetValue(HavenProperties.HorizontalAlignment, HavenHorizontalAlignment.Center);
        _tablePanel.SetValue(HavenProperties.VerticalAlignment, HavenVerticalAlignment.End);
        _tablePanel.SetValue(HavenProperties.Margin, HavenThickness.Parse("0px 0px 0px 126px"));
        _tablePanel.SetValue(HavenProperties.Width, HavenLength.Px(260));
        _tablePanel.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
        _tablePanel.Add(Caption("Table size"));
        var fields = new Container { Layout = HavenLayout.Horizontal };
        fields.SetValue(HavenProperties.Gap, HavenLength.Px(6));
        _tableRowsInput = NewInput("Canvas.Release.TableRows", "Table rows", "Rows");
        _tableRowsInput.Text = "3";
        _tableColumnsInput = NewInput("Canvas.Release.TableColumns", "Table columns", "Columns");
        _tableColumnsInput.Text = "3";
        fields.Add(_tableRowsInput); fields.Add(_tableColumnsInput);
        _tablePanel.Add(fields);
        var insert = CompactButton("Canvas.Release.InsertTable", "Insert table", "Insert table");
        insert.Invoked += (_, _) =>
        {
            var rows = int.TryParse(_tableRowsInput.Text, out var r) ? Math.Clamp(r, 1, 100) : 3;
            var columns = int.TryParse(_tableColumnsInput.Text, out var c) ? Math.Clamp(c, 1, 100) : 3;
            InsertRich("table", 360, 220, "Table", new Dictionary<string, object?> { ["rows"] = rows, ["columns"] = columns });
            HidePanels();
        };
        _tablePanel.Add(insert);
        _releaseChrome.Add(_tablePanel);
    }

    private void BuildBoardStrip()
    {
        _boardStrip = FloatingCard("Canvas.Release.Boards");
        _boardStrip.Layout = HavenLayout.Grid;
        _boardStrip.Columns = "1fr Auto";
        _boardStrip.Rows = "54px";
        _boardStrip.SetValue(HavenProperties.HorizontalAlignment, HavenHorizontalAlignment.Start);
        _boardStrip.SetValue(HavenProperties.VerticalAlignment, HavenVerticalAlignment.End);
        _boardStrip.SetValue(HavenProperties.Margin, HavenThickness.Parse("12px"));
        _boardStrip.SetValue(HavenProperties.Width, HavenLength.Px(430));

        _boardTabs = new Haven.UI.Components.TabStrip { Name = "Canvas.BoardTabs" };
        _boardTabs.SetValue(HavenProperties.Column, 0);
        _boardTabs.ItemInvoked += (_, key) =>
        {
            if (int.TryParse(key, out var index)) BoardRequested?.Invoke(index);
        };
        _boardTabs.ItemSecondaryInvoked += (_, key) =>
        {
            if (!int.TryParse(key, out var index)) return;
            var anchor = _boardTabs.ItemButtons.ElementAtOrDefault(index) ?? _addBoardButton;
            ShowMenu(anchor, [
                new PopupMenuItem("Rename board", () => BeginBoardRename(index)),
                new PopupMenuItem("Delete board", () => SpecialInsertRequested?.Invoke($"delete-board:{index}"), true)
            ], 190, "Board options");
        };
        _boardStrip.Add(_boardTabs);

        _addBoardButton = CompactButton("Canvas.Release.AddBoard", "+", "Add board");
        _addBoardButton.SetValue(HavenProperties.Column, 1);
        _addBoardButton.Invoked += (_, _) => AddBoardRequested?.Invoke(this, EventArgs.Empty);
        _boardStrip.Add(_addBoardButton);
        _releaseChrome.Add(_boardStrip);
    }

    private void BuildViewStrip()
    {
        _viewStrip = FloatingCard("Canvas.Release.View");
        _viewStrip.Layout = HavenLayout.Horizontal;
        _viewStrip.SetValue(HavenProperties.HorizontalAlignment, HavenHorizontalAlignment.End);
        _viewStrip.SetValue(HavenProperties.VerticalAlignment, HavenVerticalAlignment.End);
        _viewStrip.SetValue(HavenProperties.Margin, HavenThickness.Parse("12px"));
        _viewStrip.SetValue(HavenProperties.Gap, HavenLength.Px(3));

        _zoomOutButton = CompactButton("Canvas.Release.ZoomOut", "−", "Zoom out");
        _zoomReadout = Caption("100%");
        _zoomReadout.Name = "Canvas.Release.ZoomReadout";
        _zoomReadout.SetValue(HavenProperties.MinWidth, HavenLength.Px(52));
        _zoomReadout.SetValue(HavenProperties.VerticalAlignment, HavenVerticalAlignment.Center);
        _zoomInButton = CompactButton("Canvas.Release.ZoomIn", "+", "Zoom in");
        _fitButton = CompactButton("Canvas.Release.Fit", "Fit", "Fit selection or board");

        _zoomOutButton.Invoked += (_, _) => ZoomBy(.85);
        _zoomInButton.Invoked += (_, _) => ZoomBy(1.18);
        _fitButton.Invoked += (_, _) =>
        {
            var controller = UnifiedSurface.Controller;
            if (controller.FitSelectionOrBoard(UnifiedSurface.Bounds.Width, UnifiedSurface.Bounds.Height))
            {
                UnifiedSurface.RefreshSurface();
                SurfaceChanged?.Invoke(this, EventArgs.Empty);
                RefreshSurfaceMetadata(controller);
            }
        };
        foreach (var element in new HavenElement[] { _zoomOutButton, _zoomReadout, _zoomInButton, _fitButton }) _viewStrip.Add(element);
        _releaseChrome.Add(_viewStrip);
    }

    public void SetBoards(IReadOnlyList<string> titles, int selectedIndex)
    {
        if (_boardTabs is null) return;
        _boardTitles = titles.ToArray();
        _boardTabs.SetItems(titles.Select((title, index) => new Haven.UI.Components.TabStripItem(index.ToString(), title, index == selectedIndex, true)).ToArray());
    }

    private void ShowPointerMenu() => ShowMenu(_pointerDockButton, [
        new PopupMenuItem("Normal", () => SetPointerMode(CanvasPointerMode.Normal)),
        new PopupMenuItem("Pan", () => SetPointerMode(CanvasPointerMode.Pan)),
        new PopupMenuItem("Lasso", () => SetPointerMode(CanvasPointerMode.Lasso)),
        new PopupMenuItem("Laser Pointer", () => SetPointerMode(CanvasPointerMode.LaserPointer)),
        new PopupMenuItem("Laser Lasso", () => SetPointerMode(CanvasPointerMode.LaserLasso))
    ], 210, "Pointer mode");

    private void ShowEraserMenu() => ShowMenu(_eraserDockButton, [
        new PopupMenuItem("Snap Erase", () =>
        {
            SetPointerMode(CanvasPointerMode.Normal);
            UnifiedSurface.Controller.EraserMode = CanvasEraserMode.Snap;
            UnifiedSurface.SetTool(UnifiedCanvasTool.Eraser);
            ToolRequested?.Invoke(CanvasTool.Eraser);
        }),
        new PopupMenuItem("Chunk Erase", () =>
        {
            SetPointerMode(CanvasPointerMode.Normal);
            UnifiedSurface.Controller.EraserMode = CanvasEraserMode.Chunk;
            UnifiedSurface.SetTool(UnifiedCanvasTool.Eraser);
            ToolRequested?.Invoke(CanvasTool.Eraser);
        })
    ], 210, "Eraser style");

    private void ShowAddMenu() => ShowMenu(_addDockButton, [
        new PopupMenuItem("Text", () => UnifiedSurface.SetTool(UnifiedCanvasTool.Text)),
        new PopupMenuItem("Rectangle", () => UnifiedSurface.SetTool(UnifiedCanvasTool.Rectangle)),
        new PopupMenuItem("Rounded rectangle", () => InsertRich("rounded-rectangle", 200, 130, string.Empty, new Dictionary<string, object?> { ["shape"] = "rounded-rectangle" })),
        new PopupMenuItem("Circle", () => UnifiedSurface.SetTool(UnifiedCanvasTool.Ellipse)),
        new PopupMenuItem("Triangle", () => InsertRich("triangle", 180, 150, string.Empty, new Dictionary<string, object?> { ["shape"] = "triangle" })),
        new PopupMenuItem("Square", () => InsertRich("square", 150, 150, string.Empty, new Dictionary<string, object?> { ["shape"] = "square" })),
        new PopupMenuItem("Squircle", () => InsertRich("squircle", 170, 150, string.Empty, new Dictionary<string, object?> { ["shape"] = "squircle" })),
        new PopupMenuItem("Hexagon", () => InsertRich("hexagon", 190, 150, string.Empty, new Dictionary<string, object?> { ["shape"] = "hexagon" })),
        new PopupMenuItem("Line", () => UnifiedSurface.SetTool(UnifiedCanvasTool.Line)),
        new PopupMenuItem("Arrow →", () => InsertRich("line", 220, 4, string.Empty, new Dictionary<string, object?> { ["shape"] = "line", ["arrowEnd"] = true })),
        new PopupMenuItem("Arrow ←", () => InsertRich("line", 220, 4, string.Empty, new Dictionary<string, object?> { ["shape"] = "line", ["arrowStart"] = true })),
        new PopupMenuItem("Arrow ↔", () => InsertRich("line", 220, 4, string.Empty, new Dictionary<string, object?> { ["shape"] = "line", ["arrowStart"] = true, ["arrowEnd"] = true })),
        new PopupMenuItem("Connector", () => UnifiedSurface.SetTool(UnifiedCanvasTool.Connector)),
        new PopupMenuItem("Frame", () => UnifiedSurface.SetTool(UnifiedCanvasTool.Frame)),


        new PopupMenuItem("Table…", () => TogglePanel(_tablePanel)),
        new PopupMenuItem("Sticky note", () => InsertRich("sticky", 220, 170, "Sticky note", new Dictionary<string, object?> { ["shape"] = "sticky" })),
        new PopupMenuItem("Ruler", () => InsertRich("ruler", 360, 48, "Ruler", new Dictionary<string, object?> { ["shape"] = "ruler" })),

    ], 250, "Add to canvas");

    private void ShowAiMenu() => ShowMenu(_aiDockButton, [
        new PopupMenuItem("Generate Content", () => AiActionRequested?.Invoke("generate")),
        new PopupMenuItem("Ink to Math", () => AiActionRequested?.Invoke("ink-math")),
        new PopupMenuItem("Ink to Shape", () => AiActionRequested?.Invoke("ink-shape")),
        new PopupMenuItem("Ask Haven about this Screen", () => AiActionRequested?.Invoke("ask-screen"))
    ], 270, "Canvas AI");

    private void SetPointerMode(CanvasPointerMode mode)
    {
        HidePanels();
        _pointerOverlay.SetMode(mode);
        if (mode == CanvasPointerMode.Pan)
        {
            UnifiedSurface.SetTool(UnifiedCanvasTool.Pan);
            ToolRequested?.Invoke(CanvasTool.Pan);
        }
        else
        {
            UnifiedSurface.SetTool(UnifiedCanvasTool.Select);
            ToolRequested?.Invoke(CanvasTool.Select);
        }
    }

    private void InsertRich(string kind, double width, double height, string text, IReadOnlyDictionary<string, object?>? options = null)
    {
        var controller = UnifiedSurface.Controller;
        var center = controller.ViewportToCanvas(UnifiedSurface.Bounds.Width / 2, UnifiedSurface.Bounds.Height / 2);
        controller.AddRichObject(kind, center.X - width / 2, center.Y - height / 2, width, height, text, options);
        UnifiedSurface.SetTool(UnifiedCanvasTool.Select);
        UnifiedSurface.RefreshSurface();
        SurfaceChanged?.Invoke(this, EventArgs.Empty);
        SurfaceSelectionChanged?.Invoke(this, EventArgs.Empty);
        RefreshSurfaceMetadata(controller);
    }

    private void Mutate(Func<CanvasInteractionController, bool> action, bool selectionOnly = false)
    {
        var controller = UnifiedSurface.Controller;
        if (selectionOnly && controller.SelectedObjects.Count == 0) return;
        if (!action(controller)) return;
        UnifiedSurface.RefreshSurface();
        SurfaceChanged?.Invoke(this, EventArgs.Empty);
        SurfaceSelectionChanged?.Invoke(this, EventArgs.Empty);
        RefreshSurfaceMetadata(controller);
    }

    private void BeginInlineTextEditing()
    {
        var selected = UnifiedSurface.Controller.SelectedObject;
        if (selected is null || selected.Kind != NotesCanvasObjectKind.Text) return;
        _editingInlineText = false;
        _inlineTextInput.Text = selected.Text;
        _editingInlineText = true;
        PositionInlineEditor(selected);
        _inlineTextInput.SetValue(HavenProperties.Visibility, HavenVisibility.Visible);
    }

    private void PositionInlineEditor(NotesCanvasObject selected)
    {
        var controller = UnifiedSurface.Controller;
        var point = controller.CanvasToViewport(selected.X, selected.Y);
        _inlineTextInput.SetValue(HavenProperties.Left, HavenLength.Px(point.X));
        _inlineTextInput.SetValue(HavenProperties.Top, HavenLength.Px(point.Y));
        _inlineTextInput.SetValue(HavenProperties.Width, HavenLength.Px(Math.Max(120, selected.Width * controller.Board.Zoom)));
        _inlineTextInput.SetValue(HavenProperties.Height, HavenLength.Px(Math.Max(48, selected.Height * controller.Board.Zoom)));
    }

    private void RefreshInlineEditor(CanvasInteractionController controller)
    {
        if (!_editingInlineText || controller.SelectedObject is not { Kind: NotesCanvasObjectKind.Text } selected)
        {
            _editingInlineText = false;
            if (_inlineTextInput is not null) _inlineTextInput.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
            return;
        }
        PositionInlineEditor(selected);
    }

    private HavenButton PresetButton(string label, string colour, double width, double opacity, string effect, bool highlighter)
    {
        var button = CompactButton("Canvas.Release.Preset." + label, label, label + " pen preset");
        button.Invoked += (_, _) =>
        {
            var controller = UnifiedSurface.Controller;
            controller.PenColour = colour;
            controller.PenWidth = width;
            controller.PenOpacity = opacity;
            controller.PenEffect = effect;
            _releasePenWidth.Value = width;
            _releasePenOpacity.Value = opacity;
            SetPointerMode(CanvasPointerMode.Normal);
            UnifiedSurface.SetTool(highlighter ? UnifiedCanvasTool.Highlighter : UnifiedCanvasTool.Pen);
            ToolRequested?.Invoke(highlighter ? CanvasTool.Highlighter : CanvasTool.Pen);
            _penPanel.SetValue(HavenProperties.Visibility, HavenVisibility.Visible);
        };
        return button;
    }

    private void ZoomBy(double factor)
    {
        var controller = UnifiedSurface.Controller;
        if (!controller.ZoomBy(factor, UnifiedSurface.Bounds.Width, UnifiedSurface.Bounds.Height)) return;
        UnifiedSurface.RefreshSurface();
        SurfaceChanged?.Invoke(this, EventArgs.Empty);
        RefreshSurfaceMetadata(controller);
    }

    private void AddOperation(string label, Action action, bool destructive = false)
    {
        var button = CompactButton("Canvas.Release.Op." + label.Replace(" ", string.Empty), label, label);
        if (destructive) button.Variant = ButtonVariant.Danger;
        button.Invoked += (_, _) => action();
        _operationBar.Add(button);
    }

    private HavenButton DockButton(string label, string accessible)
    {
        var button = NewButton("Canvas.Release.Tool." + label.Replace(" ", string.Empty), label);
        button.Accessibility.AccessibleName = accessible;
        button.SetValue(HavenProperties.MinHeight, HavenLength.Px(42));
        button.SetValue(HavenProperties.Padding, HavenThickness.Parse("8px 12px"));
        return button;
    }

    private static HavenButton CompactButton(string name, string label, string accessible)
    {
        var button = new HavenButton { Name = name, Content = label, Variant = ButtonVariant.Tertiary };
        button.Accessibility.AccessibleName = accessible;
        button.SetValue(HavenProperties.MinHeight, HavenLength.Px(34));
        button.SetValue(HavenProperties.Padding, HavenThickness.Parse("6px 9px"));
        button.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(10)));
        return button;
    }

    private static Container FloatingCard(string name)
    {
        var card = new Container { Name = name, Layout = HavenLayout.Horizontal };
        card.SetValue(HavenProperties.LayoutParticipation, HavenLayoutParticipation.Overlay);
        card.SetValue(HavenProperties.Background, "SurfaceRaised");
        card.SetValue(HavenProperties.BorderColor, "Border");
        card.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
        card.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(14)));
        card.SetValue(HavenProperties.Padding, HavenThickness.Parse("6px"));
        card.SetValue(HavenProperties.Shadow, "Card");
        card.SetValue(HavenProperties.ZIndex, 110);
        return card;
    }

    private void ShowMenu(HavenElement anchor, IReadOnlyList<PopupMenuItem> items, double width, string name)
    {
        _openPopup?.Dismiss();
        var popup = new PopupMenu(anchor, Root, items, width, name);
        _openPopup = popup;
        popup.Dismissed += (_, _) =>
        {
            if (ReferenceEquals(_openPopup, popup)) _openPopup = null;
        };
        Root.Add(popup);
    }

    private void TogglePanel(Container panel)
    {
        _openPopup?.Dismiss();
        var visible = panel.GetValue(HavenProperties.Visibility) == HavenVisibility.Visible;
        HidePanels();
        if (!visible) panel.SetValue(HavenProperties.Visibility, HavenVisibility.Visible);
    }

    private void HidePanels()
    {
        if (_penPanel is not null) _penPanel.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
        if (_tablePanel is not null) _tablePanel.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
    }
}
