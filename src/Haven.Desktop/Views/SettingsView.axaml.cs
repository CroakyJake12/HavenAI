/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Desktop/Views/SettingsView.axaml.cs, in the Desktop view layer, where Avalonia controls connect XAML interaction to view models.
 * What: This file owns SettingsView. Read the type and member comments below as a map of each responsibility.
 * How: Public members form the callable contract; private members hold implementation details; asynchronous members carry cancellation through I/O.
 * Why: The file keeps one cohesive responsibility in a predictable location so callers can find and replace it without unrelated changes.
 * Maintenance: Preserve the layer boundary, nullability annotations, cancellation flow, and existing public signatures when changing this file.
 */

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Haven.Desktop.Services;

namespace Haven.Desktop.Views;

/// <summary>
/// Represents settings view and keeps its related state and behavior together.
/// </summary>
public sealed partial class SettingsView : UserControl, IDisposable
{
    /// <summary>
    /// Stores motion preferences locally so this component can preserve the dependency, cache, or state between member calls.
    /// </summary>
    private readonly MotionPreferencesService _motionPreferences;

    public SettingsView()
    {
        InitializeComponent();
        _motionPreferences = MotionPreferencesService.Current;
        _motionPreferences.Changed += OnMotionPreferencesChanged;
        ReduceAnimationsToggle.IsChecked = _motionPreferences.ReduceAnimations;
    }

    /// <summary>
    /// Handles the reduce animations toggle clicked event raised by the UI or runtime.
    /// </summary>
    private void OnReduceAnimationsToggleClicked(object? sender, RoutedEventArgs e) =>
        _motionPreferences.SetReduceAnimations(ReduceAnimationsToggle.IsChecked == true);

    /// <summary>
    /// Handles the motion preferences changed event raised by the UI or runtime.
    /// </summary>
    private void OnMotionPreferencesChanged(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(() => ReduceAnimationsToggle.IsChecked = _motionPreferences.ReduceAnimations);

    /// <summary>
    /// Performs the dispose step owned by this component.
    /// </summary>
    public void Dispose()
    {
        _motionPreferences.Changed -= OnMotionPreferencesChanged;
        DataContext = null;
    }
}
