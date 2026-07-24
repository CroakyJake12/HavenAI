from pathlib import Path
import gzip, os, subprocess, sys

root = Path(__file__).resolve().parents[1]
out = root / "artifacts" / "final-codebehind-ui"
out.mkdir(parents=True, exist_ok=True)

def run(name, args):
    with (out / f"{name}.log").open("w", encoding="utf-8", errors="replace") as log:
        return subprocess.run(args, cwd=root, stdout=log, stderr=subprocess.STDOUT, text=True, encoding="utf-8", errors="replace").returncode

archive = b"".join((root / "scripts" / f"apply_final_codebehind_ui_pass.py.gz.part{i}").read_bytes() for i in range(4))
patch_file = Path(os.environ.get("RUNNER_TEMP", str(out))) / "apply_final_codebehind_ui_pass.py"
patch_file.write_bytes(gzip.decompress(archive))

patch = run("PATCH", [sys.executable, str(patch_file)])
restore = run("RESTORE", ["dotnet", "restore", "Haven.sln"]) if patch == 0 else -1
build = run("BUILD", ["dotnet", "build", "Haven.sln", "-c", "Release", "--no-restore"]) if restore == 0 else -1
test = run("TEST", ["dotnet", "test", "tests/Haven.Core.Tests/Haven.Core.Tests.csproj", "-c", "Release", "--no-build"]) if build == 0 else -1
ok = patch == restore == build == test == 0
status = lambda value: "success" if value == 0 else "failure"
(out / "VALIDATION.md").write_text(
    "# Final code-behind UI validation\n\n"
    "| Stage | Result |\n|---|---|\n"
    f"| Transformation | {status(patch)} |\n"
    f"| Restore | {status(restore)} |\n"
    f"| Release build | {status(build)} |\n"
    f"| Core tests | {status(test)} |\n",
    encoding="utf-8")

def git(*args, check=True):
    return subprocess.run(["git", *args], cwd=root, check=check)

git("config", "user.name", "github-actions[bot]")
git("config", "user.email", "41898282+github-actions[bot]@users.noreply.github.com")

temp = [f"scripts/apply_final_codebehind_ui_pass.py.gz.part{i}" for i in range(4)] + [
    "scripts/extract_codebehind_ui_contracts.py",
    "scripts/run_final_codebehind_ui_validation.py",
    ".github/workflows/extract-codebehind-ui-contracts.yml",
    ".github/workflows/apply-final-codebehind-ui-pass.yml",
    ".github/workflows/run-final-codebehind-ui-validation.yml",
]
if ok:
    git("add", "src")
    git("rm", "-f", "--ignore-unmatch", *temp)
    git("rm", "-r", "-f", "--ignore-unmatch", "artifacts/codebehind-ui-contracts")
    message = "Apply final code-behind UI pass"
else:
    git("restore", "--worktree", "src", check=False)
    git("clean", "-fd", "src", check=False)
    git("add", str(out.relative_to(root)))
    message = "Record final code-behind UI validation failure"

git("commit", "-m", message)
git("fetch", "origin", "haven-continuation")
git("rebase", "origin/haven-continuation")
git("push", "origin", "HEAD:haven-continuation")
raise SystemExit(0 if ok else 1)
