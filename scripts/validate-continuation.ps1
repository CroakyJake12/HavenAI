# FILE DOCUMENTATION
# Where: scripts/validate-continuation.ps1 in the repository tooling area used by developers and continuous integration.
# What: This file automates or configures the repository operation described by its commands and keys.
# How: Read from top to bottom: inputs and environment first, validation/processing next, and explicit success or failure output last.
# Why: The file keeps one cohesive responsibility in a predictable location so callers can find and replace it without unrelated changes.
[CmdletBinding()]
param(
    [switch]$SkipRelease,
    [switch]$KeepProfile
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$profile = Join-Path ([System.IO.Path]::GetTempPath()) ("haven-continuation-" + [guid]::NewGuid().ToString('N'))
$previousProfile = $env:HAVEN_DATA_DIR
$locationPushed = $false

function Invoke-DotNet {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

function Clear-HavenBuildOutputs {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepositoryRoot
    )

    $fullRoot = [System.IO.Path]::GetFullPath($RepositoryRoot)
    $gitRoot = [System.IO.Path]::GetFullPath((Join-Path $fullRoot '.git'))
    $directories = Get-ChildItem -LiteralPath $fullRoot -Directory -Recurse -Force -ErrorAction Stop |
        Where-Object {
            ($_.Name -eq 'bin' -or $_.Name -eq 'obj') -and
            -not $_.FullName.StartsWith($gitRoot, [System.StringComparison]::OrdinalIgnoreCase)
        } |
        Sort-Object { $_.FullName.Length } -Descending

    foreach ($directory in $directories) {
        Write-Host "Removing stale output: $($directory.FullName)" -ForegroundColor DarkGray
        Remove-Item -LiteralPath $directory.FullName -Recurse -Force -ErrorAction Stop
    }
}

try {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw '.NET SDK was not found. Install the SDK selected by global.json.'
    }

    Push-Location $root
    $locationPushed = $true
    $env:HAVEN_DATA_DIR = $profile
    $env:DOTNET_NOLOGO = '1'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

    if (Get-Command git -ErrorAction SilentlyContinue) {
        $branch = (& git branch --show-current).Trim()
        $head = (& git rev-parse HEAD).Trim()
        Write-Host "== Haven continuation: branch $branch at $head ==" -ForegroundColor Cyan
        if ($branch -ne 'haven-continuation') {
            throw "Validation must run from haven-continuation, not '$branch'."
        }
        if ((& git status --porcelain).Count -gt 0) {
            Write-Host 'Warning: the working tree contains uncommitted changes; results are not tied solely to the printed commit.' -ForegroundColor Yellow
        }
    }

    Write-Host '== Haven continuation: stop cached build servers ==' -ForegroundColor Cyan
    Invoke-DotNet -Arguments @('build-server', 'shutdown')

    Write-Host '== Haven continuation: remove stale bin/obj outputs ==' -ForegroundColor Cyan
    Clear-HavenBuildOutputs -RepositoryRoot $root

    Write-Host '== Haven continuation: restore ==' -ForegroundColor Cyan
    Invoke-DotNet -Arguments @('restore', '.\Haven.sln', '--force-evaluate')

    Write-Host '== Haven continuation: Debug build ==' -ForegroundColor Cyan
    Invoke-DotNet -Arguments @('build', '.\Haven.sln', '-c', 'Debug', '--no-restore', '--no-incremental', '-p:UseSharedCompilation=false')

    Write-Host '== Haven continuation: Debug tests ==' -ForegroundColor Cyan
    Invoke-DotNet -Arguments @('test', '.\Haven.sln', '-c', 'Debug', '--no-build')

    Write-Host '== Haven continuation: Debug automation worker build ==' -ForegroundColor Cyan
    Invoke-DotNet -Arguments @('build', '.\src\Haven.AutomationWorker\Haven.AutomationWorker.csproj', '-c', 'Debug', '--no-restore', '--no-incremental', '-p:UseSharedCompilation=false')

    if (-not $SkipRelease) {
        Write-Host '== Haven continuation: Release build ==' -ForegroundColor Cyan
        Invoke-DotNet -Arguments @('build', '.\Haven.sln', '-c', 'Release', '--no-restore', '--no-incremental', '-p:UseSharedCompilation=false')

        Write-Host '== Haven continuation: Release tests ==' -ForegroundColor Cyan
        Invoke-DotNet -Arguments @('test', '.\Haven.sln', '-c', 'Release', '--no-build')

        Write-Host '== Haven continuation: Release automation worker build ==' -ForegroundColor Cyan
        Invoke-DotNet -Arguments @('build', '.\src\Haven.AutomationWorker\Haven.AutomationWorker.csproj', '-c', 'Release', '--no-restore', '--no-incremental', '-p:UseSharedCompilation=false')
    }

    Write-Host 'Validation commands completed successfully.' -ForegroundColor Green
    Write-Host 'Run the Windows smoke checklist in docs\HAVEN-CONTINUATION-VALIDATION.md before merging.' -ForegroundColor Yellow
}
finally {
    $env:HAVEN_DATA_DIR = $previousProfile
    if ($locationPushed) {
        Pop-Location
    }
    if (-not $KeepProfile -and (Test-Path $profile)) {
        Remove-Item $profile -Recurse -Force -ErrorAction SilentlyContinue
    }
}
