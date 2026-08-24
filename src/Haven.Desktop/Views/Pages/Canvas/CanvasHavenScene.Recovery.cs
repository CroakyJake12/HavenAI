using System.Globalization;
using Haven.Application;
using Haven.Desktop.HavenUI.Creative;
using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;

namespace Haven.Desktop.Views.Pages.Canvas;

internal sealed partial class CanvasHavenScene
{
    private Container _recoveryCard = null!;
    private Haven.UI.Components.TabStrip _boardTabs = null!;
    private Input _boardNameInput = null!;
    private HavenButton _addBoardButton = null!;
    private HavenButton _renameBoardButton = null!;
    private HavenButton _deleteBoardButton = null!;
    private HavenButton _normalPointer = null!;
    private HavenButton _panPointer = null!;
    private HavenButton _lassoPointer = null!;
    private HavenButton _laserPointer = null!;
    private HavenButton _laserLassoPointer = null!;
    private CanvasHueStrip _penHue = null!;
    private Slider _penOpacity = null!;
    private HavenButton _pressureEffect = null!;
    private HavenButton _uniformEffect = null!;
    private HavenButton _markerEffect = null!;
    private HavenButton _snapEraser = null!;
    private HavenButton _chunkEraser = null!;
    private Input _presetNameInput = null!;
    private HavenButton _savePresetButton = null!;
    private Container _presetButtons = null!;
    private int _activeBoardIndex;
    private bool _recoverySuppress;

    public event Action<int>? BoardRequested;
    public event EventHandler? AddBoardRequested;
    public event Action<int, string>? BoardRenameRequested;
    public event Action<int>? DeleteBoardRequested;
    public event Action<CanvasPenPresetPreference>? SavePenPresetRequested;

    private void BuildRecoveryControls()
    {
        _recoveryCard = NewCard("Canvas.Recovery");
        _recoveryCard.Add(new Haven.UI.Components.Text("Boards") { Level = TextLevel.H2 });
        _boardTabs = new Haven.UI.Components.TabStrip { Name = "Canvas.Boards" };
        _boardTabs.Accessibility.AccessibleName = "Canvas boards";
        _boardTabs.ItemInvoked += OnBoardTabInvoked;
        _recoveryCard.Add(_boardTabs);

        _boardNameInput = NewInput("Canvas.Board.Name", "Board name", "Board name");
        _recoveryCard.Add(_boardNameInput);
        var boardActions = NewToolbar("Canvas.Board.Actions", 0);
        _addBoardButton = NewButton("Canvas.Board.Add", "Add board");
        _renameBoardButton = NewButton("Canvas.Board.Rename", "Rename");
        _deleteBoardButton = NewButton("Canvas.Board.Delete", "Delete");
        _deleteBoardButton.Variant = ButtonVariant.Danger;
        _addBoardButton.Invoked += (_, _) => AddBoardRequested?.Invoke(this, EventArgs.Empty);
        _renameBoardButton.Invoked += (_, _) =>
        {
            var title = _boardNameInput.Text.Trim();
            if (title.Length > 0) BoardRenameRequested?.Invoke(_activeBoardIndex, title);
        };
        _deleteBoardButton.Invoked += (_, _) => DeleteBoardRequested?.Invoke(_activeBoardIndex);
        boardActions.Add(_addBoardButton); boardActions.Add(_renameBoardButton); boardActions.Add(_deleteBoardButton);
        _recoveryCard.Add(boardActions);

        _recoveryCard.Add(Caption("Pointer mode"));
        var pointerModes = NewToolbar("Canvas.Pointer.Modes", 0);
        _normalPointer = NewButton("Canvas.Pointer.Normal", "Normal");
        _panPointer = NewButton("Canvas.Pointer.Pan", "Pan");
        _lassoPointer = NewButton("Canvas.Pointer.Lasso", "Lasso");
        _laserPointer = NewButton("Canvas.Pointer.Laser", "Laser");
        _laserLassoPointer = NewButton("Canvas.Pointer.LaserLasso", "Laser lasso");
        _normalPointer.Invoked += (_, _) => SetPointerMode(UnifiedCanvasTool.Select);
        _panPointer.Invoked += (_, _) => SetPointerMode(UnifiedCanvasTool.Pan);
        _lassoPointer.Invoked += (_, _) => SetPointerMode(UnifiedCanvasTool.Lasso);
        _laserPointer.Invoked += (_, _) => SetPointerMode(UnifiedCanvasTool.LaserPointer);
        _laserLassoPointer.Invoked += (_, _) => SetPointerMode(UnifiedCanvasTool.LaserLasso);
        foreach (var button in new[] { _normalPointer, _panPointer, _lassoPointer, _laserPointer, _laserLassoPointer }) pointerModes.Add(button);
        _recoveryCard.Add(pointerModes);

        _recoveryCard.Add(new Haven.UI.Components.Text("Pen") { Level = TextLevel.H2 });
        _recoveryCard.Add(Caption("Colour"));
        _penHue = new CanvasHueStrip();
        _penHue.HueChanged += OnPenHueChanged;
        _recoveryCard.Add(_penHue);
        _recoveryCard.Add(Caption("Opacity"));
        _penOpacity = new Slider { Name = "Canvas.Pen.Opacity", Minimum = 0.05, Maximum = 1, Step = 0.05, Value = 1 };
        _penOpacity.Accessibility.AccessibleName = "Pen opacity";
        _penOpacity.ValueChanged += OnPenOpacityChanged;
        _recoveryCard.Add(_penOpacity);

        _recoveryCard.Add(Caption("Stroke response"));
        var effects = NewToolbar("Canvas.Pen.Effects", 0);
        _pressureEffect = NewButton("Canvas.Pen.Effect.Pressure", "Pressure");
        _uniformEffect = NewButton("Canvas.Pen.Effect.Uniform", "Uniform");
        _markerEffect = NewButton("Canvas.Pen.Effect.Marker", "Marker");
        _pressureEffect.Invoked += (_, _) => SetPenEffect("Pressure");
        _uniformEffect.Invoked += (_, _) => SetPenEffect("Uniform");
        _markerEffect.Invoked += (_, _) => SetPenEffect("Marker");
        effects.Add(_pressureEffect); effects.Add(_uniformEffect); effects.Add(_markerEffect);
        _recoveryCard.Add(effects);

        _recoveryCard.Add(Caption("Eraser"));
        var erasers = NewToolbar("Canvas.Eraser.Modes", 0);
        _snapEraser = NewButton("Canvas.Eraser.Snap", "Snap");
        _chunkEraser = NewButton("Canvas.Eraser.Chunk", "Chunk");
        _snapEraser.Invoked += (_, _) => SetEraserMode(CanvasEraserMode.Snap);
        _chunkEraser.Invoked += (_, _) => SetEraserMode(CanvasEraserMode.Chunk);
        erasers.Add(_snapEraser); erasers.Add(_chunkEraser);
        _recoveryCard.Add(erasers);

        _recoveryCard.Add(new Haven.UI.Components.Text("Pen presets") { Level = TextLevel.H2 });
        _presetNameInput = NewInput("Canvas.Pen.PresetName", "Custom pen preset name", "Custom pen");
        _recoveryCard.Add(_presetNameInput);
        _savePresetButton = NewButton("Canvas.Pen.SavePreset", "Save current preset");
        _savePresetButton.Invoked += (_, _) => RequestSavePreset();
        _recoveryCard.Add(_savePresetButton);
        _presetButtons = new Container { Name = "Canvas.Pen.Presets", Layout = HavenLayout.Vertical };
        _presetButtons.SetValue(HavenProperties.Gap, HavenLength.Px(4));
        _recoveryCard.Add(_presetButtons);
        Inspector.Add(_recoveryCard);
    }

    public void SetBoards(IReadOnlyList<string> titles, int selectedIndex)
    {
        titles ??= [];
        _activeBoardIndex = titles.Count == 0 ? 0 : Math.Clamp(selectedIndex, 0, titles.Count - 1);
        _boardTabs.SetItems(titles.Select((title, index) =>
            new Haven.UI.Components.TabStripItem(index.ToString(CultureInfo.InvariantCulture), title, index == _activeBoardIndex)).ToArray());
        _boardNameInput.Text = titles.Count == 0 ? string.Empty : titles[_activeBoardIndex];
        _deleteBoardButton.SetValue(HavenProperties.Enabled, titles.Count > 1);
    }

    public void SetPenPresets(IReadOnlyList<CanvasPenPresetPreference> presets)
    {
        foreach (var child in _presetButtons.Children.ToArray()) _presetButtons.Remove(child);
        foreach (var preset in presets ?? [])
        {
            var captured = preset;
            var button = NewButton("Canvas.Pen.Preset." + captured.Id, captured.Name);
            button.Accessibility.Description = $"{captured.Tool}, {captured.Thickness:0.#} px, {captured.Opacity:P0}, {captured.Effect}";
            button.Invoked += (_, _) => ApplyPenPreset(captured);
            _presetButtons.Add(button);
        }
    }

    private void OnBoardTabInvoked(object? sender, string key)
    {
        if (int.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index)) BoardRequested?.Invoke(index);
    }

    private void SetPointerMode(UnifiedCanvasTool tool)
    {
        UnifiedSurface.SetTool(tool);
        if (tool == UnifiedCanvasTool.Pan) ToolRequested?.Invoke(CanvasTool.Pan);
        else if (tool == UnifiedCanvasTool.Select) ToolRequested?.Invoke(CanvasTool.Select);
        RefreshRecoveryState(UnifiedSurface.Controller);
    }

    private void OnPenHueChanged(double hue, string hex)
    {
        if (_recoverySuppress) return;
        UnifiedSurface.Controller.PenColour = hex;
    }

    private void OnPenOpacityChanged(object? sender, EventArgs e)
    {
        if (_recoverySuppress) return;
        UnifiedSurface.Controller.PenOpacity = Math.Clamp(_penOpacity.Value, 0.05, 1);
    }

    private void SetPenEffect(string effect)
    {
        UnifiedSurface.Controller.PenEffect = effect;
        RefreshRecoveryState(UnifiedSurface.Controller);
    }

    private void SetEraserMode(CanvasEraserMode mode)
    {
        UnifiedSurface.Controller.EraserMode = mode;
        RefreshRecoveryState(UnifiedSurface.Controller);
    }

    private void RequestSavePreset()
    {
        var controller = UnifiedSurface.Controller;
        var name = string.IsNullOrWhiteSpace(_presetNameInput.Text) ? "Custom pen" : _presetNameInput.Text.Trim();
        SavePenPresetRequested?.Invoke(new CanvasPenPresetPreference(
            string.Empty, name, controller.Tool == CanvasTool.Highlighter ? "Highlighter" : "Pen",
            controller.PenColour, controller.PenOpacity, controller.PenWidth, controller.PenEffect));
    }

    private void ApplyPenPreset(CanvasPenPresetPreference preset)
    {
        var controller = UnifiedSurface.Controller;
        controller.PenColour = preset.Color;
        controller.PenOpacity = preset.Opacity;
        controller.PenWidth = preset.Thickness;
        controller.PenEffect = preset.Effect;
        var tool = preset.Tool.Equals("Highlighter", StringComparison.OrdinalIgnoreCase) ? CanvasTool.Highlighter : CanvasTool.Pen;
        UnifiedSurface.SetTool(tool == CanvasTool.Highlighter ? UnifiedCanvasTool.Highlighter : UnifiedCanvasTool.Pen);
        ToolRequested?.Invoke(tool);
        RefreshRecoveryState(controller);
    }

    private void RefreshRecoveryState(CanvasInteractionController controller)
    {
        _recoverySuppress = true;
        try
        {
            _penOpacity.Value = Math.Clamp(controller.PenOpacity, 0.05, 1);
            PenWidthSlider.Value = Math.Clamp(controller.PenWidth, PenWidthSlider.Minimum, PenWidthSlider.Maximum);
            _penHue.SetHue(ColorToHue(controller.PenColour));
            var pointerTool = UnifiedSurface.Tool;
            _normalPointer.Variant = pointerTool == UnifiedCanvasTool.Select ? ButtonVariant.Primary : ButtonVariant.Tertiary;
            _panPointer.Variant = pointerTool == UnifiedCanvasTool.Pan ? ButtonVariant.Primary : ButtonVariant.Tertiary;
            _lassoPointer.Variant = pointerTool == UnifiedCanvasTool.Lasso ? ButtonVariant.Primary : ButtonVariant.Tertiary;
            _laserPointer.Variant = pointerTool == UnifiedCanvasTool.LaserPointer ? ButtonVariant.Primary : ButtonVariant.Tertiary;
            _laserLassoPointer.Variant = pointerTool == UnifiedCanvasTool.LaserLasso ? ButtonVariant.Primary : ButtonVariant.Tertiary;
            _pressureEffect.Variant = controller.PenEffect.Equals("Pressure", StringComparison.OrdinalIgnoreCase) ? ButtonVariant.Primary : ButtonVariant.Tertiary;
            _uniformEffect.Variant = controller.PenEffect.Equals("Uniform", StringComparison.OrdinalIgnoreCase) ? ButtonVariant.Primary : ButtonVariant.Tertiary;
            _markerEffect.Variant = controller.PenEffect.Equals("Marker", StringComparison.OrdinalIgnoreCase) ? ButtonVariant.Primary : ButtonVariant.Tertiary;
            _snapEraser.Variant = controller.EraserMode == CanvasEraserMode.Snap ? ButtonVariant.Primary : ButtonVariant.Tertiary;
            _chunkEraser.Variant = controller.EraserMode == CanvasEraserMode.Chunk ? ButtonVariant.Primary : ButtonVariant.Tertiary;
        }
        finally { _recoverySuppress = false; }
    }

    public void SetRecoveryBusy(bool busy)
    {
        var enabled = !busy;
        foreach (var element in new HavenElement[] { _boardNameInput, _addBoardButton, _renameBoardButton, _deleteBoardButton, _normalPointer, _panPointer, _lassoPointer, _laserPointer, _laserLassoPointer, _penHue, _penOpacity, _pressureEffect, _uniformEffect, _markerEffect, _snapEraser, _chunkEraser, _presetNameInput, _savePresetButton })
            element.SetValue(HavenProperties.Enabled, enabled);
    }

    public bool ReleaseInteraction() => UnifiedSurface.ReleaseInputState();

    private void DisposeRecovery()
    {
        _boardTabs.ItemInvoked -= OnBoardTabInvoked;
        _penHue.HueChanged -= OnPenHueChanged;
        _penOpacity.ValueChanged -= OnPenOpacityChanged;
        BoardRequested = null; AddBoardRequested = null; BoardRenameRequested = null; DeleteBoardRequested = null; SavePenPresetRequested = null;
    }

    private static double ColorToHue(string? value)
    {
        var text = string.IsNullOrWhiteSpace(value) ? "2F80ED" : value.Trim().TrimStart('#');
        if (text.Length == 8) text = text[2..];
        if (text.Length != 6 || !int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb)) return 210;
        var r = ((rgb >> 16) & 255) / 255d; var g = ((rgb >> 8) & 255) / 255d; var b = (rgb & 255) / 255d;
        var max = Math.Max(r, Math.Max(g, b)); var min = Math.Min(r, Math.Min(g, b)); var delta = max - min;
        if (delta < .0001) return 0;
        var hue = max == r ? 60 * (((g - b) / delta) % 6) : max == g ? 60 * ((b - r) / delta + 2) : 60 * ((r - g) / delta + 4);
        return hue < 0 ? hue + 360 : hue;
    }
}
