<#
.SYNOPSIS
    在任务栏托盘区显示飞智手柄的电量 / 充电 / 连接状态。

.DESCRIPTION
    复用 flydigi_battery.ps1 的读取逻辑（dot-source -AsLibrary），
    定时刷新托盘图标（电池形状 + 电量填充）、提示文字和右键菜单。

    图标含义：
      电池形状 + 两位黑色百分比数字（1px 笔画）= 档位换算后的电量
      数值为 0/17/33/50/67/83；满电（100%）不画数字，显示满格电池
      0–2 红、3–4 黄、5–6 绿；内部填充按 level/6 从左往右递增
      右侧小凸点与底部横线变亮绿色 = 正在由 USB 供电（充电中）
      灰色电池 + `?` = 已连接，但 7 档电量不可用
      灰色手柄 + 红色斜杠 = 没检测到手柄（未连接）

    提示文字显示百分比与实际档位，例如 `67% (4/6) · 无线`；
    未连接时显示 `未连接`，已连接但无电量时显示 `已连接 · 电量不可用`。

.PARAMETER IntervalSec
    采样间隔（秒），默认 20。

.PARAMETER Install
    把本程序加入当前用户的开机启动（启动项快捷方式指向 flydigi_tray.vbs），不启动托盘。

.PARAMETER Uninstall
    移除开机启动项。

.PARAMETER Preview
    仅渲染一组图标到 PNG 供检查，不启动托盘。

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File .\flydigi_tray.ps1
    powershell -NoProfile -ExecutionPolicy Bypass -File .\flydigi_tray.ps1 -Preview -PreviewPath .\icon_preview.png
#>
[CmdletBinding()]
param(
    [int]$IntervalSec = 20,
    [switch]$Hidden,
    [switch]$Install,
    [switch]$Uninstall,
    [switch]$Preview,
    [string]$PreviewPath = ''
)

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
. (Join-Path $PSScriptRoot 'flydigi_battery.ps1') -AsLibrary

if (-not ('Tray.Native' -as [type])) {
    Add-Type -Namespace Tray -Name Native -MemberDefinition @'
[DllImport("user32.dll", SetLastError = true)]
public static extern bool DestroyIcon(IntPtr hIcon);
'@
}

# ------------------------------------------------------------ 开机启动 ----
function Get-StartupShortcut {
    Join-Path ([Environment]::GetFolderPath('Startup')) 'flydigi-battery-tray.lnk'
}

function Install-Autostart {
    $vbs = Join-Path $PSScriptRoot 'flydigi_tray.vbs'
    if (-not (Test-Path $vbs)) { throw "缺少启动器: $vbs" }
    $ws = New-Object -ComObject WScript.Shell
    $lnk = $ws.CreateShortcut((Get-StartupShortcut))
    $lnk.TargetPath = Join-Path $env:SystemRoot 'System32\wscript.exe'
    $lnk.Arguments = '"' + $vbs + '"'
    $lnk.WorkingDirectory = $PSScriptRoot
    $lnk.Description = 'Flydigi 手柄电量托盘'
    $lnk.Save()
    Write-Host "已加入开机启动: $(Get-StartupShortcut)"
}

function Remove-Autostart {
    $p = Get-StartupShortcut
    if (Test-Path $p) { Remove-Item $p -Force; Write-Host "已移除开机启动: $p" }
    else { Write-Host '本来就没有开机启动项。' }
}

if ($Install) { Install-Autostart; return }
if ($Uninstall) { Remove-Autostart; return }

# ---------------------------------------------------------------- 画图 ----
function Get-LevelColor {
    param($Level)
    if ($null -eq $Level -or [int]$Level -lt 0) { return [System.Drawing.Color]::FromArgb(255, 150, 150, 150) }
    if ([int]$Level -le 2) { return [System.Drawing.Color]::FromArgb(255, 231, 76, 60) }
    if ([int]$Level -le 4) { return [System.Drawing.Color]::FromArgb(255, 241, 196, 15) }
    return [System.Drawing.Color]::FromArgb(255, 46, 204, 113)
}

# 以 16x16 为设计栅格，按实际图标尺寸等比缩放
function New-RoundedPath {
    param([float]$X, [float]$Y, [float]$W, [float]$H, [float]$R)
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = [float](2 * $R)
    $p.AddArc($X, $Y, $d, $d, 180, 90)
    $p.AddArc($X + $W - $d, $Y, $d, $d, 270, 90)
    $p.AddArc($X + $W - $d, $Y + $H - $d, $d, $d, 0, 90)
    $p.AddArc($X, $Y + $H - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function New-DisabledGamepadBitmap {
    param([int]$Size = 16)
    $bmp = New-Object System.Drawing.Bitmap($Size, $Size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)
    $s = [double]$Size / 16.0

    # 手柄机身 + 两侧握把
    $body = [System.Drawing.Color]::FromArgb(255, 138, 138, 138)
    $bodyBrush = New-Object System.Drawing.SolidBrush($body)
    $path = New-RoundedPath -X ([float](1.9 * $s)) -Y ([float](5.2 * $s)) -W ([float](12.2 * $s)) -H ([float](6.0 * $s)) -R ([float](2.6 * $s))
    $g.FillPath($bodyBrush, $path)
    $path.Dispose()
    $gripL = New-RoundedPath -X ([float](1.6 * $s)) -Y ([float](7.6 * $s)) -W ([float](3.6 * $s)) -H ([float](5.0 * $s)) -R ([float](1.7 * $s))
    $gripR = New-RoundedPath -X ([float](10.8 * $s)) -Y ([float](7.6 * $s)) -W ([float](3.6 * $s)) -H ([float](5.0 * $s)) -R ([float](1.7 * $s))
    $g.FillPath($bodyBrush, $gripL)
    $g.FillPath($bodyBrush, $gripR)
    $gripL.Dispose(); $gripR.Dispose()
    $bodyBrush.Dispose()

    # 白色描边让灰色机身在小尺寸下也立得住
    $edge = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(200, 235, 235, 235), [float](0.7 * $s))
    $edgePath = New-RoundedPath -X ([float](1.9 * $s)) -Y ([float](5.2 * $s)) -W ([float](12.2 * $s)) -H ([float](6.0 * $s)) -R ([float](2.6 * $s))
    $g.DrawPath($edge, $edgePath)
    $edgePath.Dispose()
    $edge.Dispose()

    # 斜杠：先画白色光晕再画红线
    $x1 = [float](2.2 * $s); $y1 = [float](13.4 * $s)
    $x2 = [float](13.8 * $s); $y2 = [float](2.4 * $s)
    $halo = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(235, 255, 255, 255), [float](3.4 * $s))
    $halo.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $halo.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $g.DrawLine($halo, $x1, $y1, $x2, $y2)
    $halo.Dispose()
    $slash = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 231, 76, 60), [float](2.0 * $s))
    $slash.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $slash.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $g.DrawLine($slash, $x1, $y1, $x2, $y2)
    $slash.Dispose()

    $g.Dispose()
    return $bmp
}

# 3x7 点阵数字；16px 下用 1px 笔画，细黑字在浅色底上仍可辨认
$script:DigitGlyphs = @{
    '0' = @('###', '#.#', '#.#', '#.#', '#.#', '#.#', '###')
    '1' = @('.#.', '##.', '.#.', '.#.', '.#.', '.#.', '###')
    '2' = @('###', '..#', '..#', '###', '#..', '#..', '###')
    '3' = @('###', '..#', '..#', '###', '..#', '..#', '###')
    '4' = @('#.#', '#.#', '#.#', '###', '..#', '..#', '..#')
    '5' = @('###', '#..', '#..', '###', '..#', '..#', '###')
    '6' = @('###', '#..', '#..', '###', '#.#', '#.#', '###')
    '7' = @('###', '..#', '..#', '..#', '..#', '..#', '..#')
    '8' = @('###', '#.#', '#.#', '###', '#.#', '#.#', '###')
    '9' = @('###', '#.#', '#.#', '###', '..#', '..#', '###')
    '?' = @('###', '..#', '.##', '.#.', '...', '.#.', '.#.')
}

function Get-LightColor {
    param($Color, [double]$Amount = 0.55)
    return [System.Drawing.Color]::FromArgb(255,
        [int][math]::Round($Color.R + (255 - $Color.R) * $Amount),
        [int][math]::Round($Color.G + (255 - $Color.G) * $Amount),
        [int][math]::Round($Color.B + (255 - $Color.B) * $Amount))
}

function Get-PixWidth {
    param([int]$Chars, [int]$Cell)
    if ($Chars -le 0) { return 0 }
    $advance = $Cell * 3 + 1
    return ($advance * ($Chars - 1) + $Cell * 3)
}

function Get-PixTextWidth {
    param([string]$Text, [int]$Cell)
    if ([string]::IsNullOrEmpty($Text)) { return 0 }
    return (Get-PixWidth -Chars $Text.Length -Cell $Cell)
}

function Draw-PixDigit {
    param($g, [string]$Text, [float]$X, [float]$Y, [int]$Cell, $Brush)
    $advance = $Cell * 3 + 1
    for ($i = 0; $i -lt $Text.Length; $i++) {
        $ch = [string]$Text[$i]
        if (-not $script:DigitGlyphs.ContainsKey($ch)) { continue }
        $glyph = $script:DigitGlyphs[$ch]
        for ($r = 0; $r -lt $glyph.Count; $r++) {
            $row = $glyph[$r]
            for ($c = 0; $c -lt 3; $c++) {
                if ($row[$c] -eq '#') {
                    $g.FillRectangle($Brush, [float]($X + $i * $advance + $c * $Cell), [float]($Y + $r * $Cell), [float]($Cell + 0.3), [float]($Cell + 0.3))
                }
            }
        }
    }
}

function Test-StatusConnected {
    param($Status)
    if ($null -eq $Status) { return $false }
    if ($Status -is [System.Collections.IDictionary]) { return [bool]$Status['connected'] }
    if ($Status.PSObject.Properties.Name -contains 'connected') { return [bool]$Status.connected }
    return $false
}

function New-BatteryCoreBitmap {
    param([int]$Size, $FillColor, $EmptyColor, [double]$Fraction, [bool]$Charging, [string]$Text,
        [System.Drawing.Color]$TextColor = [System.Drawing.Color]::Black)

    $bmp = New-Object System.Drawing.Bitmap($Size, $Size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::None
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
    $g.Clear([System.Drawing.Color]::Transparent)

    $s = [double]$Size / 16.0
    $cell = [int][math]::Max(2, [math]::Round(2.0 * $s))
    $nubW = if ($Size -le 16) { 1 } else { [int][math]::Max(2, [math]::Round(1.8 * $s)) }
    $nubH = [int][math]::Max(3, [math]::Round(4.0 * $s))

    # 电池体：16px 下用满宽度承载两位数字
    $bodyH = $cell * 5 + 2
    $margin = if ($Size -ge 20) { 2 } else { 0 }
    $bodyW = [math]::Min([int][math]::Round(15.0 * $s), $Size - $nubW - $margin)
    $bx = [math]::Max(0, [int][math]::Round(($Size - $bodyW - $nubW) / 2.0))
    $by = [math]::Max(0, [int][math]::Round(($Size - $bodyH) / 2.0))
    $bw = $bodyW
    $bh = $bodyH
    $border = [int][math]::Max(1, [math]::Round($s))

    # 空电部分：同色系暗色
    $bg = New-Object System.Drawing.SolidBrush($EmptyColor)
    $g.FillRectangle($bg, $bx, $by, $bw, $bh)
    $bg.Dispose()

    $ix = $bx + $border; $iy = $by + $border
    $iw = [int][math]::Max(0, $bw - 2 * $border)
    $ih = [int][math]::Max(0, $bh - 2 * $border)
    $frac = [math]::Max(0.0, [math]::Min(1.0, $Fraction))
    $fw = [int][math]::Round($iw * $frac)
    if ($fw -gt 0) {
        $fill = New-Object System.Drawing.SolidBrush($FillColor)
        $g.FillRectangle($fill, $ix, $iy, $fw, $ih)
        $fill.Dispose()
    }

    # 外框用亮色
    $edge = New-Object System.Drawing.SolidBrush($FillColor)
    $g.FillRectangle($edge, $bx, $by, $bw, $border)
    $g.FillRectangle($edge, $bx, ($by + $bh - $border), $bw, $border)
    $g.FillRectangle($edge, $bx, $by, $border, $bh)
    $g.FillRectangle($edge, ($bx + $bw - $border), $by, $border, $bh)
    $edge.Dispose()

    $nubColor = $FillColor
    if ($Charging) { $nubColor = [System.Drawing.Color]::FromArgb(255, 120, 255, 140) }
    $nub = New-Object System.Drawing.SolidBrush($nubColor)
    $g.FillRectangle($nub, ($bx + $bw), [int]($by + ($bh - $nubH) / 2), $nubW, $nubH)
    $nub.Dispose()

    # 百分比数字：按「两位数字」确定字号，单位和十位大小一致
    if (-not [string]::IsNullOrEmpty($Text)) {
        $textCell = $cell
        $layoutChars = [math]::Max(2, $Text.Length)
        while ($textCell -gt 1 -and
            ((Get-PixWidth -Chars $layoutChars -Cell $textCell) -gt $iw -or ($textCell * 7) -gt $ih)) {
            $textCell--
        }
        if ($textCell -gt 4) { $textCell = 4 }

        $digW = [float](Get-PixTextWidth -Text $Text -Cell $textCell)
        $digH = [float]($textCell * 7)
        $dx = [float][math]::Round($bx + ($bw - $digW) / 2)
        $dy = [float][math]::Round($by + ($bh - $digH) / 2)

        $textBrush = New-Object System.Drawing.SolidBrush($TextColor)
        Draw-PixDigit $g $Text $dx $dy $textCell $textBrush
        $textBrush.Dispose()
    }

    if ($Charging) {
        $acc = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 120, 255, 140))
        $y = $by + $bh + $border
        if ($y -lt $Size) { $g.FillRectangle($acc, $bx, $y, $bw, $border) }
        $acc.Dispose()
    }

    $g.Dispose()
    return $bmp
}

function New-UnknownBatteryBitmap {
    param([int]$Size = 16)
    $gray = [System.Drawing.Color]::FromArgb(255, 150, 150, 150)
    $dark = [System.Drawing.Color]::FromArgb(255, 55, 55, 55)
    return (New-BatteryCoreBitmap -Size $Size -FillColor $gray -EmptyColor $dark -Fraction 0.0 -Charging $false -Text '?' -TextColor ([System.Drawing.Color]::White))
}

function New-BatteryBitmap {
    param($Status, [int]$Size = 16)

    $level = $null
    $maxLevel = 6
    $charging = $false
    $connected = Test-StatusConnected -Status $Status
    if ($Status) {
        if ($Status.ok -and $Status.devices.Count -gt 0) {
            $d = $Status.devices[0]
            $level = $d.Level
            $maxLevel = $d.LevelMax
            if ("$($d.Charging)" -match '充电中|已充满|接着线') { $charging = $true }
        }
    }
    if ($null -eq $level) {
        if ($connected) { return (New-UnknownBatteryBitmap -Size $Size) }
        return (New-DisabledGamepadBitmap -Size $Size)
    }

    $color = Get-LevelColor -Level $level
    $empty = Get-LightColor -Color $color
    $frac = 0.0
    if ($maxLevel -gt 0 -and [int]$level -gt 0) { $frac = [double]$level / [double]$maxLevel }
    if ($frac -gt 1.0) { $frac = 1.0 }
    $pct = Get-LevelPercent6 -Level ([int]$level)
    $label = if ($null -eq $pct) { '?' } elseif ($pct.Percent -ge 100) { '' } else { [string]$pct.Percent }
    return (New-BatteryCoreBitmap -Size $Size -FillColor $color -EmptyColor $empty -Fraction $frac -Charging $charging -Text $label -TextColor ([System.Drawing.Color]::Black))
}

function New-TrayIcon {
    param([System.Drawing.Bitmap]$Bitmap)
    $h = $Bitmap.GetHicon()
    $ico = [System.Drawing.Icon]::FromHandle($h)
    $clone = $ico.Clone()
    [Tray.Native]::DestroyIcon($h) | Out-Null
    return $clone
}

# ---------------------------------------------------------------- 预览 ----
if ($Preview) {
    $sizes = @(16, 32)
    $cellSize = 40
    $cols = 10
    $sheet = New-Object System.Drawing.Bitmap(($cols * $cellSize), ($sizes.Count * $cellSize))
    $sg = [System.Drawing.Graphics]::FromImage($sheet)
    $sg.Clear([System.Drawing.Color]::FromArgb(255, 32, 32, 32))
    $row = 0
    foreach ($sz in $sizes) {
        $col = 0
        foreach ($lv in @(0, 1, 2, 3, 4, 5, 6)) {
            $fake = [pscustomobject]@{ ok = $true; connected = $true; devices = @([pscustomobject]@{ Level = $lv; LevelMax = 6; Charging = '无线电池供电 · 未充电' }) }
            $b = New-BatteryBitmap -Status $fake -Size $sz
            $sg.DrawImage($b, ($col * $cellSize + [int](($cellSize - $sz) / 2)), ($row * $cellSize + [int](($cellSize - $sz) / 2)))
            $b.Dispose(); $col++
        }
        $chg = [pscustomobject]@{ ok = $true; connected = $true; devices = @([pscustomobject]@{ Level = 4; LevelMax = 6; Charging = 'USB 有线 · 充电中' }) }
        $b = New-BatteryBitmap -Status $chg -Size $sz
        $sg.DrawImage($b, ($col * $cellSize + [int](($cellSize - $sz) / 2)), ($row * $cellSize + [int](($cellSize - $sz) / 2))); $b.Dispose(); $col++
        $unknown = [pscustomobject]@{ ok = $false; connected = $true; devices = @() }
        $b = New-BatteryBitmap -Status $unknown -Size $sz
        $sg.DrawImage($b, ($col * $cellSize + [int](($cellSize - $sz) / 2)), ($row * $cellSize + [int](($cellSize - $sz) / 2))); $b.Dispose(); $col++
        $none = [pscustomobject]@{ ok = $false; connected = $false; devices = @() }
        $b = New-BatteryBitmap -Status $none -Size $sz
        $sg.DrawImage($b, ($col * $cellSize + [int](($cellSize - $sz) / 2)), ($row * $cellSize + [int](($cellSize - $sz) / 2))); $b.Dispose()
        $row++
    }
    $sg.Dispose()
    $sheet.Save($PreviewPath, [System.Drawing.Imaging.ImageFormat]::Png)
    Write-Host "已生成预览: $PreviewPath"
    return
}

# ---------------------------------------------------------------- 托盘 ----
$createdNew = $false
$script:trayMutex = New-Object System.Threading.Mutex($true, 'Local\FlydigiBatteryTray', [ref]$createdNew)
if (-not $createdNew) {
    Write-Host '托盘程序已经在运行了。'
    return
}

$script:ni = $null
$script:curIcon = $null
$script:lastStatus = $null
$script:lastWarnBand = 0
$script:timer = $null
$script:miAuto = $null

function Get-TooltipText {
    param($Status)
    if (-not $Status.ok -or $Status.devices.Count -eq 0) {
        if (Test-StatusConnected -Status $Status) { return '已连接 · 电量不可用' }
        return '未连接'
    }
    $d = $Status.devices[0]
    $where = '无线'
    if ("$($d.Connect)" -match 'USB 有线') { $where = '有线' }
    $percent = if ($null -ne $d.Percent -and [int]$d.Percent -ge 0) { "$($d.Percent)%" } else { '?' }
    $t = '{0} ({1}/{2}) · {3}' -f $percent, $d.Level, $d.LevelMax, $where
    if ($t.Length -gt 60) { $t = $t.Substring(0, 60) }
    return $t
}

function Get-DetailText {
    param($Status)
    if (-not $Status.ok -or $Status.devices.Count -eq 0) {
        $msg = if (Test-StatusConnected -Status $Status) { '手柄已连接（XInput），电量不可用' } else { '未连接' }
        if ($Status.error) { $msg += "`n$($Status.error)" }
        return $msg
    }
    $d = $Status.devices[0]
    $lines = @()
    $lines += ('电量: {0}/{1} {2} {3}' -f $d.Level, $d.LevelMax, $d.LevelText, $d.Approx)
    $lines += ('充电: ' + $d.Charging)
    $lines += ('连接: ' + $d.Connect)
    if ($d.PSObject.Properties.Name -contains 'Uid' -and $d.Uid) { $lines += ('UID: ' + $d.Uid) }
    $lines += ('更新: ' + (Get-Date).ToString('HH:mm:ss'))
    return ($lines -join "`n")
}

function Show-Details {
    $st = $script:lastStatus
    if ($null -eq $st) { $st = Read-Status; $script:lastStatus = $st }
    $script:ni.ShowBalloonTip(8000, '飞智手柄状态', (Get-DetailText -Status $st), [System.Windows.Forms.ToolTipIcon]::Info)
}

function Update-Tray {
    $st = $null
    try { $st = Read-Status }
    catch { $st = [pscustomobject]@{ ok = $false; connected = $false; connectionSource = $null; devices = @(); error = $_.Exception.Message } }
    if ($null -eq $st) { $st = [pscustomobject]@{ ok = $false; connected = $false; connectionSource = $null; devices = @(); error = '读取失败' } }
    $script:lastStatus = $st

    $size = [System.Windows.Forms.SystemInformation]::SmallIconSize.Width
    if ($size -lt 16) { $size = 16 }
    $bmp = New-BatteryBitmap -Status $st -Size $size
    $ico = New-TrayIcon -Bitmap $bmp
    $bmp.Dispose()

    $prev = $script:curIcon
    $script:ni.Icon = $ico
    $script:curIcon = $ico
    if ($null -ne $prev) { $prev.Dispose() }

    $script:ni.Text = Get-TooltipText -Status $st
    if ($null -ne $script:miAuto) { $script:miAuto.Checked = Test-Path (Get-StartupShortcut) }

    # 低电量提醒：每跨入更低一档只弹一次
    if ($st.ok -and $st.devices.Count -gt 0) {
        $d = $st.devices[0]
        $band = 0
        if ([int]$d.LevelMax -gt 0) {
            $frac = [double]$d.Level / [double]$d.LevelMax
            if ($frac -lt 0.15) { $band = 2 } elseif ($frac -lt 0.35) { $band = 1 }
        }
        if ($band -gt $script:lastWarnBand) {
            $script:ni.ShowBalloonTip(6000, '飞智手柄电量偏低', (Get-DetailText -Status $st), [System.Windows.Forms.ToolTipIcon]::Warning)
        }
        $script:lastWarnBand = $band
    }
}

$menu = New-Object System.Windows.Forms.ContextMenuStrip
$miRefresh = New-Object System.Windows.Forms.ToolStripMenuItem -ArgumentList '立即刷新'
$miRefresh.Add_Click({ Update-Tray })
[void]$menu.Items.Add($miRefresh)

$miDetail = New-Object System.Windows.Forms.ToolStripMenuItem -ArgumentList '查看详情'
$miDetail.Add_Click({ Show-Details })
[void]$menu.Items.Add($miDetail)

$miInterval = New-Object System.Windows.Forms.ToolStripMenuItem -ArgumentList '采样间隔'
foreach ($sec in @(10, 20, 60)) {
    $mi = New-Object System.Windows.Forms.ToolStripMenuItem -ArgumentList ("$sec 秒")
    $mi.Tag = $sec
    $mi.Add_Click({
            param($sender, $eventArgs)
            $script:timer.Interval = [int]$sender.Tag * 1000
            Update-Tray
        })
    [void]$miInterval.DropDownItems.Add($mi)
}
[void]$menu.Items.Add($miInterval)

[void]$menu.Items.Add((New-Object System.Windows.Forms.ToolStripSeparator))

$script:miAuto = New-Object System.Windows.Forms.ToolStripMenuItem -ArgumentList '开机自启'
$script:miAuto.Add_Click({
        if (Test-Path (Get-StartupShortcut)) { Remove-Autostart } else { Install-Autostart }
        Update-Tray
    })
[void]$menu.Items.Add($script:miAuto)

$miQuit = New-Object System.Windows.Forms.ToolStripMenuItem -ArgumentList '退出'
$miQuit.Add_Click({
        $script:ni.Visible = $false
        $script:ni.Dispose()
        [System.Windows.Forms.Application]::Exit()
    })
[void]$menu.Items.Add($miQuit)

$script:ni = New-Object System.Windows.Forms.NotifyIcon
$script:ni.ContextMenuStrip = $menu
$script:ni.Text = '飞智手柄电量'
$script:ni.Icon = [System.Drawing.SystemIcons]::Application
$script:ni.Visible = $true
$script:ni.Add_MouseClick({
        param($sender, $eventArgs)
        if ($eventArgs.Button -eq [System.Windows.Forms.MouseButtons]::Left) { Show-Details }
    })

$script:timer = New-Object System.Windows.Forms.Timer
$script:timer.Interval = [math]::Max(5, $IntervalSec) * 1000
$script:timer.Add_Tick({ Update-Tray })
$script:timer.Start()

Update-Tray
[System.Windows.Forms.Application]::Run()
