<#
.SYNOPSIS
    Read-only checklist to run before a demo: tools, emulator definition, port, AI settings, build output.

.DESCRIPTION
    Prints PASS, WARN or FAIL for each check and a reminder list of manual checks. It only reads:
      - the versions of dotnet and java and the output of `adb devices` (this can start the local adb background service,
        which is normal and harmless; it does not start the emulator),
      - the AVD names reported by the SDK's emulator tool (`-list-avds`; this lists definitions and starts nothing),
      - which TCP ports have a listener (no program or process is listed or inspected),
      - whether three environment variables are SET (their values are never read into the output),
      - whether the companion build output file exists.

    It does not start the emulator or the companion, does not touch any application you have open, does not print secrets,
    and does not write or change any file. Exit code: 1 when at least one check is FAIL, otherwise 0.

.PARAMETER Port
    The port the companion listens on. Default 8765.

.PARAMETER AvdName
    The AVD the demo uses. Default Wear_OS_Large_Round.

.PARAMETER SdkRoot
    Android SDK folder. Default: ANDROID_HOME, then ANDROID_SDK_ROOT, then %LOCALAPPDATA%\Android\Sdk.

.EXAMPLE
    ./scripts/demo-preflight.ps1
#>
[CmdletBinding()]
param(
    [int]$Port = 8765,
    [string]$AvdName = 'Wear_OS_Large_Round',
    [string]$SdkRoot
)

$ErrorActionPreference = 'Continue'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$script:fail = 0
$script:warn = 0

function Write-Check([string]$Level, [string]$Name, [string]$Detail) {
    switch ($Level) {
        'PASS' { $color = 'Green' }
        'WARN' { $color = 'Yellow'; $script:warn++ }
        default { $color = 'Red'; $script:fail++ }
    }
    Write-Host ('[{0}] {1}' -f $Level, $Name) -ForegroundColor $color -NoNewline
    if ($Detail) { Write-Host (' - ' + $Detail) } else { Write-Host '' }
}

function Invoke-Quiet([string]$File, [string[]]$Arguments) {
    # Runs a tool and returns @{ Ok; Lines }. Never throws.
    try {
        $lines = & $File @Arguments 2>&1 | ForEach-Object { "$_" }
        return @{ Ok = ($LASTEXITCODE -eq 0); Lines = @($lines) }
    } catch {
        return @{ Ok = $false; Lines = @() }
    }
}

Write-Host 'Presenta demo preflight (read-only)'
Write-Host ('Project root: ' + $root)
Write-Host ''

# ---- .NET SDK ----
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) {
    Write-Check 'FAIL' '.NET SDK' 'dotnet was not found on PATH'
} else {
    $r = Invoke-Quiet $dotnet.Source @('--version')
    $version = if ($r.Lines.Count -gt 0) { $r.Lines[0].Trim() } else { '' }
    if ($r.Ok -and $version -match '^10\.') { Write-Check 'PASS' '.NET SDK' ('version ' + $version) }
    elseif ($r.Ok) { Write-Check 'WARN' '.NET SDK' ("version $version; the companion targets .NET 10") }
    else { Write-Check 'FAIL' '.NET SDK' 'dotnet --version failed' }
}

# ---- JDK ----
$javaPath = $null
if ($env:JAVA_HOME -and (Test-Path -LiteralPath (Join-Path $env:JAVA_HOME 'bin\java.exe'))) { $javaPath = Join-Path $env:JAVA_HOME 'bin\java.exe' }
else { $cmd = Get-Command java -ErrorAction SilentlyContinue; if ($cmd) { $javaPath = $cmd.Source } }
if (-not $javaPath) {
    Write-Check 'FAIL' 'JDK' 'java was not found (JAVA_HOME is not set and java is not on PATH)'
} else {
    $r = Invoke-Quiet $javaPath @('-version')
    $first = if ($r.Lines.Count -gt 0) { $r.Lines[0].Trim() } else { '' }
    if ($first -match '"21\.') { Write-Check 'PASS' 'JDK' ($first + ' (' + $javaPath + ')') }
    elseif ($first) { Write-Check 'WARN' 'JDK' ("$first; the watch build expects JDK 21") }
    else { Write-Check 'FAIL' 'JDK' 'java -version produced no output' }
}

# ---- Android SDK, adb ----
if (-not $SdkRoot) {
    foreach ($candidate in @($env:ANDROID_HOME, $env:ANDROID_SDK_ROOT, (Join-Path $env:LOCALAPPDATA 'Android\Sdk'))) {
        if ($candidate -and (Test-Path -LiteralPath $candidate)) { $SdkRoot = $candidate; break }
    }
}
$adb = $null
if ($SdkRoot) { $p = Join-Path $SdkRoot 'platform-tools\adb.exe'; if (Test-Path -LiteralPath $p) { $adb = $p } }
if (-not $adb) { $cmd = Get-Command adb -ErrorAction SilentlyContinue; if ($cmd) { $adb = $cmd.Source } }

if (-not $adb) {
    Write-Check 'FAIL' 'adb' 'adb.exe was not found in the SDK platform-tools folder or on PATH'
} else {
    Write-Check 'PASS' 'adb found' ('path used: ' + $adb)
    $r = Invoke-Quiet $adb @('devices')
    if (-not $r.Ok) {
        Write-Check 'WARN' 'adb devices' 'the command failed; the adb service may not be running'
    } else {
        $devices = @($r.Lines | Where-Object { $_ -match '^\S+\s+(device|offline|unauthorized)\s*$' })
        $ready = @($devices | Where-Object { $_ -match '\sdevice\s*$' })
        if ($ready.Count -gt 0) {
            Write-Check 'PASS' 'adb devices' ("$($ready.Count) device(s) ready, $($devices.Count) listed")
        } elseif ($devices.Count -gt 0) {
            Write-Check 'WARN' 'adb devices' ("$($devices.Count) listed but none ready (offline or unauthorized); wait for the emulator to finish starting")
        } else {
            Write-Check 'WARN' 'adb devices' 'no device listed; start the emulator from Device Manager before the demo'
        }
    }
}

# ---- AVD ----
$emulator = $null
if ($SdkRoot) { $p = Join-Path $SdkRoot 'emulator\emulator.exe'; if (Test-Path -LiteralPath $p) { $emulator = $p } }
if (-not $emulator) {
    Write-Check 'FAIL' ('AVD ' + $AvdName) 'emulator.exe was not found in the SDK, so the AVD list could not be read'
} else {
    $r = Invoke-Quiet $emulator @('-list-avds')
    $names = @($r.Lines | ForEach-Object { $_.Trim() } | Where-Object { $_ -match '^[A-Za-z0-9._-]+$' })
    if ($names -contains $AvdName) { Write-Check 'PASS' ('AVD ' + $AvdName) ('listed by the SDK (' + $names.Count + ' AVD(s) in total)') }
    else { Write-Check 'FAIL' ('AVD ' + $AvdName) ('not in the list: ' + ($names -join ', ')) }
}

# ---- port ----
try {
    $listeners = [System.Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveTcpListeners()
    $busy = @($listeners | Where-Object { $_.Port -eq $Port })
    if ($busy.Count -eq 0) { Write-Check 'PASS' ("TCP port $Port") 'free' }
    else { Write-Check 'FAIL' ("TCP port $Port") 'something is already listening on it (a second companion, or another program); close it before the demo' }
} catch {
    Write-Check 'WARN' ("TCP port $Port") 'could not read the list of listening ports'
}

# ---- AI variables (presence only) ----
$aiNames = @('PRESENTA_AI_BASE_URL', 'PRESENTA_AI_MODEL', 'PRESENTA_AI_API_KEY')
$missing = @($aiNames | Where-Object { [string]::IsNullOrEmpty([System.Environment]::GetEnvironmentVariable($_, 'Process')) })
if ($missing.Count -eq 0) {
    Write-Check 'PASS' 'AI variables' 'the three PRESENTA_AI_* variables are set in this session (values not shown)'
} else {
    $hasEnvFile = Test-Path -LiteralPath (Join-Path $root '.env') -PathType Leaf
    $hint = if ($hasEnvFile) { 'a .env file exists: start with scripts/run-companion.ps1 to load it' } else { 'the AI will be off; set them in the PowerShell window that starts the companion, or copy .env.example to .env' }
    Write-Check 'WARN' 'AI variables' ('not set in this session: ' + ($missing -join ', ') + '; ' + $hint)
}

# ---- companion build ----
$exe = Join-Path $root 'windows-companion\src\Presenta.Companion.App\bin\Debug\net10.0-windows\Presenta.exe'
if (Test-Path -LiteralPath $exe -PathType Leaf) {
    Write-Check 'PASS' 'Companion build output' 'Debug Presenta.exe is present'
} else {
    Write-Check 'WARN' 'Companion build output' 'Debug Presenta.exe not found; dotnet run builds it (the first run takes about a minute)'
}

# ---- summary and manual reminders ----
Write-Host ''
Write-Host ("Result: $script:fail FAIL, $script:warn WARN") -ForegroundColor $(if ($script:fail -gt 0) { 'Red' } elseif ($script:warn -gt 0) { 'Yellow' } else { 'Green' })
Write-Host ''
Write-Host 'Manual checks this script cannot do:'
@(
    'Emulator date and time are set to automatic (Settings > System > Date & time), or history dates will look wrong.',
    'PowerPoint runs the test deck as a slide show in a window (not full screen) and is not elevated.',
    'Android Studio and PowerPoint are arranged side by side so both are visible.',
    'The companion window is open, its remembered-pairing-code switch is on and the watch is already paired.',
    'Windows Firewall: choose Cancel if it asks about Presenta (the emulator does not need it).',
    'If the watch cannot connect: run adb reverse tcp:8765 tcp:8765 and use the localhost fallback (docs/manual-test-guide.md).',
    'Close other presentations so keys cannot reach the wrong program.',
    'Have docs/delivery/demo-script.md and docs/validation/smoke-test.md open.'
) | ForEach-Object { Write-Host ('  [ ] ' + $_) }

if ($script:fail -gt 0) { exit 1 } else { exit 0 }
