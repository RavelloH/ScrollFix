using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ScrollFix;

internal sealed class MouseWheelHook : IDisposable
{
    private const int WhMouseLowLevel = 14;
    private const int WmMouseWheel = 0x020A;

    private readonly NativeMethods.HookProc _hookProc;
    private readonly WheelRollbackFilter _filter = new();
    private IntPtr _hookHandle;
    private bool _disposed;

    public MouseWheelHook()
    {
        _hookProc = HookCallback;
    }

    public bool SuppressionEnabled { get; set; } = true;

    public event EventHandler<WheelActivityEventArgs>? WheelActivity;
    public event EventHandler<RollbackDetectedEventArgs>? RollbackDetected;

    public void UpdateFilterSettings(RollbackFilterSettings settings)
    {
        _filter.UpdateSettings(settings);
    }

    public void Start()
    {
        ThrowIfDisposed();

        if (_hookHandle != IntPtr.Zero)
        {
            return;
        }

        IntPtr moduleHandle = NativeMethods.GetModuleHandle(null);
        _hookHandle = NativeMethods.SetWindowsHookEx(WhMouseLowLevel, _hookProc, moduleHandle, 0);

        if (_hookHandle == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), Strings.Error_InstallHookFailed);
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
            throw new Win32Exception(Marshal.GetLastWin32Error(), Strings.Error_UninstallHookFailed);
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
        if (nCode >= 0 && wParam == (IntPtr)WmMouseWheel)
        {
            NativeMethods.MsllHookStruct hookData = Marshal.PtrToStructure<NativeMethods.MsllHookStruct>(lParam);
            int delta = NativeMethods.GetWheelDelta(hookData.mouseData);
            RollbackDecision decision = _filter.Evaluate(delta, hookData.time);
            bool suppressed = decision.IsRollback && SuppressionEnabled;
            int correctedDelta = decision.IsRollback ? decision.CorrectedDelta : delta;

            if (decision.IsRollback && Math.Sign(correctedDelta) == Math.Sign(delta))
            {
                correctedDelta = -delta;
            }

            WheelActivityEventArgs activityEvent = new(
                delta,
                correctedDelta,
                suppressed ? 0 : delta,
                decision.IsRollback,
                suppressed,
                decision.ElapsedMs,
                DateTimeOffset.Now);

            WheelActivity?.Invoke(this, activityEvent);

            if (decision.IsRollback)
            {
                RollbackDetected?.Invoke(this, new RollbackDetectedEventArgs(activityEvent));

                if (suppressed)
                {
                    return (IntPtr)1;
                }
            }
        }

        return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(MouseWheelHook));
        }
    }
}

internal sealed class WheelActivityEventArgs : EventArgs
{
    public WheelActivityEventArgs(
        int rawDelta,
        int correctedDelta,
        int effectiveDelta,
        bool isRollback,
        bool wasSuppressed,
        uint elapsedMs,
        DateTimeOffset observedAt)
    {
        RawDelta = rawDelta;
        CorrectedDelta = correctedDelta;
        EffectiveDelta = effectiveDelta;
        IsRollback = isRollback;
        WasSuppressed = wasSuppressed;
        ElapsedMs = elapsedMs;
        ObservedAt = observedAt;
    }

    public int RawDelta { get; }

    public int CorrectedDelta { get; }

    public int EffectiveDelta { get; }

    public bool IsRollback { get; }

    public bool WasSuppressed { get; }

    public uint ElapsedMs { get; }

    public DateTimeOffset ObservedAt { get; }
}

internal sealed class RollbackDetectedEventArgs : EventArgs
{
    public RollbackDetectedEventArgs(WheelActivityEventArgs activity)
    {
        Delta = activity.RawDelta;
        ElapsedMs = activity.ElapsedMs;
        Suppressed = activity.WasSuppressed;
        ObservedAt = activity.ObservedAt;
    }

    public int Delta { get; }

    public uint ElapsedMs { get; }

    public bool Suppressed { get; }

    public DateTimeOffset ObservedAt { get; }
}
