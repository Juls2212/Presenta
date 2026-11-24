<#
.SYNOPSIS
    Checks that every Spanish text quoted in the validation documents exists in the app's resource files.

.DESCRIPTION
    Reads the watch strings (wear-app/.../strings.xml) and the companion strings (UiStrings.resx), then scans the guides for
    text in straight double quotes. Text inside backticks and fenced code blocks is ignored, and so is quoted text with no
    letters in it (numbers, times, arrows). A quoted text passes when it equals a resource string (placeholders such as
    %1$s, %d and {0} match any text), or, as a "partial" match, when it is a piece of a resource string. Anything else is
    reported and the script exits with code 1.

    It only reads files.
#>
[CmdletBinding()]
param(
    [string[]]$Files = @('docs/validation/validation-guide.md', 'docs/manual-test-guide.md', 'docs/validation/smoke-test.md', 'docs/validation/accessibility-report.md', 'docs/delivery/demo-script.md')
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

function Normalize([string]$Text) { return (($Text -replace '\s+', ' ').Trim()) }

# ---- load the resources ----
$resources = New-Object System.Collections.Generic.List[string]

[xml]$watch = Get-Content -Raw -Encoding UTF8 (Join-Path $root 'wear-app/app/src/main/res/values/strings.xml')
$months = New-Object System.Collections.Generic.List[string]
foreach ($node in $watch.SelectNodes('//string | //item')) {
    $text = $node.InnerText -replace "\'", "'" -replace '\\"', '"' -replace '\n', ' '
    $resources.Add((Normalize $text))
    if ($node.ParentNode.LocalName -eq 'string-array' -and $node.ParentNode.GetAttribute('name') -eq 'month_short') { $months.Add($text) }
}

[xml]$companion = Get-Content -Raw -Encoding UTF8 (Join-Path $root 'windows-companion/src/Presenta.Companion.ViewModels/UiStrings.resx')
foreach ($data in $companion.root.data) {
    $resources.Add((Normalize $data.value))
}

$patterns = foreach ($text in $resources) {
    # A resource that is only placeholders (such as "{0}  {1}") would match any text, so it is not a pattern.
    if (($text -replace '%\d*\$?\d*[sd]|\{\d+\}', '') -notmatch '\p{L}') { continue }
    $escaped = [regex]::Escape($text)
    # Placeholders were escaped too: %1$s -> %1\$s, {0} -> \{0}
    $escaped = [regex]::Replace($escaped, '%\d*\\?\$?\d*[sd]', '.+?')
    $escaped = [regex]::Replace($escaped, '\\\{\d+\}', '.+?')
    [pscustomobject]@{ Text = $text; Regex = [regex]::new("^$escaped$", 'IgnoreCase') }
}

# History rows are built from a two-digit day and a month abbreviation ("08 Oct"), so accept that shape for the real months.
$patterns = @($patterns) + [pscustomobject]@{ Text = 'day and month'; Regex = [regex]::new('^\d{2} (' + ($months -join '|') + ')$') }

# ---- scan the documents ----
$checked = 0; $exact = 0; $partial = 0; $ignored = 0
$failures = New-Object System.Collections.Generic.List[string]

foreach ($relative in $Files) {
    $path = if ([System.IO.Path]::IsPathRooted($relative)) { $relative } else { Join-Path $root $relative }
    $inFence = $false
    $lineNumber = 0
    foreach ($line in (Get-Content -Encoding UTF8 $path)) {
        $lineNumber++
        if ($line.TrimStart().StartsWith('```')) { $inFence = -not $inFence; continue }
        if ($inFence) { continue }
        $plain = [regex]::Replace($line, '`[^`]*`', ' ')
        foreach ($match in [regex]::Matches($plain, '"([^"]+)"')) {
            $quoted = Normalize $match.Groups[1].Value
            if ($quoted -notmatch '\p{L}') { $ignored++; continue }
            $checked++
            if ($patterns | Where-Object { $_.Regex.IsMatch($quoted) } | Select-Object -First 1) { $exact++; continue }
            if ($resources | Where-Object { $_.IndexOf($quoted, [StringComparison]::OrdinalIgnoreCase) -ge 0 } | Select-Object -First 1) { $partial++; continue }
            $failures.Add("${relative}:${lineNumber}: not found in the resources: `"$quoted`"")
        }
    }
}

Write-Host "Resource strings loaded: $($resources.Count)"
Write-Host "Quoted texts checked: $checked (exact $exact, partial $partial); ignored without letters: $ignored"
if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Host $_ -ForegroundColor Red }
    Write-Host "FAILED: $($failures.Count) quoted text(s) are not in the resource files."
    exit 1
}
Write-Host 'OK: every quoted Spanish text exists in the resource files.'
exit 0
