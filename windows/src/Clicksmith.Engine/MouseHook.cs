using System;
using System.Runtime.InteropServices;
using Clicksmith.Core;

namespace Clicksmith.Engine;

/// <summary>Global low-level mouse hook. The callback runs on the engine's UI thread and must
/// return quickly: no I/O, no logging, no actions — those are queued.</summary>
internal sealed class MouseHook : IDisposable
{
    private readonly Native.LowLevelMouseProc _proc; // kept alive for the lifetime of the hook
    private IntPtr _handle;

    /// <summary>(button, isDown) → true to swallow the event.</summary>
    public Func<ClickButton, bool, bool>? Handler { get; set; }
    public bool IsInstalled => _handle != IntPtr.Zero;

    public MouseHook() => _proc = Callback;

    /// <summary>Installs a fresh hook, then removes the old one (used after sleep/unlock).</summary>
    public bool Reinstall()
    {
        var fresh = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, _proc, Native.GetModuleHandle(null), 0);
        if (fresh == IntPtr.Zero) return false;
        var old = _handle;
        _handle = fresh;
        if (old != IntPtr.Zero) Native.UnhookWindowsHookEx(old);
        return true;
    }

    public void Uninstall()
    {
        if (_handle != IntPtr.Zero) Native.UnhookWindowsHookEx(_handle);
        _handle = IntPtr.Zero;
    }

    private IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && Handler is { } handler)
        {
            var info = Marshal.PtrToStructure<Native.MSLLHOOKSTRUCT>(lParam);
            var injected = (info.flags & Native.LLMHF_INJECTED) != 0;
            if (!injected && TryMap(wParam.ToInt32(), info.mouseData, out var button, out var down))
            {
                try
                {
                    if (handler(button, down)) return (IntPtr)1;
                }
                catch
                {
                    // Never let an exception swallow or break the user's mouse.
                }
            }
        }
        return Native.CallNextHookEx(_handle, nCode, wParam, lParam);
    }

    private static bool TryMap(int msg, uint mouseData, out ClickButton button, out bool down)
    {
        button = ClickButton.Left;
        down = false;
        switch (msg)
        {
            case Native.WM_LBUTTONDOWN: button = ClickButton.Left; down = true; return true;
            case Native.WM_LBUTTONUP: button = ClickButton.Left; return true;
            case Native.WM_RBUTTONDOWN: button = ClickButton.Right; down = true; return true;
            case Native.WM_RBUTTONUP: button = ClickButton.Right; return true;
            case Native.WM_MBUTTONDOWN: button = ClickButton.Middle; down = true; return true;
            case Native.WM_MBUTTONUP: button = ClickButton.Middle; return true;
            case Native.WM_XBUTTONDOWN:
            case Native.WM_XBUTTONUP:
                var x = (mouseData >> 16) & 0xFFFF;
                if (x == 1) button = ClickButton.Back;
                else if (x == 2) button = ClickButton.Forward;
                else return false;
                down = msg == Native.WM_XBUTTONDOWN;
                return true;
            default:
                return false;
        }
    }

    public void Dispose() => Uninstall();
}
