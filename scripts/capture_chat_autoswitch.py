from pathlib import Path

root = Path(__file__).resolve().parents[1]
source = root / "src/Haven.Desktop/ViewModels/ChatPageViewModel.cs"
lines = source.read_text(encoding="utf-8").splitlines()
matches = [i for i, line in enumerate(lines) if "AutoSwitchCompatibleModels" in line]
out = []
for index in matches:
    start = max(0, index - 35)
    end = min(len(lines), index + 36)
    out.append(f"# {source}:{index + 1}")
    out.extend(f"{number + 1:5}: {lines[number]}" for number in range(start, end))
    out.append("")
artifact = root / "artifacts/backend-beta/CHAT-AUTOSWITCH.txt"
artifact.parent.mkdir(parents=True, exist_ok=True)
artifact.write_text("\n".join(out), encoding="utf-8")
print(f"Captured {len(matches)} auto-switch reference(s).")
