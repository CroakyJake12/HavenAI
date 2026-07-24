from __future__ import annotations

import gzip
import hashlib
from pathlib import Path
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "artifacts" / "final-codebehind-ui"
OUT.mkdir(parents=True, exist_ok=True)

EXPECTED = [
    (7000, "9e98fc4560f229074fa459b94a4e278ea8742b43b5bbbbfa2a62f09dc1ebcd98"),
    (7000, "8491bbd5e7c339b07c68d5ffa4dac7bfc6abfcb7fb81e353c50cec155f537023"),
    (7000, "c4e45e343a61aa9118f7d2781cbbc83257885acbf93b5d41820fe2c33481424a"),
    (4784, "403c3c28c5afd1f54b9fbbf679b53e270a2112efb0ec849d46885252cd8a4c99"),
]

def run(name: str, args: list[str]) -> int:
    with (OUT / f"{name}.log").open("w", encoding="utf-8", errors="replace") as log:
        result = subprocess.run(
            args,
            cwd=ROOT,
            stdout=log,
            stderr=subprocess.STDOUT,
            text=True,
            encoding="utf-8",
            errors="replace",
        )
    return result.returncode

parts: list[bytes] = []
for index, (expected_size, expected_hash) in enumerate(EXPECTED):
    path = ROOT / "scripts" / f"apply_final_codebehind_ui_pass.py.gz.part{index}"
    data = path.read_bytes()
    actual_hash = hashlib.sha256(data).hexdigest()
    if len(data) != expected_size or actual_hash != expected_hash:
        raise RuntimeError(
            f"Bundle part {index} failed integrity validation: "
            f"size={len(data)} hash={actual_hash}"
        )
    parts.append(data)

patch_file = ROOT / "scripts" / "apply_final_codebehind_ui_pass.py"
patch_file.write_bytes(gzip.decompress(b"".join(parts)))
try:
    patch = run("PATCH", [sys.executable, str(patch_file)])
finally:
    patch_file.unlink(missing_ok=True)


if patch == 0:
    corrective_files = [
        ROOT / "src/Haven.Desktop/Views/Pages/WorkspaceHome/WorkspaceHomePage.axaml.cs",
        ROOT / "src/Haven.Desktop/Views/Pages/ProjectCreator/ProjectCreatorPage.GitHub.cs",
        ROOT / "src/Haven.Desktop/Views/Pages/Chat/ChatPage.CodeBehindLayout.cs",
    ]
    for source in corrective_files:
        text = source.read_text(encoding="utf-8")
        text = text.replace("Watermark =", "PlaceholderText =")
        if source.name == "WorkspaceHomePage.axaml.cs" and "using Avalonia.Controls.Primitives;" not in text:
            text = text.replace(
                "using Avalonia.Controls;\n",
                "using Avalonia.Controls;\nusing Avalonia.Controls.Primitives;\n",
                1,
            )
        source.write_text(text, encoding="utf-8", newline="\n")

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

def status(value: int) -> str:
    return "success" if value == 0 else "failure"

(OUT / "VALIDATION.md").write_text(
    "# Final code-behind UI validation\n\n"
    "| Stage | Result |\n|---|---|\n"
    f"| Transformation | {status(patch)} |\n"
    f"| Restore | {status(restore)} |\n"
    f"| Release build | {status(build)} |\n"
    f"| Core tests | {status(test)} |\n",
    encoding="utf-8",
)

def git(*args: str, check: bool = True) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        ["git", *args],
        cwd=ROOT,
        check=check,
        text=True,
        encoding="utf-8",
        errors="replace",
    )

git("config", "user.name", "github-actions[bot]")
git(
    "config",
    "user.email",
    "41898282+github-actions[bot]@users.noreply.github.com",
)

success = patch == 0 and restore == 0 and build == 0 and test == 0
temporary_paths = [
    *(f"scripts/apply_final_codebehind_ui_pass.py.gz.part{i}" for i in range(4)),
    "scripts/run_final_codebehind_ui_validation.py",
    ".github/workflows/run-final-codebehind-ui-validation.yml",
    ".github/workflows/apply-final-codebehind-ui-pass.yml",
    "artifacts/codebehind-ui-contracts",
]

if success:
    git("add", "src")
    git("rm", "-r", "-f", "--ignore-unmatch", *temporary_paths)
    git("add", str(OUT.relative_to(ROOT)))
    message = "Apply final code-behind UI pass"
else:
    git("restore", "--worktree", "src", check=False)
    git("clean", "-fd", "src", check=False)
    git("add", str(OUT.relative_to(ROOT)))
    message = "Record final code-behind UI validation failure"

diff = git("diff", "--cached", "--quiet", check=False)
if diff.returncode != 0:
    git("commit", "-m", message)
    git("fetch", "origin", "haven-continuation")
    git("rebase", "origin/haven-continuation")
    git("push", "origin", "HEAD:haven-continuation")

raise SystemExit(0 if success else 1)
