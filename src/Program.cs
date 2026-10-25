// 入口与命令行：状态输出 / JSON / 连续监视 / 图标预览 / 生成 ICO / 开机自启
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace FlydigiBattery
{
    internal sealed class Options
    {
        public string Source = "auto";
        public bool Json;
        public int Watch;
        public int PipeTimeoutMs = 350;
        public string PipeName = "fcs.sock";
        public string PreviewPath;
        public string MkIconPath;
        public bool Install;
        public bool Uninstall;
        public bool Debug;
        public bool Help;
        public bool Version;
        public bool SelfTest;
        public bool ProbePipe;
        public int IntervalSec;
    }

    internal static class Program
    {
        public const string VersionText = "2.0.1";

        internal static void WritePreview(string path)
        {
            int[] sizes = new int[] { 16, 20, 24, 32, 48 };
            int cell;
            using (Bitmap sheet = IconFactory.RenderSheet(sizes, out cell))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".");
                sheet.Save(path, ImageFormat.Png);
            }
        }

        internal static void WriteIconFile(string path)
        {
            byte[] bytes = IconFactory.EncodeIcoMulti(new int[] { 16, 20, 24, 32, 40, 48, 64, 256 });
            File.WriteAllBytes(path, bytes);
        }

#if TRAY
        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                return TrayMain(args);
            }
            catch (Exception e)
            {
                MessageBox.Show("托盘启动失败：\n" + e, "飞智手柄电量", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }

        private static int TrayMain(string[] args)
        {
            Options o = ParseArgs(args);
            if (o.Help) { ShowTrayHelp(); return 0; }
            if (o.Install) { Autostart.Enable(); return 0; }
            if (o.Uninstall) { Autostart.Disable(); return 0; }
            if (!string.IsNullOrEmpty(o.PreviewPath)) { WritePreview(o.PreviewPath); return 0; }
            if (!string.IsNullOrEmpty(o.MkIconPath)) { WriteIconFile(o.MkIconPath); return 0; }
            if (o.SelfTest) { return SelfTest.Run(o); }

            Reader.PipeName = o.PipeName;
            return TrayApp.Run(o.IntervalSec, o.Source, o.PipeTimeoutMs);
        }

        private static void ShowTrayHelp()
        {
            MessageBox.Show(
                "飞智手柄电量托盘 " + VersionText + "\n\n" +
                "直接运行 = 在任务栏托盘显示电量。\n" +
                "命令行参数：\n" +
                "  --interval N      采样间隔（秒，默认 20）\n" +
                "  --source X        auto | pipe（都读 7 档；XInput 只确认连接）\n" +
                "  --install         加入开机自启\n" +
                "  --uninstall       移除开机自启\n" +
                "  --preview PATH    导出图标对照表 PNG\n\n" +
                "文本输出请用同目录的 flydigi_battery.exe。",
                "飞智手柄电量", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
#else
        private static int Main(string[] args)
        {
            AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
            {
                Console.Error.WriteLine("未处理异常: " + e.ExceptionObject);
            };
            PrepareConsole();
            return ConsoleMain(args);
        }

        private static void PrepareConsole()
        {
            // 被重定向（脚本读取）时统一用无 BOM 的 UTF-8，避免控制台代码页带来的乱码
            try
            {
                if (Console.IsOutputRedirected)
                {
                    Console.OutputEncoding = new UTF8Encoding(false);
                }
            }
            catch { }
        }

        private static int ConsoleMain(string[] args)
        {
            Options o;
            try
            {
                o = ParseArgs(args);
            }
            catch (ArgumentException e)
            {
                Console.Error.WriteLine("参数错误: " + e.Message);
                PrintUsage();
                return 2;
            }

            if (o.Help) { PrintUsage(); return 0; }
            if (o.Version) { Console.WriteLine("flydigi_battery " + VersionText); return 0; }

            if (o.Install) { Autostart.Enable(); Console.WriteLine("已加入开机启动: " + Autostart.DescribeTarget()); return 0; }
            if (o.Uninstall) { Autostart.Disable(); Console.WriteLine("已移除开机启动。"); return 0; }
            if (!string.IsNullOrEmpty(o.PreviewPath))
            {
                WritePreview(o.PreviewPath);
                Console.WriteLine("已生成预览: " + Path.GetFullPath(o.PreviewPath));
                return 0;
            }
            if (!string.IsNullOrEmpty(o.MkIconPath))
            {
                WriteIconFile(o.MkIconPath);
                Console.WriteLine("已生成图标: " + Path.GetFullPath(o.MkIconPath));
                return 0;
            }
            if (o.SelfTest) { return SelfTest.Run(o); }
            if (o.ProbePipe) { return PipeProbe(o); }

            Reader.PipeName = o.PipeName;

            if (o.Watch > 0) { return WatchLoop(o); }

            Status status = Reader.ReadStatus(o.Source, o.PipeTimeoutMs, o.Debug);

            if (o.Json)
            {
                Console.Out.Write(JsonWriter.Status(status) + "\n");
                Console.Out.Flush();
                return status.Ok ? 0 : 1;
            }

            PrintStatus(status, o.Debug);
            return status.Ok ? 0 : 1;
        }

        private static int WatchLoop(Options o)
        {
            Console.WriteLine("监视中，每 " + o.Watch + "s 采样一次（Ctrl+C 结束）。档位变化即代表电量变化。");
            int? previous = null;
            DateTime? lastChange = null;
            while (true)
            {
                Status st = Reader.ReadStatus(o.Source, o.PipeTimeoutMs, o.Debug);
                DateTime now = DateTime.Now;
                int? level = (st.Ok && st.Devices.Count > 0 && st.Devices[0].HasLevel) ? st.Devices[0].Level : (int?)null;

                string trend = "";
                if (level.HasValue && previous.HasValue)
                {
                    if (level.Value > previous.Value) { trend = "↑ 上升"; }
                    else if (level.Value < previous.Value) { trend = "↓ 下降"; }
                }
                if (level.HasValue && previous.HasValue && level.Value != previous.Value) { lastChange = now; }
                if (level.HasValue) { previous = level; }

                string since = "";
                if (lastChange.HasValue)
                {
                    TimeSpan span = now - lastChange.Value;
                    since = string.Format("{0:00}:{1:00}:{2:00}", (int)span.TotalHours, span.Minutes, span.Seconds);
                }

                string body = Text.StatusLine(st, trend, since);
                WriteLineColored("[" + now.ToString("HH:mm:ss") + "] " + body, st.Ok ? ConsoleColor.Green : ConsoleColor.Yellow);
                Console.Out.Flush();

                Thread.Sleep(Math.Max(1, o.Watch) * 1000);
            }
        }

        internal static int PipeProbe(Options o)
        {
            Console.WriteLine("pipe probe: " + o.PipeName + "  (pid " + Process.GetCurrentProcess().Id + ")");
            try
            {
                using (NamedPipeClientStream p = new NamedPipeClientStream(".", o.PipeName, PipeDirection.InOut, PipeOptions.None))
                {
                    Stopwatch sw = Stopwatch.StartNew();
                    try
                    {
                        p.Connect(o.PipeTimeoutMs);
                        Console.WriteLine("  NamedPipeClientStream connect OK " + sw.ElapsedMilliseconds + "ms, CanRead=" + p.CanRead);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("  NamedPipeClientStream FAIL " + e.GetType().FullName +
                                          "  msg=" + e.Message + "  hr=0x" + e.HResult.ToString("X8") +
                                          "  win32=" + System.Runtime.InteropServices.Marshal.GetLastWin32Error());
                    }
                }
            }
            catch (Exception e) { Console.WriteLine("  outer FAIL " + e.GetType().Name + " " + e.Message); }

            try
            {
                using (FileStream fs = new FileStream(@"\\.\pipe\" + o.PipeName, FileMode.Open, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.None))
                {
                    Console.WriteLine("  FileStream open OK");
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("  FileStream FAIL " + e.GetType().Name + "  " + e.Message +
                                  "  win32=" + System.Runtime.InteropServices.Marshal.GetLastWin32Error());
            }
            return 0;
        }

        private static void PrintStatus(Status status, bool debug)
        {
            if (!status.Ok)
            {
                WriteLineColored("没读到电量：" + status.Error, ConsoleColor.Yellow);
                if (status.Connected)
                {
                    Console.WriteLine("提示：手柄已连接（XInput），但 7 档电量需要飞智服务管道。");
                    Console.WriteLine("      若空间站 App 正在运行，请退出 App 后重试；后台服务会继续运行，手柄不受影响。");
                }
                else
                {
                    Console.WriteLine("提示：未检测到手柄。打开手柄后重试；若空间站 App 占用管道，退出 App 后重试。");
                }
                if (debug && status.Timing != null) { Console.WriteLine("      " + status.Timing); }
                return;
            }

            Console.WriteLine("数据源: " + status.Source);
            WriteLineColored(Text.StatusLine(status, "", ""), ConsoleColor.Green);
            if (status.Source == Reader.PipeSource)
            {
                foreach (Device d in status.Devices)
                {
                    Console.WriteLine("  uid=" + (d.Uid ?? "") + "  deviceCode=" + (d.DeviceCode ?? ""));
                }
            }
            if (debug)
            {
                Console.WriteLine("  " + (status.Timing ?? ""));
            }
        }

#endif


        // ------------------------------------------------------------- 参数解析 ----

        internal static Options ParseArgs(string[] args)
        {
            Options o = new Options();
            for (int i = 0; i < args.Length; i++)
            {
                string raw = args[i];
                string a = raw.TrimStart('-', '/').ToLowerInvariant();
                string value = null;
                int eq = a.IndexOf('=');
                if (eq >= 0) { value = raw.Substring(raw.IndexOf('=') + 1); a = a.Substring(0, eq); }

                switch (a)
                {
                    case "source":
                    case "s":
                        o.Source = NeedValue(args, ref i, value, a).ToLowerInvariant();
                        if (o.Source != "auto" && o.Source != "pipe")
                        {
                            throw new ArgumentException("--source 只能是 auto / pipe（XInput 电量通道已移除）");
                        }
                        break;
                    case "json":
                        o.Json = true;
                        break;
                    case "watch":
                    case "w":
                        o.Watch = int.Parse(NeedValue(args, ref i, value, a), CultureInfo.InvariantCulture);
                        break;
                    case "pipe-timeout":
                    case "timeout":
                        o.PipeTimeoutMs = int.Parse(NeedValue(args, ref i, value, a), CultureInfo.InvariantCulture);
                        if (o.PipeTimeoutMs < 50) { throw new ArgumentException("--pipe-timeout 至少 50ms"); }
                        break;
                    case "pipe-name":
                        o.PipeName = NeedValue(args, ref i, value, a);
                        break;
                    case "interval":
                    case "i":
                        o.IntervalSec = int.Parse(NeedValue(args, ref i, value, a), CultureInfo.InvariantCulture);
                        break;
                    case "preview":
                        o.PreviewPath = NeedValue(args, ref i, value, a);
                        break;
                    case "mkicon":
                        o.MkIconPath = NeedValue(args, ref i, value, a);
                        break;
                    case "install":
                        o.Install = true;
                        break;
                    case "uninstall":
                        o.Uninstall = true;
                        break;
                    case "debug":
                    case "v":
                        o.Debug = true;
                        break;
                    case "selftest":
                        o.SelfTest = true;
                        break;
                    case "probe-pipe":
                        o.ProbePipe = true;
                        break;
                    case "version":
                        o.Version = true;
                        break;
                    case "help":
                    case "h":
                    case "?":
                        o.Help = true;
                        break;
                    case "tray":
                    case "gui":
                        o.Help = true; // 托盘是另一个 exe；这里给出提示
                        break;
                    default:
                        throw new ArgumentException("未知参数: " + raw);
                }
            }
            return o;
        }

        private static string NeedValue(string[] args, ref int i, string inlineValue, string name)
        {
            if (inlineValue != null) { return inlineValue; }
            if (i + 1 >= args.Length) { throw new ArgumentException("--" + name + " 缺少取值"); }
            i++;
            return args[i];
        }

        internal static void PrintUsage()
        {
            Console.WriteLine("飞智手柄电量 " + VersionText + " —— 读取飞智（Flydigi）手柄电量 / 充电 / 连接状态");
            Console.WriteLine();
            Console.WriteLine("用法: flydigi_battery.exe [选项]");
            Console.WriteLine();
            Console.WriteLine("  --source auto|pipe         auto/pipe 都读飞智服务（7 档）；XInput 只用于确认手柄是否已连接");
            Console.WriteLine("  --json                     输出 JSON");
            Console.WriteLine("  --watch N                  每 N 秒采样一次，显示上升/下降与距上次变化");
            Console.WriteLine("  --pipe-timeout MS          管道连接超时，默认 350ms（空间站 App 占用时会立刻回退）");
            Console.WriteLine("  --preview PATH             导出图标对照表 PNG");
            Console.WriteLine("  --mkicon PATH              生成多尺寸 ICO（构建用）");
            Console.WriteLine("  --install / --uninstall    加入 / 移除开机自启（指向 flydigi_battery_tray.exe）");
            Console.WriteLine("  --debug                    额外打印各阶段耗时");
            Console.WriteLine("  --selftest                 自检（绘制、ICO、格式化、通道）");
            Console.WriteLine("  --help / --version");
            Console.WriteLine();
            Console.WriteLine("托盘: 直接运行 flydigi_battery_tray.exe（无窗口，登录时开机自启）。");
        }

        internal static void WriteLineColored(string text, ConsoleColor color)
        {
            try
            {
                ConsoleColor old = Console.ForegroundColor;
                Console.ForegroundColor = color;
                Console.WriteLine(text);
                Console.ForegroundColor = old;
            }
            catch
            {
                Console.WriteLine(text);
            }
        }
    }
}
