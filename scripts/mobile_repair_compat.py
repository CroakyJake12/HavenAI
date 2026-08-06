from pathlib import Path

path = Path("/tmp/repair.py")
text = path.read_text()
call = 'text = replace_once(text, indicator_anchor, indicator_new, "launcher status control")'
fallback = """if indicator_anchor in text:
    text = replace_once(text, indicator_anchor, indicator_new, "launcher status control")
else:
    marker = "        _root.AddView(_pageIndicator);\n"
    if marker not in text:
        raise RuntimeError("launcher status control: page-indicator marker not found")
    status_only = indicator_new.split(marker, 1)[1].rsplit(
        "        _grid = new GridLayout(this)\n", 1)[0]
    text = text.replace(marker, marker + status_only, 1)"""
if call not in text:
    raise SystemExit("launcher status compatibility call not found")
path.write_text(text.replace(call, fallback, 1))
