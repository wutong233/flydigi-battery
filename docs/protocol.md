# 管道协议（逆向记录）

本文记录 `\\.\pipe\fcs.sock` 的传输格式与取电量所需的 protobuf 字段，供后续维护。实现见 [`src/Protocol.cs`](../src/Protocol.cs) 与 [`src/Reader.cs`](../src/Reader.cs)。

## 端点

| 项 | 值 |
|---|---|
| 管道 | `\\.\pipe\fcs.sock` |
| 服务端 | `SpaceStationService.exe`（飞智空间站后台服务） |
| 客户端 | 本工具、空间站 App |
| 并发 | 服务端只建一个实例，**同一时间只能有一个客户端** |

空间站 App 运行时它占着管道；退出 App 后后台服务继续运行，管道空闲，本工具即可读取。手柄连接不受影响。

## 传输帧

协议头是 ASCII 行，以 `\n` 结尾。服务端可能返回三种帧：

### FDG_PROTOCOL

```text
"FDG_PROTOCOL\n"  + int32le(len) + payload[len]
```

请求与最常见的响应都用这种。

### FDG_CHUNK

```text
"FDG_CHUNK\n" + int32le(total) + int32le(count) + count × (int32le(len) + bytes)
```

用于较大响应。`count` 合法范围是 1–65535。

### FDG_COMPRESSED

```text
"FDG_COMPRESSED\n" + int32le(len) + gzip(payload)
```

解压后得到 protobuf。三种帧的解码在 `FdgProtocol.Read` 中完成。

限制：单帧最大 32 MiB；协议头最长 64 字节；未知头直接抛 `IOException`。

## 请求：GetConnectedDevices

```csharp
// IpcCommand { cmdId = 4097, getConnectedDeviceParam = {} }
byte[] request = { 0x08, 0x81, 0x20, 0x1A, 0x00 };
```

- `08`：field 1, varint → `cmdId`
- `81 20`：varint 4097
- `1A`：field 3, length-delimited → `getConnectedDeviceParam`
- `00`：长度 0，即空消息

流程：连接 → 写请求 → 读一帧 → 解析 `IpcResult`。每次采样都新建连接，不常驻。

## 响应：IpcResult

本工具只关心两个字段：

| 字段 | 类型 | 含义 |
|---:|---|---|
| 3 | varint | `code`，0 为成功；非 0 时 `ok=false`，错误为“服务返回错误码 N” |
| 5 | message | `getConnectedDevices`，内含 repeated `DeviceInfo`（其 field 1） |

顶层没有 field 5 时视为“响应里没有设备列表”。

### DeviceInfo

| 字段 | 类型 | 本工具用途 |
|---:|---|---|
| 1 | string | `productName`，如 `Flydigi APEX5 Wireless` |
| 2 | varint | `category`：1=手柄、6=充电座 |
| 3 | varint | `connectType`：1=Wired、2=Dongle、3=Bluetooth |
| 4 | string | `deviceCode`，如 `k5` |
| 5 | varint | `deviceType`，未使用 |
| 6 | bytes | `deviceMac`，未使用 |
| 7 | bytes | `uid`；可能是 ASCII 十六进制串，也可能是 16 字节原始值，两种都做兼容 |
| 8 | varint | `isConnected` |
| **9** | **varint** | **`battery` 档位 0–6 —— 唯一电量来源** |
| 10 | string | `firmwareVersion`，未使用 |
| 11 | varint | `vendorId`，跳过 |
| 12 | varint | `productId`，跳过 |
| 13 | message | `firmwares`，未使用 |
| 14 | varint | `isNewArchitecture`，未使用 |

UID 解码：若字节串整体匹配 `^[0-9a-fA-F]{16,64}$`，按十六进制字符串转小写；否则逐字节转 `x2`。实机 APEX5 返回的 UID 为 26 位十六进制字符串（示例中已用占位值）。

### 枚举

`Category`：

| 值 | 含义 |
|---:|---|
| 1 | 手柄 |
| 2 | 散热器 |
| 3 | 键盘 |
| 4 | 鼠标 |
| 5 | 耳机 |
| 6 | 充电座 |

`ConnectType`：

| 值 | 含义 | 充电推断 |
|---:|---|---|
| 1 | Wired / USB 有线 | `plugged=true`，`充电中`；满档时 `已充满（接着线）` |
| 2 | Dongle / 2.4G 接收器 | 电池供电，`未充电` |
| 3 | Bluetooth | 电池供电，`未充电` |

## 其它命令

| cmdId | 名称 | 备注 |
|---:|---|---|
| 4097 | `GetConnectedDevices` | 本工具使用 |
| 4099 | `GetDeviceDetailInfo` | 需带 `category=1`；会先回 8 字节 ACK，再回真正数据帧，必须连读两帧。可 dump 固件版本、能力位、板载配置 |

### 不要发送 Init

**不要发 `Init(cmdId=1)`。** 实测会让该连接后续请求全部无响应；如果在请求中途强杀进程，还可能把服务端 worker 卡死，需要重启服务或重连 App 才能恢复。正确做法是连接后直接发 4097。

## 为什么没有百分比

- 厂商 HID 协议的 `Ping/GetUID`（cmdId 9）应答里，`offset 22` 是电量，取值只有 `0,1,2,3,4`，`255` 为未用/预留。
- App 前端按 `level <= 6` 选择 `assets/images/battery/Power0.svg … Power6.svg`，语言包里没有 `%` 电量文案。
- 管道 `DeviceInfo.battery` 同样只有 0–6。

因此“最精确值”就是档位本身。本工具输出的 `percent = round(level*100/6)`、`approx = ±8%` 只是换算展示，不应作为固件测量值。

## 为什么不再使用 XInput 电量

`XInputGetBatteryInformation` 曾作为备选通道，已删除：

1. 只有 4 档（空/低/中/满），与官方 7 档不一致。
2. `batteryType` 描述的是接收器/适配器：2.4G 接收器插在 USB 上时，无线手柄也会报 `wired`，会被误判成“充电中”。
3. 之前用 PnP 的 PID 差异推断是否插线，未在实机插线场景验证，容易误导。
4. 管道直接给出 7 档 + `connectType`，是唯一与官方界面同源的数据。

现在只保留 `XInputGetState`：管道拿不到数据时，用它区分“手柄已连接但电量不可用”和“未连接”。`XINPUT_BATTERY_INFORMATION`、PnP 设备树枚举均已移除。

## 试过但不可行的路线

| 路线 | 结果 |
|---|---|
| UIA 读界面文字 | 主窗口常驻托盘、`IsWindowVisible=False`，UIA 树里没有电池文字 |
| 直连 HID 厂商通道（`MI_02&Col01`，page `0xFFA0`） | 可被第三方打开（不独占），但读不到推送；状态报告由服务主动请求 |
| 服务日志 | 只记命令名 + 32 字节 ack，不记输入报告；`main.log` 只有请求没有响应 |
| 反编译服务 | 环境里 `dotnet` 存在但 NuGet 不可达（SSL 失败），装不了 ilspycmd |
| 配置缓存 | `Configs\Controller\k5\cache\*.dat` 是 protobuf 配置，不含电量 |

## tools/ 探针

这些脚本不参与构建，保留用于协议复查。其中 `schema.json`、`desc_bin/`、`out_*.txt` 等由飞智 App 提取，版权归原所有者；为避免再分发专有内容，这些文件被 `.gitignore` 排除，需要时可用 `desc.py` 等脚本在本机重新生成：

> 注意：运行这些脚本会在仓库根目录生成 `asar_out/`、`asar_full/`，二者已被 `.gitignore` 忽略。

| 脚本 | 用途 |
|---|---|
| `fcs_client.py` | 最小 FDG 客户端 |
| `fcs_dump.py` | dump 指定 cmdId 的响应（如 `python fcs_dump.py 4099`） |
| `fcs_probe.py` | 批量探测命令 |
| `pipe_probe.py` / `pipe_strings.py` | 管道存在性与字符串扫描 |
| `fake_fcs_server.ps1` / `mkfake.py` | 伪造服务端做离线测试 |
| `hid_enum.py` / `hid_probe.py` | HID 通道枚举（已确认不可行） |
| `schema.json` | 从 App 的 protobuf 定义解析出的 schema（未纳入仓库） |
| `out_cmds.txt` / `out_table.txt` | 命令表与枚举表（未纳入仓库） |

在 Git Bash 下运行 Python 前，确认 Python 可用；这些脚本与正式 exe 无关，删除也不影响使用。
