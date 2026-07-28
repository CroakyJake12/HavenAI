using Avalonia.Controls;
using Haven.Desktop.Controls;
using Haven.Desktop.Views.Shell;

namespace Haven.Desktop;

/// <summary>
/// Simple window shell that hosts MainView with animated tidal background.
/// </summary>
public sealed partial class MainWindow : Window
{
    private MainView? _shell;
    private TidalBackground? _tidalBackground;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainView shell)
            {
                _shell = shell;
                MainContent.Content = shell;
                SetupBackground();
            }
        };
    }

    private void SetupBackground()
    {
        _tidalBackground = new TidalBackground(this);

        // Listen for mode changes from the shell
        if (_shell is not null)
        {
            _shell.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(MainView.CurrentMode))
                {
                    _tidalBackground.SetMode(_shell.CurrentMode);
                }
            };

            // Set initial mode
            _tidalBackground.SetMode(_shell.CurrentMode);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _tidalBackground?.Dispose();
        base.OnClosed(e);
    }
}
