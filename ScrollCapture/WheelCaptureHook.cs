using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace ScrollFix;

internal sealed class WheelCaptureHook : IDisposable
{
    private const int WhMouseLowLevel = 14;
    private const int WmMouseWheel = 0x020A;
    private const int WmMouseHWheel = 0x020E;

    private readonly NativeMethods.HookProc _hookProc;
    private IntPtr _hookHandle;
    private bool _disposed;

    public WheelCaptureHook()
    {
        _hookProc = HookCallback;
    }

    public event EventHandler<WheelCaptureEventArgs>? WheelObserved;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_hookHandle != IntPtr.Zero)
        {
            return;
        }

        IntPtr moduleHandle = NativeMethods.GetModuleHandle(null);
        _hookHandle = NativeMethods.SetWindowsHookEx(WhMouseLowLevel, _hookProc, moduleHandle, 0);

        if (_hookHandle == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法安装全局鼠标滚轮钩子。");
        }
    }

    public void Stop()
    {
        if (_hookHandle == IntPtr.Zero)
        {
            return;
        }

        if (!NativeMethods.UnhookWindowsHookEx(_hookHandle))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法卸载全局鼠标滚轮钩子。");
        }

        _hookHandle = IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            Stop();
        }
        finally
        {
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == (IntPtr)WmMouseWheel || wParam == (IntPtr)WmMouseHWheel))
        {
            try
            {
                NativeMethods.MsllHookStruct hookData = Marshal.PtrToStructure<NativeMethods.MsllHookStruct>(lParam);
                int delta = NativeMethods.GetWheelDelta(hookData.mouseData);
                IntPtr foregroundWindow = NativeMethods.GetForegroundWindow();
                uint foregroundProcessId = 0;
                _ = NativeMethods.GetWindowThreadProcessId(foregroundWindow, out foregroundProcessId);

                StringBuilder title = new(512);
                _ = NativeMethods.GetWindowText(foregroundWindow, title, title.Capacity);

                WheelCaptureSample sample = new(
                    DateTimeOffset.UtcNow,
                    Stopwatch.GetTimestamp(),
                    hookData.time,
                    hookData.mouseData,
                    delta,
                    wParam == (IntPtr)WmMouseWheel ? "vertical" : "horizontal",
                    hookData.pt.x,
                    hookData.pt.y,
                    hookData.flags,
                    hookData.dwExtraInfo,
                    foregroundWindow.ToInt64(),
                    foregroundProcessId,
                    title.ToString());

                WheelObserved?.Invoke(this, new WheelCaptureEventArgs(sample));
            }
            catch
            {
                // Native hook callbacks must never let a managed exception cross the Win32 boundary.
            }
        }

        return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }
}

internal sealed class WheelCaptureEventArgs : EventArgs
{
    public WheelCaptureEventArgs(WheelCaptureSample sample)
    {
        Sample = sample;
    }

    public WheelCaptureSample Sample { get; }
}

internal readonly record struct WheelCaptureSample(
    DateTimeOffset ObservedAtUtc,
    long StopwatchTimestamp,
    uint SystemEventTimeMs,
    uint MouseData,
    int Delta,
    string Axis,
    int X,
    int Y,
    uint Flags,
    nuint ExtraInfo,
    long ForegroundWindowHandle,
    uint ForegroundProcessId,
    string ForegroundWindowTitle);

internal static class NativeMethods
{
    internal delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    internal static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    internal static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    internal static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    internal static int GetWheelDelta(uint mouseData)
    {
        return unchecked((short)((mouseData >> 16) & 0xFFFF));
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
}
