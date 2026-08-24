param(
    [Parameter(Mandatory = $true)]
    [string]$BriefPath,

    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'

$resolvedBrief = (Resolve-Path -LiteralPath $BriefPath).Path
$resolvedOutput = [System.IO.Path]::GetFullPath($OutputDirectory)
[System.IO.Directory]::CreateDirectory($resolvedOutput) | Out-Null

$lines = [System.IO.File]::ReadAllLines($resolvedBrief)
$briefName = [System.IO.Path]::GetFileName($resolvedBrief)
$briefHash = (Get-FileHash -LiteralPath $resolvedBrief -Algorithm SHA256).Hash
$indexPath = Join-Path $resolvedOutput 'GENUI_REQUIREMENT_SOURCE_INDEX.jsonl'
$metadataPath = Join-Path $resolvedOutput 'GENUI_REQUIREMENT_SOURCE_INDEX.meta.json'
$utf8NoBom = [System.Text.UTF8Encoding]::new($false)
$headingStack = New-Object 'System.Collections.Generic.List[string]'
$topLevelSection = 0
$records = 0
$substantive = 0
$insideFence = $false
$writer = [System.IO.StreamWriter]::new($indexPath, $false, $utf8NoBom)

function Get-WorkstreamId {
    param(
        [int]$Section,
        [string[]]$HeadingPath
    )

    $headings = $HeadingPath -join ' / '

    if ($Section -le 3) { return 'GENUI-00' }
    if ($Section -eq 4) { return 'GENUI-16' }
    if ($Section -eq 5) { return 'GENUI-01' }
    if ($Section -eq 6) {
        if ($headings -match 'Floating Activit') { return 'GENUI-14' }
        if ($headings -match 'Motion|Animat|Morph|Responsive|Mobile|Context Action|Long-Press|Layout Transition') { return 'GENUI-03' }
        return 'GENUI-02'
    }
    if ($Section -eq 7) { return 'GENUI-05' }
    if ($Section -ge 8 -and $Section -le 51) { return 'GENUI-12' }
    if ($Section -eq 52) { return 'GENUI-06' }
    if ($Section -eq 53) { return 'GENUI-13' }
    if ($Section -ge 54 -and $Section -le 69) { return 'GENUI-12' }
    if ($Section -ge 70 -and $Section -le 73) { return 'GENUI-08' }
    if ($Section -eq 74) { return 'GENUI-09' }
    if ($Section -eq 75) {
        if ($headings -match 'Haven Home|Launcher') { return 'GENUI-15' }
        if ($headings -match 'Header|App Panel|App Menu|Attachment') { return 'GENUI-04' }
        return 'GENUI-09'
    }
    if ($Section -ge 76 -and $Section -le 79) { return 'GENUI-13' }
    if ($Section -ge 80 -and $Section -le 84) {
        if ($headings -match 'Attach|Routing') { return 'GENUI-07' }
        if ($headings -match 'Capabilities|Instructions|Agents|Plugin|Macro|Action') { return 'GENUI-06' }
        return 'GENUI-09'
    }
    if ($Section -ge 85 -and $Section -le 87) { return 'GENUI-10' }
    if ($Section -eq 88) { return 'GENUI-09' }
    if ($Section -eq 89) { return 'GENUI-08' }
    if ($Section -eq 90) { return 'GENUI-01' }
    if ($Section -eq 91) { return 'GENUI-18' }
    if ($Section -eq 92) {
        if ($headings -match 'Haven Home|Launcher') { return 'GENUI-15' }
        if ($headings -match 'Floating Activit') { return 'GENUI-14' }
        return 'GENUI-13'
    }
    if ($Section -eq 93) { return 'GENUI-18' }
    if ($Section -eq 94) { return 'GENUI-13' }
    if ($Section -ge 95 -and $Section -le 101) { return 'GENUI-18' }
    if ($Section -eq 102) { return 'GENUI-17' }
    if ($Section -eq 103) { return 'GENUI-18' }
    return 'GENUI-00'
}

function Get-WorkstreamDependencies {
    param([string]$WorkstreamId)

    switch ($WorkstreamId) {
        'GENUI-00' { return @() }
        'GENUI-01' { return @('GENUI-00') }
        'GENUI-02' { return @('GENUI-00', 'GENUI-01') }
        'GENUI-03' { return @('GENUI-02') }
        'GENUI-04' { return @('GENUI-01', 'GENUI-02') }
        'GENUI-05' { return @('GENUI-02', 'GENUI-04') }
        'GENUI-06' { return @('GENUI-01', 'GENUI-02') }
        'GENUI-07' { return @('GENUI-04', 'GENUI-06') }
        'GENUI-08' { return @('GENUI-02', 'GENUI-06') }
        'GENUI-09' { return @('GENUI-06', 'GENUI-08') }
        'GENUI-10' { return @('GENUI-08', 'GENUI-09') }
        'GENUI-11' { return @('GENUI-08', 'GENUI-10') }
        'GENUI-12' { return @('GENUI-05', 'GENUI-06') }
        'GENUI-13' { return @('GENUI-06', 'GENUI-08') }
        'GENUI-14' { return @('GENUI-02', 'GENUI-03') }
        'GENUI-15' { return @('GENUI-04', 'GENUI-05', 'GENUI-13') }
        'GENUI-16' { return @('GENUI-01', 'GENUI-02', 'GENUI-03', 'GENUI-04', 'GENUI-05', 'GENUI-06', 'GENUI-07', 'GENUI-08', 'GENUI-09', 'GENUI-10', 'GENUI-11', 'GENUI-12', 'GENUI-13', 'GENUI-14', 'GENUI-15') }
        'GENUI-17' { return @('GENUI-00') }
        'GENUI-18' { return @('GENUI-01', 'GENUI-02', 'GENUI-03', 'GENUI-04', 'GENUI-05', 'GENUI-06', 'GENUI-07', 'GENUI-08', 'GENUI-09', 'GENUI-10', 'GENUI-11', 'GENUI-12', 'GENUI-13', 'GENUI-14', 'GENUI-15', 'GENUI-16', 'GENUI-17') }
        default { throw "Unknown workstream: $WorkstreamId" }
    }
}

try {
    for ($index = 0; $index -lt $lines.Length; $index++) {
        $lineNumber = $index + 1
        $text = $lines[$index]

        if ([string]::IsNullOrWhiteSpace($text)) {
            continue
        }

        $kind = 'prose'
        $isSubstantive = $true

        if ($text -match '^(#{1,6})\s+(.+?)\s*$') {
            $level = $matches[1].Length
            $headingText = $matches[2]
            while ($headingStack.Count -ge $level) {
                $headingStack.RemoveAt($headingStack.Count - 1)
            }
            $headingStack.Add($headingText)
            if ($level -eq 1 -and $headingText -match '^(\d+)\.') {
                $topLevelSection = [int]$matches[1]
            }
            $kind = 'heading'
            $isSubstantive = $false
        }
        elseif ($text -match '^\s*```') {
            $insideFence = -not $insideFence
            $kind = 'fence'
            $isSubstantive = $false
        }
        elseif ($insideFence) {
            $kind = 'code'
        }
        elseif ($text -match '^\s*[-*+]\s+') {
            $kind = 'bullet'
        }
        elseif ($text -match '^\s*\d+[.)]\s+') {
            $kind = 'numbered-item'
        }
        elseif ($text -match '^\s*>') {
            $kind = 'blockquote'
        }
        elseif ($text -match '^\s*\|') {
            $kind = 'table-row'
            if ($text -match '^\s*\|(?:\s*:?-+:?\s*\|)+\s*$') {
                $isSubstantive = $false
            }
        }
        elseif ($text -match '^\s*(?:---+|___+|\*\*\*+)\s*$') {
            $kind = 'separator'
            $isSubstantive = $false
        }

        $workstreamId = Get-WorkstreamId -Section $topLevelSection -HeadingPath @($headingStack)
        $record = [ordered]@{
            id = 'BRIEF-L{0:D5}' -f $lineNumber
            source = $briefName
            lineStart = $lineNumber
            lineEnd = $lineNumber
            headingPath = @($headingStack)
            kind = $kind
            substantive = $isSubstantive
            workstream = $workstreamId
            dependencies = @(Get-WorkstreamDependencies -WorkstreamId $workstreamId)
            status = if ($isSubstantive) { 'Not started' } else { 'Reference' }
            text = $text
        }

        $writer.WriteLine(($record | ConvertTo-Json -Compress -Depth 8))
        $records++
        if ($isSubstantive) {
            $substantive++
        }
    }
}
finally {
    $writer.Dispose()
}

$indexHash = (Get-FileHash -LiteralPath $indexPath -Algorithm SHA256).Hash
$metadata = [ordered]@{
    schemaVersion = 1
    authoritativeSource = $briefName
    authoritativeSourceSha256 = $briefHash
    sourceBytes = (Get-Item -LiteralPath $resolvedBrief).Length
    sourceLineCount = $lines.Length
    sourceNonEmptyLineCount = ($lines | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }).Count
    indexedRecordCount = $records
    indexedSubstantiveRecordCount = $substantive
    indexSha256 = $indexHash
    status = 'Initial source capture; implementation mapping and evidence are maintained in the release ledger.'
}

[System.IO.File]::WriteAllText(
    $metadataPath,
    (($metadata | ConvertTo-Json -Depth 6) + [Environment]::NewLine),
    $utf8NoBom)

Write-Output "Wrote $records records ($substantive substantive) to $indexPath"
Write-Output "Source SHA256: $briefHash"
Write-Output "Index SHA256:  $indexHash"
