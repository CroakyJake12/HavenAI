/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Browser/Policy/BrowserShortcutPolicy.cs, in the Browser layer, which isolates browser state, safety policy, transport, and automation.
 * What: Maps platform-neutral browser keyboard accelerators to explicit Haven browser actions.
 * How: Desktop and future platform adapters translate their key types into BrowserShortcutKey and dispatch the returned action through the existing BrowserPage commands.
 * Why: Browser keyboard semantics should be deterministic and testable without coupling the Browser layer to Avalonia or another UI toolkit.
 * Maintenance: Keep this policy platform-neutral; UI adapters own focus and command dispatch.
 */

namespace Haven.Browser;

/// <summary>
/// Platform-neutral keys used by Haven's standard browser accelerators.
/// </summary>
public enum BrowserShortcutKey
{
    Other,
    L,
    T,
    N,
    W,
    R,
    F5,
    Left,
    Right,
    D
}

/// <summary>
/// Browser actions produced by a recognized standard accelerator.
/// </summary>
public enum BrowserShortcutAction
{
    None,
    FocusAddress,
    NewTab,
    NewPrivateTab,
    CloseTab,
    Reload,
    HardReload,
    Back,
    Forward,
    ToggleBookmark
}

/// <summary>
/// Resolves keyboard modifiers and a platform-neutral key into one browser action.
/// </summary>
public static class BrowserShortcutPolicy
{
    public static BrowserShortcutAction Resolve(
        BrowserShortcutKey key,
        bool control,
        bool shift,
        bool alt)
    {
        if (alt && !control && !shift)
        {
            return key switch
            {
                BrowserShortcutKey.Left => BrowserShortcutAction.Back,
                BrowserShortcutKey.Right => BrowserShortcutAction.Forward,
                _ => BrowserShortcutAction.None
            };
        }

        if (!alt && key == BrowserShortcutKey.F5)
            return control || shift ? BrowserShortcutAction.HardReload : BrowserShortcutAction.Reload;

        if (!control || alt)
            return BrowserShortcutAction.None;

        return key switch
        {
            BrowserShortcutKey.L when !shift => BrowserShortcutAction.FocusAddress,
            BrowserShortcutKey.T when !shift => BrowserShortcutAction.NewTab,
            BrowserShortcutKey.N when shift => BrowserShortcutAction.NewPrivateTab,
            BrowserShortcutKey.W when !shift => BrowserShortcutAction.CloseTab,
            BrowserShortcutKey.R when shift => BrowserShortcutAction.HardReload,
            BrowserShortcutKey.R when !shift => BrowserShortcutAction.Reload,
            BrowserShortcutKey.D when !shift => BrowserShortcutAction.ToggleBookmark,
            _ => BrowserShortcutAction.None
        };
    }
}
