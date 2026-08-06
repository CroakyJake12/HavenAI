from pathlib import Path

layout = Path("src/Haven.Android/MainView.Mobile.Layout.cs")
interactions = Path("src/Haven.Android/MainView.Mobile.Interactions.cs")

layout_text = layout.read_text()
if "private Control? _mobilePageContent;" not in layout_text:
    marker = "    private TextBox? _mobileGoInput;\n"
    if marker not in layout_text:
        raise SystemExit("mobile content field insertion marker not found")
    layout_text = layout_text.replace(
        marker,
        marker + "    private Control? _mobilePageContent;\n",
        1,
    )

layout_lines = layout_text.splitlines()
remove_layout_prefixes = (
    "TopRail.IsVisible",
    "SidebarControl.IsVisible",
    "NativeSidebarHost.IsVisible",
    "ShellContextBar.IsVisible",
    "ContentArea.BorderThickness",
    "ContentArea.CornerRadius",
    "ContentArea.Background",
    "PageContent.Margin",
)
layout_lines = [
    line for line in layout_lines
    if not line.strip().startswith(remove_layout_prefixes)
]
layout_text = "\n".join(layout_lines) + "\n"

chrome_block = """        foreach (var child in root.Children.ToArray())
        {
            if (Grid.GetRow(child) == 0)
                child.IsVisible = false;
        }

        foreach (var child in body.Children.ToArray())
        {
            if (!ReferenceEquals(child, contentHost))
                child.IsVisible = false;
        }

"""
if "foreach (var child in root.Children.ToArray())" not in layout_text:
    marker = '        body.ColumnDefinitions = new ColumnDefinitions("*");\n'
    if marker not in layout_text:
        raise SystemExit("body layout marker not found")
    layout_text = layout_text.replace(marker, chrome_block + marker, 1)

content_block = """        _mobilePageContent = contentHost;
        _mobilePageContent.Margin = new Thickness(0, 0, 0, 92);

"""
if "_mobilePageContent = contentHost;" not in layout_text:
    marker = "        _mobileHeader = BuildMobileHeader();\n"
    if marker not in layout_text:
        raise SystemExit("mobile header marker not found")
    layout_text = layout_text.replace(marker, content_block + marker, 1)

layout.write_text(layout_text)

interaction_lines = interactions.read_text().splitlines()
result = []
index = 0
removed_margin = False
while index < len(interaction_lines):
    stripped = interaction_lines[index].strip()
    if stripped.startswith((
        "SidebarControl.IsVisible",
        "NativeSidebarHost.IsVisible",
        "ShellContextBar.IsVisible",
    )):
        index += 1
        continue
    if stripped.startswith("PageContent.Margin = isHome"):
        removed_margin = True
        while index < len(interaction_lines):
            current = interaction_lines[index].strip()
            index += 1
            if current == ": new Thickness(0);":
                break
        continue
    result.append(interaction_lines[index])
    index += 1

interaction_text = "\n".join(result) + "\n"
margin_block = """        if (_mobilePageContent is not null)
            _mobilePageContent.Margin = isHome
                ? new Thickness(0, 0, 0, 78)
                : showChatAffordance
                    ? new Thickness(0, 0, 0, 112)
                    : new Thickness(0);
"""
if "_mobilePageContent.Margin = isHome" not in interaction_text:
    marker = "        RefreshMobileTabs();\n"
    if marker not in interaction_text:
        raise SystemExit("mobile tabs refresh marker not found")
    interaction_text = interaction_text.replace(marker, margin_block + marker, 1)

interactions.write_text(interaction_text)

invalid_tokens = (
    "TopRail.IsVisible",
    "SidebarControl.IsVisible",
    "NativeSidebarHost.IsVisible",
    "ShellContextBar.IsVisible",
    "ContentArea.",
    "PageContent.",
)
for path in (layout, interactions):
    text = path.read_text()
    remaining = [token for token in invalid_tokens if token in text]
    if remaining:
        raise SystemExit(f"{path}: desktop-only references remain: {remaining}")

print("Android shell repair validated.")
