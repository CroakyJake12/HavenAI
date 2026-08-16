using System.Text.Json;
using Avalonia.Threading;
using Haven.Application;
using Haven.Core;
using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;
using HavenText = Haven.UI.Components.Text;

namespace Haven.Desktop.HavenUI.GenerativeUi;

/// <summary>
/// Trusted GenUI adapter for the Haven scene tree. It preserves instance identity, updates compatible
/// component trees in place on store patches, and routes semantic actions through the shared router.
/// </summary>
internal sealed class HavenGenUiSceneSurface : IDisposable
{
    private readonly GenerativeUiEventRouter _router;
    private readonly GenUiInstanceStore _instances;
    private readonly Dictionary<string, HavenElement> _elements = new(StringComparer.Ordinal);
    private readonly Dictionary<string, GenUiComponent> _components = new(StringComparer.Ordinal);
    private readonly Dictionary<Input, GenUiComponent> _inputs = [];
    private readonly Dictionary<string, HavenGenUiWhiteboard> _whiteboards = new(StringComparer.Ordinal);
    private readonly HavenText _activity = new();
    private GenUiDocument? _document;
    private string? _structureSignature;
    private bool _disposed;

    public HavenGenUiSceneSurface(GenerativeUiEventRouter router, GenUiInstanceStore instances)
    {
        _router = router ?? throw new ArgumentNullException(nameof(router));
        _instances = instances ?? throw new ArgumentNullException(nameof(instances));
        Root = new Container { Layout = HavenLayout.Vertical };
        Root.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        Root.SetValue(HavenProperties.Gap, HavenLength.Px(8));
        Root.Accessibility.AccessibleName = "Generated interface";
        _activity.SetValue(HavenProperties.FontSize, 11d);
        _activity.SetValue(HavenProperties.Foreground, "TextSecondary");
        _activity.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
        _activity.Accessibility.AccessibleName = "Generated interface status";
        _instances.DocumentChanged += OnDocumentChanged;
    }

    public Container Root { get; }
    public GenUiDocument? Document => _document;
    public event EventHandler<GenUiEvent>? SemanticEventEmitted;
    public event EventHandler<GenUiActionResult>? ActionCompleted;

    public void Present(GenUiDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (_disposed) throw new ObjectDisposedException(nameof(HavenGenUiSceneSurface));
        GenerativeUiContractValidator.ValidateAndThrow(document);
        _instances.Register(document);
        ApplyDocument(document);
    }

    public void PresentExisting(GenUiDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (_disposed) throw new ObjectDisposedException(nameof(HavenGenUiSceneSurface));
        GenerativeUiContractValidator.ValidateAndThrow(document);
        var registered = _instances.TryGet(document.Origin.InstanceId)
            ?? throw new InvalidOperationException("The generated UI instance is no longer registered.");
        if (registered.Origin.ThreadId != document.Origin.ThreadId)
            throw new InvalidOperationException("A generated UI instance cannot move between threads.");
        ApplyDocument(registered);
    }

    public bool OwnsInput(Input input) =>
        _inputs.ContainsKey(input) || _whiteboards.Values.Any(whiteboard => whiteboard.OwnsInput(input));

    public Task SubmitInputAsync(Input input, CancellationToken cancellationToken = default)
    {
        if (_inputs.TryGetValue(input, out var component))
            return EmitAsync(component, GenUiEventType.TextSubmitted, JsonSerializer.SerializeToElement(input.Text), cancellationToken);
        var whiteboard = _whiteboards.Values.FirstOrDefault(candidate => candidate.OwnsInput(input));
        return whiteboard is null ? Task.CompletedTask : whiteboard.SubmitInputAsync(input);
    }

    private void OnDocumentChanged(object? sender, GenUiDocument document)
    {
        if (_disposed || _document?.Origin.InstanceId != document.Origin.InstanceId) return;
        if (Dispatcher.UIThread.CheckAccess()) ApplyDocument(document);
        else Dispatcher.UIThread.Post(() =>
        {
            if (!_disposed && _document?.Origin.InstanceId == document.Origin.InstanceId) ApplyDocument(document);
        });
    }

    private void ApplyDocument(GenUiDocument document)
    {
        var signature = StructureSignature(document.Root);
        _document = document;
        if (!string.Equals(_structureSignature, signature, StringComparison.Ordinal))
        {
            _structureSignature = signature;
            Rebuild(document);
            return;
        }
        UpdateTree(document.Root);
    }

    private void Rebuild(GenUiDocument document)
    {
        foreach (var whiteboard in _whiteboards.Values) whiteboard.Dispose();
        _whiteboards.Clear();
        _elements.Clear();
        _components.Clear();
        _inputs.Clear();
        foreach (var child in Root.Children.ToArray()) Root.Remove(child);
        Root.Add(Build(document.Root));
        Root.Add(_activity);
    }

    private HavenElement Build(GenUiComponent component)
    {
        HavenElement element = component.ComponentType switch
        {
            "HavenWorkspace" or "HavenStack" or "HavenForm" or "HavenWizard" => BuildStack(component, false),
            "HavenToolbar" => BuildStack(component, true),
            "HavenGrid" => BuildGrid(component),
            "HavenSplitView" => BuildSplit(component),
            "HavenCard" => BuildCard(component),
            "HavenText" => new HavenText(),
            "HavenMarkdown" => new Markdown(),
            "HavenButton" => BuildButton(component),
            "HavenTextInput" => BuildInput(component),
            "HavenSelect" => BuildSelect(component),
            "HavenToggle" => BuildToggle(component),
            "HavenSlider" => BuildSlider(component),
            "HavenProgress" => new Progress(),
            "HavenStatus" => BuildStatus(),
            "HavenList" or "HavenTable" => BuildList(component),
            "HavenTabs" => BuildTabs(component),
            "HavenChart" or "HavenGraph" or "HavenCanvas" or "HavenImage" => BuildVisualFoundation(component),
            _ => throw new InvalidOperationException($"Trusted Haven scene renderer has no component mapping for '{component.ComponentType}'.")
        };
        element.Name = "GenUI_" + SanitizeName(component.ComponentId);
        element.Accessibility.AccessibleName = GetString(component, "automationName") ?? GetString(component, "label") ?? component.ComponentId;
        _elements.Add(component.ComponentId, element);
        _components[component.ComponentId] = component;
        UpdateControl(component, element);
        return element;
    }

    private Container BuildStack(GenUiComponent component, bool horizontal)
    {
        var stack = new Container { Layout = horizontal ? HavenLayout.Horizontal : HavenLayout.Vertical };
        stack.SetValue(HavenProperties.Gap, HavenLength.Px(GetDouble(component, "spacing", horizontal ? 8 : 10)));
        foreach (var child in component.Children) stack.Add(Build(child));
        return stack;
    }

    private Container BuildGrid(GenUiComponent component)
    {
        var columns = Math.Clamp((int)GetDouble(component, "columns", 2), 1, 6);
        var spacing = Math.Max(0, GetDouble(component, "spacing", 12));
        var responsive = GetBool(component, "responsive");
        var itemMinWidth = Math.Max(120, GetDouble(component, "itemMinWidth", 280));
        var grid = new Container
        {
            Layout = responsive ? HavenLayout.Wrap : HavenLayout.Grid,
            Columns = responsive ? string.Empty : string.Join(' ', Enumerable.Repeat("1fr", columns))
        };
        grid.SetValue(HavenProperties.Gap, HavenLength.Px(spacing));
        grid.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        grid.SetValue(HavenProperties.Responsive, responsive);
        for (var index = 0; index < component.Children.Count; index++)
        {
            var child = Build(component.Children[index]);
            if (responsive)
            {
                child.SetValue(HavenProperties.MinWidth, HavenLength.Px(itemMinWidth));
                child.SetValue(HavenProperties.Responsive, true);
            }
            else
            {
                child.SetValue(HavenProperties.Column, index % columns);
                child.SetValue(HavenProperties.Row, index / columns);
            }
            grid.Add(child);
        }
        return grid;
    }

    private Container BuildSplit(GenUiComponent component)
    {
        var split = new Container { Layout = HavenLayout.Grid, Columns = "1fr 1fr" };
        split.SetValue(HavenProperties.Gap, HavenLength.Px(12));
        for (var index = 0; index < Math.Min(2, component.Children.Count); index++)
        {
            var child = Build(component.Children[index]);
            child.SetValue(HavenProperties.Column, index);
            split.Add(child);
        }
        return split;
    }

    private Container BuildCard(GenUiComponent component)
    {
        var card = new Container { Layout = HavenLayout.Vertical };
        card.SetValue(HavenProperties.Background, string.Equals(GetString(component, "variant"), "flashcard", StringComparison.OrdinalIgnoreCase) ? "Accent" : "SurfaceRaised");
        card.SetValue(HavenProperties.Padding, HavenThickness.Uniform(HavenLength.Px(string.Equals(GetString(component, "variant"), "flashcard", StringComparison.OrdinalIgnoreCase) ? 28 : 14)));
        card.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(16)));
        card.SetValue(HavenProperties.Gap, HavenLength.Px(GetDouble(component, "spacing", 8)));
        foreach (var child in component.Children) card.Add(Build(child));
        if (component.Actions.Count > 0)
        {
            card.Accessibility.Focusable = true;
            card.SetValue(HavenProperties.Hover, true);
            card.Invoked += async (_, _) => await EmitAsync(component, GenUiEventType.ActionInvoked, null, CancellationToken.None);
        }
        return card;
    }

    private HavenButton BuildButton(GenUiComponent component)
    {
        var button = new HavenButton
        {
            Variant = GetString(component, "kind")?.ToLowerInvariant() switch
            {
                "primary" => ButtonVariant.Primary,
                "tertiary" => ButtonVariant.Tertiary,
                "negative" or "destructive" => ButtonVariant.Danger,
                "text" => ButtonVariant.Text,
                "ghost" => ButtonVariant.Ghost,
                _ => ButtonVariant.Secondary
            }
        };
        button.SetValue(HavenProperties.HorizontalAlignment, HavenHorizontalAlignment.Start);
        button.Invoked += async (_, _) => await EmitAsync(component, GenUiEventType.ActionInvoked, GetValue(component, "value"), CancellationToken.None);
        return button;
    }

    private Input BuildInput(GenUiComponent component)
    {
        var input = new Input { Multiline = GetBool(component, "multiline"), SubmitOnEnter = true };
        _inputs[input] = component;
        return input;
    }

    private Select BuildSelect(GenUiComponent component)
    {
        var select = new Select();
        select.SelectionChanged += async (_, _) =>
        {
            if (component.Actions.Count > 0 && select.Parent is not null)
                await EmitAsync(component, GenUiEventType.OptionSelected, JsonSerializer.SerializeToElement(select.SelectedItem), CancellationToken.None);
        };
        return select;
    }

    private Toggle BuildToggle(GenUiComponent component)
    {
        var toggle = new Toggle();
        toggle.Invoked += async (_, _) =>
        {
            if (component.Actions.Count > 0)
                await EmitAsync(component, GenUiEventType.ToggleChanged, JsonSerializer.SerializeToElement(toggle.IsChecked), CancellationToken.None);
        };
        return toggle;
    }

    private Slider BuildSlider(GenUiComponent component)
    {
        var slider = new Slider();
        slider.Invoked += async (_, _) =>
        {
            if (component.Actions.Count > 0)
                await EmitAsync(component, GenUiEventType.SliderChanged, JsonSerializer.SerializeToElement(slider.Value), CancellationToken.None);
        };
        return slider;
    }

    private static HavenElement BuildStatus()
    {
        var status = new HavenText();
        status.SetValue(HavenProperties.Background, "SurfaceRaised");
        status.SetValue(HavenProperties.Padding, HavenThickness.Parse("6px 10px"));
        status.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(12)));
        return status;
    }

    private Container BuildList(GenUiComponent component)
    {
        var list = new Container { Layout = HavenLayout.Vertical };
        list.SetValue(HavenProperties.Gap, HavenLength.Px(5));
        return list;
    }

    private Container BuildTabs(GenUiComponent component)
    {
        var root = new Container { Layout = HavenLayout.Vertical };
        root.SetValue(HavenProperties.Gap, HavenLength.Px(8));
        var headers = new Container { Layout = HavenLayout.Horizontal };
        headers.SetValue(HavenProperties.Gap, HavenLength.Px(6));
        var bodies = new List<HavenElement>();
        for (var index = 0; index < component.Children.Count; index++)
        {
            var tab = component.Children[index];
            var body = Build(tab);
            body.SetValue(HavenProperties.Visibility, index == 0 ? HavenVisibility.Visible : HavenVisibility.Collapsed);
            bodies.Add(body);
            var tabIndex = index;
            var button = new HavenButton { Variant = ButtonVariant.Ghost, Content = GetString(tab, "title") ?? tab.ComponentId };
            button.Invoked += (_, _) =>
            {
                for (var bodyIndex = 0; bodyIndex < bodies.Count; bodyIndex++)
                    bodies[bodyIndex].SetValue(HavenProperties.Visibility, bodyIndex == tabIndex ? HavenVisibility.Visible : HavenVisibility.Collapsed);
            };
            headers.Add(button);
        }
        root.Add(headers);
        foreach (var body in bodies) root.Add(body);
        return root;
    }

    private HavenElement BuildVisualFoundation(GenUiComponent component)
    {
        if (component.ComponentType is "HavenGraph" or "HavenChart")
            return HavenGenUiPlot.FromComponent(component);

        if (component.ComponentType.Equals("HavenCanvas", StringComparison.Ordinal))
        {
            var stateKey = "canvas." + component.ComponentId;
            JsonElement? persisted = null;
            if (_document?.State.TryGetValue(stateKey, out var state) == true) persisted = state;
            var whiteboard = new HavenGenUiWhiteboard(
                component,
                persisted,
                value =>
                {
                    var document = _document;
                    if (document is null) return;
                    _instances.ApplyPatch(new GenUiStatePatch(
                        Guid.NewGuid(), document.Origin.InstanceId, GenUiPatchOperation.Replace,
                        "state", stateKey, value, DateTimeOffset.UtcNow));
                },
                component.Actions.Count == 0
                    ? null
                    : request => EmitAsync(component, GenUiEventType.ActionInvoked, request, CancellationToken.None));
            _whiteboards[component.ComponentId] = whiteboard;
            return whiteboard;
        }

        if (component.ComponentType.Equals("HavenImage", StringComparison.Ordinal))
        {
            var image = new Image
            {
                Source = GetString(component, "source") ?? GetString(component, "url") ?? string.Empty,
                Fit = GetString(component, "fit")?.ToLowerInvariant() switch
                {
                    "cover" => HavenImageFit.Cover,
                    "fill" => HavenImageFit.Fill,
                    "none" => HavenImageFit.None,
                    _ => HavenImageFit.Contain
                }
            };
            image.SetValue(HavenProperties.MinHeight, HavenLength.Px(GetDouble(component, "minHeight", 180)));
            image.SetValue(HavenProperties.Width, HavenLength.Percent(100));
            return image;
        }

        var visual = new Container { Layout = HavenLayout.Vertical };
        visual.SetValue(HavenProperties.MinHeight, HavenLength.Px(GetDouble(component, "minHeight", 180)));
        visual.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        visual.SetValue(HavenProperties.Background, "SurfaceRaised");
        visual.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(16)));
        visual.SetValue(HavenProperties.Padding, HavenThickness.Uniform(HavenLength.Px(16)));
        var label = new HavenText { Content = GetString(component, "emptyText") ?? $"{component.ComponentType} foundation" };
        label.SetValue(HavenProperties.HorizontalAlignment, HavenHorizontalAlignment.Center);
        visual.Add(label);
        return visual;
    }

    private void UpdateTree(GenUiComponent component)
    {
        if (_elements.TryGetValue(component.ComponentId, out var element))
        {
            _components[component.ComponentId] = component;
            UpdateControl(component, element);
        }
        foreach (var child in component.Children) UpdateTree(child);
    }

    private void UpdateControl(GenUiComponent component, HavenElement element)
    {
        switch (element)
        {
            case HavenText text:
                text.Content = GetString(component, "text") ?? GetString(component, "label") ?? string.Empty;
                var textSize = GetDouble(component, "fontSize", 0);
                if (textSize > 0) text.SetValue(HavenProperties.FontSize, textSize);
                text.SetValue(HavenProperties.FontWeight, GetBool(component, "emphasis") ? 800 : 500);
                text.SetValue(HavenProperties.Opacity, Math.Clamp(GetDouble(component, "opacity", 1), 0, 1));
                break;
            case Markdown markdown:
                markdown.Content = GetString(component, "text") ?? GetString(component, "label") ?? string.Empty;
                break;
            case HavenButton button:
                button.Content = GetString(component, "label") ?? component.ComponentId;
                button.SetValue(HavenProperties.Enabled, !GetBool(component, "disabled"));
                break;
            case Input input:
                input.Placeholder = GetString(component, "placeholder") ?? string.Empty;
                var next = GetString(component, "value") ?? string.Empty;
                if (!input.State.HasFlag(HavenElementState.Focused) && input.Text != next) input.Text = next;
                break;
            case Select select:
                var options = GetStringArray(component, "options");
                select.Items = options;
                var requested = GetString(component, "value");
                select.SelectedIndex = requested is null ? (options.Count > 0 ? 0 : -1) : options.ToList().FindIndex(item => item.Equals(requested, StringComparison.Ordinal));
                break;
            case Toggle toggle:
                toggle.IsChecked = GetBool(component, "value");
                break;
            case Slider slider:
                slider.Minimum = GetDouble(component, "minimum", 0);
                slider.Maximum = GetDouble(component, "maximum", 100);
                slider.Value = GetDouble(component, "value", slider.Minimum);
                break;
            case Progress progress:
                progress.Value = GetDouble(component, "value", 0);
                break;
            case HavenGenUiPlot plot:
                plot.Update(component);
                break;
            case HavenGenUiWhiteboard whiteboard:
                whiteboard.Update(component);
                break;
            case Container list when component.ComponentType is "HavenList" or "HavenTable":
                foreach (var child in list.Children.ToArray()) list.Remove(child);
                foreach (var item in GetStringArray(component, "items")) list.Add(new HavenText { Content = item });
                break;
        }

        var minWidth = GetDouble(component, "minWidth", 0);
        if (minWidth > 0) element.SetValue(HavenProperties.MinWidth, HavenLength.Px(minWidth));
        var minHeight = GetDouble(component, "minHeight", 0);
        if (minHeight > 0) element.SetValue(HavenProperties.MinHeight, HavenLength.Px(minHeight));
        var width = GetDouble(component, "width", 0);
        if (width > 0) element.SetValue(HavenProperties.Width, HavenLength.Px(width));
        var height = GetDouble(component, "height", 0);
        if (height > 0) element.SetValue(HavenProperties.Height, HavenLength.Px(height));

        var horizontalAlignment = GetString(component, "horizontalAlignment")?.ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(horizontalAlignment))
        {
            element.SetValue(HavenProperties.HorizontalAlignment, horizontalAlignment switch
            {
                "left" or "start" => HavenHorizontalAlignment.Start,
                "center" => HavenHorizontalAlignment.Center,
                "right" or "end" => HavenHorizontalAlignment.End,
                "stretch" => HavenHorizontalAlignment.Stretch,
                _ => element.GetValue(HavenProperties.HorizontalAlignment)
            });
        }

        var verticalAlignment = GetString(component, "verticalAlignment")?.ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(verticalAlignment))
        {
            element.SetValue(HavenProperties.VerticalAlignment, verticalAlignment switch
            {
                "top" or "start" => HavenVerticalAlignment.Start,
                "center" => HavenVerticalAlignment.Center,
                "bottom" or "end" => HavenVerticalAlignment.End,
                "stretch" => HavenVerticalAlignment.Stretch,
                _ => element.GetValue(HavenProperties.VerticalAlignment)
            });
        }

        if (element is HavenText && string.Equals(GetString(component, "tone"), "onAccent", StringComparison.OrdinalIgnoreCase))
            element.SetValue(HavenProperties.Foreground, "TextOnAccent");

        element.Accessibility.AccessibleName = GetString(component, "automationName") ?? GetString(component, "label") ?? component.ComponentId;
    }

    private async Task EmitAsync(GenUiComponent component, GenUiEventType eventType, JsonElement? value, CancellationToken cancellationToken)
    {
        var document = _document;
        if (document is null || component.Actions.Count == 0) return;
        var binding = component.Actions[0];
        var semanticEvent = new GenUiEvent(
            Guid.NewGuid(), eventType, DateTimeOffset.UtcNow, document.Origin,
            component.ComponentId, binding.ActionId, null, null, value,
            JsonSerializer.SerializeToElement(new { values = CaptureCurrentInputValues(), component = component.ComponentId, value }),
            GenUiEventSource.User, "Haven Chat generated UI interaction");
        SemanticEventEmitted?.Invoke(this, semanticEvent);
        SetActivity(binding.Route == GenUiRouteKind.Local ? "Updating…" : "Haven is working…");
        try
        {
            var result = await _router.RouteAsync(semanticEvent, binding, cancellationToken);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                SetActivity(result.Summary);
                ActionCompleted?.Invoke(this, result);
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var message = $"Generated action failed: {exception.Message}";
                Root.Accessibility.Description = message;
                SetActivity(message);
            });
        }
    }

    private void SetActivity(string? value)
    {
        _activity.Content = value ?? string.Empty;
        _activity.SetValue(HavenProperties.Visibility, string.IsNullOrWhiteSpace(value) ? HavenVisibility.Collapsed : HavenVisibility.Visible);
    }

    private IReadOnlyDictionary<string, object?> CaptureCurrentInputValues()
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (componentId, element) in _elements)
        {
            switch (element)
            {
                case Input input:
                    values[componentId] = input.Text;
                    break;
                case Select select:
                    values[componentId] = select.SelectedItem;
                    break;
                case Toggle toggle:
                    values[componentId] = toggle.IsChecked;
                    break;
                case Slider slider:
                    values[componentId] = slider.Value;
                    break;
            }
        }
        return values;
    }

    private static string StructureSignature(GenUiComponent component) =>
        component.ComponentId + ":" + component.ComponentType + "[" + string.Join(',', component.Children.Select(StructureSignature)) + "]";

    private static string SanitizeName(string value)
    {
        var chars = value.Select(character => char.IsLetterOrDigit(character) || character is '_' or '-' ? character : '_').ToArray();
        return new string(chars);
    }

    private static string? GetString(GenUiComponent component, string key)
    {
        if (!component.Properties.TryGetValue(key, out var value)) return null;
        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }

    private static bool GetBool(GenUiComponent component, string key)
    {
        if (!component.Properties.TryGetValue(key, out var value)) return false;
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(value.GetString(), out var parsed) => parsed,
            _ => false
        };
    }

    private static double GetDouble(GenUiComponent component, string key, double fallback)
    {
        if (!component.Properties.TryGetValue(key, out var value)) return fallback;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number)) return number;
        return value.ValueKind == JsonValueKind.String && double.TryParse(value.GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out number) ? number : fallback;
    }

    private static JsonElement? GetValue(GenUiComponent component, string key) =>
        component.Properties.TryGetValue(key, out var value) ? value : null;

    private static IReadOnlyList<string> GetStringArray(GenUiComponent component, string key)
    {
        if (!component.Properties.TryGetValue(key, out var value) || value.ValueKind != JsonValueKind.Array) return [];
        return value.EnumerateArray().Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() ?? string.Empty : item.ToString()).ToArray();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _instances.DocumentChanged -= OnDocumentChanged;
        foreach (var whiteboard in _whiteboards.Values) whiteboard.Dispose();
        _whiteboards.Clear();
        _elements.Clear();
        _components.Clear();
        _inputs.Clear();
    }
}
