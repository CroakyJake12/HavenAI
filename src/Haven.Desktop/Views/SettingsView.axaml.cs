using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Haven.Desktop.Services;

namespace Haven.Desktop.Views;

public sealed partial class SettingsView : UserControl, IDisposable
{
    private readonly MotionPreferencesService _motionPreferences;

    public SettingsView()
    {
        InitializeComponent();
        _motionPreferences = MotionPreferencesService.Current;
        _motionPreferences.Changed += OnMotionPreferencesChanged;
        ReduceAnimationsToggle.IsChecked = _motionPreferences.ReduceAnimations;
    }

    private void OnReduceAnimationsToggleClicked(object? sender, RoutedEventArgs e) =>
        _motionPreferences.SetReduceAnimations(ReduceAnimationsToggle.IsChecked == true);

    private void OnMotionPreferencesChanged(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(() => ReduceAnimationsToggle.IsChecked = _motionPreferences.ReduceAnimations);

    public void Dispose()
    {
        _motionPreferences.Changed -= OnMotionPreferencesChanged;
        GenerativeUiSelector.Dispose();
        DataContext = null;
    }
}
