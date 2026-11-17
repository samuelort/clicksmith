# Clicksmith for Windows - one-line installer.
#   irm https://raw.githubusercontent.com/samuelort/clicksmith/main/windows/get.ps1 | iex
# Downloads the newest Windows release for this PC, verifies its SHA-256 against the release's
# release-windows.json, then runs the package's own installer (forge animation, backup, rollback).
& {
    $ErrorActionPreference = 'Stop'
    $ProgressPreference = 'SilentlyContinue'
    try { [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12 } catch { }
    $repo = 'samuelort/clicksmith'
    $copper = "$([char]27)[38;2;212;152;112m"; $reset = "$([char]27)[0m"
    Write-Host ''
    Write-Host "  ${copper}Clicksmith$reset  Small clicks. A calmer workflow."
    Write-Host '  Finding the newest Windows release...'

    $headers = @{ 'User-Agent' = 'Clicksmith-Installer'; 'Accept' = 'application/vnd.github+json' }
    $releases = Invoke-RestMethod -Uri "https://api.github.com/repos/$repo/releases?per_page=30" -Headers $headers
    $release = $releases | Where-Object { -not $_.draft -and $_.tag_name -match '^windows-v\d+\.\d+\.\d+(-[0-9A-Za-z.\-]+)?$' } | Select-Object -First 1
    if (-not $release) { Write-Host '  No Windows release has been published yet.'; return }
    $arch = if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64' -or $env:PROCESSOR_ARCHITEW6432 -eq 'ARM64') { 'win-arm64' } else { 'win-x64' }
    $indexAsset = $release.assets | Where-Object name -eq 'release-windows.json' | Select-Object -First 1
    if (-not $indexAsset) { Write-Host "  $($release.tag_name) has no release-windows.json."; return }
    $index = Invoke-RestMethod -Uri $indexAsset.browser_download_url -Headers @{ 'User-Agent' = 'Clicksmith-Installer' }
    $artifact = $index.artifacts | Where-Object arch -eq $arch | Select-Object -First 1
    if (-not $artifact) { Write-Host "  No package for $arch in $($release.tag_name)."; return }
    if ($artifact.file -notmatch '^[A-Za-z0-9._\-]{1,120}\.zip$' -or $artifact.sha256 -notmatch '^[a-f0-9]{64}$') { Write-Host '  The release index looks wrong. Nothing was downloaded.'; return }
    $asset = $release.assets | Where-Object name -eq $artifact.file | Select-Object -First 1
    if (-not $asset -or $asset.browser_download_url -notmatch '^https://github\.com/') { Write-Host '  Package not found in the release.'; return }

    $dir = Join-Path $env:TEMP ("Clicksmith-" + $release.tag_name)
    Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
    $zip = Join-Path $dir $artifact.file
    Write-Host "  Downloading $($artifact.file) ($([math]::Round($artifact.size / 1MB, 1)) MB)..."
    Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $zip -Headers @{ 'User-Agent' = 'Clicksmith-Installer' } -UseBasicParsing
    $hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($hash -ne $artifact.sha256) { Remove-Item $dir -Recurse -Force; Write-Host '  Checksum mismatch - the download was changed or damaged. Nothing was installed.'; return }
    Write-Host "  Checksum verified. Starting the installer..."
    Expand-Archive -Path $zip -DestinationPath (Join-Path $dir 'package') -Force
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $dir 'package\scripts\install-release.ps1')
}
