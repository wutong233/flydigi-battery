// 数据模型：设备、状态，以及“连接方式 / 充电状态 / 档位文字 / 百分比估算”的换算
using System;
using System.Collections.Generic;
using System.Text;

namespace FlydigiBattery
{
    internal sealed class Device
    {
        public string Source = "";
        public int Slot = -1;
        public bool Connected;
        public string ProductName = "";
        public string DeviceCode = "";
        public string Uid = "";
        public int Category = -1;
        public int ConnectType = -1;
        public int Level = -1;
        public int LevelMax = 6;
        public int Percent = -1;
        public string LevelText = "";
        public string Approx = "";
        public string Connect = "";
        public string Charging = "";
        public bool Plugged;

        public bool HasLevel { get { return Level >= 0; } }
    }

    internal sealed class Status
    {
        public bool Ok;
        /// <summary>手柄是否已连接（管道设备或 XInputGetState 确认）。</summary>
        public bool Connected;
        /// <summary>连接状态的来源："pipe" 或 "xinput"。</summary>
        public string ConnectionSource;
        public string Error;
        public string Source = "";
        public DateTime Timestamp = DateTime.Now;
        public readonly List<Device> Devices = new List<Device>();
        /// <summary>各阶段耗时（--debug 时输出，也写进 JSON 的 timing 字段）。</summary>
        public string Timing;

        public Device Dev { get { return Devices.Count > 0 ? Devices[0] : null; } }

        public static Status Fail(string message)
        {
            Status s = new Status();
            s.Ok = false;
            s.Error = message;
            return s;
        }
    }

    /// <summary>把固件档位/连接方式翻译成人话（与原 PowerShell 版逐字一致）。</summary>
    internal static class Text
    {
        private static readonly string[] LevelText6 = new string[] { "空", "极低", "低", "中低", "中", "较高", "满" };
        private static readonly string[] CategoryText = new string[] { "未知设备", "手柄", "散热器", "键盘", "鼠标", "耳机", "充电座" };
        private static readonly string[] ConnectTypeText = new string[] { "", "USB 有线", "2.4G 接收器", "蓝牙" };

        public static string Level6(int level)
        {
            if (level < 0 || level > 6) { return "未知"; }
            return LevelText6[level];
        }

        /// <summary>档位换算成整数百分比（托盘图标与提示文字共用）。</summary>
        public static int PercentOf(int level, int maxLevel)
        {
            if (level < 0 || maxLevel <= 0) { return -1; }
            int clamped = Math.Min(level, maxLevel);
            return (int)Math.Round(clamped * 100.0 / maxLevel);
        }

        /// <summary>7 档均分换算成估算区间（不是固件原始百分比）。</summary>
        public static void Percent6(int level, out int percent, out string approx)
        {
            if (level < 0 || level > 6) { percent = -1; approx = "?"; return; }
            int pct = PercentOf(level, 6);
            int lo = Math.Max(0, pct - 8);
            int hi = Math.Min(100, pct + 8);
            percent = pct;
            approx = "≈" + pct + "% (" + lo + "-" + hi + "%)";
        }

        public static string Connect(int category, int connectType)
        {
            string cat = (category >= 0 && category < CategoryText.Length) ? CategoryText[category] : ("类别" + category);
            if (connectType >= 1 && connectType < ConnectTypeText.Length)
            {
                return cat + " / " + ConnectTypeText[connectType];
            }
            return cat;
        }

        /// <summary>飞智的数据模型里没有“是否在充电”字段，只能按固件 connectType 推断。</summary>
        public static string Charging(int connectType, int level, int maxLevel, out bool plugged)
        {
            bool full = level >= 0 && maxLevel > 0 && level >= maxLevel;
            switch (connectType)
            {
                case 1:
                    plugged = true;
                    return full ? "USB 有线 · 已充满（接着线）" : "USB 有线 · 充电中";
                case 2:
                    plugged = false;
                    return "无线电池供电 · 未充电";
                case 3:
                    plugged = false;
                    return "蓝牙电池供电 · 未充电";
            }
            plugged = false;
            return "未知";
        }

        /// <summary>把原始字段补齐成可展示的文字（档位文字、估算百分比、连接、充电）。</summary>
        public static void Fill(Device dev)
        {
            dev.LevelText = Level6(dev.Level);
            Percent6(dev.Level, out dev.Percent, out dev.Approx);
            dev.Connect = Connect(dev.Category, dev.ConnectType);
            dev.Charging = Charging(dev.ConnectType, dev.Level, dev.LevelMax, out dev.Plugged);
        }

        /// <summary>单行展示，和原 PowerShell 版 Format-StatusLine 一致。</summary>
        public static string StatusLine(Status status, string trend, string sinceChange)
        {
            if (!status.Ok) { return "无数据：" + status.Error; }
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < status.Devices.Count; i++)
            {
                if (i > 0) { sb.Append('\n'); }
                Device d = status.Devices[i];
                string name = string.IsNullOrEmpty(d.ProductName) ? ("手柄 " + d.Slot) : d.ProductName;
                string level = d.HasLevel ? (d.Level + "/" + d.LevelMax) : "?";
                sb.AppendFormat("{0}  电量 {1} {2} {3}  充电: {4}  连接: {5}", name, level, d.LevelText, d.Approx, d.Charging, d.Connect);
                if (!string.IsNullOrEmpty(trend)) { sb.Append("  ").Append(trend); }
                if (!string.IsNullOrEmpty(sinceChange)) { sb.Append("  (距上次变化 ").Append(sinceChange).Append(")"); }
            }
            return sb.ToString();
        }

        /// <summary>气泡/详情里的多行文本。</summary>
        public static string Detail(Status status)
        {
            if (!status.Ok || status.Devices.Count == 0)
            {
                string msg = status.Connected ? "手柄已连接（XInput），电量不可用" : "未连接";
                if (!string.IsNullOrEmpty(status.Error)) { msg += "\n" + status.Error; }
                return msg;
            }
            Device d = status.Devices[0];
            StringBuilder sb = new StringBuilder();
            sb.AppendFormat("电量: {0}/{1} {2} {3}", d.Level, d.LevelMax, d.LevelText, d.Approx);
            sb.Append("\n充电: ").Append(d.Charging);
            sb.Append("\n连接: ").Append(d.Connect);
            if (!string.IsNullOrEmpty(d.Uid)) { sb.Append("\nUID: ").Append(d.Uid); }
            sb.Append("\n更新: ").Append(status.Timestamp.ToString("HH:mm:ss"));
            return sb.ToString();
        }
    }
}
