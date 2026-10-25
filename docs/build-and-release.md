# 构建与发布

## 前置条件

- Windows 10/11（测试环境为 Windows 10 x64）。
- .NET Framework 4.x 运行时，以及随系统提供的编译器：
  - `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`
  - 32 位系统回退到 `Framework\v4.0.30319\csc.exe`
- 不需要 Visual Studio、.NET SDK、NuGet，也不需要联网。
- PowerShell 5.1 或 7 均可运行构建脚本。

## 一条命令

```powershell
.\build.ps1
.\build.ps1 -Clean      # 先删除旧 exe
.\build.ps1 -SkipIcon   # 跳过图标生成，同时不嵌入图标
```

脚本开头设置 `$ErrorActionPreference = 'Stop'`，任一步失败都会抛错。

## 构建流水线

```text
1. 找 csc.exe（Framework64 优先，Framework 回退）
2. 收集 src\*.cs（按文件名排序）
3. [可选] 编译临时 icongen.exe → 运行 --mkicon → assets\flydigi.ico
4. 编译 flydigi_battery.exe      /target:exe    控制台
5. 编译 flydigi_battery_tray.exe /target:winexe /define:TRAY
6. 对两个 exe 执行 icacls /setintegritylevel Medium
7. 打印大小 → --version → --selftest
```

### 编译参数

```text
/nologo /platform:anycpu /optimize+ /warn:4
/r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll
```

- 命令行目标：`/target:exe`
- 托盘目标：`/target:winexe /define:TRAY`（无控制台窗口）
- 图标：`/win32icon:assets\flydigi.ico`
- `TRAY` 宏切换 `Program.Main` 的两个实现：控制台入口或 `Application.Run` 托盘入口。

所有 `.cs` 会同时编入两个 exe，包括 `SelfTest.cs`；因此两个 exe 都支持 `--selftest`、`--preview`、`--mkicon`。

### 图标生成

第一遍把源码编译到 `%TEMP%\flydigi_icongen.exe`，执行：

```powershell
flydigi_icongen.exe --mkicon assets\flydigi.ico
```

随后删除临时 exe，第二遍带 `/win32icon:` 正式编译。图标完全由 `IconFactory` 绘制，不依赖外部素材，修改图标代码后重新构建即可同步 ICO。

### 完整性标记（关键步骤）

```powershell
icacls.exe <exe> /setintegritylevel Medium
```

工作区/沙箱目录可能带 Low 强制完整性标签，exe 继承后连接 `\\.\pipe\fcs.sock` 会得到 `ERROR_ACCESS_DENIED`，写 `%TEMP%` 也会被拒绝。标成 Medium 后两个问题都消失。

验证：

```powershell
icacls .\flydigi_battery.exe
# 输出中应包含：
#   Mandatory Label\Medium Mandatory Level:(NW)
```

如果文件系统不是 NTFS，`icacls` 会失败，脚本只打印黄色警告、不中断构建；此时把 exe 复制到普通 NTFS 目录再运行即可。

## 产物

| 产物 | 子系统 | 大小（2.0.1） |
|---|---|---:|
| `flydigi_battery.exe` | 控制台 | 99,328 B（97.0 KB） |
| `flydigi_battery_tray.exe` | GUI | 96,256 B（94.0 KB） |
| `assets/flydigi.ico` | — | 45,466 B（44.4 KB） |

两个 exe 都是单文件，运行时只依赖 .NET Framework 4.x，没有配置文件、DLL 或运行时解压步骤。

## 冒烟测试

`build.ps1` 结尾自动执行：

```powershell
.\flydigi_battery.exe --version    # flydigi_battery 2.0.1
.\flydigi_battery.exe --selftest   # 全部通过，exit 0
```

`--selftest` 覆盖：

- 档位文字、连接文字、充电推断；
- 百分比换算（0/6→0、4/6→67、6/6→100）、图标文字（满电留空）与悬停提示文案；
- 0–2 红 / 3–4 黄 / 5–6 绿 / -1 灰；
- `FillFraction` 的 0、1/6、3/6、5/6、1；
- 45 张图标绘制（9 种状态 × 5 个尺寸）非空；
- 未知态、未连接态、预览图（560×280）、单图与多尺寸 ICO；
- 真实读取一次（有手柄时校验 7 档，无手柄时也允许通过）；
- JSON 序列化与 `connected` 字段。

手柄未连接时自检同样应全部通过，这是刻意的：自检验证代码路径，不依赖硬件。

## 版本号

版本号有两处，发布时必须同步修改：

- `src/AssemblyInfo.cs`：`AssemblyVersion` / `AssemblyFileVersion` / `AssemblyInformationalVersion`（`2.0.1.0`）
- `src/Program.cs`：`Program.VersionText`（`2.0.1`，命令行输出）

## 发布检查表

1. 退出正在运行的托盘（右键 → 退出），否则构建会因文件占用而拒绝执行。
2. `.\build.ps1 -Clean`。
3. 确认两个 exe 的 `icacls` 都显示 Medium。
4. 连接手柄，运行 `.\flydigi_battery.exe --debug`，确认 `ok=true` 且档位合理。
5. `.\flydigi_battery.exe --preview .\icon_preview.png`，目视检查 10 列状态。
6. `.\bench.ps1`，与 [性能](performance.md) 的记录对比，确认没有明显回退。
7. 双击托盘 exe，确认无控制台窗口、图标与悬停提示正常、右键菜单可用。
8. 分发时至少打包：两个 exe、`assets/flydigi.ico`（可选，已嵌入）、`README.md`、`LICENSE`、`docs/`。
9. 如果用户把 exe 放到新目录后报权限错误，让其在目标目录重新执行 `icacls ... /setintegritylevel Medium`。

本项目没有做 Authenticode 签名；如需对外分发，可在构建后自行签名，签名不会影响功能。

## 常见构建失败

| 现象 | 原因 | 处理 |
|---|---|---|
| `flydigi_battery_tray 正在运行，先退出它再编译` | 托盘占用 exe | 右键退出托盘，或 `Stop-Process -Name flydigi_battery_tray` |
| `找不到 csc.exe（需要 .NET Framework 4.x）` | 系统缺少 .NET Framework 4.x | 启用/安装 .NET Framework 4.8 |
| `编译失败` 且输出 CSxxxx | 源码错误 | 按 csc 报错行修复；注意本项目限定 C# 5 |
| `生成 ICO 失败` | 临时 icongen 无法写 `assets` 或 `%TEMP%` | 检查目录权限/完整性标签，或先手动确保 `assets` 可写 |
| PowerShell 报大量语法错误 | 脚本被存成无 BOM 的 UTF-8 | 用“UTF-8 with BOM”重新保存 |
| Git Bash 下 csc 参数被改写 | MSYS 路径转换把 `/nologo` 当成路径 | 在 PowerShell 中运行，或设置 `MSYS2_ARG_CONV_EXCL='*'` |
| `无法设置完整性级别` 警告 | 非 NTFS 或权限不足 | 复制到 NTFS 普通目录后手动 `icacls` |
