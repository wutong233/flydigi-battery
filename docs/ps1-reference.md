# PowerShell 参考版

PowerShell 版是 exe 之前的第一版实现，现在保留作**免编译参考**：核对协议、在不能运行 exe 的环境里应急，或对照排查问题。日常使用推荐 exe，启动更快、资源更省。

| 文件 | 作用 |
|---|---|
| `flydigi_battery.ps1` | 读取逻辑 + 命令行输出；可被 dot-source 当库用 |
| `flydigi_tray.ps1` | 托盘 UI 与图标绘制；dot-source 读取脚本 |
| `flydigi_tray.vbs` | 无窗口启动器：优先启动同目录托盘 exe，没有 exe 才启动 PS1 |

## 读取脚本

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\flydigi_battery.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\flydigi_battery.ps1 -Json
powershell -NoProfile -ExecutionPolicy Bypass -File .\flydigi_battery.ps1 -Source pipe -Watch 3
```

| 参数 | 默认 | 说明 |
|---|---|---|
| `-Source auto\|pipe` | `auto` | 都读管道 7 档；`ValidateSet` 会拒绝其它值 |
| `-Json` | 关 | 输出 JSON |
| `-PipeName` | `fcs.sock` | 管道名 |
| `-TimeoutMs` | `3000` | 管道连接超时（exe 默认 350） |
| `-Watch` | `0` | 每 N 秒采样；大于 0 时进入监视 |
| `-AsLibrary` | 关 | 只加载函数，不执行主流程；供托盘脚本 dot-source |

进入监视模式后不会输出 JSON，只输出带时间戳的文本。

## 托盘脚本

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\flydigi_tray.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\flydigi_tray.ps1 -Install
powershell -NoProfile -ExecutionPolicy Bypass -File .\flydigi_tray.ps1 -Uninstall
powershell -NoProfile -ExecutionPolicy Bypass -File .\flydigi_tray.ps1 -Preview -PreviewPath .\ps1_icon_preview.png
```

| 参数 | 默认 | 说明 |
|---|---|---|
| `-IntervalSec` | `20` | 采样间隔（最小 5） |
| `-Hidden` | 关 | 兼容 VBS 启动器传入的隐藏参数 |
| `-Install` | — | 在“启动”文件夹创建 `flydigi-battery-tray.lnk`，指向 `wscript.exe` + `flydigi_tray.vbs` |
| `-Uninstall` | — | 删除上述快捷方式 |
| `-Preview` | — | 只渲染预览图，不启动托盘 |
| `-PreviewPath` | 空 | 预览图路径 |

托盘启动时创建互斥体 `Local\FlydigiBatteryTray`，与 exe 托盘共用；已有一个实例时打印“托盘程序已经在运行了”并退出。

## 与 exe 的差异

### 行为

| 项 | exe | PS1 |
|---|---|---|
| 启动开销 | 约 25 ms | 约 250 ms（解析脚本 + 初始化类型） |
| 采样线程 | 后台线程 + UI 线程 | `System.Windows.Forms.Timer`，读取在 UI 线程 |
| 图标缓存 | 按状态缓存，状态不变不重绘 | 每次 tick 重新绘制并替换 |
| 管道超时 | 350 ms | 3000 ms |
| 开机自启 | HKCU `Run` 值，指向托盘 exe | 启动文件夹快捷方式 → `wscript.exe` + VBS |
| 自启检测 | 读注册表值 | 判断快捷方式是否存在 |
| 单实例 | 互斥体 | 同一互斥体 |
| 退出码 | 失败返回 1，参数错误 2 | 读取失败仍返回 0；只有未捕获异常才会非 0 |
| 设置持久化 | 注册表 `HKCU\Software\FlydigiBattery\Tray` | 不持久化，每次用参数/默认值 |
| 低电量提醒 | 35% / 15% 两档 | 同左 |
| 图标规则 | 0–2 红、3–4 黄、5–6 绿，黑色 1px 两位百分比，满电为满格电池，左→右填充 | 同左 |
| 悬停提示 | `67% (4/6) · 无线` | 同左 |

PS1 托盘在 UI 线程读管道，服务端慢时菜单可能短暂卡顿；exe 已改为后台采样。

### JSON 形状

两者**字段名不完全一致**，脚本消费时注意：

- exe：顶层与设备字段都是 lowerCamel，设备含 `plugged`，`source` 是中文数据源描述，含 `timing`。
- PS1：`ConvertTo-Json` 输出，设备属性是 **PascalCase**（`ProductName`、`Level`…），没有 `plugged`，没有 `timing`；成功时 `source` 为 `'pipe'`。

PS1 顶层示例（缩进由 `ConvertTo-Json` 决定）：

```json
{
  "timestamp": "2026-10-24T15:48:51",
  "ok": true,
  "connected": true,
  "connectionSource": "pipe",
  "source": "pipe",
  "devices": [
    {
      "Source": "pipe",
      "Slot": null,
      "Connected": true,
      "ProductName": "Flydigi APEX5 Wireless",
      "DeviceCode": "k5",
      "Uid": "0123456789abcdef0123456789",
      "Category": 1,
      "ConnectType": 2,
      "Connect": "手柄 / 2.4G 接收器",
      "Level": 4,
      "LevelMax": 6,
      "LevelText": "中",
      "Percent": 67,
      "Approx": "≈67% (59-75%)",
      "Charging": "无线电池供电 · 未充电"
    }
  ],
  "error": null
}
```

需要稳定字段名时请用 exe。

### 预览图

PS1 的 `-Preview` 只渲染 16px 和 32px 两行（10 列，400×80）；exe 渲染 16/20/24/32/48 五行（560×280）。两者列顺序一致：0–6 档（图标显示百分比）、充电、未知、未连接。

## VBS 启动器

`flydigi_tray.vbs` 用 `WScript.Shell.Run(..., 0, False)` 无窗口启动：

1. 同目录存在 `flydigi_battery_tray.exe` → 直接启动 exe；
2. 否则存在 `flydigi_tray.ps1` → `powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File ... -Hidden`；
3. 都没有 → 弹框提示并返回 1。

因此 PS1 的 `-Install` 虽然指向 VBS，但只要目录里有托盘 exe，实际启动的仍是 exe。**不要同时启用 exe 的 HKCU 自启和 PS1 的启动项**，否则同一登录会话可能尝试启动两次（互斥体会让第二个退出，但会留下无意义的启动项）。

## 编码

所有 `.ps1` 含中文，必须保存为 **UTF-8 with BOM**。Windows PowerShell 5.1 对无 BOM UTF-8 会按 GBK 解析，直接报语法错误。仓库中的文件已带 BOM。

## 同步要求

修改数据通道、文案、颜色、填充或图标几何时，C# 与 PS1 **两边都要改**：

- 读取与文案：`src/Reader.cs`、`src/Model.cs` ↔ `flydigi_battery.ps1`
- 图标：`src/IconFactory.cs` ↔ `flydigi_tray.ps1`
- 预览列顺序与状态定义也必须一致

改完分别生成 `icon_preview.png` 与 `ps1_icon_preview.png` 对比。
