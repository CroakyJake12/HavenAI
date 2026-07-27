using Avalonia.Interactivity;

namespace Haven.Desktop.Views.Shell.TopRail;

public sealed partial class TopRail
{
    public event EventHandler? TasksRequested;

    private void OnTasksClicked(object? sender, RoutedEventArgs e)
        => TasksRequested?.Invoke(this, EventArgs.Empty);
}
