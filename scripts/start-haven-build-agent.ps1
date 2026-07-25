param(
    [ValidateRange(1024, 65535)]
    [int]$Port = 7148
)

$ErrorActionPreference = "Stop"

$repositoryRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $repositoryRoot

if ([string]::IsNullOrWhiteSpace($env:HAVEN_BUILD_AGENT_KEY)) {
    $bytes = New-Object byte[] 32
    [System.Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
    $env:HAVEN_BUILD_AGENT_KEY = [Convert]::ToHexString($bytes)

    Write-Host "Generated a temporary Haven agent key for this PowerShell session:" -ForegroundColor Yellow
    Write-Host $env:HAVEN_BUILD_AGENT_KEY -ForegroundColor Cyan
    Write-Host "Use this value as the GPT Action's X-Haven-Agent-Key secret." -ForegroundColor Yellow
}

Write-Host "Starting Haven Build Agent on http://127.0.0.1:$Port" -ForegroundColor Green
Write-Host "The loopback endpoint is not directly reachable by a GPT Action; use a secure HTTPS proxy or tunnel." -ForegroundColor DarkYellow

dotnet run --project ".\tools\Haven.BuildAgent\Haven.BuildAgent.csproj" --urls "http://127.0.0.1:$Port"
