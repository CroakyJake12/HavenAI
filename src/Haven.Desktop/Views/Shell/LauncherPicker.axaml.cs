using Avalonia.Controls;
using Avalonia.Interactivity;
using Haven.Application;
using Haven.Core;

namespace Haven.Desktop.Views.Shell;

/// <summary>
/// Launcher picker shown on startup to choose between new and classic Haven.
/// </summary>
public sealed partial class LauncherPicker : UserControl
{
    private readonly UserPreferencesService _preferences;
    private readonly IGenerativeThemeStore _themes;

    public event EventHandler<bool>? LaunchRequested;

    public LauncherPicker(UserPreferencesService preferences, IGenerativeThemeStore themes)
    {
        _preferences = preferences;
        _themes = themes;
        InitializeComponent();
        GenerativeUiToggle.IsChecked = _preferences.GenerativeUiEnabled;
        GenerativeUiToggle.Click += OnGenerativeUiToggleClicked;
        ResetGenerativeUiButton.Click += OnResetGenerativeUiClicked;
        NewHavenButton.Click += (_, _) => LaunchRequested?.Invoke(this, true);
        ClassicHavenButton.Click += (_, _) => LaunchRequested?.Invoke(this, false);
    }

    private void OnGenerativeUiToggleClicked(object? sender, RoutedEventArgs e)
    {
        _preferences.SetGenerativeUiEnabled(GenerativeUiToggle.IsChecked == true);
        GenerativeUiStatusText.Text = GenerativeUiToggle.IsChecked == true
            ? "Enabled for the next launch."
            : "Disabled; Haven will use its default theme.";
    }

    private async void OnResetGenerativeUiClicked(object? sender, RoutedEventArgs e)
    {
        ResetGenerativeUiButton.IsEnabled = false;
        GenerativeUiStatusText.Text = "Resetting...";
        try
        {
            var themes = await _themes.GetThemesAsync(CancellationToken.None);
            var defaultTheme = themes.FirstOrDefault(theme =>
                theme.IsBuiltIn &&
                theme.Name.Equals("Haven Default", StringComparison.OrdinalIgnoreCase));
            if (defaultTheme is null)
                throw new InvalidOperationException("The built-in Haven Default theme is unavailable.");

            await _themes.SelectAsync(
                defaultTheme.Id,
                GenerativeThemeAppearance.Light,
                CancellationToken.None);
            _preferences.ApplyTheme("light");
            GenerativeUiStatusText.Text = "Reset to Haven Default Light.";
        }
        catch (Exception ex)
        {
            GenerativeUiStatusText.Foreground = Avalonia.Media.Brushes.Firebrick;
            GenerativeUiStatusText.Text = "Reset failed: " + ex.Message;
        }
        finally
        {
            ResetGenerativeUiButton.IsEnabled = true;
        }
    }
}
