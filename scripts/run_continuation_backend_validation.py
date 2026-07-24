from __future__ import annotations

from pathlib import Path
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]
ARTIFACTS = ROOT / "artifacts" / "backend-beta"
ARTIFACTS.mkdir(parents=True, exist_ok=True)


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
    (ARTIFACTS / f"{name}.log").write_text(result.stdout, encoding="utf-8")
    return result.returncode


patch = run("PATCH", [sys.executable, "scripts/apply_continuation_backend_pass.py"])
restore = run("RESTORE", ["dotnet", "restore", "Haven.sln"]) if patch == 0 else -1
build = (
    run("BUILD", ["dotnet", "build", "Haven.sln", "-c", "Release", "--no-restore"])
    if restore == 0
    else -1
)

tests: list[tuple[str, list[str]]] = [
    (
        "CORE-TEST",
        [
            "dotnet",
            "test",
            "tests/Haven.Core.Tests/Haven.Core.Tests.csproj",
            "-c",
            "Release",
            "--no-build",
        ],
    ),
    (
        "ROUTING-TEST",
        [
            "dotnet",
            "test",
            "tests/Haven.Infrastructure.Tests/Haven.Infrastructure.Tests.csproj",
            "-c",
            "Release",
            "--no-build",
            "--filter",
            "FullyQualifiedName~ProviderRoutingRegistrationTests",
        ],
    ),
    (
        "CALL-TEST",
        [
            "dotnet",
            "test",
            "tests/Haven.Desktop.Tests/Haven.Desktop.Tests.csproj",
            "-c",
            "Release",
            "--no-build",
            "--filter",
            "FullyQualifiedName~CallSingletonIntegrationTests|FullyQualifiedName~CallVoicePreviewControllerTests|FullyQualifiedName~ExperienceAutomationAndCallTests",
        ],
    ),
]

test_results: dict[str, int] = {}
if build == 0:
    for name, command in tests:
        test_results[name] = run(name, command)

rows = [
    "# Backend beta validation",
    "",
    "| Stage | Result |",
    "|---|---|",
    f"| Patch | {'success' if patch == 0 else 'failure'} |",
    f"| Restore | {'success' if restore == 0 else 'failure'} |",
    f"| Build | {'success' if build == 0 else 'failure'} |",
]
for name, result in test_results.items():
    rows.append(f"| {name} | {'success' if result == 0 else 'failure'} |")
(ARTIFACTS / "VALIDATION.md").write_text("\n".join(rows) + "\n", encoding="utf-8")

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
subprocess.run(["git", "add", str(ARTIFACTS.relative_to(ROOT))], cwd=ROOT, check=True)

if build == 0:
    subprocess.run(["git", "add", "scripts", "src", "tests"], cwd=ROOT, check=True)
    message = "Apply validated backend beta pass"
else:
    message = "Record backend beta diagnostics"

staged = subprocess.run(
    ["git", "diff", "--cached", "--quiet"],
    cwd=ROOT,
).returncode
if staged != 0:
    subprocess.run(["git", "commit", "-m", message], cwd=ROOT, check=True)
    subprocess.run(
        ["git", "push", "origin", "HEAD:haven-continuation"],
        cwd=ROOT,
        check=True,
    )

raise SystemExit(0 if patch == 0 and restore == 0 and build == 0 else 1)
