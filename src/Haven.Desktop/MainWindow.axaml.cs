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

        // Surface changes include dedicated apps such as Browse, Imagine and
        // Dashboard, whereas CurrentMode only describes conversation storage.
        if (_shell is not null)
        {
            _shell.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(MainView.CurrentSurface))
                {
                    _tidalBackground.SetSurface(_shell.CurrentSurface);
                }
            };

            _tidalBackground.SetSurface(_shell.CurrentSurface);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _tidalBackground?.Dispose();
        base.OnClosed(e);
    }
}
