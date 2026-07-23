using Avalonia.Controls;
using Haven.Desktop.Views.Shell;

namespace Haven.Desktop;

/// <summary>
/// Simple window shell that hosts MainView.
/// </summary>
public sealed partial class MainWindow : Window
{
    private MainView? _shell;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainView shell)
            {
                _shell = shell;
                MainContent.Content = shell;
            }
        };
    }
}
