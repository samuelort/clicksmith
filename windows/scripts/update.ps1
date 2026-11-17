<#
.SYNOPSIS
  Updates Clicksmith: optionally pulls the latest source, then reinstalls with the release check,
  a backup of your setup and automatic rollback if anything fails.
#>
[CmdletBinding()]
param([switch]$NoPull, [switch]$Dev, [switch]$NoAnim)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$before = $null
$exe = Join-Path $env:LOCALAPPDATA 'Programs\Clicksmith\Clicksmith.exe'
if (Test-Path $exe) { $before = (Get-Item $exe).VersionInfo.ProductVersion }

if (-not $NoPull -and (Test-Path (Join-Path $root '.git')) -and (Get-Command git -ErrorAction SilentlyContinue)) {
    Write-Host "  Getting the latest source (git pull --ff-only)..."
    Push-Location $root
    try {
        & git pull --ff-only
        if ($LASTEXITCODE -ne 0) { Write-Host '  git pull failed - nothing was changed. Resolve it and run update.cmd again.'; exit 1 }
    } finally { Pop-Location }
}

$args2 = @{ Title = 'Update' }
if ($Dev) { $args2.Dev = $true }
if ($NoAnim) { $args2.NoAnim = $true }
& (Join-Path $PSScriptRoot 'install.ps1') @args2
if ($LASTEXITCODE -and $LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
if ($before) {
    $after = (Get-Item $exe).VersionInfo.ProductVersion
    Write-Host "  Updated from $before to $after. Your buttons and profiles were kept."
}
