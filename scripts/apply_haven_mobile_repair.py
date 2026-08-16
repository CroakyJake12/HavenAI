#!/usr/bin/env python3
from __future__ import annotations

from pathlib import Path
import re
import sys

ROOT = Path(__file__).resolve().parents[1]
changed: list[str] = []


def write_if_changed(path: Path, text: str, original: str) -> None:
    if text == original:
        return
    path.write_text(text, encoding="utf-8")
    changed.append(path.relative_to(ROOT).as_posix())


def replace_once(text: str, old: str, new: str, label: str) -> str:
    if new in text:
        return text
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"{label}: expected exactly one anchor, found {count}")
    return text.replace(old, new, 1)


def patch_browser() -> None:
    path = ROOT / "src/Haven.Browser/Security/BrowserPrivateProfileManager.cs"
    original = path.read_text(encoding="utf-8")
    text = original

    for method_name in (
        "RejectReparsePointsInExistingPath",
        "RejectReparsePointIfPresent",
    ):
        marker = f"{method_name}:android-platform-guard"
        if marker in text:
            continue
        pattern = re.compile(
            rf"(?P<head>^[ \t]*private\s+static\s+void\s+{method_name}\s*\([^\n]*\)\s*\n[ \t]*\{{\s*\n)",
            re.MULTILINE,
        )
        match = pattern.search(text)
        if match is None:
            raise RuntimeError(f"Browser profile guard: could not find {method_name}")
        indent = re.match(r"[ \t]*", match.group("head")).group(0)
        body_indent = indent + "    "
        guard = (
            match.group("head")
            + f"{body_indent}// {marker}\n"
            + f"{body_indent}if (!OperatingSystem.IsWindows())\n"
            + f"{body_indent}{{\n"
            + f"{body_indent}    return;\n"
            + f"{body_indent}}}\n\n"
        )
        text = text[: match.start()] + guard + text[match.end() :]

    write_if_changed(path, text, original)


def patch_main_view_add_menus() -> None:
    path = ROOT / "src/Haven.Desktop/Interface/Shell/MainView.axaml.cs"
    original = path.read_text(encoding="utf-8")
    text = original

    old_catalogue = '''        var appsTask = _modeRegistry.GetModesAsync(CancellationToken.None);
        await Task.WhenAll(agentsTask, pluginsTask, instructionsTask, appsTask);
        page.SetAddCatalogue(await agentsTask, await pluginsTask, await instructionsTask, await appsTask);
'''
    new_catalogue = '''        var appsTask = _modeRegistry.GetModesAsync(CancellationToken.None);
        await Task.WhenAll(agentsTask, pluginsTask, instructionsTask, appsTask);
#if ANDROID
        var apps = (await appsTask)
            .Concat(await GetInstalledAndroidAppDefinitionsAsync())
            .ToArray();
#else
        var apps = await appsTask;
#endif
        page.SetAddCatalogue(await agentsTask, await pluginsTask, await instructionsTask, apps);
'''
    if new_catalogue not in text:
        count = text.count(old_catalogue)
        if count != 2:
            raise RuntimeError(f"MainView add catalogues: expected 2 blocks, found {count}")
        text = text.replace(old_catalogue, new_catalogue)

    old_chat_selection = '''    private async void OnNewChatCatalogItemSelected(object? sender, AddMenuSelection selection)
    {
        if (selection.Item is ModeDefinition app) await LaunchAppAsync(app, false);
    }
'''
    new_chat_selection = '''    private async void OnNewChatCatalogItemSelected(object? sender, AddMenuSelection selection)
    {
        if (selection.Item is not ModeDefinition app)
            return;
#if ANDROID
        if (IsAndroidAppDefinition(app))
        {
            await ConnectAndroidAppDefinitionAsync(app);
            return;
        }
#endif
        await LaunchAppAsync(app, false);
    }
'''
    text = replace_once(
        text,
        old_chat_selection,
        new_chat_selection,
        "MainView new-chat installed-app selection",
    )

    old_go_selection = '''    private async void OnGoCatalogItemSelected(object? sender, AddMenuSelection selection)
    {
        if (selection.Item is ModeDefinition app)
        {
            await LaunchAppAsync(app, false);
            return;
        }
        await OpenNewChatAsync();
        _newChatPage?.ApplyAddSelection(selection);
    }
'''
    new_go_selection = '''    private async void OnGoCatalogItemSelected(object? sender, AddMenuSelection selection)
    {
        if (selection.Item is ModeDefinition app)
       {
#if ANDROID
            if (IsAndroidAppDefinition(app))
            {
                await ConnectAndroidAppDefinitionAsync(app);
                return;
            }
#endif
            await LaunchAppAsync(app, false);
            return;
        }
        await OpenNewChatAsync();
        _newChatPage?.ApplyAddSelection(selection);
    }
'''
    if new_go_selection not in text:
        signature = "    private async void OnGoCatalogItemSelected"
        boundary = "\n    private void QueueGoSuggestionRefresh"
        start = text.find(signature)
        end = text.find(boundary, start)
        if start < 0 or end < 0:
            raise RuntimeError(
                "MainView Go installed-app selection: bounded method anchors not found"
            )
        text = text[:start] + new_go_selection + text[end:]

    write_if_changed(path, text, original)


def patch_chat_layout() -> None:
    path = ROOT / "src/Haven.Desktop/Views/Pages/Chat/ChatPage.CodeBehindLayout.cs"
    original = path.read_text(encoding="utf-8")
    text = original
    marker = "// haven-mobile-responsive-chat"
    if marker not in text:
        anchor = '''        root.Children.Add(main);

        CodeBehindHost.Children.Clear();
'''
        replacement = '''        root.Children.Add(main);

#if ANDROID
        // haven-mobile-responsive-chat
        sidebar.IsVisible = false;
        header.Margin = new Thickness(12, 8, 12, 6);
        _finalMessages.Margin = new Thickness(12, 8, 12, 14);
        composerStack.Margin = new Thickness(8, 4, 8, 8);
        composerSurface.Padding = new Thickness(8);
        root.ColumnDefinitions = new ColumnDefinitions("*");
        Grid.SetColumn(main, 0);
#endif

        CodeBehindHost.Children.Clear();
'''
        text = replace_once(text, anchor, replacement, "Chat mobile layout")

    write_if_changed(path, text, original)


def main() -> int:
    patch_browser()
    patch_main_view_add_menus()
    patch_chat_layout()
    if changed:
        print("Updated:")
        for item in changed:
            print(f"  {item}")
    else:
        print("Mobile repair compatibility patches already applied.")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as error:
        print(f"error: {error}", file=sys.stderr)
        raise
