using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.HavenUI.Creative;
using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;
using HavenText = Haven.UI.Components.Text;

namespace Haven.Desktop.HavenUI.GenerativeUi;

/// <summary>Haven-owned interactive whiteboard using the same Canvas engine as the standalone Canvas app.</summary>
internal sealed class HavenGenUiWhiteboard : Container, IDisposable
{
    private static readonly (string Name, string Value)[] Palette =
    [
        ("Black", "#111111"), ("Red", "#E53935"), ("Orange", "#FB8C00"),
        ("Green", "#43A047"), ("Cyan", "#00ACC1"), ("Blue", "#1E88E5"),
        ("Purple", "#8E24AA"), ("Pink", "#D81B60"), ("White", "#FFFFFF")
    ];

    private readonly Action<JsonElement> _persist;
    private readonly Func<JsonElement, Task>? _requestAgent;
    private readonly CanvasInteractionController _controller;
    private readonly UnifiedCanvasSurface _canvas;
    private readonly HavenText _title = new();
    private readonly HavenText _prompt = new();
    private readonly HavenText _status = new();
    private readonly Input _textInput = new();
    private readonly Input _agentInput = new();
    private readonly Slider _thickness = new();
    private readonly Select _colour = new();
    private bool _disposed;

    public HavenGenUiWhiteboard(
        GenUiComponent component,
        JsonElement? persistedState,
        Action<JsonElement> persist,
        Func<JsonElement, Task>? requestAgent)
    {
        _persist = persist ?? throw new ArgumentNullException(nameof(persist));
        _requestAgent = requestAgent;
        var restored = UnifiedCanvasStateCodec.Restore(persistedState);
        _controller = restored.Controller;

        Layout = HavenLayout.Vertical;
        SetValue(HavenProperties.Width, HavenLength.Percent(100));
        SetValue(HavenProperties.Gap, HavenLength.Px(8));
        SetValue(HavenProperties.Padding, HavenThickness.Uniform(HavenLength.Px(10)));
        SetValue(HavenProperties.Background, "Surface");
        SetValue(HavenProperties.BorderColor, "Border");
        SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
        SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(16)));
        Accessibility.Role = HavenAccessibleRole.Group;
        Accessibility.AccessibleName = "Interactive whiteboard";

        _title.SetValue(HavenProperties.FontSize, 18d);
        _title.SetValue(HavenProperties.FontWeight, 800);
        _prompt.SetValue(HavenProperties.Foreground, "TextSecondary");
        _prompt.SetValue(HavenProperties.FontSize, 12d);

        _canvas = new UnifiedCanvasSurface(_controller, () => string.IsNullOrWhiteSpace(_textInput.Text) ? "Text" : _textInput.Text.Trim())
        {
            ShowGrid = restored.ShowGrid
        };
        _canvas.SetTool(restored.Tool);
        _canvas.Changed += OnCanvasChanged;
        _canvas.SelectionChanged += OnSelectionChanged;

        var tools = new Container { Layout = HavenLayout.Wrap };
        tools.SetValue(HavenProperties.Gap, HavenLength.Px(5));
        tools.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        AddToolButton(tools, "Select", UnifiedCanvasTool.Select);
        AddToolButton(tools, "Pen", UnifiedCanvasTool.Pen);
        AddToolButton(tools, "Highlight", UnifiedCanvasTool.Highlighter);
        AddToolButton(tools, "Eraser", UnifiedCanvasTool.Eraser);
        AddToolButton(tools, "Text", UnifiedCanvasTool.Text);
        AddToolButton(tools, "Rectangle", UnifiedCanvasTool.Rectangle);
        AddToolButton(tools, "Ellipse", UnifiedCanvasTool.Ellipse);
        AddToolButton(tools, "Line", UnifiedCanvasTool.Line);
        AddToolButton(tools, "Pan", UnifiedCanvasTool.Pan);
        AddMutationButton(tools, "Undo", _controller.Undo);
        AddMutationButton(tools, "Redo", _controller.Redo);
        AddActionButton(tools, "Copy", () => _controller.CopySelection());
        AddMutationButton(tools, "Paste", () => _controller.PasteSelection());
        AddMutationButton(tools, "Delete", _controller.DeleteSelection, ButtonVariant.Danger);
        AddMutationButton(tools, "Clear", _controller.ClearBoard, ButtonVariant.Danger);

        var controls = new Container { Layout = HavenLayout.Wrap };
        controls.SetValue(HavenProperties.Gap, HavenLength.Px(6));
        controls.SetValue(HavenProperties.Width, HavenLength.Percent(100));

        _colour.Items = Palette.Select(item => item.Name).ToArray();
        _colour.SelectedIndex = Math.Max(0, Array.FindIndex(Palette, item => item.Value.Equals(_controller.PenColour, StringComparison.OrdinalIgnoreCase)));
        _colour.Accessibility.AccessibleName = "Whiteboard colour";
        _colour.SetValue(HavenProperties.MinWidth, HavenLength.Px(110));
        _colour.SelectionChanged += (_, _) =>
        {
            if (_colour.SelectedIndex < 0 || _colour.SelectedIndex >= Palette.Length) return;
            _controller.PenColour = Palette[_colour.SelectedIndex].Value;
            PersistSession();
        };
        controls.Add(_colour);

        _thickness.Minimum = 2;
        _thickness.Maximum = 32;
        _thickness.Step = 1;
        _thickness.Value = Math.Clamp(_controller.PenWidth, 2, 32);
        _thickness.Accessibility.AccessibleName = "Whiteboard pen thickness";
        _thickness.SetValue(HavenProperties.MinWidth, HavenLength.Px(150));
        _thickness.ValueChanged += (_, _) =>
        {
            _controller.PenWidth = _thickness.Value;
            PersistSession();
        };
        controls.Add(_thickness);

        _textInput.Placeholder = "Text to add or update";
        _textInput.SubmitOnEnter = true;
        _textInput.Accessibility.AccessibleName = "Whiteboard text";
        _textInput.SetValue(HavenProperties.MinWidth, HavenLength.Px(210));
        controls.Add(_textInput);
        AddActionButton(controls, "Add / Update Text", ApplyText);
        AddActionButton(controls, "Grid", ToggleGrid);
        AddMutationButton(controls, "Fit Canvas", ResetViewport);

        if (_requestAgent is not null)
        {
            _agentInput.Placeholder = "Ask Haven to add, mark, or refine something";
            _agentInput.SubmitOnEnter = true;
            _agentInput.Accessibility.AccessibleName = "Ask Haven about whiteboard";
            _agentInput.SetValue(HavenProperties.MinWidth, HavenLength.Px(260));
            controls.Add(_agentInput);
            AddActionButton(controls, "Ask Haven", () => _ = SubmitAgentAsync());
        }

        _status.SetValue(HavenProperties.FontSize, 11d);
        _status.SetValue(HavenProperties.Foreground, "TextSecondary");
        _status.Accessibility.AccessibleName = "Whiteboard status";
        SetStatus($"{_canvas.Tool} tool selected.");

        Add(_title);
        Add(_prompt);
        Add(_canvas);
        Add(tools);
        Add(controls);
        Add(_status);
        Update(component);
    }

    public bool OwnsInput(Input input) => ReferenceEquals(input, _textInput) || ReferenceEquals(input, _agentInput);

    public async Task SubmitInputAsync(Input input)
    {
        if (ReferenceEquals(input, _textInput)) { ApplyText(); return; }
        if (ReferenceEquals(input, _agentInput)) await SubmitAgentAsync();
    }

    public void Update(GenUiComponent component)
    {
        _title.Content = ReadString(component, "title") ?? "Whiteboard";
        _prompt.Content = ReadString(component, "prompt") ?? ReadString(component, "emptyText") ?? string.Empty;
        _prompt.SetValue(HavenProperties.Visibility, string.IsNullOrWhiteSpace(_prompt.Content) ? HavenVisibility.Collapsed : HavenVisibility.Visible);
        var minHeight = Math.Max(320, ReadDouble(component, "minHeight", 420));
        _canvas.SetValue(HavenProperties.Height, HavenLength.Px(Math.Max(280, minHeight - 110)));
        _canvas.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        SetValue(HavenProperties.MinHeight, HavenLength.Px(minHeight));
        Accessibility.AccessibleName = ReadString(component, "automationName") ?? _title.Content;
    }

    private void AddToolButton(Container parent, string label, UnifiedCanvasTool tool)
    {
        var button = new HavenButton { Content = label, Variant = ButtonVariant.Ghost };
        button.Accessibility.AccessibleName = $"Use {label} whiteboard tool";
        button.Invoked += (_, _) =>
        {
            _canvas.SetTool(tool);
            PersistSession();
            SetStatus($"{label} tool selected.");
        };
        parent.Add(button);
    }

    private void AddMutationButton(Container parent, string label, Func<bool> action, ButtonVariant variant = ButtonVariant.Secondary)
    {
        var button = new HavenButton { Content = label, Variant = variant };
        button.Accessibility.AccessibleName = label;
        button.Invoked += (_, _) =>
        {
            if (action()) OnCanvasChanged(this, EventArgs.Empty);
            SetStatus(label);
        };
        parent.Add(button);
    }

    private void AddActionButton(Container parent, string label, Action action, ButtonVariant variant = ButtonVariant.Secondary)
    {
        var button = new HavenButton { Content = label, Variant = variant };
        button.Accessibility.AccessibleName = label;
        button.Invoked += (_, _) => { action(); SetStatus(label); };
        parent.Add(button);
    }

    private void ApplyText()
    {
        var value = _textInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(value)) { SetStatus("Enter text first."); return; }
        if (_controller.SelectedObject is { Kind: NotesCanvasObjectKind.Text } && _controller.UpdateSelectedText(value))
        {
            OnCanvasChanged(this, EventArgs.Empty);
            SetStatus("Selected text updated.");
            return;
        }
        _canvas.SetTool(UnifiedCanvasTool.Text);
        PersistSession();
        SetStatus("Text tool selected. Click the canvas to place the text.");
    }

    private void ToggleGrid()
    {
        _canvas.ShowGrid = !_canvas.ShowGrid;
        _canvas.RefreshSurface();
        PersistSession();
        SetStatus(_canvas.ShowGrid ? "Grid shown." : "Grid hidden.");
    }

    private bool ResetViewport()
    {
        _controller.ResetView();
        _canvas.RefreshSurface();
        return true;
    }

    private void OnCanvasChanged(object? sender, EventArgs e)
    {
        PersistSession();
        _canvas.RefreshSurface();
        Invalidate();
    }

    private void OnSelectionChanged(object? sender, EventArgs e)
    {
        var selected = _controller.SelectedObjects;
        if (selected.Count == 1 && selected[0].Kind == NotesCanvasObjectKind.Text) _textInput.Text = selected[0].Text;
        SetStatus(selected.Count switch { 0 => "Nothing selected.", 1 => $"{selected[0].Kind} selected.", _ => $"{selected.Count} objects selected." });
    }

    private async Task SubmitAgentAsync()
    {
        if (_requestAgent is null) return;
        var instruction = _agentInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(instruction)) { SetStatus("Enter an instruction for Haven first."); return; }
        SetStatus("Sending whiteboard context to Haven…");
        try
        {
            await _requestAgent(JsonSerializer.SerializeToElement(new
            {
                instruction,
                title = _title.Content,
                prompt = _prompt.Content,
                selectedElementIds = _controller.SelectedObjectIds.Select(id => id.ToString("N")).ToArray(),
                canvasState = UnifiedCanvasStateCodec.ToJson(_controller, _canvas.Tool, _canvas.ShowGrid)
            }));
            _agentInput.Text = string.Empty;
            SetStatus("Haven received the whiteboard request.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            SetStatus("Haven could not process the whiteboard request: " + exception.Message);
        }
    }

    private void SetStatus(string value) => _status.Content = value;
    private void PersistSession() => _persist(UnifiedCanvasStateCodec.ToJson(_controller, _canvas.Tool, _canvas.ShowGrid));

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _canvas.Changed -= OnCanvasChanged;
        _canvas.SelectionChanged -= OnSelectionChanged;
    }

    private static string? ReadString(GenUiComponent component, string key) =>
        component.Properties.TryGetValue(key, out var value) ? value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString() : null;

    private static double ReadDouble(GenUiComponent component, string key, double fallback) =>
        component.Properties.TryGetValue(key, out var value) && value.TryGetDouble(out var result) ? result : fallback;
}
