from pathlib import Path

path = Path(".github/workflows/build-haven-android.yml")
text = path.read_text(encoding="utf-8")
lines = text.splitlines(keepends=True)

branch_line = "      - haven-android-cloud-build\n"
repair_branch_line = "      - gpt/haven-mobile-repair-android\n"
if repair_branch_line not in lines:
    try:
        branch_index = lines.index(branch_line)
    except ValueError as exc:
        raise SystemExit("build branch anchor not found") from exc
    lines.insert(branch_index + 1, repair_branch_line)

version_line = '            -p:ApplicationVersion="$((300000 + GITHUB_RUN_NUMBER))" \\\n'
display_line = '            -p:ApplicationDisplayVersion="0.3.${GITHUB_RUN_NUMBER}-mobile-preview" \\\n'
keystore_line = "            -p:AndroidKeyStore=false 2>&1 | tee artifacts/android/publish.log\n"
if version_line not in lines:
    try:
        keystore_index = lines.index(keystore_line)
    except ValueError as exc:
        raise SystemExit("Android publish anchor not found") from exc
    lines[keystore_index:keystore_index] = [version_line, display_line]

api35_line = "          api-level: 35\n"
api36_line = "          api-level: 36\n"
if api35_line in lines:
    lines[lines.index(api35_line)] = api36_line
elif api36_line not in lines:
    raise SystemExit("emulator API anchor not found")

updated = "".join(lines)
required = (
    "      - gpt/haven-mobile-repair-android\n",
    '-p:ApplicationVersion="$((300000 + GITHUB_RUN_NUMBER))"',
    '-p:ApplicationDisplayVersion="0.3.${GITHUB_RUN_NUMBER}-mobile-preview"',
    "          api-level: 36\n",
)
missing = [item for item in required if item not in updated]
if missing:
    raise SystemExit(f"postcondition failed: {missing}")

path.write_text(updated, encoding="utf-8")
