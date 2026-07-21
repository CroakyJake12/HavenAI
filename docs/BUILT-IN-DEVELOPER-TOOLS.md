# Haven built-in developer tools

Haven includes a debug-only Avalonia visual inspector so contributors can inspect and adjust the running UI without installing separately licensed diagnostics tooling.

## Shortcuts

- **F12** toggles the developer-tools window.
- **Ctrl+Shift+C** starts element-picking mode.
- **Ctrl+F** focuses the visual-tree filter while the tools window is active.
- **Escape** cancels element-picking mode.

## Features

- Runtime visual tree with filtering by type, `x:Name`, class, or visible text.
- Click-to-inspect overlay with a live bounds highlight.
- CSS-like selectors for quickly describing a runtime control.
- Common layout, input, content, data-context, and validation properties.
- Safe live editing of opacity, width, height, margin, visibility, text, and string content.
- AXAML source lookup by exact `x:Name`, with a unique-control-type fallback for unnamed elements.
- Source opening in VS Code or Rider with line information when available; otherwise the system-associated editor opens the file.

## Source lookup limitations

Avalonia control templates generate runtime elements such as `ContentPresenter`, `Border`, and template panels that may not exist directly in Haven's authored AXAML. For reliable source navigation, select the nearest named parent or add an `x:Name` to the authored control.

The tools are activated only in `DEBUG` builds. The implementation uses public Avalonia APIs and does not reference `AvaloniaUI.DiagnosticsSupport` or `Avalonia.Diagnostics`.
