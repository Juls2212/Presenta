<#
.SYNOPSIS
    Starts the companion with the settings of the project's ".env" file.

.DESCRIPTION
    Reads ".env" from the project root (simple KEY=VALUE lines), sets the PRESENTA_* values as environment variables of this
    process only, and runs the companion. Nothing is written to any file and no value is ever printed: only the names that
    were loaded and whether the AI key is set.

    Rules for ".env":
      - Blank lines and lines starting with # are ignored. A leading "export " is accepted.
      - The name is everything before the first "=", the value everything after it, both trimmed.
      - One pair of matching surrounding quotes (single or double) is removed. Comments after a value are not supported.
      - A line with no "=", or with a name that is not letters, digits and underscores, is reported by line number and skipped.
      - Only names starting with PRESENTA_ are used; others are ignored so this file cannot change PATH or similar.
      - An empty value leaves the variable unset.

    Copy ".env.example" to ".env" first and fill in the key.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

function Read-DotEnv {
    # Returns @{ Values = ordered hashtable; Problems = list of "line N: reason" }. Values are never logged by this function.
    param([Parameter(Mandatory)][string]$Path)

    $values = [ordered]@{}
    $problems = New-Object System.Collections.Generic.List[string]
    $number = 0
    foreach ($raw in [System.IO.File]::ReadAllLines($Path)) {
        $number++
        $line = $raw.TrimStart([char]0xFEFF).Trim()
        if ($line.Length -eq 0 -or $line.StartsWith('#')) { continue }
        if ($line -match '^export\s+') { $line = $line -replace '^export\s+', '' }

        $equals = $line.IndexOf('=')
        if ($equals -lt 1) { $problems.Add("line ${number}: expected NAME=VALUE"); continue }

        $name = $line.Substring(0, $equals).Trim()
        $value = $line.Substring($equals + 1).Trim()
        if ($name -notmatch '^[A-Za-z_][A-Za-z0-9_]*$') { $problems.Add("line ${number}: invalid variable name"); continue }
        if ($value.Length -ge 2 -and ($value[0] -eq '"' -or $value[0] -eq "'") -and $value[$value.Length - 1] -eq $value[0]) {
            $value = $value.Substring(1, $value.Length - 2)
        }

        $values[$name] = $value
    }

    return @{ Values = $values; Problems = $problems }
}

# Dot-sourcing this file only defines Read-DotEnv (used to test the parser); nothing below runs then.
if ($MyInvocation.InvocationName -ne '.') {

    $root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
    $envFile = Join-Path $root '.env'
    if (-not (Test-Path -LiteralPath $envFile -PathType Leaf)) {
        Write-Error "The file '.env' was not found in the project root. Copy '.env.example' to '.env', fill in PRESENTA_AI_API_KEY, and run this script again."
        exit 1
    }

    $parsed = Read-DotEnv -Path $envFile
    foreach ($problem in $parsed.Problems) { Write-Warning "Skipped $problem" }

    $loaded = New-Object System.Collections.Generic.List[string]
    $ignored = New-Object System.Collections.Generic.List[string]
    foreach ($name in $parsed.Values.Keys) {
        $value = [string]$parsed.Values[$name]
        if (-not $name.StartsWith('PRESENTA_')) { $ignored.Add($name); continue }
        if ($value.Length -eq 0) { continue }
        [System.Environment]::SetEnvironmentVariable($name, $value, 'Process')
        $loaded.Add($name)
    }

    if ($loaded.Count -gt 0) { Write-Host ('Loaded from .env: ' + ($loaded -join ', ')) } else { Write-Host 'Loaded from .env: nothing' }
    if ($ignored.Count -gt 0) { Write-Host ('Ignored (not PRESENTA_*): ' + ($ignored -join ', ')) }
    $keySet = -not [string]::IsNullOrEmpty([System.Environment]::GetEnvironmentVariable('PRESENTA_AI_API_KEY', 'Process'))
    Write-Host ('PRESENTA_AI_API_KEY is ' + $(if ($keySet) { 'set' } else { 'NOT set (the AI will be unavailable)' }))

    & dotnet run --project (Join-Path $root 'windows-companion/src/Presenta.Companion.App')
    exit $LASTEXITCODE
}
