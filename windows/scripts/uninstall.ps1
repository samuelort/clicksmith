<#
.SYNOPSIS
  Removes Clicksmith for this user. Add -Purge to also delete your setup, backups and logs.
#>
[CmdletBinding()]
param([switch]$Purge)
$install = Join-Path $env:LOCALAPPDATA 'Programs\Clicksmith'
$previous = Join-Path $env:LOCALAPPDATA 'Programs\Clicksmith.previous'
$data = Join-Path $env:APPDATA 'Clicksmith'
Get-Process -Name 'Clicksmith.Engine', 'Clicksmith' -ErrorAction SilentlyContinue | ForEach-Object { try { $_.Kill(); $_.WaitForExit(3000) | Out-Null } catch { } }
Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'Clicksmith Engine', 'Clicksmith' -ErrorAction SilentlyContinue
Remove-Item 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Clicksmith' -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{6F1C2C61-5D7A-4F5B-9F64-2B3E1C0A1D2C}_is1' -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item (Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Clicksmith.lnk') -Force -ErrorAction SilentlyContinue
# The running uninstaller may live inside the install folder; remove it after this process exits.
$self = $PSCommandPath
if ($self -and $self.StartsWith($install, [StringComparison]::OrdinalIgnoreCase)) {
    Start-Process -FilePath 'cmd.exe' -ArgumentList "/c timeout /t 2 >nul & rmdir /s /q `"$install`" & rmdir /s /q `"$previous`"" -WindowStyle Hidden
} else {
    Remove-Item $install, $previous -Recurse -Force -ErrorAction SilentlyContinue
}
if ($Purge) {
    Remove-Item $data -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host '  Clicksmith removed, including your setup, backups and logs.'
} else {
    Write-Host "  Clicksmith removed. Your setup and backups are kept in $data (use -Purge to delete)."
}
