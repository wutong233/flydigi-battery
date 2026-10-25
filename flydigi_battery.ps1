<#
.SYNOPSIS
    读取飞智（Flydigi）手柄电量。

.DESCRIPTION
    数据通道：
      pipe    - 问飞智空间站的后台服务 SpaceStationService（命名管道 \\.\pipe\fcs.sock），
                返回值与空间站界面显示的是同一个字段（0–6 档）。管道同一时间只允许一个客户端，
                空间站 App 在跑时管道被它占着，会报 ERROR_PIPE_BUSY —— 需要先退出空间站 App
                （后台服务会继续运行，手柄不受影响）。
      XInput  - 只用来确认手柄是否已连接（XInputGetState），不读取电量。

.EXAMPLE
    .\flydigi_battery.ps1
    .\flydigi_battery.ps1 -Json
    .\flydigi_battery.ps1 -Source pipe -Watch 3
#>
[CmdletBinding()]
param(
    [ValidateSet('auto', 'pipe')]
    [string]$Source = 'auto',
    [switch]$Json,
    [string]$PipeName = 'fcs.sock',
    [int]$TimeoutMs = 3000,
    [int]$Watch = 0,
    # 只加载函数、不执行主流程（供 flydigi_tray.ps1 之类的外壳 dot-source 复用）
    [switch]$AsLibrary
)

$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------- XInput ----
$script:XInputSource = @'
using System;
using System.Runtime.InteropServices;

namespace FdBattery
{
    public static class XInput
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct XINPUT_GAMEPAD
        {
            public ushort wButtons;
            public byte bLeftTrigger;
            public byte bRightTrigger;
            public short sThumbLX;
            public short sThumbLY;
            public short sThumbRX;
            public short sThumbRY;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct XINPUT_STATE
        {
            public uint dwPacketNumber;
            public XINPUT_GAMEPAD Gamepad;
        }

        [DllImport("xinput1_4.dll")]
        public static extern uint XInputGetState(uint dwUserIndex, ref XINPUT_STATE pState);
    }
}
'@

function Ensure-XInputType {
    if (-not ('FdBattery.XInput' -as [type])) {
        Add-Type -TypeDefinition $script:XInputSource
    }
}

function Test-XInputConnected {
    Ensure-XInputType
    for ($slot = 0; $slot -lt 4; $slot++) {
        $state = New-Object FdBattery.XInput+XINPUT_STATE
        if ([FdBattery.XInput]::XInputGetState([uint32]$slot, [ref]$state) -eq 0) { return $true }
    }
    return $false
}

# ------------------------------------------------------- 飞智服务端协议 ----
# 传输帧（见 asar 内 .vite/build/main.js）：
#   "FDG_PROTOCOL\n" + int32le(len) + protobuf
#   "FDG_CHUNK\n"    + int32le(total) + int32le(n) + n*(int32le(len) + bytes)
#   "FDG_COMPRESSED\n" + int32le(len) + gzip(protobuf)
function Read-Varint {
    param([byte[]]$Bytes, [ref]$Pos)
    $shift = 0
    [long]$val = 0
    while ($true) {
        if ($Pos.Value -ge $Bytes.Length) { throw 'protobuf 越界' }
        $b = $Bytes[$Pos.Value]
        $Pos.Value++
        $val = $val -bor ([long]($b -band 0x7F) -shl $shift)
        if (($b -band 0x80) -eq 0) { return $val }
        $shift += 7
        if ($shift -gt 63) { throw 'varint 过长' }
    }
}

function Read-Field {
    param([byte[]]$Bytes, [ref]$Pos)
    $key = Read-Varint -Bytes $Bytes -Pos $Pos
    $f = [int]($key -shr 3)
    $w = [int]($key -band 7)
    switch ($w) {
        0 { return @{ Field = $f; Wire = 0; Varint = (Read-Varint -Bytes $Bytes -Pos $Pos) } }
        1 { $Pos.Value += 8; return @{ Field = $f; Wire = 1 } }
        2 {
            $len = [int](Read-Varint -Bytes $Bytes -Pos $Pos)
            $buf = New-Object byte[] $len
            [Array]::Copy($Bytes, $Pos.Value, $buf, 0, $len)
            $Pos.Value += $len
            return @{ Field = $f; Wire = 2; Bytes = $buf }
        }
        5 { $Pos.Value += 4; return @{ Field = $f; Wire = 5 } }
        default { throw "不支持的 wire type $w" }
    }
}

function ConvertFrom-DeviceInfo {
    param([byte[]]$Bytes)
    $d = [ordered]@{
        productName = $null; deviceCode = $null; uid = $null; category = $null
        connectType = $null; deviceType = $null; isConnected = $null; battery = $null
        vendorId = $null; productId = $null
    }
    $pos = 0
    while ($pos -lt $Bytes.Length) {
        $fld = Read-Field -Bytes $Bytes -Pos ([ref]$pos)
        switch ($fld.Wire) {
            0 {
                switch ($fld.Field) {
                    2 { $d.category = $fld.Varint }
                    3 { $d.connectType = $fld.Varint }
                    5 { $d.deviceType = $fld.Varint }
                    8 { $d.isConnected = ($fld.Varint -ne 0) }
                    9 { $d.battery = $fld.Varint }
                    11 { $d.vendorId = $fld.Varint }
                    12 { $d.productId = $fld.Varint }
                }
            }
            2 {
                switch ($fld.Field) {
                    1 { $d.productName = [Text.Encoding]::UTF8.GetString($fld.Bytes) }
                    4 { $d.deviceCode = [Text.Encoding]::UTF8.GetString($fld.Bytes) }
                    6 { $d.deviceMac = ([BitConverter]::ToString($fld.Bytes) -replace '-', '').ToLower() }
                    7 {
                        # 有的固件直接回 ASCII 十六进制串，有的回 16 字节原始 UID
                        $s = [Text.Encoding]::UTF8.GetString($fld.Bytes)
                        if ($s -match '^[0-9a-fA-F]{16,64}$') { $d.uid = $s.ToLower() }
                        else { $d.uid = ([BitConverter]::ToString($fld.Bytes) -replace '-', '').ToLower() }
                    }
                }
            }
        }
    }
    return $d
}

function ConvertFrom-ConnectedDevices {
    param([byte[]]$Bytes)
    $devices = @()
    $pos = 0
    while ($pos -lt $Bytes.Length) {
        $fld = Read-Field -Bytes $Bytes -Pos ([ref]$pos)
        if ($fld.Wire -eq 2 -and $fld.Field -eq 1) {
            $devices += ConvertFrom-DeviceInfo -Bytes $fld.Bytes
        }
    }
    return $devices
}

function ConvertFrom-IpcResult {
    param([byte[]]$Bytes)
    $res = [ordered]@{ cmdId = $null; state = $null; code = $null; devices = @() }
    $pos = 0
    while ($pos -lt $Bytes.Length) {
        $fld = Read-Field -Bytes $Bytes -Pos ([ref]$pos)
        switch ($fld.Wire) {
            0 {
                switch ($fld.Field) {
                    1 { $res.cmdId = $fld.Varint }
                    2 { $res.state = ($fld.Varint -ne 0) }
                    3 { $res.code = $fld.Varint }
                }
            }
            2 {
                if ($fld.Field -eq 5) { $res.devices = ConvertFrom-ConnectedDevices -Bytes $fld.Bytes }
            }
        }
    }
    return $res
}

function Read-Exact {
    param([System.IO.Stream]$Stream, [int]$Count)
    $buf = New-Object byte[] $Count
    $off = 0
    while ($off -lt $Count) {
        $n = $Stream.Read($buf, $off, $Count - $off)
        if ($n -le 0) { throw '管道已关闭' }
        $off += $n
    }
    return $buf
}

function Read-FdgMessage {
    param([System.IO.Stream]$Stream)
    # 协议头：ASCII，以 \n 结束
    $sb = New-Object System.Text.StringBuilder
    while ($true) {
        $b = $Stream.ReadByte()
        if ($b -lt 0) { throw '管道已关闭' }
        if ($b -eq 10) { break }
        [void]$sb.Append([char]$b)
    }
    $header = $sb.ToString()
    $len = [BitConverter]::ToInt32((Read-Exact -Stream $Stream -Count 4), 0)
    if ($len -lt 0 -or $len -gt 50MB) { throw "长度异常: $len" }

    switch ($header) {
        'FDG_CHUNK' {
            # 已读到的 $len 是总长，紧跟的 int32 是块数
            $n = [BitConverter]::ToInt32((Read-Exact -Stream $Stream -Count 4), 0)
            if ($n -le 0 -or $n -gt 65535) { throw "分块数异常: $n" }
            $ms = New-Object System.IO.MemoryStream
            for ($k = 0; $k -lt $n; $k++) {
                $cl = [BitConverter]::ToInt32((Read-Exact -Stream $Stream -Count 4), 0)
                $data = Read-Exact -Stream $Stream -Count $cl
                $ms.Write($data, 0, $data.Length)
            }
            return $ms.ToArray()
        }
        'FDG_COMPRESSED' {
            $gz = Read-Exact -Stream $Stream -Count $len
            $in = New-Object System.IO.MemoryStream(, $gz)
            $zs = New-Object System.IO.Compression.GZipStream($in, [System.IO.Compression.CompressionMode]::Decompress)
            $out = New-Object System.IO.MemoryStream
            $zs.CopyTo($out)
            $zs.Dispose()
            return $out.ToArray()
        }
        default {
            return (Read-Exact -Stream $Stream -Count $len)
        }
    }
}

function Send-FdgMessage {
    param([System.IO.Stream]$Stream, [byte[]]$Payload)
    $head = [Text.Encoding]::ASCII.GetBytes("FDG_PROTOCOL`n")
    $Stream.Write($head, 0, $head.Length)
    $l = [BitConverter]::GetBytes([int32]$Payload.Length)
    $Stream.Write($l, 0, 4)
    $Stream.Write($Payload, 0, $Payload.Length)
    $Stream.Flush()
}

function Get-PipeBattery {
    param([int]$TimeoutMs = 3000)

    $client = New-Object System.IO.Pipes.NamedPipeClientStream('.', $PipeName, [System.IO.Pipes.PipeDirection]::InOut)
    try {
        $client.Connect($TimeoutMs)
    }
    catch {
        $client.Dispose()
        $msg = $_.Exception.Message
        if ($msg -match 'busy|正在使用|231|121|timeout|超时') {
            throw '飞智空间站 App 正占用 fcs.sock（管道单客户端）。请先退出空间站（托盘右键 → 退出），后台服务会继续运行，手柄不受影响。'
        }
        throw "连接 fcs.sock 失败：$msg"
    }

    try {
        $client.ReadMode = [System.IO.Pipes.PipeTransmissionMode]::Byte
    } catch { }

    # IpcCommand { cmdId=4097 (GetConnectedDevices), getConnectedDeviceParam=空消息 }
    $req = [byte[]](0x08, 0x81, 0x20, 0x1A, 0x00)
    Send-FdgMessage -Stream $client -Payload $req
    $reply = Read-FdgMessage -Stream $client
    $client.Dispose()

    $res = ConvertFrom-IpcResult -Bytes $reply
    if ($null -eq $res.cmdId) { throw '响应里没有 cmdId，协议不匹配' }
    return $res
}

function Get-LevelText6 {
    param([int]$Level)
    # 飞智固件把电量分成 0..6 共 7 档，界面图标为 assets/images/battery/Power0..Power6
    $map = @{ 0 = '空'; 1 = '极低'; 2 = '低'; 3 = '中低'; 4 = '中'; 5 = '较高'; 6 = '满' }
    if ($Level -gt 6 -or $Level -lt 0) { return "未知" }
    return $map[[int]$Level]
}

function Get-LevelPercent6 {
    param([int]$Level)
    # 官方只给档位、不给百分比；这里按 7 档均分换算成估算区间（仅作参考，不是固件原始值）。
    if ($Level -lt 0 -or $Level -gt 6) { return $null }
    $pct = [int][math]::Round($Level * 100.0 / 6.0)
    $lo = [math]::Max(0, $pct - 8)
    $hi = [math]::Min(100, $pct + 8)
    return [pscustomobject]@{ Percent = $pct; Text = "≈$pct% ($lo-$hi%)" }
}

function Get-ConnectText {
    param($Category, $ConnectType)
    # Category: 0 Unknown / 1 Controller / 2 Cooler / 3 Keyboard / 4 Mouse / 5 Headset / 6 Charger
    # ConnectType: 0 Unknown / 1 Wired / 2 Dongle / 3 Bluetooth
    $catMap = @{ 0 = '未知设备'; 1 = '手柄'; 2 = '散热器'; 3 = '键盘'; 4 = '鼠标'; 5 = '耳机'; 6 = '充电座' }
    $conMap = @{ 1 = 'USB 有线'; 2 = '2.4G 接收器'; 3 = '蓝牙' }
    $c = if ($null -ne $Category -and $catMap.ContainsKey([int]$Category)) { $catMap[[int]$Category] } else { "类别$Category" }
    if ($null -ne $ConnectType -and $conMap.ContainsKey([int]$ConnectType)) { return "$c / $($conMap[[int]$ConnectType])" }
    return $c
}

function Get-ChargingText {
    param($ConnectType, $Level, $MaxLevel)
    # 飞智的数据模型里没有"是否在充电"字段，只能按固件 connectType 推断。
    $isFull = ($null -ne $Level -and $null -ne $MaxLevel -and [int]$Level -ge [int]$MaxLevel)
    switch ([int]$ConnectType) {
        1 { if ($isFull) { return 'USB 有线 · 已充满（接着线）' } else { return 'USB 有线 · 充电中' } }
        2 { return '无线电池供电 · 未充电' }
        3 { return '蓝牙电池供电 · 未充电' }
    }
    return '未知'
}

# ------------------------------------------------------------------ main ----
function Read-Status {
    $out = @{
        timestamp        = (Get-Date).ToString('s')
        ok               = $false
        connected        = $false
        connectionSource = $null
        source           = $null
        devices          = @()
        error            = $null
    }

    # 只从飞智服务取 7 档电量
    try {
        $res = Get-PipeBattery -TimeoutMs $TimeoutMs
        $devs = @()
        foreach ($d in $res.devices) {
            $pct = if ($null -ne $d.battery) { Get-LevelPercent6 -Level ([int]$d.battery) } else { $null }
            $devs += [pscustomobject]@{
                Source      = 'pipe'
                Slot        = $null
                Connected   = [bool]$d.isConnected
                ProductName = $d.productName
                DeviceCode  = $d.deviceCode
                Uid         = $d.uid
                Category    = $d.category
                ConnectType = $d.connectType
                Connect     = Get-ConnectText -Category $d.category -ConnectType $d.connectType
                Level       = $d.battery
                LevelMax    = 6
                LevelText   = if ($null -ne $d.battery) { Get-LevelText6 $d.battery } else { $null }
                Percent     = $pct.Percent
                Approx      = $pct.Text
                Charging    = Get-ChargingText -ConnectType $d.connectType -Level $d.battery -MaxLevel 6
            }
        }
        if ($devs.Count -gt 0) {
            $out.ok = $true
            $out.connected = $true
            $out.connectionSource = 'pipe'
            $out.source = 'pipe'
            $out.devices = $devs
        }
        else {
            $out.error = '服务返回了响应，但里面没有已连接设备'
        }
    }
    catch {
        $out.error = $_.Exception.Message
    }

    if (-not $out.ok) {
        # XInput 只用来确认手柄是否已连接，不读电量
        $connected = Test-XInputConnected
        $out.connected = $connected
        $err = if ($out.error) { $out.error } else { '未知错误' }
        if ($connected) {
            $out.connectionSource = 'xinput'
            $out.error = "手柄已连接（XInput），但 7 档电量不可用：$err"
        }
        else {
            $out.error = "未连接手柄；管道: $err"
        }
    }
    return $out
}

function Format-StatusLine {
    param($Status, $Trend = '', $SinceChange = '')
    if (-not $Status.ok) { return "无数据：$($Status.error)" }
    $parts = @()
    foreach ($d in $Status.devices) {
        $name = if ($d.PSObject.Properties.Name -contains 'ProductName' -and $d.ProductName) { $d.ProductName } else { "手柄 $($d.Slot)" }
        $lvl = if ($null -ne $d.Level) { "$($d.Level)/$($d.LevelMax)" } else { '?' }
        $line = '{0}  电量 {1} {2} {3}  充电: {4}  连接: {5}' -f $name, $lvl, $d.LevelText, $d.Approx, $d.Charging, $d.Connect
        if ($Trend) { $line += "  $Trend" }
        if ($SinceChange) { $line += "  (距上次变化 $SinceChange)" }
        $parts += $line
    }
    return ($parts -join "`n")
}

if ($AsLibrary) { return }

# -Watch：连续采样，捕捉电量档位变化（这是能拿到的最细粒度），并据此判断在充电还是在放电
if ($Watch -gt 0) {
    $prevLevel = $null
    $lastChange = $null
    Write-Host ("监视中，每 {0}s 采样一次（Ctrl+C 结束）。档位变化即代表电量变化。" -f $Watch) -ForegroundColor DarkGray
    while ($true) {
        $st = Read-Status
        $now = Get-Date
        $level = $null
        if ($st.ok -and $st.devices.Count -gt 0) { $level = $st.devices[0].Level }

        $trend = ''
        if ($null -ne $level -and $null -ne $prevLevel) {
            if ($level -gt $prevLevel) { $trend = '↑ 上升' }
            elseif ($level -lt $prevLevel) { $trend = '↓ 下降' }
        }
        if ($null -ne $level -and $null -ne $prevLevel -and $level -ne $prevLevel) {
            $lastChange = $now
        }
        if ($null -ne $level) { $prevLevel = $level }

        $since = ''
        if ($null -ne $lastChange) {
            $since = '{0:hh\:mm\:ss}' -f ($now - $lastChange)
        }
        $stamp = $now.ToString('HH:mm:ss')
        $body = Format-StatusLine -Status $st -Trend $trend -SinceChange $since
        $color = if ($st.ok) { 'Green' } else { 'Yellow' }
        Write-Host ("[$stamp] $body") -ForegroundColor $color
        Start-Sleep -Seconds $Watch
    }
    return
}

$out = Read-Status

if ($Json) {
    $out | ConvertTo-Json -Depth 6
    return
}

if (-not $out.ok) {
    Write-Host "没读到电量：$($out.error)" -ForegroundColor Yellow
    if ($out.connected) {
        Write-Host '提示：手柄已连接（XInput），但 7 档电量需要飞智服务管道。' -ForegroundColor DarkGray
        Write-Host '      若空间站 App 正在运行，请退出 App 后重试；后台服务会继续运行，手柄不受影响。' -ForegroundColor DarkGray
    }
    else {
        Write-Host '提示：未检测到手柄。打开手柄后重试；若空间站 App 占用管道，退出 App 后重试。' -ForegroundColor DarkGray
    }
    return
}

Write-Host '数据源: 飞智后台服务（与空间站界面同源，7 档）' -ForegroundColor DarkGray
Write-Host (Format-StatusLine -Status $out) -ForegroundColor Green
foreach ($d in $out.devices) { Write-Host ("  uid={0}  deviceCode={1}" -f $d.Uid, $d.DeviceCode) -ForegroundColor DarkGray }
