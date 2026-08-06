from pathlib import Path

def replace_once(path: Path, old: str, new: str, label: str) -> None:
    text = path.read_text()
    count = text.count(old)
    if count == 0 and new in text:
        print(f"{label}: already applied")
        return
    if count != 1:
        raise SystemExit(f"{label}: expected one occurrence, found {count}")
    path.write_text(text.replace(old, new, 1))
    print(f"{label}: applied")

layout = Path("src/Haven.Android/MainView.Mobile.Layout.cs")
replace_once(
    layout,
    "    private TextBox? _mobileGoInput;\n",
    "    private TextBox? _mobileGoInput;\n"
    "    private Control? _mobilePageContent;\n",
    "mobile page content field")

old_layout = "\n".join([
    "        TopRail.IsVisible = false;",
    "        SidebarControl.IsVisible = false;",
    "        NativeSidebarHost.IsVisible = false;",
    "        ShellContextBar.IsVisible = false;",
    '        body.ColumnDefinitions = new ColumnDefinitions("*");',
    "        body.Margin = new Thickness(0);",
    "        Grid.SetColumn(contentHost, 0);",
    "        Grid.SetColumnSpan(contentHost, 1);",
    "",
    "        ContentArea.BorderThickness = new Thickness(0);",
    "        ContentArea.CornerRadius = new CornerRadius(0);",
    '        ContentArea.Background = ResourceBrush("HavenBackgroundBrush");',
    "        PageContent.Margin = new Thickness(0, 0, 0, 92);",
])
new_layout = "\n".join([
    "        foreach (var child in root.Children.ToArray())",
    "        {",
    "            if (Grid.GetRow(child) == 0)",
    "                child.IsVisible = false;",
    "        }",
    "",
    "        foreach (var child in body.Children.ToArray())",
    "        {",
    "            if (!ReferenceEquals(child, contentHost))",
    "                child.IsVisible = false;",
    "        }",
    "",
    '        body.ColumnDefinitions = new ColumnDefinitions("*");',
    "        body.Margin = new Thickness(0);",
    "        Grid.SetColumn(contentHost, 0);",
    "        Grid.SetColumnSpan(contentHost, 1);",
    "        _mobilePageContent = contentHost;",
    "        _mobilePageContent.Margin = new Thickness(0, 0, 0, 92);",
])
replace_once(layout, old_layout, new_layout, "runtime-located mobile shell controls")

interactions = Path("src/Haven.Android/MainView.Mobile.Interactions.cs")
old_chrome = "\n".join([
    "        SidebarControl.IsVisible = false;",
    "        NativeSidebarHost.IsVisible = false;",
    "        ShellContextBar.IsVisible = false;",
    "",
])
replace_once(interactions, old_chrome, "", "remove desktop named chrome references")

old_margin = "\n".join([
    "        PageContent.Margin = isHome",
    "            ? new Thickness(0, 0, 0, 78)",
    "            : showChatAffordance",
    "                ? new Thickness(0, 0, 0, 112)",
    "                : new Thickness(0);",
])
new_margin = "\n".join([
    "        if (_mobilePageContent is not null)",
    "            _mobilePageContent.Margin = isHome",
    "                ? new Thickness(0, 0, 0, 78)",
    "                : showChatAffordance",
    "                    ? new Thickness(0, 0, 0, 112)",
    "                    : new Thickness(0);",
])
replace_once(interactions, old_margin, new_margin, "runtime mobile content inset")

invalid = [
    "TopRail.IsVisible",
    "SidebarControl.IsVisible",
    "NativeSidebarHost.IsVisible",
    "ShellContextBar.IsVisible",
    "ContentArea.",
    "PageContent.",
]
for path in (layout, interactions):
    text = path.read_text()
    remaining = [token for token in invalid if token in text]
    if remaining:
        raise SystemExit(f"{path}: desktop-only references remain: {remaining}")

print("Android shell repair validated.")