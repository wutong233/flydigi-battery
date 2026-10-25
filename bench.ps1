<#
.SYNOPSIS
    exe 与 PowerShell 版耗时对比（端到端，含进程启动）。

.EXAMPLE
    .\bench.ps1
    .\bench.ps1 -Iterations 10 -Exe "$env:TEMP\fbtest\flydigi_battery.exe"
#>
[CmdletBinding()]
param(
    [int]$Iterations = 7,
    [string]$Exe,
    [string]$Ps1,
    [string]$TrayPs1
)

$ErrorActionPreference = 'Stop'
$root = if ($PSScriptRoot) { $PSScriptRoot } else { (Get-Location).Path }
if (-not $Exe) { $Exe = Join-Path $root 'flydigi_battery.exe' }
if (-not $Ps1) { $Ps1 = Join-Path $root 'flydigi_battery.ps1' }
if (-not $TrayPs1) { $TrayPs1 = Join-Path $root 'flydigi_tray.ps1' }
if (-not (Test-Path $Exe)) { throw "找不到 $Exe" }
if (-not (Test-Path $Ps1)) { throw "找不到 $Ps1" }
if (-not (Test-Path $TrayPs1)) { throw "找不到 $TrayPs1" }

# 预览图写到脚本所在目录：工作区可能带 Low 完整性标签，写 %TEMP% 会被拒绝。
$previewDir = Join-Path $root '_bench'
New-Item -ItemType Directory -Force -Path $previewDir | Out-Null

$pwsh5 = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'

function Measure-Run {
    param([scriptblock]$Action)
    $times = New-Object System.Collections.Generic.List[double]
    for ($i = 0; $i -lt $Iterations; $i++) {
        $sw = [Diagnostics.Stopwatch]::StartNew()
        $null = & $Action 2>&1
        $sw.Stop()
        $times.Add($sw.Elapsed.TotalMilliseconds)
    }
    $sorted = $times | Sort-Object
    [pscustomobject]@{
        Min    = [math]::Round($sorted[0], 0)
        Median = [math]::Round($sorted[[int]($sorted.Count / 2)], 0)
        Max    = [math]::Round($sorted[-1], 0)
    }
}

$rows = New-Object System.Collections.Generic.List[object]
function Add-Row {
    param([string]$Name, [scriptblock]$Action)
    Write-Host ("  测量 {0} ..." -f $Name) -ForegroundColor DarkGray
    $m = Measure-Run $Action
    $rows.Add([pscustomobject]@{
            '场景'      = $Name
            '最快 ms'   = $m.Min
            '中位 ms'   = $m.Median
            '最慢 ms'   = $m.Max
        })
}

Write-Host "对比对象：" -ForegroundColor Cyan
Write-Host "  exe: $Exe"
Write-Host "  ps1: $Ps1"
Write-Host "  轮次: $Iterations" -NoNewline
Write-Host ''
Write-Host ''

Add-Row 'exe  管道 7 档 --json' { & $Exe --source pipe --json }
Add-Row 'exe  自动 --json' { & $Exe --source auto --json }
Add-Row 'ps1  管道 7 档 -Json' { & $pwsh5 -NoProfile -ExecutionPolicy Bypass -File $Ps1 -Source pipe -Json }
Add-Row 'ps1  自动 -Json' { & $pwsh5 -NoProfile -ExecutionPolicy Bypass -File $Ps1 -Source auto -Json }
Add-Row 'exe  导出图标预览' { & $Exe --preview (Join-Path $previewDir 'exe.png') }
Add-Row 'ps1  导出图标预览' { & $pwsh5 -NoProfile -ExecutionPolicy Bypass -File $TrayPs1 -Preview -PreviewPath (Join-Path $previewDir 'ps1.png') }

Write-Host ''
$rows | Format-Table -AutoSize | Out-String -Width 200 | Write-Host

# 加速比（同场景 exe / ps1）
Write-Host '加速比（ps1 中位 / exe 中位）:' -ForegroundColor Cyan
$pairs = @(
    @('管道 7 档', 'exe  管道 7 档 --json', 'ps1  管道 7 档 -Json'),
    @('自动', 'exe  自动 --json', 'ps1  自动 -Json'),
    @('图标预览', 'exe  导出图标预览', 'ps1  导出图标预览')
)
foreach ($p in $pairs) {
    $e = $rows | Where-Object { $_.'场景' -eq $p[1] }
    $s = $rows | Where-Object { $_.'场景' -eq $p[2] }
    if ($e -and $s -and $e.'中位 ms' -gt 0) {
        $ratio = [math]::Round($s.'中位 ms' / [double]$e.'中位 ms', 1)
        Write-Host ("  {0,-10} {1,6} ms → {2,5} ms   {3}x" -f $p[0], $s.'中位 ms', $e.'中位 ms', $ratio)
    }
}

Remove-Item $previewDir -Recurse -Force -ErrorAction SilentlyContinue
