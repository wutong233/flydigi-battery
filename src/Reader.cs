// 读取通道：飞智服务管道（7 档）；XInput 只用于确认手柄是否已连接
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.RegularExpressions;

namespace FlydigiBattery
{
    internal static class Reader
    {
        public const string PipeNameDefault = "fcs.sock";
        public const string PipeSource = "飞智后台服务（与空间站界面同源，7 档）";

        // IpcCommand { cmdId = 4097 (GetConnectedDevices), getConnectedDeviceParam = {} }
        private static readonly byte[] GetDevicesRequest = new byte[] { 0x08, 0x81, 0x20, 0x1A, 0x00 };

        private static string _pipeName = PipeNameDefault;

        public static string PipeName
        {
            get { return _pipeName; }
            set { _pipeName = string.IsNullOrEmpty(value) ? PipeNameDefault : value; }
        }

        /// <summary>
        /// 读一次完整状态：只从飞智服务管道取 7 档电量；
        /// 管道不可用时用 XInputGetState 确认手柄是否已连接（不读电量）。
        /// </summary>
        public static Status ReadStatus(string source, int pipeTimeoutMs, bool debug)
        {
            if (source != "auto" && source != "pipe")
            {
                throw new ArgumentException("source 只能是 auto / pipe（XInput 电量通道已移除）");
            }

            Stopwatch total = Stopwatch.StartNew();
            Status pipeStatus = null;
            string pipeError = null;
            long pipeMs = 0;
            long xiMs = 0;

            if (PipeAvailability.HasAvailableInstance(_pipeName))
            {
                Stopwatch sw = Stopwatch.StartNew();
                try { pipeStatus = ReadPipe(pipeTimeoutMs); }
                catch (Exception e) { pipeError = e.Message; }
                finally { pipeMs = sw.ElapsedMilliseconds; }
            }
            else
            {
                pipeError = "管道当前不可用（服务未运行或已被占用）";
            }

            Status result;
            if (pipeStatus != null && pipeStatus.Ok && pipeStatus.Devices.Count > 0)
            {
                pipeStatus.Source = PipeSource;
                pipeStatus.Connected = true;
                pipeStatus.ConnectionSource = "pipe";
                foreach (Device d in pipeStatus.Devices) { Text.Fill(d); }
                result = pipeStatus;
            }
            else
            {
                if (pipeStatus != null)
                {
                    pipeError = pipeStatus.Ok
                        ? "服务返回了响应，但里面没有已连接设备"
                        : pipeStatus.Error;
                }

                Stopwatch sw = Stopwatch.StartNew();
                int slot;
                bool connected = TryGetConnectedSlot(out slot);
                xiMs = sw.ElapsedMilliseconds;

                string message;
                if (connected)
                {
                    message = "手柄已连接（XInput），但 7 档电量不可用：" + (pipeError ?? "未知错误");
                }
                else
                {
                    message = "未连接手柄";
                    if (!string.IsNullOrEmpty(pipeError)) { message += "；管道: " + pipeError; }
                }

                result = Status.Fail(message);
                result.Connected = connected;
                result.ConnectionSource = connected ? "xinput" : null;
            }

            result.Timestamp = DateTime.Now;
            result.Timing = string.Format("pipe={0}ms xinput-check={1}ms total={2}ms",
                pipeMs, xiMs, total.ElapsedMilliseconds);
            return result;
        }

        // ------------------------------------------------------------------ 管道 ----

        private static Status ReadPipe(int timeoutMs)
        {
            using (NamedPipeClientStream pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.None))
            {
                try
                {
                    pipe.Connect(timeoutMs);
                }
                catch (Exception e)
                {
                    throw new IOException(ExplainConnectFailure(e, timeoutMs));
                }

                FdgProtocol.Write(pipe, GetDevicesRequest);
                byte[] response = FdgProtocol.Read(pipe);
                if (response == null || response.Length == 0) { return Status.Fail("管道返回空响应"); }
                return ParseIpcResult(response);
            }
        }

        private static string ExplainConnectFailure(Exception e, int timeoutMs)
        {
            string msg = e.Message;
            if (e is UnauthorizedAccessException ||
                msg.IndexOf("拒绝", StringComparison.Ordinal) >= 0 ||
                msg.IndexOf("access is denied", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "连接 " + _pipeName + " 被拒绝：管道要求进程至少为 Medium 完整性级别。" +
                       "请运行 build.ps1（会自动标记），或执行 icacls <exe> /setintegritylevel Medium。";
            }
            if (AppProcess.IsRunning())
            {
                return "飞智空间站 App 正在运行并占用 " + _pipeName + "（管道单客户端）。" +
                       "请先退出空间站（托盘右键 → 退出），后台服务会继续运行，手柄不受影响。";
            }
            if (e is TimeoutException || msg.IndexOf("超时", StringComparison.Ordinal) >= 0 ||
                msg.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0 ||
                msg.IndexOf("信号灯", StringComparison.Ordinal) >= 0)
            {
                return "连接 " + _pipeName + " 超时（" + timeoutMs + "ms）。若空间站 App 正在运行会是这个现象；" +
                       "可退出 App 后重试。";
            }
            return "连接 " + _pipeName + " 失败：" + msg;
        }

        private static Status ParseIpcResult(byte[] data)
        {
            Status st = new Status();
            st.Ok = true;
            bool sawDeviceList = false;

            ProtoReader root = new ProtoReader(data);
            while (root.Has)
            {
                int field, wire;
                root.ReadTag(out field, out wire);
                if (field == 5 && wire == 2)
                {
                    sawDeviceList = true;
                    byte[] body = root.ReadBytes();
                    ProtoReader list = new ProtoReader(body);
                    while (list.Has)
                    {
                        int lf, lw;
                        list.ReadTag(out lf, out lw);
                        if (lf == 1 && lw == 2) { st.Devices.Add(ParseDevice(list.ReadBytes())); }
                        else { list.SkipValue(lw); }
                    }
                }
                else if (field == 3 && wire == 0)
                {
                    int code = (int)root.ReadVarint();
                    if (code != 0) { st.Ok = false; st.Error = "服务返回错误码 " + code; }
                }
                else
                {
                    root.SkipValue(wire);
                }
            }

            if (!sawDeviceList && st.Ok) { st.Error = "响应里没有设备列表"; }
            return st;
        }

        private static Device ParseDevice(byte[] data)
        {
            Device dev = new Device();
            dev.Source = "pipe";
            dev.LevelMax = 6;

            ProtoReader p = new ProtoReader(data);
            while (p.Has)
            {
                int field, wire;
                p.ReadTag(out field, out wire);
                switch (field)
                {
                    case 1: dev.ProductName = p.ReadString(); break;
                    case 2: dev.Category = (int)p.ReadVarint(); break;
                    case 3: dev.ConnectType = (int)p.ReadVarint(); break;
                    case 4: dev.DeviceCode = p.ReadString(); break;
                    case 7: dev.Uid = DecodeUid(p.ReadBytes()); break;
                    case 8: dev.Connected = p.ReadVarint() != 0; break;
                    case 9: dev.Level = (int)p.ReadVarint(); break;
                    case 11: break; // vendorId，本工具用不到
                    case 12: break; // productId，本工具用不到
                    default: p.SkipValue(wire); break;
                }
            }
            return dev;
        }

        // 有的固件直接回 ASCII 十六进制串，有的回 16 字节原始 UID
        private static string DecodeUid(byte[] raw)
        {
            string s = Encoding.UTF8.GetString(raw);
            if (Regex.IsMatch(s, "^[0-9a-fA-F]{16,64}$")) { return s.ToLowerInvariant(); }
            StringBuilder sb = new StringBuilder(raw.Length * 2);
            foreach (byte b in raw) { sb.Append(b.ToString("x2")); }
            return sb.ToString();
        }

        // -------------------------------------------------- XInput 连接检测 ----

        /// <summary>只用 XInputGetState 确认是否有手柄已连接，不读取电量。</summary>
        private static bool TryGetConnectedSlot(out int slot)
        {
            slot = -1;
            if (!XInput.Available) { return false; }
            for (uint i = 0; i < 4; i++)
            {
                XINPUT_STATE state;
                if (XInput.GetState(i, out state) == 0)
                {
                    slot = (int)i;
                    return true;
                }
            }
            return false;
        }
    }

    /// <summary>判断飞智空间站 App 是否在跑（只在管道失败时用来给出更准确的提示）。</summary>
    internal static class AppProcess
    {
        private const string ProcessName = "Flydigi Space Station";

        public static bool IsRunning()
        {
            try
            {
                Process[] ps = Process.GetProcessesByName(ProcessName);
                bool any = ps.Length > 0;
                foreach (Process p in ps) { p.Dispose(); }
                return any;
            }
            catch { return false; }
        }
    }
}
