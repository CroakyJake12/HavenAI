using Avalonia.Controls;

namespace Haven.Desktop.Controls;

/// <summary>
/// AXAML-defined context usage flyout. Shows context label, progress bar, and compact button.
/// </summary>
public sealed partial class ContextUsageFlyout : UserControl
{
    public ContextUsageFlyout()
    {
        InitializeComponent();
    }

    public string ContextLabelText
    {
        get => ContextLabel.Text ?? string.Empty;
        set => ContextLabel.Text = value;
    }

    public double ContextProgressValue
    {
        get => ContextProgress.Value;
        set => ContextProgress.Value = value;
    }

    public Button Compact => CompactButton;
}
