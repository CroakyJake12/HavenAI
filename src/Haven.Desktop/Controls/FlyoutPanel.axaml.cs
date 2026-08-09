using Avalonia;
using Avalonia.Controls;
using Haven.Desktop.HavenUI.Components;

namespace Haven.Desktop.Controls;

/// <summary>
/// Reusable flyout panel with consistent styling. Use for all dropdown/flyout menus.
/// </summary>
public sealed partial class FlyoutPanel : UserControl
{
    public static readonly StyledProperty<string> TitleTextProperty =
        AvaloniaProperty.Register<FlyoutPanel, string>(nameof(TitleText), string.Empty);

    public FlyoutPanel()
    {
        InitializeComponent();
    }

    public string TitleText
    {
        get => GetValue(TitleTextProperty);
        set => SetValue(TitleTextProperty, value);
    }

    public new StackPanel Content => ContentHost;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TitleTextProperty)
        {
            Title.Text = TitleText;
        }
    }

    /// <summary>
    /// Creates a Flyout with this panel as content.
    /// </summary>
    public Flyout CreateFlyout(PlacementMode placement = PlacementMode.BottomEdgeAlignedLeft)
    {
        return new HavenDropdown
        {
            Placement = placement,
            Content = this
        };
    }
}
