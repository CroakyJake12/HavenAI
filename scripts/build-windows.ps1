[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9.-]*$')]
    [string]$Runtime = 'win-x64'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $root 'artifacts'
$publish = Join-Path $artifacts $Runtime
$zip = Join-Path $artifacts ("Haven-$Runtime.zip")

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

Push-Location $root
try {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw '.NET SDK was not found. Install .NET 10 SDK 10.0.301 or a compatible later patch.'
    }

    Remove-Item $publish -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item $zip -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Path $publish -Force | Out-Null

    Invoke-DotNet -Arguments @('--info')
    Invoke-DotNet -Arguments @('restore', '.\Haven.sln')
    Invoke-DotNet -Arguments @('build', '.\Haven.sln', '-c', $Configuration, '--no-restore')
    Invoke-DotNet -Arguments @('test', '.\Haven.sln', '-c', $Configuration, '--no-build')

    # Runtime-specific assets are required before a self-contained publish that
    # uses --no-restore. Restore each executable for the requested RID explicitly.
    Invoke-DotNet -Arguments @(
        'restore',
        '.\src\Haven.Desktop\Haven.Desktop.csproj',
        '-r', $Runtime)
    Invoke-DotNet -Arguments @(
        'restore',
        '.\src\Haven.AutomationWorker\Haven.AutomationWorker.csproj',
        '-r', $Runtime)

    Invoke-DotNet -Arguments @(
        'publish',
        '.\src\Haven.Desktop\Haven.Desktop.csproj',
        '-c', $Configuration,
        '-r', $Runtime,
        '--self-contained', 'true',
        '--no-restore',
        '-o', $publish,
        '-p:PublishReadyToRun=false',
        '-p:DebugType=None',
        '-p:DebugSymbols=false')

    Invoke-DotNet -Arguments @(
        'publish',
        '.\src\Haven.AutomationWorker\Haven.AutomationWorker.csproj',
        '-c', $Configuration,
        '-r', $Runtime,
        '--self-contained', 'true',
        '--no-restore',
        '-o', $publish,
        '-p:PublishReadyToRun=false',
        '-p:DebugType=None',
        '-p:DebugSymbols=false')

    Copy-Item '.\README.md' $publish
    Copy-Item '.\docs\PASS9-VALIDATION.md' $publish
    Compress-Archive -Path (Join-Path $publish '*') -DestinationPath $zip -CompressionLevel Optimal
    Write-Host "Created $zip" -ForegroundColor Green
}
finally {
    Pop-Location
}
