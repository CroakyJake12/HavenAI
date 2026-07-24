from __future__ import annotations

import gzip
import os
from pathlib import Path
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "artifacts" / "final-codebehind-ui"
OUT.mkdir(parents=True, exist_ok=True)


def run(name: str, command: list[str]) -> int:
    log_path = OUT / f"{name}.log"
    with log_path.open("w", encoding="utf-8", errors="replace") as log:
        result = subprocess.run(
            command,
            cwd=ROOT,
            stdout=log,
            stderr=subprocess.STDOUT,
            text=True,
            encoding="utf-8",
            errors="replace",
        )
    return result.returncode


parts = [
    ROOT / "scripts" / f"apply_final_codebehind_ui_pass.py.gz.part{index}"
    for index in range(4)
]
archive = b"".join(path.read_bytes() for path in parts)
with tempfile.TemporaryDirectory() as temp_dir:
    patch_path = Path(temp_di
) / "apply_final_codebehind_ui_pass.py"
    patch_path.write_bytes(gzip.decompress(archive))
    patch = run("PATCH", [sys.executable, str(patch_path)])

restore = run("RESTORE", ["dotnet", "restore", "Haven.sln"]) if patch == 0 else -1
build = (
    run("BUILD", ["dotnet", "build", "Haven.sln", "-c", "Release", "--no-restore"])
    if restore == 0
    else -1
)
test = (
    run(
        "TEST",
        [
            "dotnet",
            "test",
            "tests/Haven.Core.Tests/Haven.Core.Tests.csproj",
            "-c",
            "Release",
            "--no-build",
        ],
    )
    if build == 0
    else -1
)

status = lambda code: "success" if code == 0 else "failure"
(OUT / "VALIDATION.md").write_text(
    "\n".join(
        [
            "# Final code-behind UI validation",
            "",
            "| Stage | Result |",
            "|---|---|",
            f"| Code-behind transformation | {status(patch)} |",
            f"| Restore | {status(restore)} |",
            f"| Release build | {status(build)} |",
            f"| Core tests | {status(test)} |",
            "",
        ]
    ),
    encoding="utf-8",
)

subprocess.run(["git", "config", "user.name", "github-actions[bot]"], cwd=ROOT, check=True)
subprocess.run(
    [
        "git",
        "config",
        "user.email",
        "41898282+github-actions[bot]@users.noreply.github.com",
    ],
    cwd=ROOT,
    check=True,
)

success = patch == 0 and restore == 0 and build == 0 and test == 0
temporary_paths = [
    *(f"scripts/apply_final_codebehind_ui_pass.py.gz.part{index}" for index in range(4)),
    "scripts/extract_codebehind_ui_contracts.py",
    "scripts/run_final_codebehind_ui_validation.py",
    ".github/workflows/extract-codebehind-ui-contracts.yml",
    ".github/workflows/apply-final-codebehind-ui-pass.yml",
    ".github/workflows/run-final-codebehind-ui-validation.yml",
]

if success:
    subprocess.run(["git", "add", "src"], cwd=ROOT, check=True)
    subprocess.run(
        ["git", "rm", "-f", "--ignore-unmatch", *temporary_paths],
        cwd=ROOT,
        check=True,
    )
    subprocess.run(
        ["git", "rm", "-r", "-f", "--ignore-unmatch", "artifacts/codebehind-ui-contracts"],
        cwd=ROOT,
        check=True,
    )
    message = "Apply final code-behind UI pass"
else:
    subprocess.run(["git", "restore", "--worktree", "src"], cwd=ROOT, check=False)
    subprocess.run(["git", "clean", "-fd", "src"], cwd=ROOT, check=False)
    subprocess.run(["git", "add", str(OUT.relative_to(ROOT))], cwd=ROOT, check=True)
    message = "Record final code-behind UI validation failure"

subprocess.run(["git", "commit", "-m", message], cwd=ROOT, check=True)
subprocess.run(["git", "fetch", "origin", "haven-continuation"], cwd=ROOT, check=True)
subprocess.run(["git", "rebase", "origin/haven-continuation"], cwd=ROOT, check=True)
subprocess.run(
    ["git", "push", "origin", "HEAD:haven-continuation"],
    cwd=ROOT,
    check=True,
)

raise SystemExit(0 if success else 1)
