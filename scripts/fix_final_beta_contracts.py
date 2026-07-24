from pathlib import Path

root = Path(__file__).resolve().parents[1]

capability = root / "src/Haven.Core/ChatCapabilitySelection.cs"
text = capability.read_text(encoding="utf-8")
old = '"inspect code", "edit code", "edit file", "run tests", "build project", "run command"'
new = '"inspect code", "edit code", "edit file", "create file", "create a file", "create the file", "write file", "write a file", "save file", "run tests", "build project", "run command"'
if old in text:
    text = text.replace(old, new, 1)
elif "create the file" not in text:
    raise RuntimeError("Tool-intent phrase anchor was not found.")
capability.write_text(text, encoding="utf-8", newline="\n")

chat = root / "src/Haven.Desktop/Views/Pages/Chat/ChatPage.axaml"
xml = chat.read_text(encoding="utf-8")
xml = xml.replace('Text="Resolve Errors"', 'Text="Resolve Problems"')
xml = xml.replace('Content="Resolve Errors"', 'Content="Resolve Problems"')
xml = xml.replace('ToolTip.Tip="Resolve errors"', 'ToolTip.Tip="Resolve problems"')
chat.write_text(xml, encoding="utf-8", newline="\n")

print("Fixed workspace tool intent and Resolve Problems labels.")
