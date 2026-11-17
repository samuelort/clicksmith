# Clicksmith terminal UI for Windows PowerShell 5.1+ and PowerShell 7.
# Forge animation, gradient banner, step loader with progress bar, failure summary and finale.
# The source is ASCII-only; every symbol is built from its code point so any console shows it.
# Plain text is used when output is redirected, when NO_COLOR is set, or with -NoAnim.

try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }

$script:Esc = [char]27
$script:Vt = $false
try { $script:Vt = [bool]$Host.UI.SupportsVirtualTerminal -and -not [Console]::IsOutputRedirected } catch { }
$script:Color = $script:Vt -and -not $env:NO_COLOR
$script:Anim = $script:Vt -and ($env:CLICKSMITH_NO_ANIM -ne '1')
$script:StepIndex = 0
$script:StepTotal = 0
$script:StartTime = Get-Date
$script:OnFail = $null
if (-not $script:LogFile) { $script:LogFile = Join-Path $env:LOCALAPPDATA 'Clicksmith\install.log' }

function Rgb([int]$r, [int]$g, [int]$b) { if ($script:Color) { "$($script:Esc)[38;2;$r;$g;${b}m" } else { '' } }
$script:R = if ($script:Color) { "$($script:Esc)[0m" } else { '' }
$script:B = if ($script:Color) { "$($script:Esc)[1m" } else { '' }
$script:Dim = if ($script:Color) { "$($script:Esc)[2m" } else { '' }
# Nordic palette (dark-terminal values): copper, sand, sage, clay, stone, paper.
$script:Copper = Rgb 212 152 112
$script:Sand = Rgb 226 201 168
$script:Sage = Rgb 142 176 137
$script:Clay = Rgb 214 128 112
$script:Stone = Rgb 150 142 132
$script:Paper = Rgb 236 232 226
$script:Brown = Rgb 150 110 80
$script:Gradient = @((Rgb 236 220 196), (Rgb 226 201 168), (Rgb 214 172 132), (Rgb 200 140 98), (Rgb 176 108 68))

$script:Block = [string][char]0x2588
$script:Shade = [string][char]0x2591
$script:Tick = [string][char]0x2714
$script:Cross = [string][char]0x2718
$script:Arrow = [string][char]0x21B3
$script:Ellipsis = [string][char]0x2026
$script:Dot = [string][char]0x00B7
$script:Spin = if ($env:WT_SESSION) { @(0x280B, 0x2819, 0x2839, 0x2838, 0x283C, 0x2834, 0x2826, 0x2827, 0x2807, 0x280F) | ForEach-Object { [string][char]$_ } } else { @('|', '/', '-', '\') }

function Write-Raw([string]$text) { [Console]::Write($text) }
function Write-Line([string]$text = '') { [Console]::WriteLine($text) }

function Format-Elapsed([TimeSpan]$t) {
    $s = [int][Math]::Floor($t.TotalSeconds)
    if ($s -lt 60) { return "${s}s" }
    return ('{0}m {1:00}s' -f [int][Math]::Floor($s / 60), ($s % 60))
}

# ---------- Forge animation ----------

function Get-Frame([string]$kind) {
    $base = @(
        "$($script:Paper)        |       |$($script:R)",
        "$($script:Stone)    ____$($script:Paper)\_______/$($script:Stone)____$($script:R)",
        "$($script:Stone)    \_______________/$($script:R)",
        "$($script:Stone)         |     |$($script:R)",
        "$($script:Stone)        /_______\$($script:R)")
    switch ($kind) {
        'raised' { $top = @(
            "$($script:Stone)                         _______$($script:R)",
            "$($script:Stone)                        |_______|$($script:R)",
            "$($script:Brown)                           | |$($script:R)",
            "$($script:Paper)          .---.$($script:Brown)            | |$($script:R)",
            "$($script:Paper)         /  |  \$($script:Brown)           | |$($script:R)",
            "$($script:Paper)        |   |   |$($script:Brown)          |_|$($script:R)") }
        'swing' { $top = @(
            '',
            "$($script:Stone)                    _______$($script:R)",
            "$($script:Stone)                   |_______|$($script:Brown)=========$($script:R)",
            "$($script:Paper)          .---.$($script:R)",
            "$($script:Paper)         /  |  \$($script:R)",
            "$($script:Paper)        |   |   |$($script:R)") }
        'strike1' { $top = @(
            "$($script:Sand)       *    .   *    .   *$($script:R)",
            "$($script:Copper)    *   $($script:Stone)  _______$($script:Copper)   .    *$($script:R)",
            "$($script:Sand)   -  - $($script:Stone) |_______|$($script:Brown)============$($script:R)",
            "$($script:Copper)     *  . $($script:Paper).---.$($script:Copper) .  *$($script:R)",
            "$($script:Paper)         /  $($script:Copper)|$($script:Paper)  \$($script:R)",
            "$($script:Paper)        |   $($script:Copper)|$($script:Paper)   |$($script:R)") }
        'strike2' { $top = @(
            "$($script:Copper)     .    *   .    *    .   *$($script:R)",
            "$($script:Copper)    *   $($script:Stone)  _______$($script:Copper)   .    *$($script:R)",
            "$($script:Sand)   -  - $($script:Stone) |_______|$($script:Brown)============$($script:R)",
            "$($script:Copper)     *  . $($script:Paper).---.$($script:Copper) .  *$($script:R)",
            "$($script:Paper)         /  $($script:Copper)|$($script:Paper)  \$($script:R)",
            "$($script:Paper)        |   $($script:Copper)|$($script:Paper)   |$($script:R)") }
        default { $top = @(
            "$($script:Stone)                         _______$($script:R)",
            "$($script:Stone)                        |_______|$($script:R)",
            "$($script:Brown)                           | |$($script:R)",
            "$($script:Paper)          .$($script:Copper)-*-$($script:Paper).$($script:Brown)            | |$($script:R)",
            "$($script:Paper)         /  $($script:Copper)|$($script:Paper)  \$($script:Brown)           | |$($script:R)",
            "$($script:Paper)        |   $($script:Copper)|$($script:Paper)   |$($script:Brown)          |_|$($script:R)") }
    }
    return @($top + $base)
}

$script:Drawn = $false
function Show-Frame([string[]]$lines) {
    if ($script:Drawn -and $script:Vt) { Write-Raw "$($script:Esc)[$($lines.Count)A" }
    foreach ($l in $lines) {
        if ($script:Vt) { Write-Raw "`r$($script:Esc)[K" }
        Write-Line "  $l"
    }
    $script:Drawn = $true
}

function Show-ForgeAnimation {
    if (-not $script:Anim) { Show-Frame (Get-Frame 'glow'); return }
    Write-Raw "$($script:Esc)[?25l"
    for ($i = 0; $i -lt 3; $i++) {
        Show-Frame (Get-Frame 'raised'); Start-Sleep -Milliseconds 320
        Show-Frame (Get-Frame 'swing'); Start-Sleep -Milliseconds 90
        Show-Frame (Get-Frame 'strike1'); Start-Sleep -Milliseconds 140
        Show-Frame (Get-Frame 'strike2'); Start-Sleep -Milliseconds 180
    }
    Show-Frame (Get-Frame 'glow')
    Write-Raw "$($script:Esc)[?25h"
}

function Show-Banner([string]$subtitle, [switch]$NoAnimation) {
    Write-Line
    $wide = $true
    try { $wide = [Console]::WindowWidth -ge 64 } catch { }
    if ($wide) {
        if ($NoAnimation) { $saved = $script:Anim; $script:Anim = $false; Show-ForgeAnimation; $script:Anim = $saved }
        else { Show-ForgeAnimation }
        Write-Line
        $art = @(
            ' #### ##    ##  #### ##  ##  #### ##   ## ## ###### ##  ##',
            '##    ##    ## ##    ## ##  ##    ### ### ##   ##   ##  ##',
            '##    ##    ## ##    ####    ###  ## # ## ##   ##   ######',
            '##    ##    ## ##    ## ##     ## ##   ## ##   ##   ##  ##',
            ' #### ##### ##  #### ##  ## ####  ##   ## ##   ##   ##  ##')
        for ($i = 0; $i -lt $art.Count; $i++) {
            Write-Line ("  " + $script:Gradient[$i] + $script:B + $art[$i].Replace('#', $script:Block) + $script:R)
        }
    } else {
        Write-Line "  $($script:Copper)$($script:B)CLICKSMITH$($script:R)"
    }
    Write-Line
    Write-Line "  $($script:Dim)Small clicks. A calmer workflow.$($script:R)"
    Write-Line "  $($script:Stone)$subtitle$($script:R)"
    Write-Line
}

# ---------- Steps ----------

$script:Quips = @('heating the forge', 'teaching the mouse new tricks', 'hammering out bugs', 'polishing every pixel',
    'convincing the compiler', 'aligning the sparks', 'tempering the steel', 'counting mouse buttons')

function Get-Bar([int]$done) {
    $cells = 12
    $filled = 0
    if ($script:StepTotal -gt 0) { $filled = [int][Math]::Floor($done * $cells / $script:StepTotal) }
    return ($script:Block * $filled) + ($script:Shade * ($cells - $filled))
}

function Write-StepDone([string]$label, [TimeSpan]$elapsed) {
    $counter = '{0,2}/{1}' -f $script:StepIndex, $script:StepTotal
    if ($script:Vt) {
        Write-Line ("`r$($script:Esc)[K  $($script:Sage)$($script:Tick)$($script:R) $($script:Sage)$(Get-Bar $script:StepIndex)$($script:R)  $counter  {0,-32} $($script:Stone){1,6}$($script:R)" -f $label, (Format-Elapsed $elapsed))
    } else {
        Write-Line ("  [$counter] {0} - done ({1})" -f $label, (Format-Elapsed $elapsed))
    }
}

function Add-Log([string]$text) {
    try {
        $dir = Split-Path -Parent $script:LogFile
        if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
        Add-Content -Path $script:LogFile -Value $text -Encoding UTF8
    } catch { }
}

function Start-Log([string]$title) {
    $dir = Split-Path -Parent $script:LogFile
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    Set-Content -Path $script:LogFile -Encoding UTF8 -Value ("=== Clicksmith $title - " + (Get-Date).ToString('u') + " ===")
    Add-Log ([Environment]::OSVersion.VersionString + ' ' + $env:PROCESSOR_ARCHITECTURE)
}

# Runs an external program with a live spinner. Output goes to the log.
function Invoke-ProcessStep([string]$label, [string]$file, [string]$arguments) {
    $script:StepIndex++
    Add-Log "`n### [$($script:StepIndex)/$($script:StepTotal)] $label`n> $file $arguments"
    $start = Get-Date
    $out = [IO.Path]::GetTempFileName()
    $err = [IO.Path]::GetTempFileName()
    try {
        $spArgs = @{ FilePath = $file; NoNewWindow = $true; PassThru = $true; RedirectStandardOutput = $out; RedirectStandardError = $err; WorkingDirectory = (Get-Location).Path }
        if ($arguments) { $spArgs.ArgumentList = $arguments }
        $p = Start-Process @spArgs
        $null = $p.Handle  # makes ExitCode reliable in Windows PowerShell
        $n = 0
        if ($script:Vt) { Write-Raw "$($script:Esc)[?25l" }
        while (-not $p.HasExited) {
            if ($script:Vt) {
                $el = (Get-Date) - $start
                $quip = ''
                try { if ([Console]::WindowWidth -ge 100 -and $el.TotalSeconds -ge 3) { $quip = "$($script:Dim)$($script:Dot) " + $script:Quips[[int]([Math]::Floor($el.TotalSeconds / 4)) % $script:Quips.Count] + "$($script:Ellipsis)$($script:R)" } } catch { }
                $counter = '{0,2}/{1}' -f $script:StepIndex, $script:StepTotal
                Write-Raw ("`r$($script:Esc)[K  $($script:Copper){0}$($script:R) $($script:Copper)$(Get-Bar ($script:StepIndex - 1))$($script:R)  $counter  {1,-32} $($script:Stone){2,6}$($script:R)  {3}" -f $script:Spin[$n % $script:Spin.Count], $label, (Format-Elapsed $el), $quip)
                $n++
            }
            Start-Sleep -Milliseconds 80
        }
        $p.WaitForExit()
        if ($script:Vt) { Write-Raw "$($script:Esc)[?25h" }
        Add-Log (Get-Content $out -Raw -ErrorAction SilentlyContinue)
        Add-Log (Get-Content $err -Raw -ErrorAction SilentlyContinue)
        $script:LastOutput = (Get-Content $out -Raw -ErrorAction SilentlyContinue)
        if ($p.ExitCode -ne 0) { Fail $label "exit code $($p.ExitCode)" }
        Write-StepDone $label ((Get-Date) - $start)
    } finally {
        Remove-Item $out, $err -Force -ErrorAction SilentlyContinue
    }
}

# Runs a PowerShell script block (quick steps: copying, registry, shortcuts).
function Invoke-Step([string]$label, [scriptblock]$action) {
    $script:StepIndex++
    Add-Log "`n### [$($script:StepIndex)/$($script:StepTotal)] $label"
    $start = Get-Date
    if ($script:Vt) {
        $counter = '{0,2}/{1}' -f $script:StepIndex, $script:StepTotal
        Write-Raw ("`r$($script:Esc)[K  $($script:Copper){0}$($script:R) $($script:Copper)$(Get-Bar ($script:StepIndex - 1))$($script:R)  $counter  {1}" -f $script:Spin[0], $label)
    }
    $script:LastResult = ''
    try {
        $result = & $action
        if ($result) {
            Add-Log ($result | Out-String)
            $script:LastResult = [string]($result | Select-Object -Last 1)
        }
    } catch {
        Add-Log ($_ | Out-String)
        Fail $label $_.Exception.Message
    }
    Write-StepDone $label ((Get-Date) - $start)
}

function Skip-Step([string]$label) {
    $script:StepIndex++
    Write-Line "  $($script:Sand)!$($script:R)  $label"
}

function Write-Note([string]$text) { if ($text) { Write-Line "      $($script:Stone)$($script:Arrow) $text$($script:R)" } }

function Fail([string]$label, [string]$reason) {
    if ($script:Vt) { Write-Raw "`r$($script:Esc)[K$($script:Esc)[?25h" }
    Write-Line ("  $($script:Clay)$($script:Cross)$($script:R) {0,2}/{1}  $($script:Clay)$($script:B){2}$($script:R)" -f $script:StepIndex, $script:StepTotal, $label)
    Write-Line
    Write-Line "  $($script:Clay)$($script:B)The forge went cold at `"$label`".$($script:R) $reason"
    if ($script:OnFail) {
        Write-Line "  $($script:Sand)Putting the previous version back$($script:Ellipsis)$($script:R)"
        try { & $script:OnFail; Write-Line "  $($script:Sage)$($script:Tick) Previous version restored.$($script:R)" }
        catch { Write-Line "  $($script:Clay)Rollback failed: $($_.Exception.Message)$($script:R)" }
    }
    Write-Line
    Write-Line "  Last lines of the log:"
    Write-Line
    Get-Content $script:LogFile -Tail 25 -ErrorAction SilentlyContinue | ForEach-Object { Write-Line "    $($script:Stone)|$($script:R) $_" }
    Write-Line
    Write-Line "  Full log: $($script:LogFile)"
    Write-Line '  Paste the lines above when asking for help.'
    Write-Line
    exit 1
}

function Show-Finale([string]$message) {
    $total = Format-Elapsed ((Get-Date) - $script:StartTime)
    Write-Line
    Write-Line "  $($script:Sand)            .   *   .$($script:R)"
    Write-Line "  $($script:Copper)         *   \  |  /   *$($script:R)"
    Write-Line "  $($script:Copper)      .   --- $($script:Paper).-*-.$($script:Copper) ---   .$($script:R)"
    Write-Line "  $($script:Paper)         /  $($script:Copper)|$($script:Paper)  \$($script:R)"
    Write-Line "  $($script:Paper)        |   $($script:Copper)|$($script:Paper)   |$($script:R)"
    Write-Line "  $($script:Paper)        |       |$($script:R)"
    Write-Line "  $($script:Paper)         \_____/$($script:R)"
    Write-Line
    Write-Line "  $($script:Sage)$($script:B)$($script:Tick) $message$($script:R)  $($script:Stone)($total)$($script:R)"
    Write-Line
}

function Show-Box([string]$title, [string[]]$lines) {
    $w = 62
    $h = [string][char]0x2500
    Write-Line ("  $($script:Stone)" + [char]0x256D + ($h * ($w + 2)) + [char]0x256E + "$($script:R)")
    Write-Line ("  $($script:Stone)" + [char]0x2502 + "$($script:R) $($script:B)" + $title.PadRight($w) + "$($script:R) $($script:Stone)" + [char]0x2502 + "$($script:R)")
    foreach ($l in $lines) {
        Write-Line ("  $($script:Stone)" + [char]0x2502 + "$($script:R) " + $l.PadRight($w) + " $($script:Stone)" + [char]0x2502 + "$($script:R)")
    }
    Write-Line ("  $($script:Stone)" + [char]0x2570 + ($h * ($w + 2)) + [char]0x256F + "$($script:R)")
    Write-Line
}
