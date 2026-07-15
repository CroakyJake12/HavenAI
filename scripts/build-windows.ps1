[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $root "artifacts"
$publish = Join-Path $artifacts $Runtime
$zip = Join-Path $artifacts "Haven-windows-x64.zip"

Push-Location $root
try {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw ".NET SDK was not found. Install .NET 10 SDK 10.0.301 or a compatible later patch."
    }

    Remove-Item $publish -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item $zip -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Path $publish -Force | Out-Null

    dotnet --info
    dotnet restore .\Haven.sln
    dotnet build .\Haven.sln -c $Configuration --no-restore
    dotnet test .\Haven.sln -c $Configuration --no-build

    dotnet publish .\src\Haven.Desktop\Haven.Desktop.csproj `
        -c $Configuration -r $Runtime --self-contained true --no-restore `
        -o $publish -p:PublishReadyToRun=false -p:DebugType=None -p:DebugSymbols=false

    dotnet publish .\src\Haven.AutomationWorker\Haven.AutomationWorker.csproj `
        -c $Configuration -r $Runtime --self-contained true --no-restore `
        -o $publish -p:PublishReadyToRun=false -p:DebugType=None -p:DebugSymbols=false

    Copy-Item .\README.md $publish
    Copy-Item .\docs\PASS9-VALIDATION.md $publish
    Compress-Archive -Path (Join-Path $publish "*") -DestinationPath $zip -CompressionLevel Optimal
    Write-Host "Created $zip" -ForegroundColor Green
}
finally {
    Pop-Location
}
