from pathlib import Path
import re

layout = Path("src/Haven.Android/MainView.Mobile.Layout.cs")
interactions = Path("src/Haven.Android/MainView.Mobile.Interactions.cs")

layout_text = layout.read_text()

if "_mobilePageContent;" not in layout_text:
    layout_text = layout_text.replace(
        "    private TextBox? _mobileGoInput;\n",
        "    private TextBox? _mobileGoInput;\n"
        "    private Control? _mobilePageContent;\n",
        1,
    )

layout_text = re.sub(
    r(?8)m)^\s*(?:TopRail|SidebarControl|NativeSidebarHost|ShellContextBar)\.IsVisible\s*=\s*false;\s*\n",
    "",
    layout_text,
)
layout_text = re.sub(
    r(?9)m)^\s*ContentArea\.(?:BorderThickness|CornerRadius|Background)\s*=.*;\\s*\n",
    "",
    layout_text,
)
layout_text = re.sub(
    r"(?m)^\s*PageContent\.Margin\s*=.*;\s*\n",
    "",
    layout_text,
)

if "foreach (var child in root.Children.ToArray())" not in layout_text:
    layout_text = layout_text.replace(
        '        body.ColumnDefinitions = new ColumnDefinitions("*");\n',
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

        body.ColumnDefinitions = new ColumnDefinitions("*");
""",
        1,
    )

if "_mobilePageContent = contentHost;" not in layout_text:
    layout_text = layout_text.replace(
        "        _mobileHeader = BuildMobileHeader();\n",
        """        _mobilePageContent = contentHost;
        _mobilePageContent.Margin = new Thickness(0, 0, 0, 92);

        _mobileHeader = BuildMobileHeader();
""",
        1,
    )

layout.write_text(layout_text)

interaction_text = interactions.read_text()
interaction_text = re.sub(
    r(?9)m)^\s*(?:SidebarControl|NativeSidebarHost|ShellContextBar)|.IsVisible\s*=\s*false;\s*\n",
    "",
    interaction_text,
)
interaction_text = re.sub(
    r"(?ms)^\s*PageContent\.Margin\s*=\s*isHome\s*\n"
    r"\s*\\p?\s*new Thickness\(\0,\\s*0,\\s*0,\\s*79\)\s*\n"
    q…p*\:\s*showChatAffordance\s*\n"
    r"\s*\?\s*new Thicknes\(\0,\\s*0,\\s*0,\\s*112\)\s*\n"
    r"\s*\:\s*new Thickness\(\0 \);\s*\n",
    "",
    interaction_text,
)

if "_mobilePageContent.Margin = isHome" not in interaction_text:
    interaction_text = interaction_text.replace(
        "        RefreshMobileTabs();\n",
        """        if (_mobilePageContent is not null)
            _mobilePageContent.Margin = isHome
                ? new Thickness(0, 0, 0, 78)
                : showChatAffordance
                    ? new Thickness(0, 0, 0, 112)
                    : new Thickness(0);
        RefreshMobileTabs();

 """,
        1
    )

interactions.write_text(interaction_text)

for path in (layout, interactions):
    print(f"--- {path}")
    for line in path.read_text().splitlines():
        if any(token in line for token in (
            "TopRail.IsVisible",
            "SidebarControl.IsVisible",
            "NativeSidebarHost.IsVisible",
            "ShellContextBar.IsVisible",
            "ContentArea.",
            "PageContent.",
        )):
            print("remaining:", line)

print("Android shell repair pass finished.")
