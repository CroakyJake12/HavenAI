#!/usr/bin/env python3
# FILE DOCUMENTATION
# Where: scripts/validate_source.py in the repository tooling area used by developers and continuous integration.
# What: This file automates or configures the repository operation described by its commands and keys.
# How: Read from top to bottom: inputs and environment first, validation/processing next, and explicit success or failure output last.
# Why: The file keeps one cohesive responsibility in a predictable location so callers can find and replace it without unrelated changes.
"""Static validation that does not replace dotnet build/test."""
from pathlib import Path
import json
import re
import sys
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
errors: list[str] = []

xml_files = list(ROOT.rglob("*.axaml")) + list(ROOT.rglob("*.csproj")) + list(ROOT.glob("*.props"))
for path in xml_files:
    try:
        ET.parse(path)
    except Exception as exc:
        errors.append(f"Invalid XML {path.relative_to(ROOT)}: {exc}")

try:
    json.loads((ROOT / "global.json").read_text(encoding="utf-8"))
except Exception as exc:
    errors.append(f"Invalid global.json: {exc}")

for project in ROOT.rglob("*.csproj"):
    for element in ET.parse(project).getroot().iter():
        if element.tag.endswith("ProjectReference"):
            target = (project.parent / element.attrib["Include"].replace("\\", "/")).resolve()
            if not target.exists():
                errors.append(f"Missing ProjectReference {project.relative_to(ROOT)} -> {target}")

solution = (ROOT / "Haven.sln").read_text(encoding="utf-8")
for project_path in re.findall(r'Project\("[^"]+"\) = "[^"]+", "([^"]+\.csproj)"', solution):
    if not (ROOT / project_path.replace("\\", "/")).exists():
        errors.append(f"Missing solution project {project_path}")

for pattern in ("*.go", "*.html", "*.js", "*.ts"):
    for path in ROOT.rglob(pattern):
        errors.append(f"Forbidden sidecar source {path.relative_to(ROOT)}")


def without_strings_and_comments(source: str) -> str:
    output: list[str] = []
    index = 0
    length = len(source)
    while index < length:
        if source.startswith("//", index):
            end = source.find("\n", index + 2)
            index = length if end < 0 else end
            output.append("\n")
            continue
        if source.startswith("/*", index):
            end = source.find("*/", index + 2)
            index = length if end < 0 else end + 2
            output.append(" ")
            continue

        raw_start = index
        while raw_start < length and source[raw_start] == "$":
            raw_start += 1
        quote_count = 0
        while raw_start + quote_count < length and source[raw_start + quote_count] == '"':
            quote_count += 1
        if quote_count >= 3:
            end = source.find('"' * quote_count, raw_start + quote_count)
            index = length if end < 0 else end + quote_count
            output.append(" ")
            continue

        if source.startswith('$@"', index) or source.startswith('@$"', index):
            index += 3
            while index < length:
                if source.startswith('""', index):
                    index += 2
                elif source[index] == '"':
                    index += 1
                    break
                else:
                    index += 1
            output.append(" ")
            continue

        if source.startswith('@"', index):
            index += 2
            while index < length:
                if source.startswith('""', index):
                    index += 2
                elif source[index] == '"':
                    index += 1
                    break
                else:
                    index += 1
            output.append(" ")
            continue

        if source.startswith('$"', index) or source[index] == '"':
            index += 2 if source.startswith('$"', index) else 1
            while index < length:
                if source[index] == "\\":
                    index += 2
                elif source[index] == '"':
                    index += 1
                    break
                else:
                    index += 1
            output.append(" ")
            continue

        if source[index] == "'":
            index += 1
            while index < length:
                if source[index] == "\\":
                    index += 2
                elif source[index] == "'":
                    index += 1
                    break
                else:
                    index += 1
            output.append(" ")
            continue

        output.append(source[index])
        index += 1
    return "".join(output)


for path in ROOT.rglob("*.cs"):
    stack: list[tuple[str, int]] = []
    pairs = {")": "(", "]": "[", "}": "{"}
    cleaned = without_strings_and_comments(path.read_text(encoding="utf-8"))
    for position, character in enumerate(cleaned):
        if character in "([{":
            stack.append((character, position))
        elif character in pairs:
            if not stack or stack[-1][0] != pairs[character]:
                errors.append(f"Delimiter mismatch {path.relative_to(ROOT)} at {position}: {character}")
                break
            stack.pop()
    else:
        if stack:
            errors.append(f"Unclosed delimiter {path.relative_to(ROOT)}: {stack[-1]}")

for path in ROOT.rglob("*.axaml"):
    class_name = ET.parse(path).getroot().attrib.get("{http://schemas.microsoft.com/winfx/2006/xaml}Class")
    if class_name:
        code_behind = Path(str(path) + ".cs")
        if not code_behind.exists() or class_name.rsplit(".", 1)[-1] not in code_behind.read_text(encoding="utf-8"):
            errors.append(f"x:Class mismatch {path.relative_to(ROOT)}")

print(f"XML files parsed: {len(xml_files)}")
print(f"C# files checked: {len(list(ROOT.rglob('*.cs')))}")
print(f"Solution projects: {len(list(ROOT.rglob('*.csproj')))}")
print(f"Errors: {len(errors)}")
for error in errors:
    print(f"ERROR: {error}")
sys.exit(1 if errors else 0)
