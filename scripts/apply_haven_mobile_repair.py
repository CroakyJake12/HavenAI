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


def patch_new_chat_layout() -> None:
    path = ROOT / "src/Haven.Desktop/Views/Pages/Chat/NewChatPage.axaml"
    original = path.read_text(encoding="utf-8")
    text = original

    # Apply each mobile sizing change only when its desktop value is still present.
    # This keeps the repair safe to run repeatedly after main-branch merges.
    replacements = (
        ('Margin="54,28,54,28"', 'Margin="12,10,12,10"'),
        ('Margin="36,24,36,28"', 'Margin="12,10,12,10"'),
        ('RowSpacing="16"', 'RowSpacing="10"'),
        ('ColumnDefinitions="Auto,640,Auto"', 'ColumnDefinitions="Auto,*,Auto"'),
        ('Width="58" Height="58"', 'Width="50" Height="50"'),
        ('MinHeight="58" MaxHeight="150"', 'MinHeight="50" MaxHeight="132"'),
        ('Padding="22,0"', 'Padding="14,0"'),
        ('Width="64" Height="58"', 'Width="52" Height="50"'),
    )
    for old, new in replacements:
        if old in text:
            text = text.replace(old, new, 1)

    composer_anchor = 'ColumnDefinitions="Auto,*,Auto" ColumnSpacing="10"'
    if composer_anchor not in text:
        raise RuntimeError("New chat responsive layout: responsive composer grid not found")

    if 'MaxWidth="780"' not in text:
        for alignment in (
            'HorizontalAlignment="Stretch" ',
            'HorizontalAlignment="Center" ',
        ):
            anchor = alignment + composer_anchor
            if anchor in text:
                text = text.replace(
                    anchor,
                    'HorizontalAlignment="Stretch" MaxWidth="780" ' + composer_anchor,
                    1,
                )
                break
        else:
            raise RuntimeError(
                "New chat responsive layout: composer alignment anchor not found"
            )

    instruction_name = 'x:Name="InstructionBox"'
    instruction_index = text.find(instruction_name)
    if instruction_index < 0:
        raise RuntimeError("New chat responsive layout: InstructionBox not found")

    tag_start = text.rfind("<TextBox", 0, instruction_index)
    tag_end = text.find(">", instruction_index)
    if tag_start < 0 or tag_end < 0:
        raise RuntimeError("New chat responsive layout: incomplete InstructionBox tag")

    instruction_tag = text[tag_start:tag_end]
    if 'MinWidth="0"' not in instruction_tag:
        column_anchor = 'Grid.Column="1"'
        column_index = text.find(column_anchor, instruction_index, tag_end)
        if column_index < 0:
            raise RuntimeError(
                "New chat responsive layout: InstructionBox Grid.Column anchor not found"
            )
        insert_at = column_index + len(column_anchor)
        text = text[:insert_at] + ' MinWidth="0"' + text[insert_at:]

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
    patch_new_chat_layout()
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
