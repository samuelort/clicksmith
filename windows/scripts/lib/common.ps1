# Shared paths and helpers for install, update, verify, rollback and uninstall.

$script:Root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$script:InstallDir = Join-Path $env:LOCALAPPDATA 'Programs\Clicksmith'
$script:PreviousDir = Join-Path $env:LOCALAPPDATA 'Programs\Clicksmith.previous'
$script:DataDir = Join-Path $env:APPDATA 'Clicksmith'
$script:BuildOut = Join-Path $script:Root 'build\out'
$script:StartMenuLink = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Clicksmith.lnk'
$script:RunKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$script:UninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Clicksmith'

function Get-ClicksmithVersion {
    $props = Join-Path $script:Root 'Directory.Build.props'
    if (-not (Test-Path $props)) { return 'unknown' }
    return ([xml](Get-Content $props -Raw)).Project.PropertyGroup.Version
}

function Get-RuntimeId {
    if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64' -or $env:PROCESSOR_ARCHITEW6432 -eq 'ARM64') { return 'win-arm64' }
    return 'win-x64'
}

# Compares every release file against MANIFEST.sha256 and refuses changed, missing or extra files.
function Test-ReleaseFiles {
    $manifest = Join-Path $script:Root 'MANIFEST.sha256'
    if (-not (Test-Path $manifest)) { throw 'MANIFEST.sha256 is missing - this is not a release package. Use -Dev for your own changes.' }
    $expected = @{}
    foreach ($line in Get-Content $manifest) {
        if ($line -match '^([a-f0-9]{64})\s+\*?(.+)$') { $expected[$Matches[2].Trim()] = $Matches[1] }
    }
    $bad = @()
    foreach ($rel in $expected.Keys) {
        $path = Join-Path $script:Root ($rel -replace '/', '\')
        if (-not (Test-Path -LiteralPath $path)) { $bad += "missing: $rel"; continue }
        $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($hash -ne $expected[$rel]) { $bad += "changed: $rel" }
    }
    $tracked = @('src', 'scripts', 'assets') | ForEach-Object { Join-Path $script:Root $_ } | Where-Object { Test-Path $_ } |
        ForEach-Object { Get-ChildItem -Path $_ -Recurse -File } |
        Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' }
    foreach ($f in $tracked) {
        $rel = $f.FullName.Substring($script:Root.Length + 1) -replace '\\', '/'
        if (-not $expected.ContainsKey($rel)) { $bad += "extra: $rel" }
    }
    if ($bad.Count -gt 0) {
        $bad | Select-Object -First 15 | ForEach-Object { Write-Output $_ }
        throw "$($bad.Count) file(s) differ from the release. Download a fresh copy, or use -Dev if you changed the code yourself."
    }
    "All $($expected.Count) files match the $(Get-ClicksmithVersion) release"
}

function Get-DotnetMajor {
    $cmd = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $cmd) { return 0 }
    $best = 0
    foreach ($line in (& dotnet --list-sdks 2>$null)) {
        if ($line -match '^(\d+)\.') { $best = [Math]::Max($best, [int]$Matches[1]) }
    }
    return $best
}

function Update-PathFromRegistry {
    $machine = [Environment]::GetEnvironmentVariable('Path', 'Machine')
    $user = [Environment]::GetEnvironmentVariable('Path', 'User')
    $env:Path = "$machine;$user"
}

function Stop-Clicksmith {
    $request = Join-Path $script:DataDir 'engine-request.json'
    if (Get-Process -Name 'Clicksmith.Engine' -ErrorAction SilentlyContinue) {
        try {
            $body = @{ id = [guid]::NewGuid().ToString(); kind = 'quit'; at = (Get-Date).ToUniversalTime().ToString('o') } | ConvertTo-Json
            if (-not (Test-Path $script:DataDir)) { New-Item -ItemType Directory -Path $script:DataDir -Force | Out-Null }
            [IO.File]::WriteAllText($request, $body)
        } catch { }
        for ($i = 0; $i -lt 30 -and (Get-Process -Name 'Clicksmith.Engine' -ErrorAction SilentlyContinue); $i++) { Start-Sleep -Milliseconds 100 }
    }
    Get-Process -Name 'Clicksmith.Engine', 'Clicksmith' -ErrorAction SilentlyContinue | ForEach-Object {
        try { $_.Kill(); $_.WaitForExit(3000) | Out-Null } catch { }
    }
}

function Save-UserSetup {
    $config = Join-Path $script:DataDir 'config.json'
    if (-not (Test-Path $config)) { return 'No existing setup to back up (first install)' }
    $dir = Join-Path $script:DataDir 'Configuration Backups'
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    $stamp = [DateTime]::UtcNow.ToString("yyyyMMdd'T'HHmmssfff'Z'")
    $target = Join-Path $dir "config-$stamp-install.json"
    Copy-Item -LiteralPath $config -Destination $target -Force
    "Setup backed up to Configuration Backups"
}

function Install-Files {
    if (-not (Test-Path (Join-Path $script:BuildOut 'Clicksmith.exe'))) { throw 'Build output is missing Clicksmith.exe' }
    if (-not (Test-Path (Join-Path $script:BuildOut 'Clicksmith.Engine.exe'))) { throw 'Build output is missing Clicksmith.Engine.exe' }
    if (Test-Path $script:InstallDir) {
        if (Test-Path $script:PreviousDir) { Remove-Item $script:PreviousDir -Recurse -Force }
        Move-Item -LiteralPath $script:InstallDir -Destination $script:PreviousDir
    }
    New-Item -ItemType Directory -Path $script:InstallDir -Force | Out-Null
    Copy-Item -Path (Join-Path $script:BuildOut '*') -Destination $script:InstallDir -Recurse -Force
    $tools = Join-Path $script:InstallDir 'tools'
    New-Item -ItemType Directory -Path $tools -Force | Out-Null
    Copy-Item (Join-Path $script:Root 'scripts\uninstall.ps1') $tools -Force
    Copy-Item (Join-Path $script:Root 'scripts\rollback.ps1') $tools -Force
    Copy-Item (Join-Path $script:Root 'scripts\lib') $tools -Recurse -Force
    "Installed to $($script:InstallDir)"
}

function Restore-Previous {
    if (-not (Test-Path $script:PreviousDir)) { return }
    Stop-Clicksmith
    if (Test-Path $script:InstallDir) { Remove-Item $script:InstallDir -Recurse -Force }
    Move-Item -LiteralPath $script:PreviousDir -Destination $script:InstallDir
    $engine = Join-Path $script:InstallDir 'Clicksmith.Engine.exe'
    if (Test-Path $engine) { Start-Process -FilePath $engine -WorkingDirectory $script:InstallDir }
}

function Register-Clicksmith([string]$version) {
    $app = Join-Path $script:InstallDir 'Clicksmith.exe'
    $engine = Join-Path $script:InstallDir 'Clicksmith.Engine.exe'
    $icon = Join-Path $script:InstallDir 'assets\clicksmith.ico'

    $shell = New-Object -ComObject WScript.Shell
    $link = $shell.CreateShortcut($script:StartMenuLink)
    $link.TargetPath = $app
    $link.WorkingDirectory = $script:InstallDir
    $link.IconLocation = $icon
    $link.Description = 'Clicksmith - mouse button remapper'
    $link.Save()

    # Engine at sign-in unless the user turned it off in Settings.
    $atLogin = $true
    $config = Join-Path $script:DataDir 'config.json'
    if (Test-Path $config) {
        try { $s = (Get-Content $config -Raw | ConvertFrom-Json).settings; if ($null -ne $s.launchEngineAtLogin) { $atLogin = [bool]$s.launchEngineAtLogin } } catch { }
    }
    if (-not (Test-Path $script:RunKey)) { New-Item -Path $script:RunKey -Force | Out-Null }
    if ($atLogin) { Set-ItemProperty -Path $script:RunKey -Name 'Clicksmith Engine' -Value "`"$engine`"" }
    else { Remove-ItemProperty -Path $script:RunKey -Name 'Clicksmith Engine' -ErrorAction SilentlyContinue }
    # Keep an existing tray-at-login entry pointing at the new install.
    if (Get-ItemProperty -Path $script:RunKey -Name 'Clicksmith' -ErrorAction SilentlyContinue) {
        Set-ItemProperty -Path $script:RunKey -Name 'Clicksmith' -Value "`"$app`" --tray"
    }

    # Apps & features entry (per user, no admin).
    if (-not (Test-Path $script:UninstallKey)) { New-Item -Path $script:UninstallKey -Force | Out-Null }
    $uninstall = Join-Path $script:InstallDir 'tools\uninstall.ps1'
    $size = [int]((Get-ChildItem $script:InstallDir -Recurse -File | Measure-Object Length -Sum).Sum / 1KB)
    $values = @{
        DisplayName = 'Clicksmith'; DisplayVersion = $version; Publisher = 'Clicksmith contributors'
        DisplayIcon = $icon; InstallLocation = $script:InstallDir
        UninstallString = "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$uninstall`""
        URLInfoAbout = 'https://github.com/samuelort/clicksmith'
    }
    foreach ($k in $values.Keys) { Set-ItemProperty -Path $script:UninstallKey -Name $k -Value $values[$k] }
    Set-ItemProperty -Path $script:UninstallKey -Name 'NoModify' -Value 1 -Type DWord
    Set-ItemProperty -Path $script:UninstallKey -Name 'NoRepair' -Value 1 -Type DWord
    Set-ItemProperty -Path $script:UninstallKey -Name 'EstimatedSize' -Value $size -Type DWord
    "Start menu, sign-in and Apps & features entries updated"
}

function Read-EngineStatus {
    $path = Join-Path $script:DataDir 'engine-status.json'
    if (-not (Test-Path $path)) { return $null }
    try { return Get-Content $path -Raw | ConvertFrom-Json } catch { return $null }
}

function Start-EngineAndWait([string]$version) {
    $engine = Join-Path $script:InstallDir 'Clicksmith.Engine.exe'
    Start-Process -FilePath $engine -WorkingDirectory $script:InstallDir
    $started = (Get-Date).ToUniversalTime().AddSeconds(-2)
    for ($i = 0; $i -lt 50; $i++) {
        Start-Sleep -Milliseconds 200
        $s = Read-EngineStatus
        if ($s -and $s.version -eq $version) {
            # PowerShell 7 turns ISO dates into DateTime; Windows PowerShell keeps the string.
            if ($s.heartbeat -is [datetime]) { $beat = $s.heartbeat.ToUniversalTime() }
            else { $beat = [DateTimeOffset]::Parse([string]$s.heartbeat, [Globalization.CultureInfo]::InvariantCulture).UtcDateTime }
            if ($beat -ge $started) {
                if (-not $s.hookActive) { throw 'The engine started but Windows refused the mouse hook.' }
                return "Engine $version is listening"
            }
        }
    }
    throw 'The engine did not report in within 10 seconds.'
}
