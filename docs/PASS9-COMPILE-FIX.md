# Pass 9 compile/restore fix

This patch fixes the first Visual Studio build failures reported against Pass 9.

- Added a non-generic `RequireHost(Func<IEmbeddedBrowserHost, Task>)` overload for browser commands that return plain `Task`.
- Added the missing `Microsoft.Extensions.DependencyInjection` reference to `Haven.Automations`.
- Removed the invalid `Avalonia.Diagnostics` 12.0.1 reference. Haven did not call the legacy DevTools API, so no replacement package is needed.
- Replaced the default bundled SQLite native library with `Microsoft.Data.Sqlite.Core` plus the Windows system `winsqlite3` provider.
- Added one-time SQLite provider initialisation before opening a connection.

After replacing the source, delete all `bin` and `obj` folders, restore NuGet packages, and rebuild `Haven.Desktop`.
