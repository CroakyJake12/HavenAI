from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]

main_path = ROOT / "src/Haven.Desktop/Views/Shell/MainView.axaml.cs"
main = main_path.read_text(encoding="utf-8")
if "AttachBetaOverlays();" not in main:
    anchor = "        BuildCommandPalette();"
    if main.count(anchor) < 1:
        raise RuntimeError("MainView BuildCommandPalette constructor anchor was not found.")
    main = main.replace(anchor, anchor + "\n        AttachBetaOverlays();", 1)
main_path.write_text(main, encoding="utf-8", newline="\n")

chat_path = ROOT / "src/Haven.Desktop/Views/Pages/Chat/ChatPage.axaml"
chat = chat_path.read_text(encoding="utf-8")
chat = chat.replace('Content="Resolve Errors"', 'Content="Resolve Problems"')
chat = chat.replace('ToolTip.Tip="Resolve errors"', 'ToolTip.Tip="Resolve problems"')
chat_path.write_text(chat, encoding="utf-8", newline="\n")

print("Mounted beta overlays and updated the Chat recovery surface.")
