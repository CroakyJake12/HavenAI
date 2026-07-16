using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Layout;
using Haven.Application;
using Haven.Desktop.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Controls;

public sealed class GenerativeUiSlot : StackPanel
{
    public static readonly StyledProperty<string> RegionProperty =
        AvaloniaProperty.Register<GenerativeUiSlot, string>(nameof(Region), string.Empty);

    private GenerativeUiThemeRuntime? _runtime;
    private bool _subscribed;

    public GenerativeUiSlot()
    {
        Orientation = Orientation.Horizontal;
        Spacing = 6;
        VerticalAlignment = VerticalAlignment.Center;
        DataContextChanged += (_, _) => Rebuild();
        AttachedToVisualTree += (_, _) => AttachRuntime();
        DetachedFromVisualTree += (_, _) => DetachRuntime();
    }

    public string Region
    {
        get => GetValue(RegionProperty);
        set => SetValue(RegionProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == RegionProperty) Rebuild();
    }

    private void AttachRuntime()
    {
        if (_subscribed) return;
        _runtime = App.Services?.GetService<GenerativeUiThemeRuntime>();
        if (_runtime is null) return;
        _runtime.ThemeChanged += OnThemeChanged;
        _subscribed = true;
        Rebuild();
    }

    private void DetachRuntime()
    {
        if (!_subscribed || _runtime is null) return;
        _runtime.ThemeChanged -= OnThemeChanged;
        _subscribed = false;
    }

    private void OnThemeChanged(object? sender, EventArgs e) => Rebuild();

    private void Rebuild()
    {
        Children.Clear();
        if (_runtime is null || string.IsNullOrWhiteSpace(Region)) return;
        foreach (var placement in _runtime.GetPlacements(Region))
        {
            var control = CreateItem(placement.ItemId, placement.Presentation);
            if (control is not null) Children.Add(control);
        }
    }

    private Control? CreateItem(string itemId, string presentation) => itemId switch
    {
        "chat.temporary" => CreateTemporary(presentation),
        "chat.model" => CreateModel(presentation),
        "chat.effort" => CreateEffort(presentation),
        "chat.context" => CreateContext(presentation),
        _ => null
    };

    private Button CreateTemporary(string presentation)
    {
        var button = new Button
        {
            Classes = { presentation == "compact" ? "chip" : "chrome" },
            VerticalAlignment = VerticalAlignment.Center
        };
        button.Bind(ContentControl.ContentProperty, BindingFor("TemporaryLabel"));
        button.Bind(Button.CommandProperty, BindingFor("ToggleTemporaryCommand"));
        ToolTip.SetTip(button, "Toggle temporary chat");
        return button;
    }

    private Button CreateModel(string presentation)
    {
        var text = new TextBlock
        {
            MaxWidth = presentation == "compact" ? 110 : 180,
            TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        text.Bind(TextBlock.TextProperty, BindingFor("SelectedModel.Name"));
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children = { text, new TextBlock { Text = "⌄", VerticalAlignment = VerticalAlignment.Center } }
        };
        var button = new Button
        {
            Content = panel,
            Classes = { presentation == "compact" ? "chip" : "ghost" },
            VerticalAlignment = VerticalAlignment.Center
        };
        button.Bind(Button.CommandProperty, BindingFor("OpenModelPickerCommand"));
        ToolTip.SetTip(button, "Choose model");
        return button;
    }

    private ComboBox CreateEffort(string presentation)
    {
        var combo = new ComboBox
        {
            MinWidth = presentation == "compact" ? 74 : 88,
            VerticalAlignment = VerticalAlignment.Center
        };
        combo.Bind(ItemsControl.ItemsSourceProperty, BindingFor("EffortLevels"));
        combo.Bind(SelectingItemsControl.SelectedItemProperty, BindingFor("SelectedEffort", BindingMode.TwoWay));
        ToolTip.SetTip(combo, "Reasoning effort");
        return combo;
    }

    private Button CreateContext(string presentation)
    {
        var label = new TextBlock
        {
            FontSize = 9,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        var labelBinding = BindingFor("ContextPercent");
        labelBinding.StringFormat = "{}{0}%";
        label.Bind(TextBlock.TextProperty, labelBinding);

        var button = new Button
        {
            Width = presentation == "labelled" ? 94 : 42,
            Height = 40,
            CornerRadius = new CornerRadius(20),
            Padding = new Thickness(0),
            Content = label,
            VerticalContentAlignment = VerticalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        ToolTip.SetTip(button, "Context usage");

        var contextLabel = new TextBlock();
        contextLabel.Classes.Add("muted");
        contextLabel.Bind(TextBlock.TextProperty, BindingFor("ContextLabel"));
        var progress = new ProgressBar { Maximum = 100 };
        progress.Bind(RangeBase.ValueProperty, BindingFor("ContextPercent"));
        var compact = new Button { Content = "Compact now" };
        compact.Bind(Button.CommandProperty, BindingFor("CompactContextCommand"));
        button.Flyout = new Flyout
        {
            Content = new StackPanel
            {
                Width = 250,
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = "Context", FontWeight = Avalonia.Media.FontWeight.SemiBold },
                    contextLabel,
                    progress,
                    compact
                }
            }
        };
        return button;
    }

    private Binding BindingFor(string chatPath, BindingMode mode = BindingMode.OneWay) => new()
    {
        Path = Region.Equals(GenerativeUiCatalog.ShellHeaderRight, StringComparison.OrdinalIgnoreCase)
            ? "CurrentChat." + chatPath
            : chatPath,
        Mode = mode
    };
}
