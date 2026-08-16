using System.Text.Json;
using Haven.Core;
using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;
using HavenText = Haven.UI.Components.Text;

namespace Haven.Desktop.HavenUI.GenerativeUi;

/// <summary>Haven-owned interactive surface for a trusted HavenCanvas component.</summary>
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
    private readonly HavenWhiteboardSession _session;
    private readonly HavenWhiteboardCanvas _canvas;
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
        _session = HavenWhiteboardSession.Restore(persistedState);
        _session.Changed += PersistSession;
        _session.Invalidated += Invalidate;

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

        _canvas = new HavenWhiteboardCanvas(
            _session,
            () => string.IsNullOrWhiteSpace(_textInput.Text) ? "Text" : _textInput.Text.Trim(),
            OnSelectionChanged);

        var tools = new Container { Layout = HavenLayout.Wrap };
        tools.SetValue(HavenProperties.Gap, HavenLength.Px(5));
        tools.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        AddToolButton(tools, "Select", HavenWhiteboardTool.Select);
        AddToolButton(tools, "Pen", HavenWhiteboardTool.Pen);
        AddToolButton(tools, "Highlight", HavenWhiteboardTool.Highlighter);
        AddToolButton(tools, "Eraser", HavenWhiteboardTool.Eraser);
        AddToolButton(tools, "Text", HavenWhiteboardTool.Text);
        AddToolButton(tools, "Rectangle", HavenWhiteboardTool.Rectangle);
        AddToolButton(tools, "Ellipse", HavenWhiteboardTool.Ellipse);
        AddToolButton(tools, "Line", HavenWhiteboardTool.Line);
        AddToolButton(tools, "Pan", HavenWhiteboardTool.Pan);
        AddActionButton(tools, "Undo", _session.Undo);
        AddActionButton(tools, "Redo", _session.Redo);
        AddActionButton(tools, "Copy", _session.CopySelected);
        AddActionButton(tools, "Paste", _session.Paste);
        AddActionButton(tools, "Delete", _session.DeleteSelected, ButtonVariant.Danger);
        AddActionButton(tools, "Clear", _session.Clear, ButtonVariant.Danger);

        var controls = new Container { Layout = HavenLayout.Wrap };
        controls.SetValue(HavenProperties.Gap, HavenLength.Px(6));
        controls.SetValue(HavenProperties.Width, HavenLength.Percent(100));

        _colour.Items = Palette.Select(item => item.Name).ToArray();
        _colour.SelectedIndex = Math.Max(0, Array.FindIndex(Palette, item =>
            item.Value.Equals(_session.Color, StringComparison.OrdinalIgnoreCase)));
        _colour.Accessibility.AccessibleName = "Whiteboard colour";
        _colour.SetValue(HavenProperties.MinWidth, HavenLength.Px(110));
        _colour.SelectionChanged += (_, _) =>
        {
            if (_colour.SelectedIndex >= 0 && _colour.SelectedIndex < Palette.Length)
                _session.Color = Palette[_colour.SelectedIndex].Value;
        };
        controls.Add(_colour);

        _thickness.Minimum = 2;
        _thickness.Maximum = 32;
        _thickness.Step = 1;
        _thickness.Value = _session.Thickness;
        _thickness.Accessibility.AccessibleName = "Whiteboard pen thickness";
        _thickness.SetValue(HavenProperties.MinWidth, HavenLength.Px(150));
        _thickness.Invoked += (_, _) => _session.Thickness = _thickness.Value;
        controls.Add(_thickness);

        _textInput.Placeholder = "Text to add or update";
        _textInput.SubmitOnEnter = true;
        _textInput.Accessibility.AccessibleName = "Whiteboard text";
        _textInput.SetValue(HavenProperties.MinWidth, HavenLength.Px(210));
        controls.Add(_textInput);
        AddActionButton(controls, "Add / Update Text", ApplyText);
        AddActionButton(controls, "Grid", ToggleGrid);
        AddActionButton(controls, "Fit Canvas", _session.ResetViewport);

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
        SetStatus($"{_session.Tool} tool selected.");

        Add(_title);
        Add(_prompt);
        Add(_canvas);
        Add(tools);
        Add(controls);
        Add(_status);
        Update(component);
    }

    public bool OwnsInput(Input input) =>
        ReferenceEquals(input, _textInput) || ReferenceEquals(input, _agentInput);

    public async Task SubmitInputAsync(Input input)
    {
        if (ReferenceEquals(input, _textInput))
        {
            ApplyText();
            return;
        }
        if (ReferenceEquals(input, _agentInput)) await SubmitAgentAsync();
    }

    public void Update(GenUiComponent component)
    {
        _title.Content = ReadString(component, "title") ?? "Whiteboard";
        _prompt.Content = ReadString(component, "prompt") ?? ReadString(component, "emptyText") ?? string.Empty;
        _prompt.SetValue(HavenProperties.Visibility,
            string.IsNullOrWhiteSpace(_prompt.Content) ? HavenVisibility.Collapsed : HavenVisibility.Visible);
        var minHeight = Math.Max(320, ReadDouble(component, "minHeight", 420));
        _canvas.SetValue(HavenProperties.Height, HavenLength.Px(Math.Max(280, minHeight - 110)));
        _canvas.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        SetValue(HavenProperties.MinHeight, HavenLength.Px(minHeight));
        Accessibility.AccessibleName = ReadString(component, "automationName") ?? _title.Content;
    }

    private void AddToolButton(Container parent, string label, HavenWhiteboardTool tool)
    {
        var button = new HavenButton { Content = label, Variant = ButtonVariant.Ghost };
        button.Accessibility.AccessibleName = $"Use {label} whiteboard tool";
        button.Invoked += (_, _) =>
        {
            _session.Tool = tool;
            SetStatus($"{label} tool selected.");
        };
        parent.Add(button);
    }

    private void AddActionButton(Container parent, string label, Action action, ButtonVariant variant = ButtonVariant.Secondary)
    {
        var button = new HavenButton { Content = label, Variant = variant };
        button.Accessibility.AccessibleName = label;
        button.Invoked += (_, _) =>
        {
            action();
            SetStatus(label);
        };
        parent.Add(button);
    }

    private void ApplyText()
    {
        var value = _textInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            SetStatus("Enter text first.");
            return;
        }
        if (_session.UpdateSelectedText(value))
        {
            SetStatus("Selected text updated.");
            return;
        }
        _session.Tool = HavenWhiteboardTool.Text;
        SetStatus("Text tool selected. Click the canvas to place the text.");
    }

    private void ToggleGrid()
    {
        _session.ShowGrid = !_session.ShowGrid;
        SetStatus(_session.ShowGrid ? "Grid shown." : "Grid hidden.");
    }

    private void OnSelectionChanged(HavenWhiteboardElement? selected)
    {
        if (selected?.Kind == HavenWhiteboardElementKind.Text) _textInput.Text = selected.Text;
        SetStatus(selected is null ? "Nothing selected." : $"{selected.Kind} selected.");
    }

    private async Task SubmitAgentAsync()
    {
        if (_requestAgent is null) return;
        var instruction = _agentInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(instruction))
        {
            SetStatus("Enter an instruction for Haven first.");
            return;
        }
        SetStatus("Sending whiteboard context to Haven…");
        try
        {
            await _requestAgent(JsonSerializer.SerializeToElement(new
            {
                instruction,
                title = _title.Content,
                prompt = _prompt.Content,
                selectedElementIds = _session.SelectedId is null ? Array.Empty<string>() : new[] { _session.SelectedId },
                canvasState = _session.ToJson()
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
    private void PersistSession() => _persist(_session.ToJson());

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _session.Changed -= PersistSession;
        _session.Invalidated -= Invalidate;
    }

    private static string? ReadString(GenUiComponent component, string key) =>
        component.Properties.TryGetValue(key, out var value)
            ? value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString()
            : null;

    private static double ReadDouble(GenUiComponent component, string key, double fallback) =>
        component.Properties.TryGetValue(key, out var value) && value.TryGetDouble(out var result) ? result : fallback;
}
