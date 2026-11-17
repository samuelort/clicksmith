<#
.SYNOPSIS
  Installs a prebuilt Clicksmith release package (no .NET SDK, no building). Used by install.cmd in
  the release zip, by the one-line web installer and by the in-app updater.
#>
[CmdletBinding()]
param([switch]$NoAnim, [switch]$NoLaunch, [string]$Title = 'Installer')

$ErrorActionPreference = 'Stop'
if ($NoAnim) { $env:CLICKSMITH_NO_ANIM = '1' }
$script:LogFile = Join-Path $env:LOCALAPPDATA 'Clicksmith\install.log'
. (Join-Path $PSScriptRoot 'lib\ui.ps1')
. (Join-Path $PSScriptRoot 'lib\common.ps1')
Set-Location $script:Root

$version = Get-ClicksmithVersion
$appDir = Join-Path $script:Root 'app'
Start-Log "$Title $version (package)"
Show-Banner "$Title $version for Windows $($script:Dot) log: $($script:LogFile)"

# .NET 8 Desktop Runtime (asked up front, before the step loader starts).
if (-not (Test-DesktopRuntime)) {
    Write-Line "  $($script:Sand)Clicksmith needs the free Microsoft .NET 8 Desktop Runtime (about 55 MB, installed once).$($script:R)"
    if (Get-Command winget -ErrorAction SilentlyContinue) {
        $answer = Read-Host '  Install it with winget now? Windows may ask for permission. [Y/n]'
        if ($answer -eq '' -or $answer -match '^[Yy]') {
            & winget install --id Microsoft.DotNet.DesktopRuntime.8 -e --accept-source-agreements --accept-package-agreements
        }
    }
    if (-not (Test-DesktopRuntime)) {
        Write-Line '  Install ".NET Desktop Runtime 8" from https://dotnet.microsoft.com/download/dotnet/8.0 and run this again.'
        exit 1
    }
    Write-Line
}

$script:StepTotal = 9
Invoke-Step 'Checking the package' { Test-PackageFiles }
Write-Note $script:LastResult
Invoke-Step 'Checking the runtime' { ".NET Desktop Runtime $(Test-DesktopRuntime)" }
Write-Note $script:LastResult
Invoke-ProcessStep 'Stress-testing the metal' (Join-Path $script:ScriptsDir 'selftest\Clicksmith.SelfTest.exe') ''
Write-Note ([string](([string]$script:LastOutput -split "`n" | Where-Object { $_ -match 'Self-test' } | Select-Object -Last 1))).Trim()
Invoke-Step 'Saving your setup' { Save-UserSetup }
Write-Note $script:LastResult
Invoke-Step 'Cooling the old version' { Stop-Clicksmith; 'stopped' }

$script:OnFail = { Restore-Previous }
Invoke-Step 'Installing to %LOCALAPPDATA%\Programs' { Install-Files $appDir }
Invoke-Step 'Adding to Start menu and sign-in' { Register-Clicksmith $version }
Invoke-Step 'Waking up the engine' { Start-EngineAndWait $version }
$script:OnFail = $null

if ($NoLaunch) { Skip-Step 'Not opening Clicksmith (-NoLaunch)' }
else { Invoke-Step 'Opening Clicksmith' { Start-Process -FilePath (Join-Path $script:InstallDir 'Clicksmith.exe') -WorkingDirectory $script:InstallDir; 'opened' } }

Show-Finale "Clicksmith $version is forged and installed"
Show-Box 'Next steps' @(
    '1. Clicksmith opens with a short welcome guide.',
    '2. Press a mouse button and choose what it does.',
    '3. Overview > Test a button: see what it does, safely.',
    '',
    'Updates: Settings > Updates (verified download, automatic',
    'rollback). Health check: tools\verify.ps1 in the install folder.')
if ($Title -eq 'Update') { Write-Line '  You can close this window.' }
