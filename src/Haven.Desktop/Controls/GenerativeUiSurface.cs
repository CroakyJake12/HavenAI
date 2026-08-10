using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.HavenUI.Components;
using Haven.Desktop.HavenUI.Registry;
using Haven.Desktop.HavenUI.Tokens;

namespace Haven.Desktop.Controls;

/// <summary>
/// Trusted HavenUI renderer for structured documents. It maps component types
/// to known controls, emits semantic events, and applies store patches without
/// replacing the whole surface when document structure is unchanged.
/// </summary>
public sealed class GenerativeUiSurface : UserControl, IDisposable
{
    private readonly GenerativeUiEventRouter _router;
    private readonly GenUiInstanceStore _instances;
    private readonly Dictionary<string, Control> _controls = new(StringComparer.Ordinal);
    private readonly Dictionary<string, object?> _inputValues = new(StringComparer.Ordinal);
    private readonly TextBlock _activity = new()
    {
        Classes = { "muted" },
        FontSize = 11,
        HorizontalAlignment = HorizontalAlignment.Left
    };
    private GenUiDocument? _document;
    private bool _suppressStoreNotification;
    private bool _disposed;

    public GenerativeUiSurface(GenerativeUiEventRouter router, GenUiInstanceStore instances)
    {
        _router = router;
        _instances = instances;
        _instances.DocumentChanged += OnDocumentChanged;
    }

    public event EventHandler<GenUiEvent>? SemanticEventEmitted;
    public event EventHandler<GenUiActionResult>? ActionCompleted;

    public GenUiDocument? Document => _document;

    public void Present(GenUiDocument document)
    {
        GenerativeUiContractValidator.ValidateAndThrow(document);
        _document = document;
        _suppressStoreNotification = true;
        try
        {
            _instances.Register(document);
        }
        finally
        {
            _suppressStoreNotification = false;
        }
        Rebuild(document);
    }

    public void PresentExisting(GenUiDocument document)
    {
        GenerativeUiContractValidator.ValidateAndThrow(document);
        var registered = _instances.TryGet(document.Origin.InstanceId)
            ?? throw new InvalidOperationException("The generated UI instance is no longer registered.");
        if (registered.Origin.ThreadId != document.Origin.ThreadId)
            throw new InvalidOperationException("A generated UI instance cannot move between threads.");
        _document = registered;
        Rebuild(registered);
    }

    private void OnDocumentChanged(object? sender, GenUiDocument document)
    {
        if (_disposed || _suppressStoreNotification || _document?.Origin.InstanceId != document.Origin.InstanceId) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (_document is null) return;
            if (StructureKey(_document.Root) == StructureKey(document.Root)) UpdateTree(document.Root);
            else Rebuild(document);
            _document = document;
        });
    }

    private void Rebuild(GenUiDocument document)
    {
        _controls.Clear();
        _inputValues.Clear();
        var root = Build(document.Root);
        var content = new StackPanel
        {
            Spacing = 8,
            Children = { root, _activity }
        };

        // Apply per-surface accent scoping if the document specifies one.
        // This changes accent colors only for this surface, not globally.
        var accentSurface = ResolveAccentSurface(document.AccentKey);
        if (accentSurface.HasValue)
        {
            Content = new HavenAccentScope
            {
                AccentSurface = accentSurface.Value,
                Content = content
            };
        }
        else
        {
            Content = content;
        }
    }

    private static HavenSurface? ResolveAccentSurface(string? accentKey)
    {
        if (string.IsNullOrWhiteSpace(accentKey)) return null;
        return accentKey.ToLowerInvariant() switch
        {
            "blue" or "studio" => HavenSurface.Studio,
            "green" or "play" => HavenSurface.Play,
            "orange" or "tasks" => HavenSurface.Tasks,
            "purple" or "imagine" or "violet" => HavenSurface.Imagine,
            "teal" or "data" or "cyan" => HavenSurface.Data,
            "pink" or "rose" => HavenSurface.Imagine,
            "yellow" or "plan" or "gold" => HavenSurface.Plan,
            "red" or "danger" => HavenSurface.Tasks,
            "indigo" or "study" => HavenSurface.Study,
            "browse" or "sky" => HavenSurface.Browse,
            "home" => HavenSurface.Home,
            "chat" => HavenSurface.Chat,
            "translate" => HavenSurface.Translate,
            "present" => HavenSurface.Present,
            "vision" => HavenSurface.Vision,
            _ => null
        };
    }

    private Control Build(GenUiComponent component)
    {
        Control control = component.ComponentType switch
        {
            "HavenWorkspace" or "HavenStack" or "HavenForm" or "HavenWizard" => BuildStack(component),
            "HavenToolbar" => BuildToolbar(component),
            "HavenGrid" => BuildGrid(component),
            "HavenSplitView" => BuildSplit(component),
            "HavenCard" => BuildCard(component),
            "HavenText" or "HavenMarkdown" => new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                FontWeight = GetBool(component, "emphasis") ? FontWeight.ExtraBold : FontWeight.Medium
            },
            "HavenButton" => BuildButton(component),
            "HavenTextInput" => BuildTextInput(component),
            "HavenSelect" => BuildSelect(component),
            "HavenToggle" => BuildToggle(component),
            "HavenSlider" => BuildSlider(component),
            "HavenProgress" => new HavenProgressBar { Minimum = 0, Maximum = 100, MinWidth = 180 },
            "HavenStatus" => new HavenStatusChip(),
            "HavenList" or "HavenTable" => new StackPanel { Spacing = 5 },
            "HavenTabs" => BuildTabs(component),
            "HavenChart" or "HavenGraph" or "HavenCanvas" or "HavenImage" => BuildVisualFoundation(component),
            _ => throw new InvalidOperationException($"Trusted renderer has no component mapping for '{component.ComponentType}'.")
        };
        _controls.Add(component.ComponentId, control);
        AutomationProperties.SetAutomationId(control, component.ComponentId);
        AutomationProperties.SetName(control, GetString(component, "automationName") ?? GetString(component, "label") ?? component.ComponentId);
        UpdateControl(component, control);
        return control;
    }

    private Control BuildStack(GenUiComponent component)
    {
        var stack = new StackPanel { Spacing = GetDouble(component, "spacing", 10) };
        foreach (var child in component.Children) stack.Children.Add(Build(child));
        return stack;
    }

    private Control BuildHorizontal(GenUiComponent component)
    {
        var stack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = GetDouble(component, "spacing", 8) };
        foreach (var child in component.Children) stack.Children.Add(Build(child));
        return stack;
    }

    private Control BuildToolbar(GenUiComponent component) => new HavenToolbar
    {
        Child = BuildHorizontal(component)
    };

    private Control BuildGrid(GenUiComponent component)
    {
        var columns = Math.Clamp((int)GetDouble(component, "columns", 2), 1, 6);
        var rows = (int)Math.Ceiling(component.Children.Count / (double)columns);
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions(string.Join(',', Enumerable.Repeat("*", columns))),
            RowDefinitions = new RowDefinitions(string.Join(',', Enumerable.Repeat("Auto", Math.Max(1, rows)))),
            ColumnSpacing = 8,
            RowSpacing = 8
        };
        for (var index = 0; index < component.Children.Count; index++)
        {
            var child = Build(component.Children[index]);
            Grid.SetColumn(child, index % columns);
            Grid.SetRow(child, index / columns);
            grid.Children.Add(child);
        }
        return grid;
    }

    private Control BuildSplit(GenUiComponent component)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 12 };
        for (var index = 0; index < Math.Min(2, component.Children.Count); index++)
        {
            var child = Build(component.Children[index]);
            Grid.SetColumn(child, index);
            grid.Children.Add(child);
        }
        return grid;
    }

    private Control BuildCard(GenUiComponent component)
    {
        var stack = new StackPanel { Spacing = 8 };
        foreach (var child in component.Children) stack.Children.Add(Build(child));
        return new HavenCard
        {
            Padding = new Thickness(14),
            Child = stack
        };
    }

    private Button BuildButton(GenUiComponent component)
    {
        var button = GetString(component, "kind")?.ToLowerInvariant() switch
        {
            "primary" => (HavenButtonBase)new HavenPrimaryButton(),
            "tertiary" => new HavenTertiaryButton(),
            "negative" or "destructive" => new HavenNegativeButton(),
            "text" or "ghost" => new HavenTextButton(),
            _ => new HavenSecondaryButton()
        };
        button.HorizontalContentAlignment = HorizontalAlignment.Center;
        button.Click += async (_, _) => await EmitAsync(component, GenUiEventType.ActionInvoked, GetValue(component, "value"));
        return button;
    }

    private TextBox BuildTextInput(GenUiComponent component)
    {
        HavenTextInput input = GetBool(component, "multiline")
            ? new HavenMultilineInput()
            : new HavenTextInput();
        input.TextWrapping = TextWrapping.Wrap;
        input.TextChanged += (_, _) => _inputValues[component.ComponentId] = input.Text ?? string.Empty;
        input.KeyDown += async (_, args) =>
        {
            if (args.Key != Key.Enter || args.KeyModifiers.HasFlag(KeyModifiers.Shift) || component.Actions.Count == 0) return;
            args.Handled = true;
            await EmitAsync(component, GenUiEventType.TextSubmitted, JsonSerializer.SerializeToElement(input.Text ?? string.Empty));
        };
        return input;
    }

    private HavenSelect BuildSelect(GenUiComponent component)
    {
        var select = new HavenSelect();
        select.SelectionChanged += async (_, _) =>
        {
            _inputValues[component.ComponentId] = select.SelectedItem;
            if (component.Actions.Count > 0 && select.IsAttachedToVisualTree())
                await EmitAsync(component, GenUiEventType.OptionSelected, JsonSerializer.SerializeToElement(select.SelectedItem));
        };
        return select;
    }

    private ToggleSwitch BuildToggle(GenUiComponent component)
    {
        var toggle = new HavenSwitch();
        toggle.IsCheckedChanged += async (_, _) =>
        {
            _inputValues[component.ComponentId] = toggle.IsChecked;
            if (component.Actions.Count > 0)
                await EmitAsync(component, GenUiEventType.ToggleChanged, JsonSerializer.SerializeToElement(toggle.IsChecked));
        };
        return toggle;
    }

    private Slider BuildSlider(GenUiComponent component)
    {
        var slider = new HavenSlider { MinWidth = 180 };
        slider.ValueChanged += (_, _) => _inputValues[component.ComponentId] = slider.Value;
        slider.PointerCaptureLost += async (_, _) =>
        {
            if (component.Actions.Count > 0)
                await EmitAsync(component, GenUiEventType.SliderChanged, JsonSerializer.SerializeToElement(slider.Value));
        };
        return slider;
    }

    private Control BuildTabs(GenUiComponent component)
    {
        var tabs = new HavenTabView();
        tabs.ItemsSource = component.Children.Select(child => new HavenTabItem
        {
            Header = GetString(child, "title") ?? child.ComponentId,
            Content = Build(child)
        }).ToArray();
        return tabs;
    }

    private static Control BuildVisualFoundation(GenUiComponent component) => new HavenPanel
    {
        MinHeight = 180,
        CornerRadius = new CornerRadius(16),
        Child = new TextBlock
        {
            Text = GetString(component, "emptyText") ?? $"{component.ComponentType} foundation",
            Classes = { "muted" },
            FontWeight = FontWeight.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        }
    };

    private void UpdateTree(GenUiComponent component)
    {
        if (_controls.TryGetValue(component.ComponentId, out var control)) UpdateControl(component, control);
        foreach (var child in component.Children) UpdateTree(child);
    }

    private void UpdateControl(GenUiComponent component, Control control)
    {
        switch (control)
        {
            case HavenStatusChip status:
                status.Content = GetString(component, "text") ?? GetString(component, "label") ?? string.Empty;
                break;
            case ToggleSwitch toggle:
                toggle.OnContent = GetString(component, "onLabel") ?? "On";
                toggle.OffContent = GetString(component, "offLabel") ?? "Off";
                toggle.IsChecked = GetBool(component, "value");
                break;
            case HavenSelect select:
                var options = GetStringArray(component, "options");
                select.ItemsSource = options;
                var requested = GetString(component, "value");
                select.SelectedIndex = requested is null
                    ? (options.Count > 0 ? 0 : -1)
                    : options.ToList().FindIndex(item => item.Equals(requested, StringComparison.Ordinal));
                _inputValues[component.ComponentId] = select.SelectedItem;
                break;
            case Button button:
                button.Content = GetString(component, "label") ?? component.ComponentId;
                button.IsEnabled = !GetBool(component, "disabled");
                break;
            case TextBox input:
                input.PlaceholderText = GetString(component, "placeholder") ?? string.Empty;
                var next = GetString(component, "value") ?? string.Empty;
                if (!input.IsFocused && input.Text != next) input.Text = next;
                _inputValues[component.ComponentId] = input.Text ?? next;
                break;
            case Slider slider:
                slider.Minimum = GetDouble(component, "minimum", 0);
                slider.Maximum = GetDouble(component, "maximum", 100);
                slider.Value = GetDouble(component, "value", slider.Minimum);
                break;
            case ProgressBar progress:
                progress.Value = GetDouble(component, "value", 0);
                break;
            case TextBlock text:
                text.Text = GetString(component, "text") ?? GetString(component, "label") ?? string.Empty;
                break;
            case StackPanel list when component.ComponentType is "HavenList" or "HavenTable":
                list.Children.Clear();
                foreach (var item in GetStringArray(component, "items"))
                    list.Children.Add(new TextBlock { Text = item, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.Medium });
                break;
        }
    }

    private async Task EmitAsync(GenUiComponent component, GenUiEventType eventType, JsonElement? value)
    {
        if (_document is null || component.Actions.Count == 0) return;
        var binding = component.Actions[0];
        CaptureCurrentInputValues();
        var payload = JsonSerializer.SerializeToElement(new
        {
            values = _inputValues,
            component = component.ComponentId
        });
        var semanticEvent = new GenUiEvent(
            Guid.NewGuid(), eventType, DateTimeOffset.UtcNow, _document.Origin,
            component.ComponentId, binding.ActionId, null, null, value,
            payload, GenUiEventSource.User, $"User interacted with {component.ComponentId}.");
        SemanticEventEmitted?.Invoke(this, semanticEvent);
        _activity.Text = binding.Route == GenUiRouteKind.Local ? "Updating…" : "Haven is working…";
        try
        {
            var result = await _router.RouteAsync(semanticEvent, binding, CancellationToken.None);
            _activity.Text = result.Summary;
            ActionCompleted?.Invoke(this, result);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException)
        {
            _activity.Text = "The generated action failed: " + exception.Message;
        }
    }

    private void CaptureCurrentInputValues()
    {
        foreach (var (componentId, control) in _controls)
        {
            switch (control)
            {
                case TextBox input:
                    _inputValues[componentId] = input.Text ?? string.Empty;
                    break;
                case ComboBox select:
                    _inputValues[componentId] = select.SelectedItem;
                    break;
                case ToggleSwitch toggle:
                    _inputValues[componentId] = toggle.IsChecked;
                    break;
                case Slider slider:
                    _inputValues[componentId] = slider.Value;
                    break;
            }
        }
    }

    private static string StructureKey(GenUiComponent component) =>
        component.ComponentId + ":" + component.ComponentType + "[" + string.Join(',', component.Children.Select(StructureKey)) + "]";

    private static JsonElement? GetValue(GenUiComponent component, string key) =>
        component.Properties.TryGetValue(key, out var value) ? value : null;

    private static string? GetString(GenUiComponent component, string key) =>
        component.Properties.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool GetBool(GenUiComponent component, string key) =>
        component.Properties.TryGetValue(key, out var value) && value.ValueKind is JsonValueKind.True;

    private static double GetDouble(GenUiComponent component, string key, double fallback) =>
        component.Properties.TryGetValue(key, out var value) && value.TryGetDouble(out var result) ? result : fallback;

    private static IReadOnlyList<string> GetStringArray(GenUiComponent component, string key) =>
        component.Properties.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Select(item => item.ToString()).ToArray()
            : [];

    private static IBrush ResourceBrush(string key, Color fallback) =>
        Avalonia.Application.Current?.TryFindResource(key, out var value) == true && value is IBrush brush
            ? brush
            : new SolidColorBrush(fallback);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _instances.DocumentChanged -= OnDocumentChanged;
        Content = null;
        _controls.Clear();
    }
}
