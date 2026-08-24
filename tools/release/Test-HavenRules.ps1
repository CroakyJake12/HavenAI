$ErrorActionPreference = 'Stop'
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$requiredFiles = @(
    'AGENTS.md',
    'HAVEN_UI_RULES.md',
    'docs\ARCHITECTURE_RULES.md',
    'docs\SECURITY_RULES.md',
    'docs\PLATFORM_RULES.md',
    'docs\GENUI_RULES.md',
    'docs\BACKGROUND_LEARNING_RULES.md',
    'docs\AGENT_TOOL_EXECUTION_RULES.md',
    'docs\VALIDATION_RULES.md',
    'docs\releases\generative-ui-2026-08-08\GENUI_RELEASE_SUCCESS_RUBRIC.md',
    'docs\releases\generative-ui-2026-08-08\GENUI_REQUIREMENT_SOURCE_INDEX.meta.json',
    'docs\releases\generative-ui-2026-08-08\GENUI_REQUIREMENT_SOURCE_INDEX.jsonl'
)
$errors = New-Object 'System.Collections.Generic.List[string]'

foreach ($relativePath in $requiredFiles) {
    $path = Join-Path $repositoryRoot $relativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        $errors.Add("Missing mandatory rule/release file: $relativePath")
    }
}

if ($errors.Count -eq 0) {
    $agents = Get-Content -LiteralPath (Join-Path $repositoryRoot 'AGENTS.md') -Raw
    foreach ($reference in $requiredFiles | Where-Object { $_ -match 'RULES\.md$|HAVEN_UI_RULES\.md$' }) {
        $portable = $reference -replace '\\', '/'
        if ($agents -notmatch [regex]::Escape($portable)) {
            $errors.Add("AGENTS.md does not reference $portable")
        }
    }

    $uiRules = Get-Content -LiteralPath (Join-Path $repositoryRoot 'HAVEN_UI_RULES.md') -Raw
    foreach ($requiredPhrase in @('Theme Modification Lock', 'Montserrat', 'Super Bright', 'Super Dark', 'Floating Activities')) {
        if ($uiRules -notmatch [regex]::Escape($requiredPhrase)) {
            $errors.Add("HAVEN_UI_RULES.md is missing: $requiredPhrase")
        }
    }

    $defaultTheme = Get-Content -LiteralPath (Join-Path $repositoryRoot 'src\Haven.Desktop\Styles\DefaultTheme.axaml') -Raw
    foreach ($requiredTypographyContract in @(
        'avares://Haven/Assets/Fonts/MontserratStatic#Montserrat',
        '<Style Selector="TemplatedControl">',
        '<Setter Property="FontWeight" Value="SemiBold" />',
        '<Style Selector="Button">',
        '<Setter Property="FontWeight" Value="SemiBold" />'
    )) {
        if ($defaultTheme -notmatch [regex]::Escape($requiredTypographyContract)) {
            $errors.Add("DefaultTheme.axaml is missing typography contract: $requiredTypographyContract")
        }
    }

    $androidProject = Get-Content -LiteralPath (Join-Path $repositoryRoot 'src\Haven.Android\Haven.Android.csproj') -Raw
    foreach ($androidFont in @('Montserrat-Medium.ttf', 'Montserrat-SemiBold.ttf', 'Montserrat-ExtraBold.ttf')) {
        if ($androidProject -notmatch [regex]::Escape($androidFont)) {
            $errors.Add("Haven.Android.csproj does not package $androidFont")
        }
    }
    $androidTypographyPath = Join-Path $repositoryRoot 'src\Haven.Android\AndroidTypography.cs'
    if (-not (Test-Path -LiteralPath $androidTypographyPath -PathType Leaf)) {
        $errors.Add('Missing native Android Montserrat enforcement: src/Haven.Android/AndroidTypography.cs')
    }

    $forbiddenFontWeightHits = Get-ChildItem -LiteralPath (Join-Path $repositoryRoot 'src') -Recurse -File |
        Where-Object { $_.Extension -in @('.cs', '.axaml') -and $_.FullName -notmatch '\\(bin|obj)\\' } |
        Select-String -Pattern 'FontWeight\s*=\s*"(?:Thin|ExtraLight|Light)"|FontWeight\.(?:Thin|ExtraLight|Light)' -CaseSensitive:$false
    if ($forbiddenFontWeightHits) {
        foreach ($hit in $forbiddenFontWeightHits) {
            $relative = [System.IO.Path]::GetRelativePath($repositoryRoot, $hit.Path)
            $errors.Add("Forbidden light UI font weight at ${relative}:$($hit.LineNumber)")
        }
    }

    $releaseDirectory = Join-Path $repositoryRoot 'docs\releases\generative-ui-2026-08-08'
    $metadata = Get-Content -LiteralPath (Join-Path $releaseDirectory 'GENUI_REQUIREMENT_SOURCE_INDEX.meta.json') -Raw | ConvertFrom-Json
    $indexPath = Join-Path $releaseDirectory 'GENUI_REQUIREMENT_SOURCE_INDEX.jsonl'
    $actualCount = (Get-Content -LiteralPath $indexPath).Count
    $actualHash = (Get-FileHash -LiteralPath $indexPath -Algorithm SHA256).Hash
    if ($actualCount -ne $metadata.indexedRecordCount) {
        $errors.Add("Requirement index count mismatch: expected $($metadata.indexedRecordCount), got $actualCount")
    }
    if ($actualHash -ne $metadata.indexSha256) {
        $errors.Add("Requirement index hash mismatch: expected $($metadata.indexSha256), got $actualHash")
    }
}

if ($errors.Count -gt 0) {
    $errors | ForEach-Object { Write-Error $_ }
    exit 1
}

Write-Output "Haven mandatory rule paths, Montserrat contracts, and release-index integrity are valid."
