<#
.SYNOPSIS
  Verifies, builds, self-tests and installs Clicksmith for Windows (per user, no admin).
.EXAMPLE
  .\install.cmd            # with the forge animation
  .\install.cmd -NoAnim    # plain and quick
  .\install.cmd -Dev       # skip the release check (for code you changed yourself)
#>
[CmdletBinding()]
param([switch]$Dev, [switch]$NoAnim, [switch]$NoLaunch, [string]$Title = 'Installer')

$ErrorActionPreference = 'Stop'
if ($NoAnim) { $env:CLICKSMITH_NO_ANIM = '1' }
$script:LogFile = Join-Path $env:LOCALAPPDATA 'Clicksmith\install.log'
. (Join-Path $PSScriptRoot 'lib\ui.ps1')
. (Join-Path $PSScriptRoot 'lib\common.ps1')
Set-Location $script:Root

$version = Get-ClicksmithVersion
$rid = Get-RuntimeId
Start-Log "$Title $version"
Show-Banner "$Title $version for Windows $($script:Dot) log: $($script:LogFile)"

# .NET SDK (asked up front, before the step loader starts).
if ((Get-DotnetMajor) -lt 8) {
    Write-Line "  $($script:Sand)Clicksmith needs the free .NET SDK (version 8 or newer) to build.$($script:R)"
    if (Get-Command winget -ErrorAction SilentlyContinue) {
        $answer = Read-Host '  Install the .NET 8 SDK with winget now? [Y/n]'
        if ($answer -eq '' -or $answer -match '^[Yy]') {
            & winget install --id Microsoft.DotNet.SDK.8 -e --accept-source-agreements --accept-package-agreements
            Update-PathFromRegistry
        }
    }
    if ((Get-DotnetMajor) -lt 8) {
        Write-Line "  Install it from https://dotnet.microsoft.com/download/dotnet/8.0 and run install.cmd again."
        exit 1
    }
    Write-Line
}

$script:StepTotal = 14
$sln = Join-Path $script:Root 'src'
$publish = "-c Release -r $rid --self-contained false -o `"$($script:BuildOut)`" -p:DebugType=None -p:DebugSymbols=false -nologo"

if ($Dev) { Skip-Step 'Release check skipped (-Dev)' }
else {
    Invoke-Step 'Checking the release files' { Test-ReleaseFiles }
    Write-Note $script:LastResult
}

Invoke-ProcessStep 'Checking the toolbox' 'dotnet' '--version'
Write-Note (".NET SDK " + ([string]$script:LastOutput).Trim() + " $($script:Dot) $rid")
Invoke-Step 'Clearing the workbench' { if (Test-Path $script:BuildOut) { Remove-Item $script:BuildOut -Recurse -Force }; 'ok' }
Invoke-ProcessStep 'Forging the self-tests' 'dotnet' "build `"$sln\Clicksmith.SelfTest\Clicksmith.SelfTest.csproj`" -c Release -nologo"
Invoke-ProcessStep 'Stress-testing the metal' 'dotnet' "`"$sln\Clicksmith.SelfTest\bin\Release\net8.0\Clicksmith.SelfTest.dll`""
Write-Note ([string](([string]$script:LastOutput -split "`n" | Where-Object { $_ -match 'Self-test' } | Select-Object -Last 1))).Trim()
Invoke-ProcessStep 'Forging the input engine' 'dotnet' "publish `"$sln\Clicksmith.Engine\Clicksmith.Engine.csproj`" $publish"
Invoke-ProcessStep 'Forging the settings app' 'dotnet' "publish `"$sln\Clicksmith.App\Clicksmith.App.csproj`" $publish"
Invoke-ProcessStep 'Forging the DPI probe' 'dotnet' "publish `"$sln\Clicksmith.DpiProbe\Clicksmith.DpiProbe.csproj`" $publish"
Invoke-Step 'Saving your setup' { Save-UserSetup }
Write-Note $script:LastResult
Invoke-Step 'Cooling the old version' { Stop-Clicksmith; 'stopped' }

# From here on, any failure puts the previous version back.
$script:OnFail = { Restore-Previous }
Invoke-Step "Installing to %LOCALAPPDATA%\Programs" { Install-Files }
Invoke-Step 'Adding to Start menu and sign-in' { Register-Clicksmith $version }
Invoke-Step 'Waking up the engine' { Start-EngineAndWait $version }
$script:OnFail = $null

if ($NoLaunch) { Skip-Step 'Not opening Clicksmith (-NoLaunch)' }
else { Invoke-Step 'Opening Clicksmith' { Start-Process -FilePath (Join-Path $script:InstallDir 'Clicksmith.exe') -WorkingDirectory $script:InstallDir; 'opened' } }

Show-Finale "Clicksmith $version is forged and installed"
Show-Box 'Next steps' @(
    '1. Open Clicksmith from the Start menu or the tray icon.',
    '2. Buttons > pick a button, or start with a suggestion.',
    '3. Overview > Test a button: see what it does, safely.',
    '',
    'No permissions needed. Your setup is backed up on every change.',
    'Previous version kept for rollback:  .\rollback.cmd',
    'Health check:                        .\verify.cmd',
    'Update later:                        .\update.cmd  (or Settings > Updates)')
