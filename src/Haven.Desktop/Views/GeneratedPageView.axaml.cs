using Avalonia.Controls;
using Haven.Desktop.ViewModels;

namespace Haven.Desktop.Views;

public sealed partial class GeneratedPageView : UserControl, IDisposable
{
    public GeneratedPageView() => InitializeComponent();

    public void Dispose()
    {
        if (DataContext is IDisposable disposable) disposable.Dispose();
        DataContext = null;
    }
}
