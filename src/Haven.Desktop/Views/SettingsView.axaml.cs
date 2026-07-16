using Avalonia.Controls;

namespace Haven.Desktop.Views;

public sealed partial class SettingsView : UserControl, IDisposable
{
    public SettingsView() => InitializeComponent();

    public void Dispose()
    {
        GenerativeUiSelector.Dispose();
        DataContext = null;
    }
}
