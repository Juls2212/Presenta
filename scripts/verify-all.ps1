<#
.SYNOPSIS
    Runs every automated check of the project in order and writes a summary to docs/validation/automated-run-latest.md.

.DESCRIPTION
    Steps, in order:
      0. check that the Spanish text quoted in the guides exists in the resource files
      1. dotnet build of the companion solution with warnings as errors
      2. dotnet test of the companion solution
      3. watch unit tests (Gradle testDebugUnitTest)
      4. watch lint (Gradle lintDebug)
      5. watch debug build (Gradle assembleDebug)
      6. optional, -CrossStack: the real watch client against the real companion server (Gradle crossStackTest)
      7. optional, -Desktop: the desktop tests, which open small helper windows and press real arrow keys into them

    The script stops at the first failing step unless -ContinueOnError is given. It changes no source file, does not touch
    Git, does not start the emulator, and uses no network beyond what Gradle and NuGet already use. The only file it writes
    is docs/validation/automated-run-latest.md.

.PARAMETER ContinueOnError
    Keep going after a failing step instead of stopping.

.PARAMETER CrossStack
    Also run the opt-in cross-stack tests (needs the .NET SDK, which the first step already requires). They start the
    developer host as a helper process and close it again.

.PARAMETER Desktop
    Also run the opt-in desktop tests. Small helper windows appear for a few seconds and arrow keys are pressed into them,
    so do not type in another program while this step runs. Not run by default.

.EXAMPLE
    ./scripts/verify-all.ps1
    ./scripts/verify-all.ps1 -CrossStack
    ./scripts/verify-all.ps1 -CrossStack -Desktop -ContinueOnError
#>
[CmdletBinding()]
param(
    [switch]$ContinueOnError,
    [switch]$CrossStack,
    [switch]$Desktop
)

$ErrorActionPreference = 'Continue'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$solution = Join-Path $root 'windows-companion/Presenta.Companion.sln'
$watchDir = Join-Path $root 'wear-app'
$gradle = Join-Path $watchDir 'gradlew.bat'
$watchResults = Join-Path $watchDir 'app/build/test-results/testDebugUnitTest'
$reportPath = Join-Path $root 'docs/validation/automated-run-latest.md'

$results = New-Object System.Collections.Generic.List[object]
$timingLines = New-Object System.Collections.Generic.List[string]
$stopped = $false

function Add-Result([string]$Step, [string]$Outcome, [TimeSpan]$Duration, [string]$Counts) {
    $results.Add([pscustomobject]@{ Step = $Step; Result = $Outcome; Duration = ('{0:n0} s' -f $Duration.TotalSeconds); Counts = $Counts })
}

# Runs a native command, echoing its output, and returns the exit code and the captured text.
function Invoke-Native([string]$Exe, [string[]]$Arguments) {
    $lines = New-Object System.Collections.Generic.List[string]
    & $Exe @Arguments 2>&1 | ForEach-Object {
        $text = "$_"
        $lines.Add($text)
        Write-Host $text
    }
    return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Text = ($lines -join "`n") }
}

# Sums the xUnit summary lines. The console language of the .NET SDK may be English or Spanish.
function Get-DotnetCounts([string]$Text) {
    $passed = 0; $failed = 0; $skipped = 0
    foreach ($m in [regex]::Matches($Text, '(?:Passed|Superado):\s+(\d+)')) { $passed += [int]$m.Groups[1].Value }
    foreach ($m in [regex]::Matches($Text, '(?:Failed|Con error):\s+(\d+)')) { $failed += [int]$m.Groups[1].Value }
    foreach ($m in [regex]::Matches($Text, '(?:Skipped|Omitido):\s+(\d+)')) { $skipped += [int]$m.Groups[1].Value }
    return "passed $passed, failed $failed, skipped $skipped"
}

# Counts the JUnit result files that match a name filter.
function Get-WatchCounts([string]$Filter) {
    $tests = 0; $failed = 0; $skipped = 0
    Get-ChildItem -Path $watchResults -Filter $Filter -ErrorAction SilentlyContinue | ForEach-Object {
        $suite = ([xml](Get-Content $_.FullName -Raw)).testsuite
        $tests += [int]$suite.tests; $failed += [int]$suite.failures + [int]$suite.errors; $skipped += [int]$suite.skipped
    }
    return [pscustomobject]@{ Tests = $tests; Failed = $failed; Skipped = $skipped }
}

function Invoke-Step([string]$Name, [scriptblock]$Body) {
    if ($script:stopped) {
        Add-Result $Name 'NOT RUN' ([TimeSpan]::Zero) 'an earlier step failed'
        return
    }
    Write-Host ''
    Write-Host "=== $Name ===" -ForegroundColor Cyan
    $clock = [System.Diagnostics.Stopwatch]::StartNew()
    $outcome = & $Body
    $clock.Stop()
    Add-Result $Name $(if ($outcome.Ok) { 'PASS' } else { 'FAIL' }) $clock.Elapsed $outcome.Counts
    if (-not $outcome.Ok) {
        Write-Host "STEP FAILED: $Name" -ForegroundColor Red
        if (-not $ContinueOnError) {
            Write-Host 'Stopping here. Use -ContinueOnError to run the remaining steps anyway.' -ForegroundColor Red
            $script:stopped = $true
        }
    }
}

Invoke-Step 'Guide strings exist in the resources' {
    $run = Invoke-Native 'powershell' @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $PSScriptRoot 'check-guide-strings.ps1'))
    $summary = if ($run.Text -match 'Quoted texts checked: (\d+)') { "checked $($Matches[1])" } else { '' }
    [pscustomobject]@{ Ok = ($run.ExitCode -eq 0); Counts = $summary }
}

Invoke-Step 'Companion build (warnings as errors)' {
    $run = Invoke-Native 'dotnet' @('build', $solution, '-warnaserror', '--nologo', '-v', 'q')
    $warnings = if ($run.Text -match '(\d+)\s+(?:Warning|Advertencia)') { $Matches[1] } else { '?' }
    [pscustomobject]@{ Ok = ($run.ExitCode -eq 0); Counts = "warnings $warnings" }
}

Invoke-Step 'Companion tests' {
    $run = Invoke-Native 'dotnet' @('test', $solution, '--nologo', '-v', 'q')
    [pscustomobject]@{ Ok = ($run.ExitCode -eq 0); Counts = (Get-DotnetCounts $run.Text) }
}

Invoke-Step 'Watch unit tests' {
    $run = Invoke-Native $gradle @('-p', $watchDir, '--no-build-cache', 'cleanTestDebugUnitTest', 'testDebugUnitTest', '--console=plain', '-q')
    $c = Get-WatchCounts '*.xml'
    [pscustomobject]@{ Ok = ($run.ExitCode -eq 0 -and $c.Failed -eq 0); Counts = "passed $($c.Tests - $c.Failed - $c.Skipped), failed $($c.Failed), skipped $($c.Skipped)" }
}

Invoke-Step 'Watch lint' {
    $run = Invoke-Native $gradle @('-p', $watchDir, 'lintDebug', '--console=plain', '-q')
    $text = $run.Text
    $summary = if ($text -match 'Lint found (\d+) errors?') { "errors $($Matches[1])" } else { 'errors 0' }
    [pscustomobject]@{ Ok = ($run.ExitCode -eq 0); Counts = $summary }
}

Invoke-Step 'Watch debug build' {
    $run = Invoke-Native $gradle @('-p', $watchDir, 'assembleDebug', '--console=plain', '-q')
    [pscustomobject]@{ Ok = ($run.ExitCode -eq 0); Counts = '' }
}

if ($CrossStack) {
    Invoke-Step 'Cross-stack tests (real client, real server)' {
        $run = Invoke-Native $gradle @('-p', $watchDir, '--no-build-cache', 'cleanTestDebugUnitTest', 'crossStackTest', '--console=plain', '-q')
        $c = Get-WatchCounts '*CrossStack*.xml'
        Get-ChildItem -Path $watchResults -Filter '*CrossStack*.xml' -ErrorAction SilentlyContinue | ForEach-Object {
            Select-String -Path $_.FullName -Pattern '^\s*(?:<system-out><!\[CDATA\[)?(TIMING .*)$' | ForEach-Object {
                $script:timingLines.Add($_.Matches[0].Groups[1].Value.Trim())
            }
        }
        [pscustomobject]@{ Ok = ($run.ExitCode -eq 0 -and $c.Failed -eq 0 -and $c.Tests -gt 0); Counts = "passed $($c.Tests - $c.Failed - $c.Skipped), failed $($c.Failed), skipped $($c.Skipped)" }
    }
}
else {
    Add-Result 'Cross-stack tests' 'SKIPPED' ([TimeSpan]::Zero) 'pass -CrossStack to run'
}

if ($Desktop) {
    Invoke-Step 'Desktop tests (real helper windows)' {
        Write-Host 'WARNING: small helper windows will appear for a few seconds and arrow keys will be pressed into them.' -ForegroundColor Yellow
        Write-Host 'Do not type in another program until this step ends. Press Ctrl+C within 5 seconds to cancel.' -ForegroundColor Yellow
        Start-Sleep -Seconds 5
        $env:PRESENTA_DESKTOP_TESTS = '1'
        try {
            $run = Invoke-Native 'dotnet' @('test', (Join-Path $root 'windows-companion/tests/Presenta.Desktop.Tests'), '--nologo', '-v', 'q')
        }
        finally {
            Remove-Item Env:PRESENTA_DESKTOP_TESTS -ErrorAction SilentlyContinue
        }
        [pscustomobject]@{ Ok = ($run.ExitCode -eq 0); Counts = (Get-DotnetCounts $run.Text) }
    }
}
else {
    Add-Result 'Desktop tests' 'SKIPPED' ([TimeSpan]::Zero) 'pass -Desktop to run (opens windows)'
}

# ---- summary ----
Write-Host ''
Write-Host '=== Summary ===' -ForegroundColor Cyan
$results | Format-Table -AutoSize | Out-String | Write-Host

$stamp = Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz'
$dotnetVersion = (& dotnet --version 2>$null)
$builder = New-Object System.Text.StringBuilder
[void]$builder.AppendLine('# Automated run, latest')
[void]$builder.AppendLine('')
[void]$builder.AppendLine("Written by ``scripts/verify-all.ps1`` on $stamp. Overwritten by every run; do not edit by hand.")
[void]$builder.AppendLine('')
[void]$builder.AppendLine("Options: ContinueOnError=$($ContinueOnError.IsPresent), CrossStack=$($CrossStack.IsPresent), Desktop=$($Desktop.IsPresent). .NET SDK $dotnetVersion.")
[void]$builder.AppendLine('')
[void]$builder.AppendLine('| Step | Result | Duration | Counts |')
[void]$builder.AppendLine('|---|---|---|---|')
foreach ($r in $results) { [void]$builder.AppendLine("| $($r.Step) | $($r.Result) | $($r.Duration) | $($r.Counts) |") }
[void]$builder.AppendLine('')
if ($timingLines.Count -gt 0) {
    [void]$builder.AppendLine('## Measured by the cross-stack tests (loopback, developer host)')
    [void]$builder.AppendLine('')
    [void]$builder.AppendLine('```')
    foreach ($line in $timingLines) { [void]$builder.AppendLine($line) }
    [void]$builder.AppendLine('```')
    [void]$builder.AppendLine('')
    [void]$builder.AppendLine('These are the real client against the real server on this computer. They do not include the real key injection, the slideshow repaint, or a real network.')
}
else {
    [void]$builder.AppendLine('No timing was measured in this run (use `-CrossStack`).')
}
[System.IO.File]::WriteAllText($reportPath, $builder.ToString(), (New-Object System.Text.UTF8Encoding($false)))
Write-Host "Report written to $reportPath"

$failedSteps = @($results | Where-Object { $_.Result -in @('FAIL', 'NOT RUN') })
if ($failedSteps.Count -gt 0) { exit 1 }
exit 0
