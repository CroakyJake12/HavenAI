using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Haven.Desktop.HavenUI.Components;

/// <summary>The canonical pill input from slide 4.</summary>
public class HavenTextInput : TextBox
{
    public HavenTextInput()
    {
        Theme = HavenControlThemeResolver.For(typeof(TextBox));
        Classes.Add("havenInput");
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        LayoutUpdated -= HideFluentTemplateButtonsAfterLayout;
        LayoutUpdated += HideFluentTemplateButtonsAfterLayout;
        HideFluentTemplateButtons();
    }

    private void HideFluentTemplateButtonsAfterLayout(object? sender, EventArgs e)
    {
        if (!HideFluentTemplateButtons()) return;
        LayoutUpdated -= HideFluentTemplateButtonsAfterLayout;
    }

    private bool HideFluentTemplateButtons()
    {
        // Fluent creates its unnamed clear affordance after OnApplyTemplate
        // returns. Remove it after the first completed layout and keep the
        // deck-defined pill visually uninterrupted.
        var found = false;
        foreach (var button in this.GetVisualDescendants().OfType<Button>())
        {
            button.IsVisible = false;
            found = true;
        }
        return found;
    }
}

/// <summary>A search-specialised Haven input with compact leading-icon spacing.</summary>
public sealed class HavenSearchInput : HavenTextInput
{
    public HavenSearchInput()
    {
        Classes.Add("havenSearchInput");
        PlaceholderText = "Search";
    }
}

/// <summary>The multi-line text surface used by composers and long-form editors.</summary>
public sealed class HavenMultilineInput : HavenTextInput
{
    public HavenMultilineInput()
    {
        Classes.Add("havenMultilineInput");
        AcceptsReturn = true;
        TextWrapping = Avalonia.Media.TextWrapping.Wrap;
    }
}

/// <summary>The deck-defined orange track and light thumb slider.</summary>
public class HavenSlider : Slider
{
    public HavenSlider()
    {
        Classes.Add("havenSlider");
    }
}

/// <summary>
/// Guaranteed-visible gradient track for <see cref="HavenSlider"/>. Avalonia's
/// stock Track sizes its two RepeatButtons around the thumb, which made the
/// mockup track disappear at either endpoint on some desktop templates. This
/// visual owns the complete bar while the transparent Track above it continues
/// to own pointer, keyboard and accessibility behaviour.
/// </summary>
public sealed class HavenSliderTrack : Control
{
    public static readonly StyledProperty<double> MinimumProperty =
        AvaloniaProperty.Register<HavenSliderTrack, double>(nameof(Minimum));

    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<HavenSliderTrack, double>(nameof(Maximum), 100d);

    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<HavenSliderTrack, double>(nameof(Value));

    public static readonly StyledProperty<IBrush?> ActiveBrushProperty =
        AvaloniaProperty.Register<HavenSliderTrack, IBrush?>(nameof(ActiveBrush));

    public static readonly StyledProperty<IBrush?> InactiveBrushProperty =
        AvaloniaProperty.Register<HavenSliderTrack, IBrush?>(nameof(InactiveBrush));

    static HavenSliderTrack()
    {
        AffectsRender<HavenSliderTrack>(
            MinimumProperty,
            MaximumProperty,
            ValueProperty,
            ActiveBrushProperty,
            InactiveBrushProperty);
    }

    public double Minimum { get => GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public IBrush? ActiveBrush { get => GetValue(ActiveBrushProperty); set => SetValue(ActiveBrushProperty, value); }
    public IBrush? InactiveBrush { get => GetValue(InactiveBrushProperty); set => SetValue(InactiveBrushProperty, value); }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Bounds.Width <= 0 || Bounds.Height <= 0) return;

        var radius = Math.Min(Bounds.Height / 2, 12);
        var full = new Rect(0, 0, Bounds.Width, Bounds.Height);
        context.DrawRectangle(InactiveBrush, null, full, radius, radius, default);

        var range = Maximum - Minimum;
        var ratio = range <= 0 ? 0 : Math.Clamp((Value - Minimum) / range, 0, 1);
        if (ratio <= 0 || ActiveBrush is null) return;

        var active = new Rect(0, 0, Math.Max(Bounds.Height, Bounds.Width * ratio), Bounds.Height);
        using (context.PushClip(full))
            context.DrawRectangle(ActiveBrush, null, active, radius, radius, default);
    }
}

/// <summary>A non-interactive progress presentation that shares slider geometry.</summary>
public sealed class HavenProgressBar : ProgressBar
{
    public HavenProgressBar()
    {
        Classes.Add("havenProgress");
    }
}

/// <summary>The compact pill switch from slide 4.</summary>
public sealed class HavenSwitch : ToggleSwitch
{
    public HavenSwitch()
    {
        Classes.Add("havenSwitch");
    }
}

/// <summary>A canonical native selection field for forms.</summary>
public sealed class HavenComboBox : ComboBox
{
    public HavenComboBox()
    {
        Theme = HavenControlThemeResolver.For(typeof(ComboBox));
        Classes.Add("havenComboBox");
    }
}

/// <summary>
/// Fully Haven-owned selection field. Unlike the compatibility ComboBox, its
/// detached menu is composed from HavenDropdownCard and HavenDropdownItemButton
/// so the operating-system/Fluent square grey popup can never leak through.
/// </summary>
public sealed class HavenSelect : HavenButtonBase
{
    private IReadOnlyList<string> _items = [];
    private int _selectedIndex = -1;
    private HavenDropdown? _dropdown;

    public HavenSelect() : base("havenSelect")
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        DetachedFromVisualTree += (_, _) => _dropdown?.Hide();
        RefreshContent();
    }

    public event EventHandler? SelectionChanged;

    public IEnumerable<string>? ItemsSource
    {
        get => _items;
        set
        {
            _items = value?.ToArray() ?? [];
            if (_selectedIndex >= _items.Count) _selectedIndex = _items.Count - 1;
            RefreshContent();
        }
    }

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            var next = value >= 0 && value < _items.Count ? value : -1;
            if (_selectedIndex == next) return;
            _selectedIndex = next;
            RefreshContent();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public string? SelectedItem => _selectedIndex >= 0 && _selectedIndex < _items.Count
        ? _items[_selectedIndex]
        : null;

    protected override void OnClick()
    {
        base.OnClick();
        if (!IsEnabled || _items.Count == 0) return;
        _dropdown?.Hide();
        _dropdown = BuildDropdown();
        _dropdown.ShowAt(this);
    }

    private HavenDropdown BuildDropdown()
    {
        var rows = new StackPanel { Spacing = 5 };
        for (var index = 0; index < _items.Count; index++)
        {
            var itemIndex = index;
            var row = new HavenDropdownItemButton
            {
                Content = _items[index],
                MinHeight = 46,
                Padding = new Thickness(16, 10),
                HorizontalContentAlignment = HorizontalAlignment.Left
            };
            if (index == _selectedIndex) row.Classes.Add("selected");
            row.Click += (_, _) =>
            {
                SelectedIndex = itemIndex;
                _dropdown?.Hide();
            };
            rows.Children.Add(row);
        }

        var card = new HavenDropdownCard
        {
            MinWidth = Math.Max(220, Bounds.Width),
            MaxWidth = 420,
            Padding = new Thickness(8),
            Child = new ScrollViewer
            {
                MaxHeight = 360,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = rows
            }
        };
        return new HavenDropdown { Content = card };
    }

    private void RefreshContent()
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12 };
        grid.Children.Add(new TextBlock
        {
            Text = SelectedItem ?? "Select",
            FontWeight = FontWeight.Bold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        });
        var chevron = new TextBlock
        {
            Text = "⌄",
            FontSize = 18,
            FontWeight = FontWeight.ExtraBold,
            Opacity = 0.76,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(chevron, 1);
        grid.Children.Add(chevron);
        Content = grid;
    }
}

/// <summary>A value editor with the same field geometry as HavenTextInput.</summary>
public sealed class HavenNumericInput : NumericUpDown
{
    public HavenNumericInput()
    {
        Theme = HavenControlThemeResolver.For(typeof(NumericUpDown));
        Classes.Add("havenNumericInput");
    }
}

/// <summary>Canonical binary selection control.</summary>
public sealed class HavenCheckBox : CheckBox
{
    public HavenCheckBox()
    {
        Theme = HavenControlThemeResolver.For(typeof(CheckBox));
        Classes.Add("havenCheckBox");
    }
}

/// <summary>Canonical mutually-exclusive selection control.</summary>
public sealed class HavenRadioButton : RadioButton
{
    public HavenRadioButton()
    {
        Theme = HavenControlThemeResolver.For(typeof(RadioButton));
        Classes.Add("havenRadioButton");
    }
}

/// <summary>Canonical date field using Haven input geometry.</summary>
public sealed class HavenDatePicker : DatePicker
{
    public HavenDatePicker()
    {
        Theme = HavenControlThemeResolver.For(typeof(DatePicker));
        Classes.Add("havenDatePicker");
    }
}

/// <summary>Canonical text-and-calendar date field used by planning forms.</summary>
public sealed class HavenCalendarPicker : CalendarDatePicker
{
    public HavenCalendarPicker()
    {
        Theme = HavenControlThemeResolver.For(typeof(CalendarDatePicker));
        Classes.Add("havenDatePicker");
    }
}

/// <summary>Canonical time field using Haven input geometry.</summary>
public sealed class HavenTimePicker : TimePicker
{
    public HavenTimePicker()
    {
        Theme = HavenControlThemeResolver.For(typeof(TimePicker));
        Classes.Add("havenTimePicker");
    }
}
