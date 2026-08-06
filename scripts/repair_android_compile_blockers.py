#!/usr/bin/env python3
from __future__ import annotations

from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]


def read(path: str) -> tuple[Path, str]:
    file_path = ROOT / path
    return file_path, file_path.read_text(encoding="utf-8")


def write_if_changed(path: Path, original: str, updated: str) -> None:
    if updated != original:
        path.write_text(updated, encoding="utf-8")
        print(f"Updated {path.relative_to(ROOT).as_posix()}")


def ensure_using(text: str, using_line: str, anchor: str) -> str:
    if using_line in text:
        return text
    if anchor not in text:
        raise RuntimeError(f"Missing using anchor: {anchor}")
    return text.replace(anchor, anchor + using_line, 1)


def replace_required(text: str, old: str, new: str, label: str) -> str:
    if new in text:
        return text
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"{label}: expected one occurrence, found {count}")
    return text.replace(old, new, 1)


def patch_settings_overlay() -> None:
    path, original = read(
        "src/Haven.Desktop/Interface/Shell/Pop-Ups/SettingsOverlay.axaml.cs"
    )
    text = ensure_using(
        original,
        "using Avalonia.Markup.Xaml;\n",
        "using Avalonia.Controls;\n",
    )
    text = replace_required(
        text,
        "        InitializeComponent();",
        "        AvaloniaXamlLoader.Load(this);",
        "SettingsOverlay XAML load",
    )
    old = """        SettingsPageHost.Content = new SettingsPage(bus, preferences, ollama);
        BackButton.Click += (_, _) =>
        {
            if (this.FindAncestorOfType<MainView>() is { } mainView)
                mainView.HideOverlay();
        };"""
    new = """        var settingsPageHost = this.FindControl<ContentControl>("SettingsPageHost")
            ?? throw new InvalidOperationException("Settings page host was not loaded.");
        var backButton = this.FindControl<Button>("BackButton")
            ?? throw new InvalidOperationException("Settings back button was not loaded.");

        settingsPageHost.Content = new SettingsPage(bus, preferences, ollama);
        backButton.Click += (_, _) =>
        {
            if (this.FindAncestorOfType<MainView>() is { } mainView)
                mainView.HideOverlay();
        };"""
    text = replace_required(text, old, new, "SettingsOverlay named controls")
    write_if_changed(path, original, text)


def patch_project_settings_overlay() -> None:
    path, original = read(
        "src/Haven.Desktop/Interface/Shell/Pop-Ups/ProjectSettingsOverlay.axaml.cs"
    )
    text = ensure_using(
        original,
        "using Avalonia.Markup.Xaml;\n",
        "using Avalonia.Controls;\n",
    )
    text = replace_required(
        text,
        "        InitializeComponent();",
        "        AvaloniaXamlLoader.Load(this);",
        "ProjectSettingsOverlay XAML load",
   )
    old = """        var item = new ContainerItemViewModel(definition);
        PageContentHost.Content = new ContainerSettingsPage(bus, item, repository, () => Task.CompletedTask);
        BackButton.Click += (_, _) =>
        {
            if (this.FindAncestorOfType<MainView>() is { } mainView)
                mainView.HideOverlay();
        };"""
    new = """        var pageContentHost = this.FindControl<ContentControl>("PageContentHost")
            ?? throw new InvalidOperationException("Project settings page host was not loaded.");
        var backButton = this.FindControl<Button>("BackButton")
            ?? throw new InvalidOperationException("Project settings back button was not loaded.");

        var item = new ContainerItemViewModel(definition);
        pageContentHost.Content = new ContainerSettingsPage(bus, item, repository, () => Task.CompletedTask);
        backButton.Click += (_, _) =>
        {
            if (this.FindAncestorOfType<MainView>() is { } mainView)
                mainView.HideOverlay();
        };"""
    text = replace_required(
        text,
        old,
        new,
        "ProjectSettingsOverlay named controls",
    )
    write_if_changed(path, original, text)


def patch_browser_safety_view() -> None:
    path, original = read(
        "src/Haven.Desktop/Interface/BrowserSafetyView.axaml.cs"
    )
    text = ensure_using(
        original,
        "using Avalonia.Markup.Xaml;\n",
        "using Avalonia.Controls;\n",
    )
    text = replace_required(
        text,
        "        InitializeComponent();",
        "        AvaloniaXamlLoader.Load(this);",
        "BrowserSafetyView XAML load",
    )
    write_if_changed(path, original, text)


def patch_studio_project_view() -> None:
    path, original = read(
        "src/Haven.Desktop/Views/StudioProjectView.axaml.cs"
    )
    text = replace_required(
        original,
        "        Panel.SetZIndex(_settingsOverlay, 100);",
        "        _settingsOverlay.ZIndex = 100;",
        "StudioProjectView ZIndex",
    )
    write_if_changed(path, original, text)


def patch_launcher_icons() -> None:
    path, original = read("src/Haven.Android/HavenLauncherActivity.cs")
    text = original.replace(
        "Android.Resource.Drawable.IcMenuView",
        'SystemDrawable("ic_menu_view")',
    )
    text = text.replace(
        "Android.Resource.Drawable.IcMenuManage",
        'SystemDrawable("ic_menu_manage")',
    )
    if (
        "Android.Resource.Drawable.IcMenuView" in text
        or "Android.Resource.Drawable.IcMenuManage" in text
    ):
        raise RuntimeError("Launcher drawable constants were not fully repaired.")
    write_if_changed(path, original, text)


def main() -> int:
    patch_settings_overlay()
    patch_project_settings_overlay()
    patch_browser_safety_view()
    patch_studio_project_view()
    patch_launcher_icons()
    print("Android compile blocker repair completed.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
