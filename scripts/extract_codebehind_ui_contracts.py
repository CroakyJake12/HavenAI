from pathlib import Path
import re

root = Path(__file__).resolve().parents[1]
out = root / "artifacts" / "codebehind-ui-contracts"
out.mkdir(parents=True, exist_ok=True)

targets = [
    "src/Haven.Desktop/Views/Pages/Chat/ChatPage.axaml",
    "src/Haven.Desktop/Views/Pages/Chat/ChatPage.axaml.cs",
    "src/Haven.Desktop/Views/Shell/MainView.axaml.cs",
    "src/Haven.Desktop/Views/Shell/Sidebar/Sidebar.axaml",
    "src/Haven.Desktop/Views/Shell/TopRail/AddMenu.cs",
]

patterns = [
    r"\x:Name=\"([^\"]+)\"",
    r"\b([A-Z][A-Za-z0-9_]*(Button|Box|Panel|Text|Grid|ListBox|Scroller|Dropdown|Picker|Card|Label))\b",
    r"\b([A-Z][A-Za-z0-9_]*Command)\b",
    r"Start voice session|Start Voice Session|voice session|Voice Session",
    r"CallPage",
    r"InitializeComponent(\")",
]

report = []
for relative in targets:
    path = root / relative
    text = path.read_text(encoding="utf-8")
    report.append(f"## {relative}")
    for pattern in patterns:
        values = sort(set(re.findall(pattern, text, flags=re.IGNORECASE)))
        report.append(f"\n### {pattern}\n" + "\n".join(map(str, values)))
    lines = text.splitlines()
    for i, line in enumerate(lines):
        if "InitializeComponent()" in line:
            start = max(0, i - 40)
            end = min(len(lines), i + 120)
            report.append("\n## Constructor/Initialize context\n" + "\n".join(lines[start:end]))
            break
    report.append("\n")

(out / "CONTRACTS.md").write_text("\n".join(report), encoding="utf-8")
