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
