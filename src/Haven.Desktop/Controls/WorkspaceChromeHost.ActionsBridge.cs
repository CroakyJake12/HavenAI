using Avalonia;
using Avalonia.Interactivity;

namespace Haven.Desktop.Controls;

public sealed partial class WorkspaceChromeHost
{
    private EventHandler<RoutedEventArgs>? _actionsBridgeHandler;

    private void InitializeActionsBridge()
    {
        _actionsSearch.Padding = new Thickness(36, 9, 12, 9);
        if (_actionsButton is null) return;

        // The button must use the same command path as Ctrl+K so the view model builds and
        // filters its command collection before the modern flyout is shown. Keeping the
        // flyout detached also prevents Avalonia from opening it a second time automatically.
        _actionsButton.Flyout = null;
        _actionsBridgeHandler = (_, _) =>
        {
            if (_modernShell?.OpenCommandPaletteCommand.CanExecute(null) == true)
            {
                _modernShell.OpenCommandPaletteCommand.Execute(null);
                return;
            }

            if (_actionsFlyout is null || _actionsButton is null) return;
            RebuildActions();
            _actionsFlyout.ShowAt(_actionsButton);
        };
        _actionsButton.Click += _actionsBridgeHandler;
    }

    private void DisposeActionsBridge()
    {
        if (_actionsButton is not null && _actionsBridgeHandler is not null)
            _actionsButton.Click -= _actionsBridgeHandler;
        _actionsBridgeHandler = null;
    }
}
