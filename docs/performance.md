# 性能

## 结论

exe 相对 PowerShell 版的收益分两类：

- **含管道读取的场景：约 1.2–1.3 倍。** 主要成本在飞智服务端响应，exe 省掉的是 PowerShell 启动与解释开销。
- **纯本地场景（图标绘制、JSON、连接检测）：10 倍以上。** 没有服务端延迟，编译代码与 PS1 解释执行的差距完全显现。

托盘常驻进程内存约 30 MB（`flydigi_battery_tray.exe` 工作集，实测 27–30 MB），采样间隔默认 20 秒。

## 测量环境

| 项 | 值 |
|---|---|
| 系统 | Windows 10 x64（10.0.19045） |
| 运行时 | .NET Framework 4.8，Windows PowerShell 5.1 |
| 手柄 | Flydigi APEX5 Wireless，2.4G 接收器，管道返回 `4/6` |
| 版本 | 2.0.1 |
| 轮次 | 5 |
| 计时 | `System.Diagnostics.Stopwatch` 包住整次进程调用 |

端到端包含进程创建、运行时初始化、读取、输出；不是只测函数耗时。

## 方法

`bench.ps1` 对每个场景跑 N 次，取排序后的中位数：

```powershell
$sw = [Diagnostics.Stopwatch]::StartNew()
$null = & $Action 2>&1
$sw.Stop()
```

被测命令：

```powershell
flydigi_battery.exe --source pipe --json
flydigi_battery.exe --source auto --json
powershell.exe -NoProfile -ExecutionPolicy Bypass -File flydigi_battery.ps1 -Source pipe -Json
powershell.exe -NoProfile -ExecutionPolicy Bypass -File flydigi_battery.ps1 -Source auto -Json
flydigi_battery.exe --preview _bench\exe.png
powershell.exe -NoProfile -ExecutionPolicy Bypass -File flydigi_tray.ps1 -Preview -PreviewPath _bench\ps1.png
```

预览图写到脚本目录的 `_bench`（工作区可能带 Low 完整性标签，写 `%TEMP%` 会被拒绝），结束时递归删除。

## 结果

单位 ms：

| 场景 | ps1 最快 / 中位 / 最慢 | exe 最快 / 中位 / 最慢 | 加速（中位） |
|---|---:|---:|---:|
| 管道 7 档 `--json` | 256 / 260 / 347 | 32 / 204 / 215 | **1.3x** |
| 自动 `--json` | 256 / 261 / 266 | 204 / 218 / 218 | **1.2x** |
| 导出图标预览 | 375 / 380 / 405 | 30 / 31 / 34 | **12.3x** |

### 怎么读这组数

- **管道场景波动很大**：exe 最快 32 ms、中位 204 ms。管道可用时服务端响应从几毫秒到一百多毫秒不等，`--debug` 常见 `pipe=125ms`；这是服务端调度/重连成本，不是本工具的代码问题。PS1 的最慢 347 ms 同样来自这里。
- **PS1 有约 50 ms 的固定开销**：启动 PowerShell、解析 17 KB 脚本、初始化类型。即使服务端很快，PS1 也很难低于 250 ms。
- **自动与管道几乎一样**：管道成功时完全不调用 XInput，两条路径在成功场景下等价。
- **预览差距最大**：C# 自检中绘制 45 张图标约 10 ms；PS1 需要 `Add-Type` + 逐张 GDI+ 绘制，整个进程约 380 ms。

## 已做的优化

| 优化 | 效果 |
|---|---|
| 编译为原生 IL exe，无脚本解析 | 省掉 PowerShell 启动与解释开销 |
| `WaitNamedPipeW(..., 0)` 零等待探测 | 管道不存在/被占用时立刻回退，不白等 `Connect` 超时 |
| 管道成功后跳过 XInput | 成功路径少一次动态库加载与 4 个 slot 探测 |
| 每次采样新建短连接 | 不与空间站 App 长期抢占唯一管道实例；也避免服务端残留 worker |
| 图标按 `size:level:levelMax:plugged` 缓存 | 状态不变时不重绘；托盘每 20 秒采样也只换引用 |
| 采样在后台线程 | UI 线程不阻塞，管道慢时菜单/气泡仍可响应 |
| `/optimize+`、`anycpu`、零第三方依赖 | 启动快、单文件、无解压/加载额外程序集 |
| 重定向时无 BOM UTF-8 | 脚本消费 JSON 不会遇到编码前缀 |

## 复现

```powershell
# 最干净的测量：先退出托盘，避免它每 20 秒抢占一次管道
.\bench.ps1 -Iterations 5
.\bench.ps1 -Iterations 10
```

可指定被测文件：

```powershell
.\bench.ps1 -Exe "$env:TEMP\fbtest\flydigi_battery.exe" -Iterations 10
```

## 注意

- 服务端响应时间受空间站服务状态、杀毒软件、首次 JIT 影响，绝对值不可跨机器比较；加速比更有参考意义。
- 手柄未连接时管道仍会返回空设备列表，耗时结构相同，但无法反映有电量时的 JSON 大小。
- 基准期间托盘若在运行，会周期性占用管道几毫秒，可能让个别样本变慢；对中位数影响很小。
- `--watch` 是循环读取，单次成本与上表管道场景一致；间隔越短，服务端压力越大。
