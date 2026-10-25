// 任务栏托盘（对应 flydigi_tray.ps1）：后台线程采样，UI 线程只负责换图标/换文本
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace FlydigiBattery
{
    internal sealed class TrayApp : ApplicationContext
    {
        /// <summary>和旧的 PowerShell 托盘共用同一个互斥体，避免同时出现两个图标。</summary>
        private const string MutexName = "Local\\FlydigiBatteryTray";

        private readonly NotifyIcon _icon = new NotifyIcon();
        private readonly ContextMenuStrip _menu = new ContextMenuStrip();
        private readonly Form _marshal;
        private readonly AutoResetEvent _wake = new AutoResetEvent(false);
        private readonly Dictionary<string, IconFactory.IconEntry> _iconCache = new Dictionary<string, IconFactory.IconEntry>();

        private Thread _worker;
        private volatile bool _stop;
        private int _intervalSec;
        private string _source;
        private int _pipeTimeoutMs;
        private Status _last;
        private int _lastWarnBand;
        private ToolStripMenuItem _autoItem;
        private ToolStripMenuItem[] _intervalItems;

        private TrayApp(int intervalSec, string source, int pipeTimeoutMs)
        {
            _intervalSec = Math.Max(5, intervalSec);
            _source = source;
            _pipeTimeoutMs = pipeTimeoutMs;

            _marshal = new Form();
            IntPtr marshalHandle = _marshal.Handle; // 强制创建消息窗口，供后台线程 BeginInvoke
            GC.KeepAlive(marshalHandle);

            BuildMenu();
            Autostart.HealPath();

            _icon.ContextMenuStrip = _menu;
            _icon.Text = "飞智手柄电量";
            _icon.Visible = true;
            _icon.MouseClick += OnIconMouseClick;
            _icon.DoubleClick += delegate { ShowDetails(); };

            _worker = new Thread(WorkerLoop);
            _worker.IsBackground = true;
            _worker.Name = "flydigi-sampler";
            _worker.Start();
        }

        // ------------------------------------------------------------- 生命周期 ----

        public static int Run(int intervalSec, string source, int pipeTimeoutMs)
        {
            bool createdNew;
            using (Mutex mutex = new Mutex(true, MutexName, out createdNew))
            {
                if (!createdNew)
                {
                    // 已经有一个托盘在跑（可能是旧的 PowerShell 版），直接退
                    return 0;
                }

                Settings settings = Settings.Load();
                if (intervalSec <= 0) { intervalSec = settings.IntervalSec; }
                if (string.IsNullOrEmpty(source)) { source = settings.Source; }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                TrayApp app = new TrayApp(intervalSec, source, pipeTimeoutMs);
                Application.Run(app);
                return 0;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _stop = true;
                _wake.Set();
                if (_worker != null && _worker.IsAlive) { _worker.Join(1500); }

                _icon.Visible = false;
                _icon.Dispose();
                _menu.Dispose();
                foreach (IconFactory.IconEntry e in _iconCache.Values) { e.Dispose(); }
                _iconCache.Clear();
                _marshal.Dispose();
                _wake.Dispose();
            }
            base.Dispose(disposing);
        }

        private void Quit()
        {
            _stop = true;
            _wake.Set();
            ExitThread();
        }

        // ------------------------------------------------------------------ 菜单 ----

        private void BuildMenu()
        {
            ToolStripMenuItem refresh = new ToolStripMenuItem("立即刷新");
            refresh.Click += delegate { _wake.Set(); };
            _menu.Items.Add(refresh);

            ToolStripMenuItem detail = new ToolStripMenuItem("查看详情");
            detail.Click += delegate { ShowDetails(); };
            _menu.Items.Add(detail);

            ToolStripMenuItem intervalRoot = new ToolStripMenuItem("采样间隔");
            int[] choices = new int[] { 10, 20, 60 };
            _intervalItems = new ToolStripMenuItem[choices.Length];
            for (int i = 0; i < choices.Length; i++)
            {
                int seconds = choices[i];
                ToolStripMenuItem item = new ToolStripMenuItem(seconds + " 秒");
                item.CheckOnClick = false;
                item.Click += delegate
                {
                    _intervalSec = seconds;
                    UpdateIntervalChecks();
                    Settings.Save(_intervalSec, _source);
                    _wake.Set();
                };
                _intervalItems[i] = item;
                intervalRoot.DropDownItems.Add(item);
            }
            _menu.Items.Add(intervalRoot);

            _menu.Items.Add(new ToolStripSeparator());

            _autoItem = new ToolStripMenuItem("开机自启");
            _autoItem.Click += delegate
            {
                try
                {
                    if (Autostart.IsEnabled()) { Autostart.Disable(); } else { Autostart.Enable(); }
                }
                catch (Exception e)
                {
                    _icon.ShowBalloonTip(5000, "开机自启设置失败", e.Message, ToolTipIcon.Warning);
                }
                RefreshAutostartCheck();
                _wake.Set();
            };
            _menu.Items.Add(_autoItem);

            ToolStripMenuItem quit = new ToolStripMenuItem("退出");
            quit.Click += delegate { Quit(); };
            _menu.Items.Add(quit);

            UpdateIntervalChecks();
            RefreshAutostartCheck();
        }

        private void UpdateIntervalChecks()
        {
            int[] choices = new int[] { 10, 20, 60 };
            for (int i = 0; i < _intervalItems.Length; i++)
            {
                _intervalItems[i].Checked = choices[i] == _intervalSec;
            }
        }

        private void RefreshAutostartCheck()
        {
            if (_autoItem != null) { _autoItem.Checked = Autostart.IsEnabled(); }
        }

        // ------------------------------------------------------------------ 采样 ----

        private void WorkerLoop()
        {
            while (!_stop)
            {
                Status st = Sample();
                if (_stop) { break; }

                Status captured = st;
                try
                {
                    _marshal.BeginInvoke((MethodInvoker)delegate { ApplyStatus(captured); });
                }
                catch (ObjectDisposedException) { break; }
                catch (InvalidOperationException) { break; }

                int waitMs = Math.Max(5, _intervalSec) * 1000;
                if (_wake.WaitOne(waitMs) && _stop) { break; }
            }
        }

        private Status Sample()
        {
            try
            {
                return Reader.ReadStatus(_source, _pipeTimeoutMs, false);
            }
            catch (Exception e)
            {
                return Status.Fail(e.Message);
            }
        }

        private void ApplyStatus(Status st)
        {
            try
            {
                _last = st;

                int size = SystemInformation.SmallIconSize.Width;
                if (size < 16) { size = 16; }

                string key = IconKey(st, size);
                IconFactory.IconEntry entry;
                if (!_iconCache.TryGetValue(key, out entry))
                {
                    using (Bitmap bmp = IconFactory.Render(st, size))
                    {
                        entry = IconFactory.CreateIcon(bmp);
                    }
                    _iconCache[key] = entry;
                }
                _icon.Icon = entry.Icon;
                _icon.Text = Tooltip(st);
                RefreshAutostartCheck();
                WarnIfLowBattery(st);
            }
            catch (Exception e)
            {
                Debug.WriteLine("ApplyStatus failed: " + e);
            }
        }

        private static string IconKey(Status st, int size)
        {
            if (st == null) { return size + ":none"; }
            if (!st.Ok || st.Devices.Count == 0)
            {
                return size + (st.Connected ? ":connected" : ":none");
            }
            Device d = st.Devices[0];
            if (!d.HasLevel) { return size + (st.Connected ? ":connected" : ":none"); }
            return size + ":" + d.Level + ":" + d.LevelMax + ":" + (d.Plugged ? "1" : "0");
        }

        internal static string Tooltip(Status st)
        {
            if (st == null) { return "未连接"; }
            if (!st.Ok || st.Devices.Count == 0)
            {
                return st.Connected ? "已连接 · 电量不可用" : "未连接";
            }
            Device d = st.Devices[0];
            string where = d.Connect != null && d.Connect.IndexOf("USB 有线", StringComparison.Ordinal) >= 0 ? "有线" : "无线";
            string percent = d.Percent >= 0 ? d.Percent + "%" : "?";
            string text = string.Format("{0} ({1}/{2}) · {3}", percent, d.Level, d.LevelMax, where);
            if (text.Length > 60) { text = text.Substring(0, 60); }
            return text;
        }

        private void WarnIfLowBattery(Status st)
        {
            if (st == null || !st.Ok || st.Devices.Count == 0) { return; }
            Device d = st.Devices[0];
            int band = 0;
            if (d.LevelMax > 0 && d.HasLevel)
            {
                double frac = (double)d.Level / d.LevelMax;
                if (frac < 0.15) { band = 2; }
                else if (frac < 0.35) { band = 1; }
            }
            if (band > _lastWarnBand)
            {
                _icon.ShowBalloonTip(6000, "飞智手柄电量偏低", Text.Detail(st), ToolTipIcon.Warning);
            }
            _lastWarnBand = band;
        }

        // --------------------------------------------------------------- 交互 ----

        private void OnIconMouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) { ShowDetails(); }
        }

        private void ShowDetails()
        {
            Status st = _last;
            if (st == null)
            {
                st = Sample();
                _last = st;
                ApplyStatus(st);
            }
            _icon.ShowBalloonTip(8000, "飞智手柄状态", Text.Detail(st), ToolTipIcon.Info);
        }
    }

    /// <summary>托盘设置持久化（采样间隔 / 数据源），存在 HKCU，不再每次启动都回到默认。</summary>
    internal sealed class Settings
    {
        private const string KeyPath = "Software\\FlydigiBattery\\Tray";

        public int IntervalSec = 20;
        public string Source = "auto";

        public static Settings Load()
        {
            Settings s = new Settings();
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(KeyPath))
                {
                    if (key == null) { return s; }
                    object interval = key.GetValue("IntervalSec");
                    if (interval is int)
                    {
                        int v = (int)interval;
                        if (v >= 5 && v <= 3600) { s.IntervalSec = v; }
                    }
                    string source = key.GetValue("Source") as string;
                    if (source == "auto" || source == "pipe") { s.Source = source; }
                    // 旧版本存过 xinput：电量通道已移除，回落到默认 auto（管道）
                }
            }
            catch { }
            return s;
        }

        public static void Save(int intervalSec, string source)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(KeyPath))
                {
                    if (key == null) { return; }
                    key.SetValue("IntervalSec", intervalSec, RegistryValueKind.DWord);
                    key.SetValue("Source", source, RegistryValueKind.String);
                }
            }
            catch { }
        }
    }

    /// <summary>开机自启：HKCU\...\Run（不再撒 VBS + 快捷方式，登录时不会闪黑框）。</summary>
    internal static class Autostart
    {
        private const string RunKeyPath = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";
        private const string RunValueName = "FlydigiBatteryTray";
        private const string LegacyShortcutName = "flydigi-battery-tray.lnk";

        private static string ExecutablePath
        {
            get
            {
                // 用命令行 exe 跑 --install 时，启动项要指向同目录的托盘 exe
                string path = Application.ExecutablePath;
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                {
                    string tray = Path.Combine(dir, "flydigi_battery_tray.exe");
                    if (!string.Equals(Path.GetFileName(path), "flydigi_battery_tray.exe", StringComparison.OrdinalIgnoreCase)
                        && File.Exists(tray))
                    {
                        return tray;
                    }
                }
                return path;
            }
        }

        private static string LegacyShortcutPath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), LegacyShortcutName); }
        }

        public static bool IsEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath))
                {
                    if (key == null) { return false; }
                    return key.GetValue(RunValueName) != null;
                }
            }
            catch { return false; }
        }

        public static void Enable()
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath))
            {
                if (key == null) { throw new InvalidOperationException("无法写入启动项注册表"); }
                key.SetValue(RunValueName, "\"" + ExecutablePath + "\"", RegistryValueKind.String);
            }
            RemoveLegacyShortcut();
        }

        public static string DescribeTarget()
        {
            return ExecutablePath;
        }

        public static void Disable()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                {
                    if (key != null && key.GetValue(RunValueName) != null) { key.DeleteValue(RunValueName, false); }
                }
            }
            catch { }
            RemoveLegacyShortcut();
        }

        /// <summary>如果 exe 被移动过，启动项里的旧路径要顺手改掉。</summary>
        public static void HealPath()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath))
                {
                    if (key == null) { return; }
                    string current = key.GetValue(RunValueName) as string;
                    if (current == null) { return; }
                    string expected = "\"" + ExecutablePath + "\"";
                    if (!string.Equals(current, expected, StringComparison.OrdinalIgnoreCase)) { Enable(); }
                }
            }
            catch { }
        }

        private static void RemoveLegacyShortcut()
        {
            try
            {
                string path = LegacyShortcutPath;
                if (File.Exists(path)) { File.Delete(path); }
            }
            catch { }
        }
    }
}
