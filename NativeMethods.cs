using System.Runtime.InteropServices;

namespace ScrollFix;

internal static class NativeMethods
{
    private const int EnumCurrentSettings = -1;

    internal delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    internal static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    internal static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    internal static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    internal static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("kernel32.dll")]
    internal static extern IntPtr GetCurrentProcess();

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("psapi.dll", SetLastError = true)]
    internal static extern bool EmptyWorkingSet(IntPtr hProcess);

    [DllImport("dwmapi.dll", SetLastError = true)]
    internal static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern bool EnumDisplaySettings(string? lpszDeviceName, int iModeNum, ref DevMode lpDevMode);

    internal static int GetWheelDelta(uint mouseData)
    {
        return unchecked((short)((mouseData >> 16) & 0xFFFF));
    }

    internal static int GetDisplayRefreshRate(string? deviceName)
    {
        DevMode devMode = new()
        {
            dmSize = (short)Marshal.SizeOf<DevMode>()
        };

        if (!EnumDisplaySettings(deviceName, EnumCurrentSettings, ref devMode))
        {
            return 60;
        }

        int refreshRate = devMode.dmDisplayFrequency;
        return refreshRate is >= 30 and <= 500 ? refreshRate : 60;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Point
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MsllHookStruct
    {
        public Point pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public nuint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct DevMode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmDeviceName;

        public short dmSpecVersion;
        public short dmDriverVersion;
        public short dmSize;
        public short dmDriverExtra;
        public int dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public int dmDisplayOrientation;
        public int dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmFormName;

        public short dmLogPixels;
        public int dmBitsPerPel;
        public int dmPelsWidth;
        public int dmPelsHeight;
        public int dmDisplayFlags;
        public int dmDisplayFrequency;
        public int dmICMMethod;
        public int dmICMIntent;
        public int dmMediaType;
        public int dmDitherType;
        public int dmReserved1;
        public int dmReserved2;
        public int dmPanningWidth;
        public int dmPanningHeight;
    }
}
