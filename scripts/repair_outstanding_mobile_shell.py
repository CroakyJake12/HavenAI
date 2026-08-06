from pathlib import Path

def replace_exact(path: Path, old: str, new: str, label: str) -> None:
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
interactions = Path("src/Haven.Android/MainView.Mobile.Interactions.cs")

replace_exact(
    layout,
    "    private TextBox? _mobileGoInput;\n",
    "    private TextBox? _mobileGoInput;\n"
    "    private Control? _mobilePageContent;\n",
    "mobile content field",
)

replace_exact(
    layout,
    """        TopRail.IsVisible = false;
        SidebarControl.IsVisible = false;
        NativeSidebarHost.IsVisible = false;
        ShellContextBar.IsVisible = false;
""",
    """        foreach (var child in root.Children.ToArray())
        {
            if (Grid.GetRow(child) == 0)
                child.IsVisible = false;
        }

        foreach (var child in body.Children.ToArray())
        {
            if (!ReferenceEquals(child, contentHost))
                child.IsVisible = false;
        }
""",
    "desktop chrome replacement",
)

replace_exact(
    layout,
    """        ContentArea.BorderThickness = new Thickness(0);
        ContentArea.CornerRadius = new CornerRadius(0);
        ContentArea.Background = ResourceBrush("HavenBackgroundBrush");
        PageContent.Margin = new Thickness(0, 0, 0, 92);
""",
    """        _mobilePageContent = contentHost;
        _mobilePageContent.Margin = new Thickness(0, 0, 0, 92);
""",
    "mobile content host replacement",
)

replace_exact(
    interactions,
    """        SidebarControl.IsVisible = false;
        NativeSidebarHost.IsVisible = false;
        ShellContextBar.IsVisible = false;	""",
    "",
    "remove desktop chrome refresh references",
)

replace_exact(
    interactions,
    """        PageContent.Margin = isHome
            ? new Thickness(0, 0, 0, 78)
            : showChatAffordance
                ? new Thickness(0, 0, 0, 112)
                : new Thickness(0);
""",
    """        if (_mobilePageContent is not null)
            _mobilePageContent.Margin = isHome
                ? new Thickness(0, 0, 0, 78)
                : showChatAffordance
                    ? new Thickness(0, 0, 0, 112)
                    : new Thickness(0);
""",
    "mobile content margin replacement",
)

invalid_tokens = [
    "TopRail.IsVisible",
    "SidebarControl.IsVisible",
    "NativeSidebarHost.IsVisible",
    "ShellContextBar.IsVisible",
    "ContentArea.",
    "PageContent.",
]
for path in (layout, interactions):
    text = path.read_text()
    remaining = [token for token in invalid_tokens if token in text]
    if remaining:
        raise SystemExit(f"{path}: desktop-only references remain: {remaining}")

print("Android shell repair validated.")
