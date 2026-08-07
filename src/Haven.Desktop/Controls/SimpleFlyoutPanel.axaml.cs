using Avalonia;
using Avalonia.Controls;

namespace Haven.Desktop.Controls;

/// <summary>
/// Reusable simple flyout panel with title, input, and action button.
/// Used for create group, rename, and similar simple input dialogs.
/// </summary>
public sealed partial class SimpleFlyoutPanel : UserControl
{
    public SimpleFlyoutPanel()
    {
        InitializeComponent();
    }

    public string TitleText
    {
        get => Title.Text ?? string.Empty;
        set => Title.Text = value;
    }

    public string InputPlaceholder
    {
        get => InputBox.PlaceholderText ?? string.Empty;
        set => InputBox.PlaceholderText = value;
    }

    public string InputValue
    {
        get => InputBox.Text ?? string.Empty;
        set => InputBox.Text = value;
    }

    public string ActionLabel
    {
        get => ActionButton.Content?.ToString() ?? string.Empty;
        set => ActionButton.Content = value;
    }

    public TextBox Input => InputBox;
    public Button Action => ActionButton;

    public void FocusInput()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() => InputBox.Focus(), Avalonia.Threading.DispatcherPriority.Background);
    }
}
