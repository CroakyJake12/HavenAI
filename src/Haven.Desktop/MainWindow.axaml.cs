using Avalonia.Controls;
using Haven.Desktop.Controls;
using Haven.Desktop.Views.Shell;

namespace Haven.Desktop;

/// <summary>
/// Simple window shell that hosts MainView with animated tidal background.
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly UserPreferencesService? _preferences;
    private MainView? _shell;
    private TidalBackground? _tidalBackground;

    public MainWindow() : this(null)
    {
    }

    public MainWindow(UserPreferencesService? preferences)
    {
        _preferences = preferences;
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainView shell)
            {
                _shell = shell;
                SetupBackground();
                MainContent.Content = shell;
            }
        };
    }

    private void SetupBackground()
    {
        _tidalBackground?.Dispose();
        _tidalBackground = new TidalBackground(this, _preferences?.Appearance ?? Haven.Core.HavenUiAppearance.SuperDark);
        if (_preferences is not null)
        {
            _preferences.AppearanceChanged -= OnAppearanceChanged;
            _preferences.AppearanceChanged += OnAppearanceChanged;
        }

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

    private void OnAppearanceChanged(object? sender, EventArgs e) =>
        _tidalBackground?.SetAppearance(_preferences?.Appearance ?? Haven.Core.HavenUiAppearance.SuperDark);

    protected override void OnClosed(EventArgs e)
    {
        if (_preferences is not null)
            _preferences.AppearanceChanged -= OnAppearanceChanged;
        _tidalBackground?.Dispose();
        base.OnClosed(e);
    }
}
