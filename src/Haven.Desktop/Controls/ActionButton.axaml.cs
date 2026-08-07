using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Haven.Desktop.Controls;

/// <summary>
/// Reusable action button for flyout menus. Consistent with AddMenu style.
/// </summary>
public sealed partial class ActionButton : UserControl
{
    public static readonly StyledProperty<string> IconKeyProperty =
        AvaloniaProperty.Register<ActionButton, string>(nameof(IconKey), "chat");

    public static readonly StyledProperty<string> LabelTextProperty =
        AvaloniaProperty.Register<ActionButton, string>(nameof(LabelText), string.Empty);

    public static readonly StyledProperty<string?> DescriptionTextProperty =
        AvaloniaProperty.Register<ActionButton, string?>(nameof(DescriptionText));

    public static readonly StyledProperty<bool> IsDangerousProperty =
        AvaloniaProperty.Register<ActionButton, bool>(nameof(IsDangerous));

    public ActionButton()
    {
        InitializeComponent();
        UpdateVisualState();
    }

    public string IconKey
    {
        get => GetValue(IconKeyProperty);
        set => SetValue(IconKeyProperty, value);
    }

    public string LabelText
    {
        get => GetValue(LabelTextProperty);
        set => SetValue(LabelTextProperty, value);
    }

    public string? DescriptionText
    {
        get => GetValue(DescriptionTextProperty);
        set => SetValue(DescriptionTextProperty, value);
    }

    public bool IsDangerous
    {
        get => GetValue(IsDangerousProperty);
        set => SetValue(IsDangerousProperty, value);
    }

    public event EventHandler<global::Avalonia.Interactivity.RoutedEventArgs>? Click
    {
        add => Button.Click += value;
        remove => Button.Click -= value;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IconKeyProperty ||
            change.Property == LabelTextProperty ||
            change.Property == DescriptionTextProperty ||
            change.Property == IsDangerousProperty)
        {
            UpdateVisualState();
        }
    }

    private void UpdateVisualState()
    {
        Icon.IconKey = IconKey;
        Label.Text = LabelText;

        var hasDescription = !string.IsNullOrWhiteSpace(DescriptionText);
        Description.IsVisible = hasDescription;
        Description.Text = DescriptionText ?? string.Empty;

        if (IsDangerous)
        {
            var dangerBrush = ResourceBrush("HavenDangerBrush", Color.Parse("#FFD32F2F"));
            Icon.Foreground = dangerBrush;
            Label.Foreground = dangerBrush;
            Button.Classes.Add("danger");
        }
        else
        {
            Icon.Foreground = ResourceBrush("HavenTextBrush", Colors.Black);
            Label.Foreground = ResourceBrush("HavenTextBrush", Colors.Black);
            Button.Classes.Remove("danger");
        }

        Button.Classes.Add("sidebar");
    }

    private static IBrush ResourceBrush(string key, Color fallback) =>
        Avalonia.Application.Current?.TryFindResource(key, out var value) == true && value is IBrush brush
            ? brush
            : new SolidColorBrush(fallback);
}
