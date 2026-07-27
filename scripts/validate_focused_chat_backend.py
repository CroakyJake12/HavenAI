from pathlib import Path
import subprocess
import sys

root = Path(__file__).resolve().parents[1]
out = root / "artifacts" / "focused-chat"
out.mkdir(parents=True, exist_ok=True)


def run(name: str, command: list[str]) -> int:
    result = subprocess.run(
        command,
        cwd=root,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        encoding="utf-8",
        errors="replace",
    )
    (out / f"{name}.log").write_text(result.stdout, encoding="utf-8")
    return result.returncode


patch = run("PATCH", [sys.executable, "scripts/apply_continuation_chat_backend.py"])
restore = run("RESTORE", ["dotnet", "restore", "Haven.sln"]) if patch == 0 else -1
build = run(
    "BUILD",
    ["dotnet", "build", "Haven.sln", "-c", "Release", "--no-restore"],
) if restore == 0 else -1

status = lambda code: "success" if code == 0 else "failure"
(out / "VALIDATION.md").write_text(
    "\n".join(
        [
            "# Focused Chat backend validation",
            "",
            "| Stage | Result |",
            "|---|---|",
            f"| Patch | {status(patch)} |",
            f"| Restore | {status(restore)} |",
            f"| Build | {status(build)} |",
            "",
        ]
    ),
    encoding="utf-8",
)

subprocess.run(["git", "config", "user.name", "github-actions[bot]"], cwd=root, check=True)
subprocess.run(
    [
        "git",
        "config",
        "user.email",
        "41898282+github-actions[bot]@users.noreply.github.com",
    ],
    cwd=root,
    check=True,
)
subprocess.run(["git", "add", str(out.relative_to(root))], cwd=root, check=True)
if build == 0:
    subprocess.run(["git", "add", "src", "tests", "scripts"], cwd=root, check=True)

if subprocess.run(["git", "diff", "--cached", "--quiet"], cwd=root).returncode != 0:
    subprocess.run(
        ["git", "commit", "-m", "Record focused Chat backend validation"],
        cwd=root,
        check=True,
    )
    subprocess.run(
        ["git", "push", "origin", "HEAD:haven-continuation"],
        cwd=root,
        check=True,
    )

raise SystemExit(0 if patch == 0 and restore == 0 and build == 0 else 1)
