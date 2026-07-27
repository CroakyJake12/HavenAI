from pathlib import Path

root = Path(__file__).resolve().parents[1]
path = root / "src/Haven.Application/ChatSessionService.cs"
lines = path.read_text(encoding="utf-8").splitlines()
patterns = ["canUseTools", "ExecuteToolAsync", "ChatWithToolsAsync", "CreateAvailabilityPlan", "WorkspaceToolRuntime"]
out = []
for pattern in patterns:
    out.append(f"# PATTERN: {pattern}")
    for i, line in enumerate(lines):
        if pattern not in line:
            continue
        start = max(0, i - 25)
        end = min(len(lines), i + 60)
        out.append(f"-- {path}:{i + 1} --")
        for n in range(start, end):
            out.append(f"{n + 1:5d}: {lines[n]}")
        out.append("")
artifact = root / "artifacts/final-beta-pass/CHAT-TOOL-PATH.txt"
artifact.parent.mkdir(parents=True, exist_ok=True)
artifact.write_text("\n".join(out), encoding="utf-8")
