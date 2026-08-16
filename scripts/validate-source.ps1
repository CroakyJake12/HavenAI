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
    "src/Haven.Desktop/Views/Pages/Chat/NewChatPage.cs",
    "src/Haven.Infrastructure/Database/SqliteDatabase.cs",
    "src/Haven.Infrastructure/Providers/OllamaClient.cs",
    "src/Haven.Desktop/Program.cs"
)

foreach ($relative in $required) {
    if (-not (Test-Path (Join-Path $root $relative))) { throw "Missing required file: $relative" }
}

$retiredChatSurface = @(
    "src/Haven.Desktop/Views/Pages/Chat/NewChatPage.axaml",
    "src/Haven.Desktop/Views/Pages/Chat/NewChatPage.axaml.cs"
)
foreach ($relative in $retiredChatSurface) {
    if (Test-Path (Join-Path $root $relative)) { throw "Retired Chat surface returned to active source: $relative" }
}

$chatRollback = @(
    "migration-rollback/2026-08-16-chat-current-base/desktop/NewChatPage.axaml",
    "migration-rollback/2026-08-16-chat-current-base/desktop/NewChatPage.axaml.cs",
    "migration-rollback/2026-08-16-chat-current-base/desktop/NativeChatSidebar.cs"
)
foreach ($relative in $chatRollback) {
    if (-not (Test-Path (Join-Path $root $relative))) { throw "Missing Chat migration rollback file: $relative" }
}

$activeChatSidebar = Join-Path $root "src/Haven.Desktop/Interface/Shell/NativePresentation/NativeChatSidebar.cs"
$activeChatSidebarSource = Get-Content $activeChatSidebar -Raw
$forbiddenChatSidebarVisuals = @(
    "new TextBox", "new TextBlock", "new StackPanel", "new Grid", "new ScrollViewer",
    "new ContextMenu", "new HavenContextMenu", "new Flyout", "new HavenAdaptivePopup", "new MenuItem", "new HavenMenuItem"
)
foreach ($token in $forbiddenChatSidebarVisuals) {
    if ($activeChatSidebarSource.IndexOf($token, [StringComparison]::Ordinal) -ge 0) {
        throw "Migrated Chat sidebar returned to ordinary Avalonia visual construction: $token"
    }
}

$forbidden = Get-ChildItem $root -Recurse -File | Where-Object { $_.Extension -in @(".go", ".html", ".js", ".ts") }
if ($forbidden) { throw "Hidden sidecar source found: $($forbidden.FullName -join ', ')" }

Get-ChildItem $root -Recurse -File -Include *.axaml,*.csproj,*.props |
    Where-Object { $_.FullName -notmatch '\\(?:bin|obj)(?:-[^\\]+)?\\' } |
    ForEach-Object {
    $candidate = $_
    try { [xml](Get-Content $candidate.FullName -Raw) | Out-Null }
    catch { throw "Invalid XML: $($candidate.FullName): $($_.Exception.Message)" }
}

Write-Host "Static source validation passed." -ForegroundColor Green
