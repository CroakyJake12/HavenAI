using Avalonia.Controls;
using Avalonia.VisualTree;

namespace Haven.Desktop.Views;

public sealed partial class SettingsView : UserControl, IDisposable
{
    public SettingsView() => InitializeComponent();

    public void Dispose()
    {
        foreach (var studio in this.GetVisualDescendants().OfType<GenerativeUiThemeSelectorView>())
            studio.Dispose();
        DataContext = null;
    }
}
