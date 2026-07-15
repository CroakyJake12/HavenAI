# Haven Browse

## Ownership

- `src/Haven.Browser/BrowserSessionService.cs` owns browser state and the model-facing browser contract.
- `src/Haven.Browser/BrowserDataService.cs` owns persistent browser data and Windows credential integration.
- `src/Haven.Desktop/Views/BrowserView.axaml` owns layout.
- `src/Haven.Desktop/Views/BrowserView.axaml.cs` creates and attaches the single native WebView host.
- `src/Haven.Desktop/ViewModels/BrowserPageViewModel.cs` owns tab/bookmark/history/settings/assistant commands.
- `src/Haven.Application/BrowserToolRuntime.cs` exposes background versus interactive tool definitions.

## Host lifecycle

Keep one `NativeWebView` mounted inside the browser layout. Horizontal and vertical tab layouts must move navigation chrome around that same host, not create multiple native controls. Recreating native hosts is expensive and can trigger child-window/manifest failures.

`BrowserSessionService.IsInteractiveAvailable` is true only while the native host is attached. Background Web Search can navigate/read through the browser runtime; click/fill/back/forward tools are not advertised when no interactive host exists.

The Windows app manifest and WebView2 Runtime are required for the embedded browser. `src/Haven.Desktop/app.manifest` must remain assigned as `ApplicationManifest` in the desktop project.

## Tabs

`BrowserTabItemViewModel` owns ID, title, address, privacy and group. `SelectedTab` updates the one native host. Non-private tabs are persisted when restore-tabs is enabled; private tabs are excluded.

`BrowserSettings.VerticalTabs` switches between:

- the compact horizontal tab strip;
- the 232px vertical tab rail.

Both layouts bind the same tab collection and selected tab. This setting is unrelated to horizontal workspace tabs in the app shell.

## Bookmarks

The bookmark bar is always discoverable. The manager supports:

- saving the current page;
- an optional group name;
- opening a bookmark in the active tab;
- deleting a bookmark;
- persistent empty/status states.

`BrowserDataService.AddBookmarkAsync` validates HTTP/HTTPS URLs and updates an existing bookmark with the same canonical address rather than creating duplicates.

## History and private tabs

History is capped and only recorded when history is enabled and the tab is not private. Clearing history persists immediately. Never record a private visit or restore a private tab.

## Logins

Only Windows Credential Manager stores passwords. JSON stores the origin, username, ID and timestamp. Keep the credential target stable if metadata changes, or migrate existing secrets explicitly.

## Extensions

Haven extensions are intentionally not Chrome extensions. An imported manifest declares a name, description, allowed origins and a script path inside its own folder. The loader confines script paths to that folder. Scripts execute only when extensions are enabled and the active origin matches the allow-list.

Do not add arbitrary native APIs, filesystem access, or process spawning to the extension runtime. `OriginMatches` currently recognizes `<all_urls>`, `http://*/*`, and `https://*/*` as broad patterns in addition to host patterns; treat these as high-scope permissions, review them before enabling an import, and do not introduce broader matching implicitly.

## Browser smoke test

After browser changes, verify:

1. open Browse without a native-child-window exception;
2. navigate to a normal HTTPS page;
3. create, switch, group and close tabs;
4. toggle vertical tabs and confirm the page host is retained;
5. add/open/delete a grouped bookmark and restart to confirm persistence;
6. open a private tab and confirm it is absent after restart;
7. reload and hard reload;
8. open developer tools and print;
9. confirm `@BrowserUse` is hidden when the native host is detached.
