using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Haven.Desktop.Controls;

/// <summary>
/// Reusable dashboard card for modes and conversations.
/// </summary>
public sealed partial class DashboardCard : UserControl
{
    public static readonly StyledProperty<string> IconKeyProperty =
        AvaloniaProperty.Register<DashboardCard, string>(nameof(IconKey), "chat");

    public static readonly StyledProperty<string> TitleTextProperty =
        AvaloniaProperty.Register<DashboardCard, string>(nameof(TitleText), string.Empty);

    public static readonly StyledProperty<string> DetailTextProperty =
        AvaloniaProperty.Register<DashboardCard, string>(nameof(DetailText), string.Empty);

    public DashboardCard()
    {
        InitializeComponent();
        UpdateVisualState();
    }

    public string IconKey
    {
        get => GetValue(IconKeyProperty);
        set => SetValue(IconKeyProperty, value);
    }

    public string TitleText
    {
        get => GetValue(TitleTextProperty);
        set => SetValue(TitleTextProperty, value);
    }

    public string DetailText
    {
        get => GetValue(DetailTextProperty);
        set => SetValue(DetailTextProperty, value);
    }

    public event EventHandler<RoutedEventArgs>? Click
    {
        add => Card.Click += value;
        remove => Card.Click -= value;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IconKeyProperty ||
            change.Property == TitleTextProperty ||
            change.Property == DetailTextProperty)
        {
            UpdateVisualState();
        }
    }

    private void UpdateVisualState()
    {
        Icon.IconKey = IconKey;
        Title.Text = TitleText;
        Detail.Text = DetailText;
    }
}
