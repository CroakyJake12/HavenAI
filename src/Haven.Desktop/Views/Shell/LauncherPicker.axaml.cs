using Avalonia.Controls;
using Avalonia.Interactivity;
using Haven.Desktop.Views.Shell;

namespace Haven.Desktop.Views.Shell;

/// <summary>
/// Launcher picker shown on startup to choose between new and classic Haven.
/// </summary>
public sealed partial class LauncherPicker : UserControl
{
    public event EventHandler<bool>? LaunchRequested;

    public LauncherPicker()
    {
        InitializeComponent();
        NewHavenButton.Click += (_, _) => LaunchRequested?.Invoke(this, true);
        ClassicHavenButton.Click += (_, _) => LaunchRequested?.Invoke(this, false);
    }
}
