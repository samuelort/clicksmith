<#
.SYNOPSIS
  Builds the prebuilt release: one package per CPU architecture (zip), release-windows.json and
  SHA256SUMS.txt in build\release, plus staged folders for Setup.exe. Used by GitHub Actions and
  works locally with the .NET 8 SDK.
#>
[CmdletBinding()]
param([string[]]$Rids = @('win-x64', 'win-arm64'))
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib\common.ps1')
Set-Location $script:Root

$version = Get-ClicksmithVersion
if ($env:GITHUB_REF_TYPE -eq 'tag' -and $env:GITHUB_REF_NAME -ne "windows-v$version") {
    throw "Tag $($env:GITHUB_REF_NAME) doesn't match the version in Directory.Build.props ($version)."
}
$src = Join-Path $script:Root 'src'
$out = Join-Path $script:Root 'build'
$stage = Join-Path $out 'pkg'
$release = Join-Path $out 'release'
Remove-Item $stage, $release -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $stage, $release -Force | Out-Null

function Run([string]$file, [string]$arguments) {
    Write-Host "> $file $arguments"
    $p = Start-Process -FilePath $file -ArgumentList $arguments -NoNewWindow -Wait -PassThru
    if ($p.ExitCode -ne 0) { throw "$file failed with exit code $($p.ExitCode)" }
}

# Self-test once on the build machine before packaging anything.
Run 'dotnet' "build `"$src\Clicksmith.SelfTest\Clicksmith.SelfTest.csproj`" -c Release -nologo"
Run 'dotnet' "`"$src\Clicksmith.SelfTest\bin\Release\net8.0\Clicksmith.SelfTest.dll`""

$artifacts = @()
foreach ($rid in $Rids) {
    $pkg = Join-Path $stage $rid
    $app = Join-Path $pkg 'app'
    $common = "-c Release -r $rid --self-contained false -p:DebugType=None -p:DebugSymbols=false -nologo"
    foreach ($proj in 'Clicksmith.Engine', 'Clicksmith.App', 'Clicksmith.DpiProbe') {
        Run 'dotnet' "publish `"$src\$proj\$proj.csproj`" $common -o `"$app`""
    }
    Run 'dotnet' "publish `"$src\Clicksmith.SelfTest\Clicksmith.SelfTest.csproj`" $common -o `"$pkg\scripts\selftest`""
    foreach ($f in 'install-release.ps1', 'uninstall.ps1', 'rollback.ps1', 'verify.ps1') { Copy-Item (Join-Path $PSScriptRoot $f) (Join-Path $pkg 'scripts') }
    Copy-Item (Join-Path $PSScriptRoot 'lib') (Join-Path $pkg 'scripts') -Recurse
    Set-Content -Path (Join-Path $pkg 'VERSION') -Value $version -NoNewline -Encoding ascii
    Set-Content -Path (Join-Path $pkg 'install.cmd') -Encoding ascii -Value (@(
        '@echo off', 'setlocal',
        'powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\install-release.ps1" %*',
        'set "code=%errorlevel%"', 'echo %cmdcmdline% | find /i "/c" >nul && pause', 'exit /b %code%') -join "`r`n")
    Set-Content -Path (Join-Path $pkg 'README.txt') -Encoding utf8 -Value @"
Clicksmith $version for Windows ($rid)

Double-click install.cmd. It verifies every file, checks for the .NET 8 Desktop Runtime
(and offers to install it), backs up your setup, installs to your user folder without
administrator rights, and opens Clicksmith.

https://github.com/samuelort/clicksmith
"@
    # Manifest of every file in the package (checked by install-release.ps1).
    $lines = Get-ChildItem $pkg -Recurse -File | Where-Object { $_.Name -ne 'PACKAGE.sha256' } | Sort-Object FullName | ForEach-Object {
        $rel = $_.FullName.Substring($pkg.Length + 1) -replace '\\', '/'
        "$((Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant())  $rel"
    }
    Set-Content -Path (Join-Path $pkg 'PACKAGE.sha256') -Value $lines -Encoding ascii

    $zipName = "Clicksmith-$version-$rid.zip"
    $zip = Join-Path $release $zipName
    Compress-Archive -Path (Join-Path $pkg '*') -DestinationPath $zip -CompressionLevel Optimal
    $artifacts += [ordered]@{ arch = $rid; file = $zipName; sha256 = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant(); size = (Get-Item $zip).Length }
}

[ordered]@{ version = $version; artifacts = $artifacts } | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $release 'release-windows.json') -Encoding utf8
Set-Content -Path (Join-Path $out 'version.txt') -Value $version -NoNewline -Encoding ascii
Write-Host "Packages ready in $release"
