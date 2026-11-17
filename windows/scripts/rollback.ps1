<#
.SYNOPSIS
  Puts the previous Clicksmith version back (kept automatically by every install/update).
  Your setup is not touched; older setups are under Settings > Backups.
#>
$ErrorActionPreference = 'Stop'
$install = Join-Path $env:LOCALAPPDATA 'Programs\Clicksmith'
$previous = Join-Path $env:LOCALAPPDATA 'Programs\Clicksmith.previous'
if (-not (Test-Path $previous)) { Write-Host '  There is no previous version to go back to.'; exit 1 }
$current = $null
if (Test-Path "$install\Clicksmith.exe") { $current = (Get-Item "$install\Clicksmith.exe").VersionInfo.ProductVersion }
$older = (Get-Item "$previous\Clicksmith.exe").VersionInfo.ProductVersion
$answer = Read-Host "  Go back from $current to $older? [y/N]"
if ($answer -notmatch '^[Yy]') { Write-Host '  Nothing was changed.'; exit 0 }
Get-Process -Name 'Clicksmith.Engine', 'Clicksmith' -ErrorAction SilentlyContinue | ForEach-Object { try { $_.Kill(); $_.WaitForExit(3000) | Out-Null } catch { } }
$swap = "$install.swap"
if (Test-Path $swap) { Remove-Item $swap -Recurse -Force }
if (Test-Path $install) { Move-Item $install $swap }
Move-Item $previous $install
if (Test-Path $swap) { Move-Item $swap $previous }
Start-Process -FilePath "$install\Clicksmith.Engine.exe" -WorkingDirectory $install
Write-Host "  Clicksmith $older is back. Run rollback again to return to $current."
