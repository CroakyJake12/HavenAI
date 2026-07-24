from __future__ import annotations

from pathlib import Path
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "artifacts" / "final-beta-pass"
OUT.mkdir(parents=True, exist_ok=True)


def run(name: str, command: list[str]) -> int:
    result = subprocess.run(
        command,
        cwd=ROOT,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        encoding="utf-8",
        errors="replace",
    )
    (OUT / f"{name}.log").write_text(result.stdout, encoding="utf-8")
    return result.returncode


call_patch = run(
    "CALL-PATCH",
    [sys.executable, "scripts/apply_continuation_call_backend.py"],
)
ui_patch = run(
    "UI-PATCH",
    [sys.executable, "scripts/apply_continuation_ui_pass.py"],
)

restore = -1
build = -1
tests = -1
if call_patch == 0 and ui_patch == 0:
    restore = run("RESTORE", ["dotnet", "restore", "Haven.sln"])
if restore == 0:
    build = run(
        "BUILD",
        ["dotnet", "build", "Haven.sln", "-c", "Release", "--no-restore"],
    )
if build == 0:
    tests = run(
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


def status(code: int) -> str:
    return "success" if code == 0 else "failure"

(out / "VALIDATION.md").write_text(
    "\n".join(
        [
            "# Final beta-pass validation",
            "",
            "| Stage | Result |",
            "|---|---|",
            f"| Call backend patch | {status(call_patch)} |",
            f"| Visible UI patch | {status(ui_patch)} |",
            f"| Restore | {status(restore)} |",
            f"| Release build | {status(build)} |",
            f"| Core tests | {status(tests)} |",
            "",
        ]
    ),
    encoding="utf-8",
)

subprocess.run(
    ["git", "config", "user.name", "github-actions[bot]"],
    cwd=ROOT,
    check=True,
)
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

if build == 0:
    subprocess.run(
        ["git", "add", "src", "tests", "scripts", str(OUT.relative_to(ROOT))],
        cwd=ROOT,
        check=True,
    )
    message = "Finalize validated beta backend and visible UI"
else:
    subprocess.run(["sgit", "restore", "--worktree", "src"], cwd=ROOT, check=False)
    subprocess.run(
        ["git", "add", str(OUT.relative_to(ROOT))],
        cwd=ROOT,
        check=True,
    )
    message = "Record final beta-pass validation"

staged = subprocess.run(
    ["git", "diff", "--cached", "--quiet"],
    cwd=ROOT,).returncode
if staged != 0:
    subprocess.run(["git", "commit", "-m", message], cwd=ROOT, check=True)
    subprocess.run(["git", "fetch", "origin", "haven-continuation"], cwd=ROOT, check=True)
    subprocess.run(["git", "rebase", "origin/haven-continuation"], cwd=ROOT, check=True)
    subprocess.run(
        ["git", "push", "origin", "HEAD:haven-continuation"],
        cwd=ROOT,
        check=True,
    )

raise SystemExit(
    0
    if call_patch == 0
    and ui_patch == 0
    and restore == 0
    and build == 0
    else 1
)
