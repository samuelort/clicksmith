using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;

namespace Clicksmith.App;

internal sealed record MouseDevice(string Name, string Connection, ushort Vid, ushort Pid)
{
    /// <summary>SteelSeries (USB 0x1038, Bluetooth 0x0111): their DPI button isn't one of Windows' five buttons.</summary>
    public bool IsSteelSeries => Vid is 0x1038 or 0x0111;
}

/// <summary>Lists the mice Windows sees, via Raw Input device names and HID product strings.
/// Read-only and cached per device path; cheap enough to refresh every few seconds.</summary>
internal static class Devices
{
    private static readonly Dictionary<string, MouseDevice?> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Regex UsbVid = new(@"VID_([0-9A-F]{4})", RegexOptions.IgnoreCase);
    private static readonly Regex UsbPid = new(@"PID_([0-9A-F]{4})", RegexOptions.IgnoreCase);
    private static readonly Regex BtVid = new(@"VID&[0-9A-F]{2}([0-9A-F]{4})", RegexOptions.IgnoreCase);
    private static readonly Regex BtPid = new(@"PID&([0-9A-F]{4})", RegexOptions.IgnoreCase);

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTDEVICELIST { public IntPtr hDevice; public uint dwType; }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputDeviceList([Out] RAWINPUTDEVICELIST[]? list, ref uint count, uint size);
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint GetRawInputDeviceInfo(IntPtr device, uint command, StringBuilder? data, ref uint size);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr sec, uint disposition, uint flags, IntPtr template);
    [DllImport("hid.dll")]
    private static extern bool HidD_GetProductString(SafeFileHandle h, byte[] buffer, int length);

    public static List<MouseDevice> Mice()
    {
        var result = new List<MouseDevice>();
        try
        {
            uint count = 0;
            var size = (uint)Marshal.SizeOf<RAWINPUTDEVICELIST>();
            GetRawInputDeviceList(null, ref count, size);
            if (count == 0) return result;
            var list = new RAWINPUTDEVICELIST[count];
            if (GetRawInputDeviceList(list, ref count, size) == uint.MaxValue) return result;
            foreach (var d in list.Take((int)count))
            {
                if (d.dwType != 0) continue; // RIM_TYPEMOUSE
                var path = DeviceName(d.hDevice);
                if (path is null || path.Contains("RDP_MOU", StringComparison.OrdinalIgnoreCase) || path.StartsWith(@"\\?\Root#", StringComparison.OrdinalIgnoreCase)) continue;
                if (!Cache.TryGetValue(path, out var mouse))
                {
                    mouse = Describe(path);
                    Cache[path] = mouse;
                }
                if (mouse is not null && !result.Contains(mouse)) result.Add(mouse);
            }
        }
        catch { /* never let device listing break the UI */ }
        return result.OrderBy(m => m.Connection == "Built-in" ? 1 : 0).ThenBy(m => m.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static string? DeviceName(IntPtr device)
    {
        uint size = 0;
        GetRawInputDeviceInfo(device, 0x20000007, null, ref size); // RIDI_DEVICENAME
        if (size == 0 || size > 1024) return null;
        var sb = new StringBuilder((int)size);
        return GetRawInputDeviceInfo(device, 0x20000007, sb, ref size) == uint.MaxValue ? null : sb.ToString();
    }

    private static MouseDevice? Describe(string path)
    {
        var upper = path.ToUpperInvariant();
        ushort vid = 0, pid = 0;
        string connection;
        if (upper.Contains("{00001124-") || upper.Contains("{00001812-"))
        {
            connection = "Bluetooth";
            if (BtVid.Match(upper) is { Success: true } bv) vid = Convert.ToUInt16(bv.Groups[1].Value, 16);
            if (BtPid.Match(upper) is { Success: true } bp) pid = Convert.ToUInt16(bp.Groups[1].Value, 16);
        }
        else if (UsbVid.Match(upper) is { Success: true } uv)
        {
            connection = "USB";
            vid = Convert.ToUInt16(uv.Groups[1].Value, 16);
            if (UsbPid.Match(upper) is { Success: true } up) pid = Convert.ToUInt16(up.Groups[1].Value, 16);
        }
        else connection = "Built-in";

        string? name = null;
        try
        {
            using var h = CreateFile(path, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
            if (!h.IsInvalid)
            {
                var buf = new byte[256];
                if (HidD_GetProductString(h, buf, buf.Length)) name = Encoding.Unicode.GetString(buf).TrimEnd('\0').Trim();
            }
        }
        catch { }
        if (string.IsNullOrWhiteSpace(name))
            name = connection == "Built-in" ? "Built-in touchpad or pointer" : $"Mouse {vid:X4}:{pid:X4}";
        if (connection == "USB" && vid is 0x1038) connection = "USB receiver";
        return new MouseDevice(name, connection, vid, pid);
    }
}
