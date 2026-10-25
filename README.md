# 飞智手柄电量托盘（Flydigi Battery）

在 Windows 任务栏托盘显示飞智（Flydigi）八爪鱼5手柄的电量、充电与连接状态。

数据来自飞智空间站后台服务 `SpaceStationService.exe` 的命名管道 `\\.\pipe\fcs.sock`，与官方界面同源，只有 **0–6 共 7 档**。

项目同时提供编译好的 exe（推荐）与 PowerShell 参考实现，两者的数据通道与图标规则保持一致。

> 非官方项目，与飞智（Flydigi）无关联，也未获得其授权。主程序只读取本机飞智服务已经暴露给本机进程的数据，不修改固件或服务；`tools/` 仅用于协议研究。

---

<img width="36" height="32" alt="image" src="https://github.com/user-attachments/assets/2740aa56-8df6-43f9-b36a-4231af445f76" />
<img width="168" height="72" alt="image" src="https://github.com/user-attachments/assets/e4b1052b-e63e-4ef9-b5ba-18f6a329ac90" />


## 快速开始

要求：Windows 10/11，系统自带 .NET Framework 4.x，无需安装、无需管理员权限。

仓库内已包含编译好的 exe；如果拿到的是纯源码包，先运行 `.\build.ps1` 生成。

```powershell
.\flydigi_battery.exe          # 命令行读一次
.\flydigi_battery.exe --json   # JSON
.\flydigi_battery_tray.exe     # 托盘常驻（无控制台窗口）
```

首次拿到 exe 后如果提示 `管道要求进程至少为 Medium 完整性级别`，说明 exe 继承了所在目录的 Low 完整性标签，执行一次：

```powershell
icacls .\flydigi_battery.exe /setintegritylevel Medium
icacls .\flydigi_battery_tray.exe /setintegritylevel Medium
```

`build.ps1` 编译后会自动做这一步。详见 [排错](docs/troubleshooting.md)。

从源码构建：

```powershell
.\build.ps1            # 生成图标 → 编译两个 exe → 标记 Medium → 自检
.\build.ps1 -Clean     # 先删旧产物
.\bench.ps1            # exe / ps1 端到端耗时对比
```

---

## 产物

| 文件 | 用途 | 大小 |
|---|---|---:|
| [`flydigi_battery.exe`](flydigi_battery.exe) | 命令行（控制台子系统） | 97.0 KB |
| [`flydigi_battery_tray.exe`](flydigi_battery_tray.exe) | 托盘（GUI 子系统，无黑框） | 94.0 KB |
| [`assets/flydigi.ico`](assets/flydigi.ico) | 多尺寸图标，已嵌入两个 exe | 44.4 KB |

版本 `2.0.1.0`（命令行 `--version` 输出 `2.0.1`）。

---

## 目录结构

```text
.
├─ README.md                  本文件
├─ LICENSE                    MIT 许可证
├─ .gitignore                 临时文件与逆向输出忽略规则
├─ docs/                      详细文档
├─ src/                       C# 源码（10 个文件，约 2,500 行）
├─ build.ps1                  构建脚本
├─ bench.ps1                  基准脚本
├─ assets/flydigi.ico         构建时生成的图标
├─ flydigi_battery.exe        命令行产物
├─ flydigi_battery_tray.exe   托盘产物
├─ flydigi_battery.ps1        PowerShell 参考实现（读取）
├─ flydigi_tray.ps1           PowerShell 参考实现（托盘）
├─ flydigi_tray.vbs           无窗口启动器（优先 exe）
├─ icon_preview.png           exe 图标对照表
├─ ps1_icon_preview.png       PS1 图标对照表
└─ tools/                     逆向与协议探针（维护用，不参与构建）
```

---

## 关键结论

- **7 档，不是百分比。** 固件与 App 只提供 `0–6`；`percent` / `approx` 是按档位均分换算的估算值（如 4 → `≈67% (59-75%)`）。托盘图标用黑色 1px 笔画显示两位百分比，满电（100%）显示满格电池，悬停提示仍给出 `100% (6/6)`。
- **充电状态是推断的。** 飞智 schema 没有 `isCharging`，按 `connectType` 判断：`Wired(1)` = 接着线，`Dongle(2)` / `Bluetooth(3)` = 电池供电。
- **管道是单客户端。** 空间站 App 运行时管道被占用，退出 App 后后台服务仍在，托盘会自动恢复 7 档。
- **需要 Medium 完整性。** Low IL 的 exe 连接 `fcs.sock` 会得到 `ERROR_ACCESS_DENIED`。

状态组合：

| `ok` | `connected` | `connectionSource` | 含义 | 托盘 |
|---|---|---|---|---|
| `true` | `true` | `pipe` | 读到 7 档电量 | 彩色电池 + 黑色两位百分比（满电为满格电池） |
| `false` | `true` | `xinput` | 手柄已连接，但管道不可用 | 灰色 `?` 电池 |
| `false` | `false` | `null` | 未检测到手柄 | 灰色手柄 + 红斜杠 |

---

## 性能

测试环境实测（Windows 10，APEX5 已连接，端到端含进程启动，5 轮中位值）：

| 场景 | ps1 | exe | 加速 |
|---|---:|---:|---:|
| 管道 7 档 `--json` | 260 ms | 204 ms | 1.3x |
| 自动 `--json` | 261 ms | 218 ms | 1.2x |
| 导出图标预览 | 380 ms | 31 ms | 12.3x |

管道场景的主要成本是服务端响应，波动较大（单次 7–150 ms）；进程启动约 25 ms。纯本地路径（图标绘制、JSON、XInput 检测）没有服务端延迟，因此 exe 优势明显。方法与复现见 [性能](docs/performance.md)。

---

## 文档索引

| 文档 | 内容 |
|---|---|
| [架构](docs/architecture.md) | 模块划分、数据流、状态模型、线程与缓存 |
| [命令行与 JSON](docs/cli-and-json.md) | 全部参数、退出码、JSON 字段与示例 |
| [托盘图标](docs/tray-icon.md) | 配色、填充、几何尺寸、充电/未知/未连接态、菜单 |
| [构建与发布](docs/build-and-release.md) | csc 编译流程、图标生成、完整性标记、发布检查表 |
| [管道协议](docs/protocol.md) | FDG 帧、protobuf、cmd 4097/4099、字段与枚举 |
| [性能](docs/performance.md) | 基准方法、结果、瓶颈与优化点 |
| [排错](docs/troubleshooting.md) | 权限、管道占用、未连接、预览报错、自启动 |
| [PowerShell 参考版](docs/ps1-reference.md) | PS1 用法、与 exe 的差异、VBS 启动器 |

---

## 已知限制

- 空间站 App 在运行时无法读管道（单实例），需退出 App；后台服务不受影响。
- 电量精度就是档位本身，百分比只是换算值。
- 未连接时只能显示“未连接”；已连接但拿不到管道时显示“电量不可用”，不会猜测电量。
- 所有脚本含中文，**必须保存为 UTF-8 带 BOM**，否则 Windows PowerShell 5.1 会按 GBK 解析并报语法错误。
- 托盘图标可能被收进任务栏溢出区，需要在“任务栏设置 → 其他系统托盘图标”里打开。

---

## 许可证

本项目原创代码与文档采用 [MIT 许可证](LICENSE)。

`tools/` 中的探针脚本采用 MIT 许可证。由飞智 App 提取的 `schema.json`、`desc_bin/`、`out_*.txt` 等数据不属于本项目，版权归原所有者；为避免再分发专有内容，这些文件不纳入仓库，需要时可在本机安装飞智 App 后自行提取生成。

## 免责声明

本项目为非官方工具，与飞智（Flydigi）无任何关联，也未获得其授权。使用本工具造成的任何问题由使用者自行承担；请遵守当地法律与飞智的服务条款。所有产品名、商标归其各自所有者。
