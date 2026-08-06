from pathlib import Path

layout = Path("src/Haven.Android/MainView.Mobile.Layout.cs")
interactions = Path("src/Haven.Android/MainView.Mobile.Interactions.cs")

layout_lines = layout.read_text().splitlines()
result = []
inserted_field = any("_mobilePageContent" in line for line in layout_lines)

remove_prefixes = (
    "TopRail.IsVisible",
    "SidebarControl.IsVisible",
    "NativeSidebarHost.IsVisible",
    "ShellContextBar.IsVisible",
    "ContentArea.BorderThickness",
    "ContentArea.CornerRadius",
    "ContentArea.Background",
    "PageContent.Margin",
)

for line in layout_lines:
    stripped = line.strip()
    if stripped.startswith(remove_prefixes):
        continue
    result.append(line)
    if not inserted_field and stripped == "private TextBox? _mobileGoInput;":
        result.append("    private Control? _mobilePageContent;")
        inserted_field = True

layout_text = "\n".join(result) + "\n"

if "foreach (var child in root.Children.ToArray())" not in layout_text:
    marker = '        body.ColumnDefinitions = new ColumnDefinitions("*");\n'
    block = """        foreach (var child in root.Children.ToArray())
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
    if marker not in layout_text:
        raise SystemExit("body column marker not found")
    layout_text = layout_text.replace(marker, block + marker, 1)

if "_mobilePageContent = contentHost;" not in layout_text:
    marker = "        _mobileHeader = BuildMobileHeader();\n"
    if marker not in layout_text:
        raise SystemExit("mobile header marker not found")
    layout_text = layout_text.replace(
        marker,
        "        _mobilePageContent = contentHost;\n"
        "        _mobilePageContent.Margin = new Thickness(0, 0, 0, 92);\n\n"
        + marker,
        1,
    )

layout.write_text(layout_text)

interaction_lines = interactions.read_text().splitlines()
result = []
skip_margin = False

for line in interaction_lines:
    stripped = line.strip()

    if skip_margin:
        if stripped == ": new Thickness(0);":
            skip_margin = False
        continue

    if stripped.startswith((
        "SidebarControl.IsVisible",
        "NativeSidebarHost.IsVisible",
        "ShellContextBar.IsVisible",
    )):
        continue

    if stripped.startswith("PageContent.Margin = isHome"):
        skip_margin = True
        continue

    result.append(line)

interaction_text = "\n".join(result) + "\n"

if "_mobilePageContent.Margin = isHome" not in interaction_text:
    marker = "        RefreshMobileTabs();\n"
    block = """        if (_mobilePageContent is not null)
            _mobilePageContent.Margin = isHome
                ? new Thickness(0, 0, 0, 78)
                : showChatAffordance
                    ? new Thickness(0, 0, 0, 112)
                    : new Thickness(0);
"""
    if marker not in interaction_text:
        raise SystemExit("RefreshMobileTabs marker not found")
    interaction_text = interaction_text.replace(marker, block + marker, 1)

interactions.write_text(interaction_text)

invalid_prefixes = (
    "TopRail.",
    "SidebarControl.",
    "NativeSidebarHost.",
    "ShellContextBar.",
    "ContentArea.",
    "PageContent.",
)
for path in (layout, interactions):
    remaining = [
        line.strip()
        for line in path.read_text().splitlines()
        if line.strip().startswith(invalid_prefixes)
    ]
    if remaining:
        raise SystemExit(f"{path}: remaining desktop-only references: {remaining}")

print("Android mobile shell repair applied and validated.")
