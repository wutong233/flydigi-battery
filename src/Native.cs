// 原生互操作：控制台、XInput 连接检测（动态加载）、图标句柄
using System;
using System.Runtime.InteropServices;

namespace FlydigiBattery
{
    internal static class ConsoleNative
    {
        public const uint ATTACH_PARENT_PROCESS = 0xFFFFFFFF;
        public const int SW_HIDE = 0;

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool AttachConsole(uint dwProcessId);

        [DllImport("kernel32.dll")]
        public static extern IntPtr GetConsoleWindow();

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    }

    internal static class IconNative
    {
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool DestroyIcon(IntPtr hIcon);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct XINPUT_GAMEPAD
    {
        public ushort wButtons;
        public byte bLeftTrigger;
        public byte bRightTrigger;
        public short sThumbLX;
        public short sThumbLY;
        public short sThumbRX;
        public short sThumbRY;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct XINPUT_STATE
    {
        public uint dwPacketNumber;
        public XINPUT_GAMEPAD Gamepad;
    }

    /// <summary>
    /// 动态加载 xinput1_4 / 1_3 / 9_1_0，只用来确认手柄是否已连接（不读取电量）。
    /// 老系统上没有 1_4 时不再抛 DllNotFoundException。
    /// </summary>
    internal static class XInput
    {
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate uint GetStateFn(uint dwUserIndex, out XINPUT_STATE pState);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryW(string lpFileName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

        private static readonly object Gate = new object();
        private static bool _loaded;
        private static GetStateFn _getState;

        private static void EnsureLoaded()
        {
            lock (Gate)
            {
                if (_loaded) { return; }
                _loaded = true;
                string[] candidates = new string[] { "xinput1_4.dll", "xinput1_3.dll", "xinput9_1_0.dll" };
                foreach (string name in candidates)
                {
                    IntPtr h = LoadLibraryW(name);
                    if (h == IntPtr.Zero) { continue; }
                    IntPtr a = GetProcAddress(h, "XInputGetState");
                    if (a == IntPtr.Zero) { continue; }
                    _getState = (GetStateFn)Marshal.GetDelegateForFunctionPointer(a, typeof(GetStateFn));
                    break;
                }
            }
        }

        public static bool Available
        {
            get { EnsureLoaded(); return _getState != null; }
        }

        public static uint GetState(uint userIndex, out XINPUT_STATE state)
        {
            EnsureLoaded();
            if (_getState == null) { state = new XINPUT_STATE(); return 1167u; } // ERROR_DEVICE_NOT_CONNECTED
            return _getState(userIndex, out state);
        }
    }

    /// <summary>
    /// 零等待检查命名管道实例。ERROR_PIPE_BUSY=231、ERROR_FILE_NOT_FOUND=2 时，
    /// 直接跳过 Connect 超时，避免白等。
    /// </summary>
    internal static class PipeAvailability
    {
        private const int ERROR_FILE_NOT_FOUND = 2;
        private const int ERROR_PIPE_BUSY = 231;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool WaitNamedPipeW(string lpNamedPipeName, uint nTimeOut);

        public static bool HasAvailableInstance(string pipeName)
        {
            try
            {
                if (WaitNamedPipeW(@"\\.\pipe\" + pipeName, 0)) { return true; }
                int error = Marshal.GetLastWin32Error();
                if (error == ERROR_FILE_NOT_FOUND || error == ERROR_PIPE_BUSY) { return false; }
            }
            catch { }
            // 探测本身异常时交给正式 Connect 处理，保持原有错误信息。
            return true;
        }
    }
}
