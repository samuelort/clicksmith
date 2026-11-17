<#
.SYNOPSIS
  Health check: installation, engine, mouse hook, settings, backups, and real CPU/memory use.
.EXAMPLE
  .\verify.cmd        # 10-second measurement
  .\verify.cmd 30     # 30 seconds - move the mouse during the measurement
#>
param([int]$Seconds = 10)
$ErrorActionPreference = 'Continue'
. (Join-Path $PSScriptRoot 'lib\ui.ps1')
. (Join-Path $PSScriptRoot 'lib\common.ps1')
$pass = 0; $warn = 0; $fail = 0
function Ok($t) { $script:pass++; Write-Line "  $($script:Sage)$($script:Tick)$($script:R) $t" }
function Warn($t) { $script:warn++; Write-Line "  $($script:Sand)!$($script:R) $t" }
function Bad($t) { $script:fail++; Write-Line "  $($script:Clay)$($script:Cross)$($script:R) $t" }
function Head($t) { Write-Line; Write-Line "  $($script:B)$t$($script:R)" }

Show-Banner 'Health check' -NoAnimation

Head 'Installation'
$app = Join-Path $script:InstallDir 'Clicksmith.exe'
$engineExe = Join-Path $script:InstallDir 'Clicksmith.Engine.exe'
$appVersion = $null
if (Test-Path $app) {
    $appVersion = (Get-Item $app).VersionInfo.ProductVersion -replace '\+.*$', ''
    Ok "Installed: $($script:InstallDir) ($appVersion)"
} else { Bad 'Clicksmith is not installed (run install.cmd)' }
if (Test-Path $engineExe) { Ok 'Engine present' } else { Bad 'Clicksmith.Engine.exe missing' }
if (Test-Path $script:StartMenuLink) { Ok 'Start menu shortcut' } else { Warn 'No Start menu shortcut' }
if (Test-Path $script:PreviousDir) { Ok 'Previous version kept for rollback' } else { Warn 'No previous version to roll back to (normal after the first install)' }
if (Test-Path (Join-Path $script:Root 'MANIFEST.sha256')) {
    try { $null = Test-ReleaseFiles; Ok 'Source folder matches the release' } catch { Warn 'Source folder differs from the release manifest' }
}

Head 'Engine'
$proc = Get-Process -Name 'Clicksmith.Engine' -ErrorAction SilentlyContinue | Select-Object -First 1
if ($proc) { Ok "Running (pid $($proc.Id))" } else { Bad 'Not running - open Clicksmith and press Start engine' }
if (Get-ItemProperty -Path $script:RunKey -Name 'Clicksmith Engine' -ErrorAction SilentlyContinue) { Ok 'Starts when you sign in' } else { Warn 'Not set to start when you sign in' }
$s = Read-EngineStatus
if ($s) {
    if ($s.heartbeat -is [datetime]) { $beat = $s.heartbeat.ToUniversalTime() } else { $beat = [DateTimeOffset]::Parse([string]$s.heartbeat, [Globalization.CultureInfo]::InvariantCulture).UtcDateTime }
    $age = ((Get-Date).ToUniversalTime() - $beat).TotalSeconds
    if ($age -lt 30) { Ok ("Status is fresh ({0:0} s old)" -f $age) } else { Bad ("Status is stale ({0:0} s old)" -f $age) }
    if ($appVersion -and $s.version -and $s.version -ne $appVersion) { Warn "Engine is $($s.version) but the app is $appVersion - restart the engine" } else { Ok "Engine version $($s.version)" }
    if ($s.hookActive) { Ok 'Mouse hook active' } else { Bad 'Mouse hook not active - restart the engine' }
    if ($s.emergencyPaused) { Warn 'Emergency pause is on' } elseif ($s.paused) { Warn 'Mappings are paused' } else { Ok 'Mappings active' }
    Ok "Profile in use: $($s.activeProfile) ($($s.activeMappings) buttons)"
    if ($s.lastRecovery) { Ok "Last refresh after sleep/unlock: $($s.lastRecovery)" } else { Warn 'No sleep/wake refresh yet (sleep the PC once to test)' }
    if ($s.lastError) { Warn "Last engine message: $($s.lastError)" }
} else { Bad 'No engine status yet' }

Head 'Settings & backups'
$config = Join-Path $script:DataDir 'config.json'
if (Test-Path $config) {
    try { $c = Get-Content $config -Raw | ConvertFrom-Json; Ok "Setup readable (schema $($c.schemaVersion), $(@($c.profiles).Count) profile(s))" } catch { Bad 'config.json is not valid JSON' }
} else { Warn 'No setup saved yet' }
$backups = @(Get-ChildItem (Join-Path $script:DataDir 'Configuration Backups') -Filter 'config-*.json' -ErrorAction SilentlyContinue)
if ($backups.Count -gt 0) { Ok "$($backups.Count) backup(s) of your setup" } else { Warn 'No backups yet (made automatically on the next change)' }

if ($proc) {
    Head "Resource use (measuring $Seconds s - move the mouse now)"
    $proc.Refresh(); $cpu0 = $proc.TotalProcessorTime.TotalSeconds
    Start-Sleep -Seconds $Seconds
    $proc.Refresh(); $cpu1 = $proc.TotalProcessorTime.TotalSeconds
    $pct = ($cpu1 - $cpu0) / $Seconds * 100
    $mb = $proc.WorkingSet64 / 1MB
    if ($pct -lt 1) { Ok ("Engine CPU: {0:0.00}% of one core" -f $pct) } else { Warn ("Engine CPU: {0:0.00}% of one core (expected under 1%)" -f $pct) }
    if ($mb -lt 80) { Ok ("Engine memory: {0:0.0} MB" -f $mb) } else { Warn ("Engine memory: {0:0.0} MB" -f $mb) }
    $ui = Get-Process -Name 'Clicksmith' -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($ui) { Ok ("Settings app memory: {0:0.0} MB (can be closed; the engine keeps working)" -f ($ui.WorkingSet64 / 1MB)) }
}

Write-Line
Write-Line ("  $($script:Sage){0} passed$($script:R) $($script:Dot) $($script:Sand){1} warnings$($script:R) $($script:Dot) $($script:Clay){2} failed$($script:R)" -f $pass, $warn, $fail)
Write-Line '  Manual tests: docs\TESTPLAN.md'
Write-Line
if ($fail -gt 0) { exit 1 }
