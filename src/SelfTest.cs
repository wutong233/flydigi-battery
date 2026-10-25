// --selftest：不依赖真实手柄，验证绘制 / ICO / 文案 / 通道 / JSON
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;

namespace FlydigiBattery
{
    internal static class SelfTest
    {
        private static int _failures;

        public static int Run(Options o)
        {
            _failures = 0;
            Console.WriteLine("flydigi_battery 自检 " + Program.VersionText);
            Console.WriteLine("----------------------------------------");

            CheckText();
            CheckRendering();
            CheckIco();
            CheckChannels(o);
            CheckJson();

            Console.WriteLine("----------------------------------------");
            Console.WriteLine(_failures == 0 ? "全部通过" : ("失败 " + _failures + " 项"));
            return _failures == 0 ? 0 : 1;
        }

        private static void Pass(string name, string detail)
        {
            Console.WriteLine("  PASS  " + name + (string.IsNullOrEmpty(detail) ? "" : "   " + detail));
        }

        private static void Fail(string name, string detail)
        {
            _failures++;
            Console.WriteLine("  FAIL  " + name + (string.IsNullOrEmpty(detail) ? "" : "   " + detail));
        }

        private static void Expect(string name, string actual, string expected)
        {
            if (string.Equals(actual, expected, StringComparison.Ordinal)) { Pass(name, actual); }
            else { Fail(name, "期望 [" + expected + "] 实际 [" + actual + "]"); }
        }

        private static void ExpectColor(string name, Color actual, int r, int g, int b)
        {
            if (actual.R == r && actual.G == g && actual.B == b) { Pass(name, actual.ToString()); }
            else { Fail(name, actual.ToString()); }
        }

        private static void ExpectFraction(string name, double actual, double expected)
        {
            if (Math.Abs(actual - expected) < 0.0001) { Pass(name, actual.ToString("0.###")); }
            else { Fail(name, "期望 " + expected + " 实际 " + actual); }
        }

        // ------------------------------------------------------------- 文案 ----

        private static void CheckText()
        {
            Expect("Level6(5)", Text.Level6(5), "较高");
            Expect("Level6(0)", Text.Level6(0), "空");
            Expect("Connect(手柄,2.4G)", Text.Connect(1, 2), "手柄 / 2.4G 接收器");
            Expect("Connect(手柄,USB)", Text.Connect(1, 1), "手柄 / USB 有线");
            Expect("Connect(手柄,蓝牙)", Text.Connect(1, 3), "手柄 / 蓝牙");

            int percent;
            string approx;
            Text.Percent6(5, out percent, out approx);
            if (percent == 83) { Pass("Percent6(5)", percent + " " + approx); } else { Fail("Percent6(5)", percent.ToString()); }
            Expect("Approx6(5)", approx, "≈83% (75-91%)");
            Text.Percent6(4, out percent, out approx);
            if (percent == 67) { Pass("Percent6(4)", percent + " " + approx); } else { Fail("Percent6(4)", percent.ToString()); }
            Expect("Approx6(4)", approx, "≈67% (59-75%)");
            Expect("PercentOf(0/6)", Text.PercentOf(0, 6).ToString(), "0");
            Expect("PercentOf(4/6)", Text.PercentOf(4, 6).ToString(), "67");
            Expect("PercentOf(6/6)", Text.PercentOf(6, 6).ToString(), "100");
            Expect("图标文字(4/6)", IconFactory.PercentLabel(4, 6), "67");
            Expect("图标文字(6/6)", IconFactory.PercentLabel(6, 6), "");

            bool plugged;
            Expect("Charging(wired,4/6)", Text.Charging(1, 4, 6, out plugged), "USB 有线 · 充电中");
            if (!plugged) { Fail("Charging(wired) plugged", "false"); }
            Expect("Charging(wired,full)", Text.Charging(1, 6, 6, out plugged), "USB 有线 · 已充满（接着线）");
            Expect("Charging(dongle)", Text.Charging(2, 5, 6, out plugged), "无线电池供电 · 未充电");
            Expect("Charging(bluetooth)", Text.Charging(3, 5, 6, out plugged), "蓝牙电池供电 · 未充电");
            Expect("Charging(unknown)", Text.Charging(-1, 5, 6, out plugged), "未知");

            Device d = new Device();
            d.Category = 1;
            d.ConnectType = 2;
            d.Level = 4;
            d.LevelMax = 6;
            Text.Fill(d);
            Expect("Fill(levelText)", d.LevelText, "中");
            Expect("Fill(connect)", d.Connect, "手柄 / 2.4G 接收器");
            Expect("Fill(charging)", d.Charging, "无线电池供电 · 未充电");

            Status withBattery = new Status();
            withBattery.Ok = true;
            withBattery.Connected = true;
            withBattery.Devices.Add(d);
            Expect("托盘提示(有电量)", TrayApp.Tooltip(withBattery), "67% (4/6) · 无线");
            Status noBattery = Status.Fail("x");
            noBattery.Connected = true;
            Expect("托盘提示(无电量)", TrayApp.Tooltip(noBattery), "已连接 · 电量不可用");

            Device full = new Device();
            full.Category = 1;
            full.ConnectType = 2;
            full.Level = 6;
            full.LevelMax = 6;
            Text.Fill(full);
            Status fullStatus = new Status();
            fullStatus.Ok = true;
            fullStatus.Connected = true;
            fullStatus.Devices.Add(full);
            Expect("托盘提示(满电)", TrayApp.Tooltip(fullStatus), "100% (6/6) · 无线");

            ExpectColor("颜色 0", IconFactory.LevelColor(0), 231, 76, 60);
            ExpectColor("颜色 1", IconFactory.LevelColor(1), 231, 76, 60);
            ExpectColor("颜色 2", IconFactory.LevelColor(2), 231, 76, 60);
            ExpectColor("颜色 3", IconFactory.LevelColor(3), 241, 196, 15);
            ExpectColor("颜色 4", IconFactory.LevelColor(4), 241, 196, 15);
            ExpectColor("颜色 5", IconFactory.LevelColor(5), 46, 204, 113);
            ExpectColor("颜色 6", IconFactory.LevelColor(6), 46, 204, 113);
            ExpectColor("颜色 -1", IconFactory.LevelColor(-1), 150, 150, 150);

            ExpectFraction("填充 0/6", IconFactory.FillFraction(0, 6), 0.0);
            ExpectFraction("填充 1/6", IconFactory.FillFraction(1, 6), 1.0 / 6.0);
            ExpectFraction("填充 3/6", IconFactory.FillFraction(3, 6), 0.5);
            ExpectFraction("填充 5/6", IconFactory.FillFraction(5, 6), 5.0 / 6.0);
            ExpectFraction("填充 6/6", IconFactory.FillFraction(6, 6), 1.0);
        }

        // ------------------------------------------------------------- 绘制 ----

        private static void CheckRendering()
        {
            int[] sizes = new int[] { 16, 20, 24, 32, 48 };
            Stopwatch sw = Stopwatch.StartNew();
            int count = 0;
            foreach (int size in sizes)
            {
                for (int level = 0; level <= 6; level++)
                {
                    using (Bitmap b = IconFactory.RenderBattery(level, 6, level == 4, size))
                    {
                        if (b.Width != size || b.Height != size) { Fail("RenderBattery 尺寸", size + "→" + b.Width); }
                        if (!HasOpaquePixel(b)) { Fail("RenderBattery 空白", "level=" + level + " size=" + size); }
                        count++;
                    }
                }
                using (Bitmap b = IconFactory.RenderUnknown(size))
                {
                    if (!HasOpaquePixel(b)) { Fail("RenderUnknown 空白", "size=" + size); }
                    count++;
                }
                using (Bitmap b = IconFactory.RenderDisabled(size))
                {
                    if (!HasOpaquePixel(b)) { Fail("RenderDisabled 空白", "size=" + size); }
                    count++;
                }
            }
            sw.Stop();
            Pass("图标绘制", count + " 张 / " + sw.ElapsedMilliseconds + "ms");

            Status bad = Status.Fail("x");
            using (Bitmap b = IconFactory.Render(bad, 16))
            {
                if (b.Width == 16) { Pass("Render(未连接)", "16x16"); } else { Fail("Render(未连接)", b.Width.ToString()); }
            }

            Status linked = Status.Fail("x");
            linked.Connected = true;
            using (Bitmap b = IconFactory.Render(linked, 16))
            {
                if (b.Width == 16) { Pass("Render(已连接无电量)", "16x16"); } else { Fail("Render(已连接无电量)", b.Width.ToString()); }
            }

            // 图标对照表（只在内存里渲染，不落盘；需要看图请用 --preview）
            int cell;
            using (Bitmap sheet = IconFactory.RenderSheet(sizes, out cell))
            {
                if (sheet.Width > 0 && sheet.Height > 0) { Pass("预览图渲染", sheet.Width + "x" + sheet.Height); }
                else { Fail("预览图渲染", "空图"); }
            }
        }

        private static bool HasOpaquePixel(Bitmap b)
        {
            for (int y = 0; y < b.Height; y++)
            {
                for (int x = 0; x < b.Width; x++)
                {
                    if (b.GetPixel(x, y).A > 128) { return true; }
                }
            }
            return false;
        }

        // -------------------------------------------------------------- ICO ----

        private static void CheckIco()
        {
            using (Bitmap b = IconFactory.RenderBattery(5, 6, false, 32))
            {
                byte[] bytes = IconFactory.EncodeIco(b);
                try
                {
                    using (MemoryStream ms = new MemoryStream(bytes))
                    using (Icon icon = new Icon(ms))
                    {
                        if (icon.Width == 32 && icon.Height == 32) { Pass("ICO 单图", bytes.Length + " bytes"); }
                        else { Fail("ICO 单图", icon.Width + "x" + icon.Height); }
                    }
                }
                catch (Exception e) { Fail("ICO 单图", e.Message); }
            }

            byte[] multi = IconFactory.EncodeIcoMulti(new int[] { 16, 20, 24, 32, 48 });
            try
            {
                using (MemoryStream ms = new MemoryStream(multi))
                using (Icon icon = new Icon(ms))
                {
                    if (icon.Width > 0) { Pass("ICO 多尺寸", multi.Length + " bytes, " + icon.Width + "x" + icon.Height); }
                    else { Fail("ICO 多尺寸", "0"); }
                }
            }
            catch (Exception e) { Fail("ICO 多尺寸", e.Message); }
        }

        // ------------------------------------------------------------ 通道 ----

        private static void CheckChannels(Options o)
        {
            Stopwatch sw = Stopwatch.StartNew();
            Status st;
            try
            {
                st = Reader.ReadStatus(o.Source, o.PipeTimeoutMs, true);
                sw.Stop();
                string detail = st.Ok
                    ? st.Source + " · connected=" + st.Connected
                    : st.Error;
                Pass("读取状态", sw.ElapsedMilliseconds + "ms · " + detail);
                if (!string.IsNullOrEmpty(st.Timing)) { Pass("分阶段耗时", st.Timing); }
            }
            catch (Exception e)
            {
                sw.Stop();
                Fail("读取状态", e.GetType().Name + ": " + e.Message);
            }
        }

        private static void CheckJson()
        {
            Status fake = new Status();
            fake.Ok = true;
            fake.Connected = true;
            fake.ConnectionSource = "pipe";
            fake.Source = "pipe";
            Device dev = new Device();
            dev.Source = "pipe";
            dev.Connected = true;
            dev.ProductName = "Flydigi";
            dev.Category = 1;
            dev.ConnectType = 2;
            dev.Level = 4;
            dev.LevelMax = 6;
            Text.Fill(dev);
            fake.Devices.Add(dev);

            string json = JsonWriter.Status(fake);
            if (json.StartsWith("{") && json.EndsWith("}") && json.Contains("\"connected\":true") &&
                json.Contains("\"level\":4") && json.Contains("\"connectionSource\":\"pipe\""))
            {
                Pass("JSON 序列化", json.Length + " chars");
            }
            else
            {
                Fail("JSON 序列化", json);
            }

            Status failed = Status.Fail("测试\"转义\"");
            failed.Connected = true;
            failed.ConnectionSource = "xinput";
            string json2 = JsonWriter.Status(failed);
            if (json2.Contains("\"ok\":false") && json2.Contains("\"connected\":true") && json2.Contains("\\\"转义\\\""))
            {
                Pass("JSON 连接状态", json2.Length + " chars");
            }
            else
            {
                Fail("JSON 连接状态", json2);
            }
        }
    }
}
