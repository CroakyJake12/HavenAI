using Avalonia.Input;
using Avalonia.Interactivity;
using Haven.Browser;
using Haven.Desktop.Views.Pages.Browser;

namespace Haven.Desktop.Views;

public sealed partial class BrowserView
{
    private void WireBrowserShortcuts() =>
        AddHandler(KeyDownEvent, OnBrowserShortcutKeyDown, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, true);

    private void OnBrowserShortcutKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || DataContext is not BrowserPage vm) return;
        var m = e.KeyModifiers;
        var action = BrowserShortcutPolicy.Resolve(MapShortcutKey(e.Key),
            m.HasFlag(KeyModifiers.Control), m.HasFlag(KeyModifiers.Shift), m.HasFlag(KeyModifiers.Alt));

        switch (action)
        {
            case BrowserShortcutAction.FocusAddress: AddressBox.Focus(); AddressBox.SelectAll(); break;
            case BrowserShortcutAction.NewTab: vm.NewTabCommand.Execute(null); break;
            case BrowserShortcutAction.NewPrivateTab: vm.NewPrivateTabCommand.Execute(null); break;
            case BrowserShortcutAction.CloseTab when vm.SelectedTab is { } tab: vm.CloseTabCommand.Execute(tab); break;
            case BrowserShortcutAction.Reload when vm.ReloadCommand.CanExecute(null): vm.ReloadCommand.Execute(null); break;
            case BrowserShortcutAction.HardReload when vm.HardReloadCommand.CanExecute(null): vm.HardReloadCommand.Execute(null); break;
            case BrowserShortcutAction.Back when vm.BackCommand.CanExecute(null): vm.BackCommand.Execute(null); break;
            case BrowserShortcutAction.Forward when vm.ForwardCommand.CanExecute(null): vm.ForwardCommand.Execute(null); break;
            case BrowserShortcutAction.ToggleBookmark: vm.ToggleBookmarkCommand.Execute(null); break;
            default: return;
        }
        e.Handled = true;
    }

    private static BrowserShortcutKey MapShortcutKey(Key key) => key switch
    {
        Key.L => BrowserShortcutKey.L, Key.T => BrowserShortcutKey.T, Key.N => BrowserShortcutKey.N,
        Key.W => BrowserShortcutKey.W, Key.R => BrowserShortcutKey.R, Key.F5 => BrowserShortcutKey.F5,
        Key.Left => BrowserShortcutKey.Left, Key.Right => BrowserShortcutKey.Right, Key.D => BrowserShortcutKey.D,
        _ => BrowserShortcutKey.Other
    };
}
