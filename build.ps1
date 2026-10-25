<#
.SYNOPSIS
    编译 flydigi_battery.exe（命令行）与 flydigi_battery_tray.exe（托盘）。

.DESCRIPTION
    只用系统自带的 .NET Framework 编译器（.NET 4.8 运行时随 Win10/11 预装），
    不需要 SDK、不需要联网，产物是单文件原生 exe，启动 20ms 级。

    第一次编译先生成 assets\flydigi.ico（图标由程序自己画），再带着图标编一遍，
    这样 exe 在资源管理器/任务栏里也有正确的图标。

.EXAMPLE
    .\build.ps1
    .\build.ps1 -Clean
#>
[CmdletBinding()]
param(
    [switch]$Clean,
    [switch]$SkipIcon
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$src = Join-Path $root 'src'
$assets = Join-Path $root 'assets'
$ico = Join-Path $assets 'flydigi.ico'
$cliOut = Join-Path $root 'flydigi_battery.exe'
$trayOut = Join-Path $root 'flydigi_battery_tray.exe'

# ---------------------------------------------------------------- 找编译器 ----
$candidates = @(
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'),
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe')
)
$csc = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $csc) { throw '找不到 csc.exe（需要 .NET Framework 4.x）' }

$sources = @(Get-ChildItem -Path $src -Filter *.cs | Sort-Object Name | ForEach-Object { $_.FullName })
if ($sources.Count -eq 0) { throw "src 下没有 .cs 文件: $src" }

$refs = @(
    '/r:System.dll',
    '/r:System.Core.dll',
    '/r:System.Drawing.dll',
    '/r:System.Windows.Forms.dll'
)
$common = @('/nologo', '/platform:anycpu', '/optimize+', '/warn:4')

# ---------------------------------------------------------------- 清理产物 ----
if ($Clean) {
    foreach ($p in @($cliOut, $trayOut)) { if (Test-Path $p) { Remove-Item $p -Force } }
    Write-Host '已清理旧产物。' -ForegroundColor DarkGray
}
foreach ($p in @($cliOut, $trayOut)) {
    if (Test-Path $p) {
        $name = [IO.Path]::GetFileNameWithoutExtension($p)
        if (Get-Process -Name $name -ErrorAction SilentlyContinue) {
            throw "$name 正在运行，先退出它再编译（托盘右键 → 退出）。"
        }
    }
}

function Invoke-Csc {
    param([string[]]$Arguments)
    $out = & $csc @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        $out | ForEach-Object { Write-Host $_ -ForegroundColor Red }
        throw "编译失败（exit $LASTEXITCODE）"
    }
    $out | Where-Object { $_ -match 'warning' } | ForEach-Object { Write-Host $_ -ForegroundColor Yellow }
}

# ------------------------------------------------------- 第一遍：生成图标 ----
New-Item -ItemType Directory -Force -Path $assets | Out-Null
if (-not $SkipIcon) {
    $tmp = Join-Path $env:TEMP 'flydigi_icongen.exe'
    Write-Host '生成图标 ...' -ForegroundColor DarkGray
    Invoke-Csc ($common + @('/target:exe', "/out:$tmp") + $refs + $sources)
    & $tmp --mkicon $ico | Out-Null
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $ico)) { throw '生成 ICO 失败' }
    Remove-Item $tmp -Force -ErrorAction SilentlyContinue
}
$iconArg = if ((Test-Path $ico) -and -not $SkipIcon) { "/win32icon:$ico" } else { $null }

# --------------------------------------------- 命令行 exe（控制台子系统）----
Write-Host '编译 flydigi_battery.exe ...' -ForegroundColor DarkGray
$argsCli = @($common + @('/target:exe', "/out:$cliOut"))
if ($iconArg) { $argsCli += $iconArg }
$argsCli += $refs + $sources
Invoke-Csc $argsCli

# --------------------------------------------- 托盘 exe（GUI 子系统，不闪窗）----
Write-Host '编译 flydigi_battery_tray.exe ...' -ForegroundColor DarkGray
$argsTray = @($common + @('/target:winexe', '/define:TRAY', "/out:$trayOut"))
if ($iconArg) { $argsTray += $iconArg }
$argsTray += $refs + $sources
Invoke-Csc $argsTray

# ------------------------------------------- 完整性级别（7 档管道权限）----
# 沙箱/受限工作区里的文件可能带 Low 标签，exe 继承 Low IL 后连接 fcs.sock
# 会得到 ERROR_ACCESS_DENIED。显式标成 Medium（普通用户级别），管道即可用。
foreach ($p in @($cliOut, $trayOut)) {
    & icacls.exe $p /setintegritylevel Medium 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-Host "  警告: 无法设置完整性级别（非 NTFS 或权限不足）: $p" -ForegroundColor Yellow
    }
}

# ------------------------------------------------------------------ 报告 ----
Write-Host ''
foreach ($p in @($cliOut, $trayOut)) {
    $f = Get-Item $p
    Write-Host ("  {0,-28} {1,8:N1} KB" -f $f.Name, ($f.Length / 1KB)) -ForegroundColor Green
}
Write-Host ''
Write-Host '冒烟测试：' -ForegroundColor DarkGray
& $cliOut --version
& $cliOut --selftest
if ($LASTEXITCODE -ne 0) { throw "冒烟测试失败（exit $LASTEXITCODE）" }
Write-Host '编译完成。' -ForegroundColor Green
