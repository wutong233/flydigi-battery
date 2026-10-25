# 命令行与 JSON

## 命令行 exe

```text
flydigi_battery.exe [选项]
```

| 选项 | 说明 |
|---|---|
| `--source auto\|pipe` | 都从飞智服务读取 7 档；`xinput` 已移除，传其它值会报参数错误。保留两个值是兼容旧脚本 |
| `--json` | 输出单行 JSON |
| `--watch N` | 每 N 秒采样一次，显示 `↑/↓` 与距上次变化时间；Ctrl+C 结束 |
| `--pipe-timeout MS` | 管道连接超时，默认 `350`，最小 `50` |
| `--pipe-name NAME` | 管道名，默认 `fcs.sock`（一般不需要改） |
| `--preview PATH` | 导出 10 列 × 5 行的图标对照表 PNG |
| `--mkicon PATH` | 生成多尺寸 ICO（16/20/24/32/40/48/64/256），构建用 |
| `--install` | 加入开机自启；命令行 exe 会自动指向同目录托盘 exe |
| `--uninstall` | 移除开机自启 |
| `--debug` | 额外打印 `pipe / xinput-check / total` 各阶段耗时 |
| `--selftest` | 运行自检，全部通过返回 0 |
| `--probe-pipe` | 隐藏诊断项：分别用 `NamedPipeClientStream` 与 `FileStream` 打开管道并打印错误码 |
| `--version` | 输出 `flydigi_battery 2.0.1` |
| `--help` / `-h` | 用法 |

参数也接受单横线与 `/` 前缀，以及 `--name=value` 形式（例如 `--watch=2`）。

### 退出码

| 码 | 含义 |
|---:|---|
| `0` | 成功（含 `--help`、`--version`、`--install`、预览导出、自检全过） |
| `1` | 没读到电量（管道失败 / 未连接 / 服务返回错误），或自检有失败项 |
| `2` | 参数错误（未知参数、`--source xinput`、缺少取值、超时小于 50ms） |

注意：`ok=false` 但 `connected=true` 时退出码仍是 `1`。自动化脚本如果需要区分“未连接”，请读 JSON 的 `connected`。

### 文本输出

```text
数据源: 飞智后台服务（与空间站界面同源，7 档）
Flydigi APEX5 Wireless  电量 4/6 中 ≈67% (59-75%)  充电: 无线电池供电 · 未充电  连接: 手柄 / 2.4G 接收器
  uid=0123456789abcdef0123456789  deviceCode=k5
```

`--debug` 会在末尾追加：

```text
  pipe=126ms xinput-check=0ms total=188ms
```

失败时：

```text
没读到电量：手柄已连接（XInput），但 7 档电量不可用：飞智空间站 App 正在运行并占用 fcs.sock（管道单客户端）。…
提示：手柄已连接（XInput），但 7 档电量需要飞智服务管道。
      若空间站 App 正在运行，请退出 App 后重试；后台服务会继续运行，手柄不受影响。
```

### 监视模式

```text
监视中，每 2s 采样一次（Ctrl+C 结束）。档位变化即代表电量变化。
[15:48:51] Flydigi APEX5 Wireless  电量 4/6 中 ≈67% (59-75%)  充电: 无线电池供电 · 未充电  连接: 手柄 / 2.4G 接收器
[15:48:53] Flydigi APEX5 Wireless  电量 4/6 中 ≈67% (59-75%)  充电: 无线电池供电 · 未充电  连接: 手柄 / 2.4G 接收器  ↑ 上升  (距上次变化 00:00:12)
```

每次采样都会重新连接管道；`--watch` 与 `--json` 同时使用时只输出文本。

### 重定向编码

当标准输出被重定向（管道/文件）时，程序切换到无 BOM 的 UTF-8，避免控制台代码页造成乱码。直接在控制台运行时使用系统默认。

## 托盘 exe

```text
flydigi_battery_tray.exe [选项]
```

直接运行即常驻托盘。支持：

| 选项 | 说明 |
|---|---|
| `--interval N` | 采样间隔（秒，最小 5，默认 20） |
| `--source auto\|pipe` | 数据源；未指定时读注册表里的设置 |
| `--install` / `--uninstall` | 加入 / 移除开机自启（HKCU Run） |
| `--preview PATH` | 导出图标对照表，不启动托盘 |
| `--mkicon PATH` | 生成 ICO |
| `--selftest` | 自检 |
| `--help` | 弹出参数说明对话框 |

单实例由互斥体保证：已有托盘在跑时，新进程静默退出并返回 0。

## JSON

单行、无缩进、UTF-8。顶层字段：

| 字段 | 类型 | 说明 |
|---|---|---|
| `ok` | bool | 是否拿到 7 档电量 |
| `connected` | bool | 是否有手柄连接 |
| `connectionSource` | string \| null | `pipe` / `xinput` / `null` |
| `source` | string \| null | 数据源名称；无电量时为 `null` |
| `timestamp` | string | 本地时间，ISO 风格无时区，如 `2026-10-24T15:48:51` |
| `error` | string \| null | 失败原因；成功时为 `null` |
| `timing` | string | 始终存在：`pipe=Nms xinput-check=Nms total=Nms` |
| `devices` | array | 设备数组，失败时为空数组 |

设备字段：

| 字段 | 类型 | 说明 |
|---|---|---|
| `source` | string | 固定 `pipe` |
| `slot` | int \| null | XInput 槽位；管道设备为 `null` |
| `connected` | bool | `DeviceInfo.isConnected` |
| `productName` | string | 如 `Flydigi APEX5 Wireless` |
| `deviceCode` | string | 如 `k5` |
| `uid` | string | 小写十六进制 |
| `category` | int \| null | 1=手柄、6=充电座 |
| `connectType` | int \| null | 1=Wired、2=Dongle、3=Bluetooth |
| `level` | int \| null | 档位 0–6 |
| `levelMax` | int | 固定 6 |
| `levelText` | string | `空/极低/低/中低/中/较高/满` |
| `percent` | int \| null | 估算百分比 = `round(level*100/6)` |
| `approx` | string | 估算区间，如 `≈67% (59-75%)`（±8%） |
| `connect` | string | 如 `手柄 / 2.4G 接收器` |
| `charging` | string | `USB 有线 · 充电中` / `USB 有线 · 已充满（接着线）` / `无线电池供电 · 未充电` / `蓝牙电池供电 · 未充电` / `未知` |
| `plugged` | bool | 是否按有线连接推断为接着线 |

### 成功

```json
{
  "ok": true,
  "connected": true,
  "connectionSource": "pipe",
  "source": "飞智后台服务（与空间站界面同源，7 档）",
  "timestamp": "2026-10-24T15:48:51",
  "error": null,
  "timing": "pipe=125ms xinput-check=0ms total=181ms",
  "devices": [
    {
      "source": "pipe",
      "slot": null,
      "connected": true,
      "productName": "Flydigi APEX5 Wireless",
      "deviceCode": "k5",
      "uid": "0123456789abcdef0123456789",
      "category": 1,
      "connectType": 2,
      "level": 4,
      "levelMax": 6,
      "levelText": "中",
      "percent": 67,
      "approx": "≈67% (59-75%)",
      "connect": "手柄 / 2.4G 接收器",
      "charging": "无线电池供电 · 未充电",
      "plugged": false
    }
  ]
}
```

### 已连接但管道不可用

```json
{
  "ok": false,
  "connected": true,
  "connectionSource": "xinput",
  "source": null,
  "timestamp": "2026-10-24T15:50:00",
  "error": "手柄已连接（XInput），但 7 档电量不可用：飞智空间站 App 正在运行并占用 fcs.sock（管道单客户端）。请先退出空间站（托盘右键 → 退出），后台服务会继续运行，手柄不受影响。",
  "timing": "pipe=0ms xinput-check=1ms total=2ms",
  "devices": []
}
```

### 未连接

```json
{
  "ok": false,
  "connected": false,
  "connectionSource": null,
  "source": null,
  "timestamp": "2026-10-24T15:51:00",
  "error": "未连接手柄；管道: 管道当前不可用（服务未运行或已被占用）",
  "timing": "pipe=0ms xinput-check=1ms total=2ms",
  "devices": []
}
```

### 在脚本里消费

```powershell
# Windows PowerShell 5.1 默认用 OEM 代码页解码原生命令输出，中文会乱码
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
$j = .\flydigi_battery.exe --json | ConvertFrom-Json
if ($j.ok) {
    "电量 {0}/{1}，约 {2}%" -f $j.devices[0].level, $j.devices[0].levelMax, $j.devices[0].percent
} elseif ($j.connected) {
    "手柄已连接，但电量不可用：$($j.error)"
} else {
    "未连接手柄"
}
```

PowerShell 7 默认按 UTF-8 解码，不需要手动设置。

不要把 `percent` 当成固件值：它是 `level/6` 的换算，真实精度只有 7 档。字段来源与协议见 [管道协议](protocol.md)。
