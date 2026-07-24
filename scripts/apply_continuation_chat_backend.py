from __future__ import annotations

from pathlib import Path
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]
PATCH = ROOT / "scripts" / "apply_beta_chat_patch.py"


def replace_once(text: str, old: str, new: str, label: str) -> str:
    count = text.count(old)
    if count == 0 and new in text:
        return text
    if count != 1:
        raise RuntimeError(f"{label}: expected one match, found {count}")
    return text.replace(old, new, 1)


text = PATCH.read_text(encoding="utf-8")

old_stream = '''    send_text = replace_once(
        send_text,
        "        if (!canUseTools)\\n        {\\n            await foreach",
        "        if (!canUseTools)\\n"
        "        {\\n"
        "            var firstChunk = true;\\n"
        "            await foreach",
        "stream first chunk state",
    )'''
new_stream = '''    send_text = replace_once(
        send_text,
        "        else\\n        {\\n            await foreach",
        "        else\\n"
        "        {\\n"
        "            var firstChunk = true;\\n"
        "            await foreach",
        "stream first chunk state",
    )'''
text = replace_once(text, old_stream, new_stream, "streaming transform")

old_guard = '''    if count != 1 and "AutoSwitchCompatibleModels" in text:
        raise RuntimeError(
            "ChatPageViewModel temporary fallback: expected one auto-switch block"
        )
'''
new_guard = '''    if count != 1 and "AutoSwitchCompatibleModels" in text:
        lines = text.splitlines(keepends=True)
        auto_index = next(i for i, line in enumerate(lines) if "AutoSwitchCompatibleModels" in line)
        start = next(
            i for i in range(auto_index, -1, -1)
            if "var check = _preflight.Evaluate" in lines[i]
        )
        end = next(
            i for i in range(auto_index + 1, len(lines))
            if "var model = SelectedModel ??" in lines[i]
        )
        text = "".join(lines[:start] + lines[end:])
'''
text = replace_once(text, old_guard, new_guard, "temporary fallback guard")
text = replace_once(
    text,
    "check.Message",
    'string.Join("; ", check.Missing.Select(item => item.Reason))',
    "preflight failure reasons",
)

PATCH.write_text(text, encoding="utf-8", newline="\n")
subprocess.run([sys.executable, str(PATCH)], cwd=ROOT, check=True)
