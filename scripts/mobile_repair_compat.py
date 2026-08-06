from pathlib import Path

path = Path("/tmp/repair.py")
text = path.read_text()

status_call = 'text = replace_once(text, indicator_anchor, indicator_new, "launcher status control")'
status_fallback = """if indicator_anchor in text:
    text = replace_once(text, indicator_anchor, indicator_new, "launcher status control")
else:
    marker = "        _root.AddView(_pageIndicator);" + chr(10)
    if marker not in text:
        raise RuntimeError("launcher status control: page-indicator marker not found")
    grid_marker = "        _grid = new GridLayout(this)" + chr(10)
    status_only = indicator_new.split(marker, 1)[1].rsplit(grid_marker, 1)[0]
    text = text.replace(marker, marker + status_only, 1)"""
if status_call not in text:
    raise SystemExit("launcher status compatibility call not found")
text = text.replace(status_call, status_fallback, 1)

bottom_call = 'text = replace_once(text, bottom_anchor, bottom_new, "launcher drawer button")'
bottom_fallback = """if bottom_anchor in text:
    text = replace_once(text, bottom_anchor, bottom_new, "launcher drawer button")
else:
    dashboard_line = '            "Open Haven dashboard",'
    if dashboard_line not in text:
        raise RuntimeError("launcher drawer button: dashboard marker not found")
    block_start = text.rfind("        row.AddView(IconButton(", 0, text.index(dashboard_line))
    block_end = text.index("));", text.index(dashboard_line)) + 3
    if block_start < 0:
        raise RuntimeError("launcher drawer button: block start not found")
    text = text[:block_start] + bottom_new + text[block_end:]"""
if bottom_call not in text:
    raise SystemExit("launcher drawer compatibility call not found")
text = text.replace(bottom_call, bottom_fallback, 1)

path.write_text(text)
