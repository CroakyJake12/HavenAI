using Avalonia.Input;
using Avalonia.Interactivity;
using Haven.Browser;

namespace Haven.Desktop.Views.Pages.Browser;

public sealed partial class BrowserPage
{
    private void WireBrowserShortcuts() =>
        AddHandler(KeyDownEvent, OnBrowserShortcutKeyDown,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble, true);

    private void OnBrowserShortcutKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled) return;
        var modifiers = e.KeyModifiers;
        var action = BrowserShortcutPolicy.Resolve(
            MapShortcutKey(e.Key),
            modifiers.HasFlag(KeyModifiers.Control),
            modifiers.HasFlag(KeyModifiers.Shift),
            modifiers.HasFlag(KeyModifiers.Alt));

        switch (action)
        {
            case BrowserShortcutAction.FocusAddress:
                _sceneControl.FocusElement(_havenScene.AddressInput);
                break;
            case BrowserShortcutAction.NewTab:
                NewTabCommand.Execute(null);
                break;
            case BrowserShortcutAction.NewPrivateTab:
                NewPrivateTabCommand.Execute(null);
                break;
            case BrowserShortcutAction.CloseTab:
                if (SelectedTab is not null) CloseTabCommand.Execute(SelectedTab);
                break;
            case BrowserShortcutAction.Reload:
                if (ReloadCommand.CanExecute(null)) ReloadCommand.Execute(null);
                break;
            case BrowserShortcutAction.HardReload:
                if (HardReloadCommand.CanExecute(null)) HardReloadCommand.Execute(null);
                break;
            case BrowserShortcutAction.Back:
                if (BackCommand.CanExecute(null)) BackCommand.Execute(null);
                break;
            case BrowserShortcutAction.Forward:
                if (ForwardCommand.CanExecute(null)) ForwardCommand.Execute(null);
                break;
            case BrowserShortcutAction.ToggleBookmark:
                ToggleBookmarkCommand.Execute(null);
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private static BrowserShortcutKey MapShortcutKey(Key key) => key switch
    {
        Key.L => BrowserShortcutKey.L,
        Key.T => BrowserShortcutKey.T,
        Key.N => BrowserShortcutKey.N,
        Key.W => BrowserShortcutKey.W,
        Key.R => BrowserShortcutKey.R,
        Key.F5 => BrowserShortcutKey.F5,
        Key.Left => BrowserShortcutKey.Left,
        Key.Right => BrowserShortcutKey.Right,
        Key.D => BrowserShortcutKey.D,
        _ => BrowserShortcutKey.Other
    };
}
