[CmdletBinding()]
param(
    [switch]$SkipRelease,
    [switch]$KeepProfile
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$profile = Join-Path ([System.IO.Path]::GetTempPath()) ("haven-continuation-" + [guid]::NewGuid().ToString('N'))
$previousProfile = $env:HAVEN_DATA_DIR

try {
    Set-Location $root
    $env:HAVEN_DATA_DIR = $profile
    $env:DOTNET_NOLOGO = '1'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

    Write-Host '== Haven continuation: restore ==' -ForegroundColor Cyan
    dotnet restore .\Haven.sln

    Write-Host '== Haven continuation: Debug build ==' -ForegroundColor Cyan
    dotnet build .\Haven.sln -c Debug --no-restore

    Write-Host '== Haven continuation: Debug tests ==' -ForegroundColor Cyan
    dotnet test .\Haven.sln -c Debug --no-build

    if (-not $SkipRelease) {
        Write-Host '== Haven continuation: Release build ==' -ForegroundColor Cyan
        dotnet build .\Haven.sln -c Release --no-restore

        Write-Host '== Haven continuation: Release tests ==' -ForegroundColor Cyan
        dotnet test .\Haven.sln -c Release --no-build

        Write-Host '== Haven continuation: automation worker ==' -ForegroundColor Cyan
        dotnet build .\src\Haven.AutomationWorker\Haven.AutomationWorker.csproj -c Release --no-restore
    }

    Write-Host 'Validation commands completed successfully.' -ForegroundColor Green
    Write-Host 'Run the Windows smoke checklist in docs\HAVEN-CONTINUATION-VALIDATION.md before merging.' -ForegroundColor Yellow
}
finally {
    $env:HAVEN_DATA_DIR = $previousProfile
    if (-not $KeepProfile -and (Test-Path $profile)) {
        Remove-Item $profile -Recurse -Force -ErrorAction SilentlyContinue
    }
}
