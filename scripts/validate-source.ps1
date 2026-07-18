# FILE DOCUMENTATION
# Where: scripts/validate-source.ps1 in the repository tooling area used by developers and continuous integration.
# What: This file automates or configures the repository operation described by its commands and keys.
# How: Read from top to bottom: inputs and environment first, validation/processing next, and explicit success or failure output last.
# Why: The file keeps one cohesive responsibility in a predictable location so callers can find and replace it without unrelated changes.
[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$required = @(
    "Haven.sln",
    "src/Haven.Desktop/App.axaml",
    "src/Haven.Desktop/MainWindow.axaml",
    "src/Haven.Infrastructure/SqliteDatabase.cs",
    "src/Haven.Infrastructure/OllamaClient.cs",
    "src/Haven.AutomationWorker/Program.cs"
)

foreach ($relative in $required) {
    if (-not (Test-Path (Join-Path $root $relative))) { throw "Missing required file: $relative" }
}

$forbidden = Get-ChildItem $root -Recurse -File | Where-Object { $_.Extension -in @(".go", ".html", ".js", ".ts") }
if ($forbidden) { throw "Hidden sidecar source found: $($forbidden.FullName -join ', ')" }

Get-ChildItem $root -Recurse -File -Include *.axaml,*.csproj,*.props | ForEach-Object {
    try { [xml](Get-Content $_.FullName -Raw) | Out-Null }
    catch { throw "Invalid XML: $($_.FullName): $($_.Exception.Message)" }
}

Write-Host "Static source validation passed." -ForegroundColor Green
