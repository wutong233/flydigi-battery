# 排错

## 先跑这三条

```powershell
.\flydigi_battery.exe --debug        # 读取结果 + 各阶段耗时
.\flydigi_battery.exe --probe-pipe   # 管道能否打开，失败时打印 Win32 错误码
.\flydigi_battery.exe --selftest     # 排掉绘制/JSON/格式化的代码问题
```

`--probe-pipe` 会分别尝试 `NamedPipeClientStream` 和 `FileStream`，能直接区分“管道不存在”“被占用”“权限不足”。

## 症状速查

| 症状 | 最可能原因 | 处理 |
|---|---|---|
| `ERROR_ACCESS_DENIED` / “管道要求进程至少为 Medium 完整性级别” | exe 继承了 Low 完整性标签 | `icacls <exe> /setintegritylevel Medium` |
| “空间站 App 正在运行并占用 fcs.sock” | 管道单客户端，App 占着 | 退出空间站 App；后台服务继续运行 |
| “管道当前不可用（服务未运行或已被占用）” | 服务没起来，或 App 在跑 | 启动一次空间站让服务起来，或退出 App |
| “未连接手柄” | 手柄关机/休眠/接收器未插 | 开手柄、按一下按键唤醒、重插接收器 |
| “已连接 · 电量不可用” | 手柄在，但管道读不到 | 按上一行处理管道问题 |
| `--preview` 报 GDI+ 一般性错误 | Low IL 无法写目标目录或 `%TEMP%` | 输出到普通目录，或把 exe 标成 Medium |
| 托盘没有图标 | 被收进溢出区 / 已有实例 | 任务栏设置里打开；检查是否已有托盘在跑 |
| 开机没有自启 | 注册表项缺失或路径过期 | 菜单勾选“开机自启”，或 `--install` |
| 出现两个托盘图标 | exe 与旧 PS1 同时启动 | 退出其中一个；互斥体只防同一版本 |
| PS1 报大量语法错误 | 文件被存成无 BOM UTF-8 | 用 UTF-8 with BOM 重新保存 |
| 构建报“正在运行，先退出” | 托盘占用 exe 文件 | 右键退出托盘后重编 |
| `--source xinput` 报错 | 电量通道已移除 | 用 `auto` 或 `pipe` |
| 档位长时间不变 | 7 档粒度粗 + 采样间隔长 | 正常现象；可设 10 秒间隔观察 |
| JSON 前面有乱码字符 | 消费端按错误编码读取 | 用 UTF-8 读取；重定向输出本身无 BOM |

---

## 1. 权限：Medium 完整性

### 现象

```text
没读到电量：连接 fcs.sock 被拒绝：管道要求进程至少为 Medium 完整性级别。…
```

或 `--probe-pipe` 显示 `win32=5`（`ERROR_ACCESS_DENIED`）。

### 原因

工作区/沙箱目录中的文件可能带 **Low mandatory integrity label**。exe 放在那里会继承 Low IL，而飞智服务管道要求至少 Medium。表现和“服务 ACL 拒绝”很像，容易误判为需要管理员权限。

### 处理

```powershell
icacls .\flydigi_battery.exe /setintegritylevel Medium
icacls .\flydigi_battery_tray.exe /setintegritylevel Medium
```

验证输出包含：

```text
Mandatory Label\Medium Mandatory Level:(NW)
```

`build.ps1` 编译后会自动执行这两条。**不需要管理员权限**；这是给文件打标签，不是提权运行。

Low IL 也会导致写 `%TEMP%` 被拒绝，从而让 `--preview` 抛出 GDI+ 一般性错误；标成 Medium 后一并解决。

## 2. 管道被空间站 App 占用

### 现象

```text
手柄已连接（XInput），但 7 档电量不可用：飞智空间站 App 正在运行并占用 fcs.sock（管道单客户端）。…
```

托盘显示灰色 `?` 电池，提示 `已连接 · 电量不可用`。

### 原因

服务端只创建一个管道实例，空间站 App 启动后长期持有。后台服务与手柄本身不受影响。

### 处理

1. 在空间站托盘图标上右键 → 退出。
2. 等待下一次采样（默认 20 秒），或右键托盘 → 立即刷新。
3. 需要同时用 App 时，只能二选一：App 打开期间本工具无法读 7 档。

不要通过杀进程的方式关闭空间站；正常退出即可，服务端会释放管道。

## 3. 服务未运行

若从未启动过空间站，`SpaceStationService.exe` 可能没在跑：

```powershell
Get-Process SpaceStationService -ErrorAction SilentlyContinue
```

处理：启动一次飞智空间站（登录后可退出 App，服务通常继续驻留），再运行本工具。

如果服务在但管道仍不可用，用 `--probe-pipe` 看具体 Win32 错误码：

| win32 | 含义 |
|---:|---|
| 2 | 管道不存在（服务未运行） |
| 5 | 拒绝访问（完整性级别/ACL） |
| 231 | 所有实例忙（App 占用） |

## 4. 手柄未连接

错误为 `未连接手柄`，托盘为灰色手柄 + 红斜杠。

排查顺序：

1. 手柄是否开机、是否休眠（按任意键唤醒）。
2. 2.4G 接收器是否插好；换 USB 口。
3. 蓝牙模式是否在系统里配对并连接。
4. 用 `XInputGetState` 能检测到才算“已连接”；某些模式/固件下 XInput 不可见，但管道仍可能返回设备。

如果管道返回了设备但 `connected=false`，仍会按 `ok=true` 展示电量；`connected` 只影响失败时的提示。

## 5. 已连接但电量不可用

这是有意设计的状态，不是 bug：

```json
{ "ok": false, "connected": true, "connectionSource": "xinput" }
```

它表示 XInput 能看到手柄，但 7 档电量必须走管道。按第 1–3 节修管道即可。工具不会用 XInput 的 4 档数据凑数，也不会猜百分比。

## 6. 图标预览报错

```text
System.Runtime.InteropServices.ExternalException: GDI+ 中发生一般性错误。
```

常见原因：

- exe 是 Low IL，写 `%TEMP%` 或目标目录被拒绝；
- 目标路径指向只读位置或非法文件名；
- 目录不存在（程序会自动创建，但父路径必须可写）。

处理：

```powershell
.\flydigi_battery.exe --preview .\icon_preview.png   # 写到当前目录
```

仍失败时检查 `icacls`，或先把 exe 复制到普通 NTFS 目录。

## 7. 托盘没有图标 / 看不到

- Windows 10/11 默认把新图标放进**溢出区**：点任务栏的 `^`，或“设置 → 个人化 → 任务栏 → 其他系统托盘图标”里打开。
- 已有实例在跑时，新实例会静默退出（返回 0）。检查：

```powershell
Get-Process flydigi_battery_tray -ErrorAction SilentlyContinue
```

- 若进程在但图标不可见，右键退出后重新启动；必要时注销/重启 Explorer。
- 悬停提示只显示 `67% (4/6) · 无线` 这类短文本，完整信息在左键气泡里。

## 8. 托盘双开 / 关不掉

- exe 托盘与 PS1 托盘共用互斥体 `Local\FlydigiBatteryTray`，正常不会双开。
- 如果看到两个图标，通常是旧版本（不同互斥体）或 Explorer 的残留图标。先列出所有相关进程：

```powershell
Get-Process flydigi_battery_tray -ErrorAction SilentlyContinue
Get-CimInstance Win32_Process -Filter "Name='powershell.exe'" |
  Where-Object { $_.CommandLine -match 'flydigi_tray\.ps1' } |
  Select-Object ProcessId,CommandLine
```

确认后退出一个即可。更推荐右键 → 退出，让程序释放图标和线程。

## 9. 开机自启不生效

exe 版写入：

```text
HKCU\Software\Microsoft\Windows\CurrentVersion\Run
  值名: FlydigiBatteryTray
  数据: "C:\...\flydigi_battery_tray.exe"
```

检查：

```powershell
Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' FlydigiBatteryTray
```

- 移动过 exe：托盘启动时会 `HealPath()` 自动修正；没启动过就手动重新 `--install`。
- 用命令行 exe 安装时，会自动指向同目录的托盘 exe。
- `--install` 会删除旧 PS1 版留下的 `flydigi-battery-tray.lnk`，避免两个版本同时自启。
- PS1 版的自启动是另一套（启动项快捷方式指向 `flydigi_tray.vbs`），不要与 exe 版同时启用。
- 任务管理器“启动”页被禁用也会导致不启动。

## 10. PowerShell 脚本语法错误

现象：脚本开头就报大量 `Unexpected token`、中文变乱码。

原因：Windows PowerShell 5.1 对无 BOM 的 UTF-8 按系统 ANSI（中文系统为 GBK）解析。

处理：把 `build.ps1`、`bench.ps1`、`flydigi_battery.ps1`、`flydigi_tray.ps1` 保存为 **UTF-8 with BOM**。仓库中的文件已经带 BOM，编辑时注意不要被工具改掉。

调用时也建议加：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\flydigi_battery.ps1
```

## 11. 构建失败

| 报错 | 处理 |
|---|---|
| `flydigi_battery_tray 正在运行，先退出它再编译` | 右键退出托盘 |
| `找不到 csc.exe` | 安装/启用 .NET Framework 4.x |
| `生成 ICO 失败` | 检查 `assets` 可写与完整性标签 |
| Git Bash 下参数被改写 | 改用 PowerShell，或 `MSYS2_ARG_CONV_EXCL='*'` |
| `无法设置完整性级别` | 非 NTFS 或权限不足；复制到普通 NTFS 目录手动 `icacls` |

## 12. 档位看起来“不准”

7 档的精度就是 16.7% 一档：

| 档位 | 估算 |
|---:|---|
| 0 | 空 |
| 1 | ≈17% |
| 2 | ≈33% |
| 3 | ≈50% |
| 4 | ≈67% |
| 5 | ≈83% |
| 6 | 满 |

`percent` 和 `approx` 是换算值，不是固件测量值。档位停留很久是正常的，尤其采样间隔 60 秒时。想看变化过程用：

```powershell
.\flydigi_battery.exe --watch 10
```

## 13. 收集诊断信息

反馈问题时附上：

```powershell
.\flydigi_battery.exe --version
.\flydigi_battery.exe --debug
.\flydigi_battery.exe --probe-pipe
icacls .\flydigi_battery.exe
Get-Process flydigi_battery_tray,'Flydigi Space Station',SpaceStationService -ErrorAction SilentlyContinue |
  Select-Object Name,Id,Path
```

注意 `--json` 里包含手柄 UID 和 MAC 类标识，公开前自行脱敏。
