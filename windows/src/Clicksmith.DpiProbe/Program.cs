using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

// Clicksmith DPI probe — READ-ONLY.
// Lists every HID collection of SteelSeries mice (USB 0x1038, Bluetooth 0x0111) and listens to the
// ones Windows lets apps read, while you press the DPI button. It never writes to the device:
// no WriteFile, no SetFeature/GetFeature, no SetOutputReport. Only standard descriptor queries and
// passive input-report reads.

var all = args.Contains("--all");
var seconds = 15;
for (var i = 0; i < args.Length - 1; i++) if (args[i] == "--seconds" && int.TryParse(args[i + 1], out var s)) seconds = Math.Clamp(s, 5, 120);

Console.OutputEncoding = Encoding.UTF8;
var report = new StringBuilder();
void Out(string line = "") { Console.WriteLine(line); report.AppendLine(line); }
void Head(string line) { Console.ForegroundColor = ConsoleColor.DarkYellow; Out(line); Console.ResetColor(); }

Head("Clicksmith DPI probe (read-only)");
Out($"Windows {Environment.OSVersion.Version} · {RuntimeInformation.OSArchitecture} · {(all ? "all HID devices" : "SteelSeries devices")}");
Out("Nothing is written to any device.");
Out();

var collections = Hid.Enumerate().Where(c => all || c.Vid is 0x1038 or 0x0111).ToList();
if (collections.Count == 0)
{
    Out("No SteelSeries HID devices found. Connect the mouse (Bluetooth or 2.4 GHz receiver) and run again,");
    Out("or run with --all to list every HID device.");
    Finish();
    return 0;
}

foreach (var group in collections.GroupBy(c => (c.Vid, c.Pid, c.Product)))
{
    Head($"{group.Key.Product ?? "Unknown device"}  [{group.Key.Vid:X4}:{group.Key.Pid:X4}]  {group.First().Connection}");
    foreach (var c in group)
    {
        var access = c.Readable ? "readable" : c.IsMouseOrKeyboard ? "reserved by Windows (expected)" : "not readable";
        Out($"  #{c.Index,-2} page {c.UsagePage:X4} usage {c.Usage:X4}  {Hid.Describe(c.UsagePage, c.Usage),-22} in {c.InputLength,3} / out {c.OutputLength,3} / feature {c.FeatureLength,3} bytes  — {access}");
    }
    Out();
}

var listen = collections.Where(c => c.Readable && c.InputLength > 0).ToList();
if (listen.Count == 0)
{
    Head("Result");
    Out("Windows reserves every collection of this mouse for itself, so no app can read the DPI button here.");
    Out("If you used Bluetooth, try again with the 2.4 GHz receiver — it often exposes an extra vendor collection.");
    Out("Otherwise the remaining routes are an extra button via the mouse's own software (F13–F24) or a driver.");
    Finish();
    return 0;
}

Head($"Listening to {listen.Count} collection(s) for {seconds} s");
Out("Press the DPI button 3–5 times now. Then press Back and Forward once each for comparison.");
Out();

var start = Environment.TickCount64;
var lines = 0;
var counts = new int[listen.Count];
var gate = new object();
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
var tasks = listen.Select((c, i) => Task.Run(async () =>
{
    try
    {
        using var handle = Hid.OpenForRead(c.Path);
        if (handle.IsInvalid) return;
        await using var stream = new FileStream(handle, FileAccess.Read, 1, isAsync: true);
        var buffer = new byte[c.InputLength];
        while (!cts.IsCancellationRequested)
        {
            var n = await stream.ReadAsync(buffer, cts.Token);
            if (n <= 0) break;
            lock (gate)
            {
                counts[i]++;
                if (lines++ < 300)
                    Out($"  +{(Environment.TickCount64 - start) / 1000.0,6:0.000}s  #{c.Index,-2} page {c.UsagePage:X4}  {string.Join(' ', buffer.Take(n).Select(b => b.ToString("X2")))}");
            }
        }
    }
    catch (OperationCanceledException) { }
    catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
    {
        lock (gate) Out($"  #{c.Index}: read error — {e.GetType().Name}: {e.Message}");
    }
})).ToArray();
try { Task.WaitAll(tasks); } catch (AggregateException) { }

Out();
Head("Result");
var heard = listen.Where((c, i) => counts[i] > 0).ToList();
if (heard.Count == 0)
{
    Out("No reports arrived while you pressed buttons. The DPI press is handled inside the mouse or by Windows only.");
    Out("Try the 2.4 GHz receiver if you used Bluetooth, or use an extra button (F13–F24) via SteelSeries GG.");
}
else
{
    foreach (var c in heard) Out($"  #{c.Index} page {c.UsagePage:X4}: {counts[listen.IndexOf(c)]} report(s).");
    Out("Reports arrived on a readable collection. If they appear exactly when you press DPI, Clicksmith can");
    Out("likely read the DPI button directly. Send this report so the engine can learn the pattern.");
}
Finish();
return 0;

void Finish()
{
    Out();
    try
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Clicksmith-DPI-probe.txt");
        File.WriteAllText(path, report.ToString());
        Console.WriteLine($"Saved to {path}");
    }
    catch { }
    if (!Console.IsInputRedirected)
    {
        Console.WriteLine("Press Enter to close.");
        Console.ReadLine();
    }
}

internal sealed record HidCollection(int Index, string Path, ushort Vid, ushort Pid, string? Product, string Connection,
    ushort UsagePage, ushort Usage, int InputLength, int OutputLength, int FeatureLength, bool Readable)
{
    public bool IsMouseOrKeyboard => UsagePage == 0x01 && Usage is 0x02 or 0x06 or 0x01;
}

internal static class Hid
{
    private const int DIGCF_PRESENT = 0x02, DIGCF_DEVICEINTERFACE = 0x10;
    private const uint GENERIC_READ = 0x80000000, FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2, OPEN_EXISTING = 3, FILE_FLAG_OVERLAPPED = 0x40000000;

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid InterfaceClassGuid; public int Flags; public IntPtr Reserved; }

    [StructLayout(LayoutKind.Sequential)]
    private struct HIDD_ATTRIBUTES { public int Size; public ushort VendorID; public ushort ProductID; public ushort VersionNumber; }

    [StructLayout(LayoutKind.Sequential)]
    private struct HIDP_CAPS
    {
        public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes, NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices,
            NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices,
            NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
    }

    [DllImport("hid.dll")] private static extern void HidD_GetHidGuid(out Guid guid);
    [DllImport("hid.dll")] private static extern bool HidD_GetAttributes(SafeFileHandle h, ref HIDD_ATTRIBUTES a);
    [DllImport("hid.dll")] private static extern bool HidD_GetProductString(SafeFileHandle h, byte[] buffer, int length);
    [DllImport("hid.dll")] private static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr data);
    [DllImport("hid.dll")] private static extern bool HidD_FreePreparsedData(IntPtr data);
    [DllImport("hid.dll")] private static extern int HidP_GetCaps(IntPtr data, out HIDP_CAPS caps);
    [DllImport("setupapi.dll", SetLastError = true)] private static extern IntPtr SetupDiGetClassDevs(ref Guid g, IntPtr e, IntPtr p, int flags);
    [DllImport("setupapi.dll", SetLastError = true)] private static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr info, ref Guid g, int index, ref SP_DEVICE_INTERFACE_DATA data);
    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SP_DEVICE_INTERFACE_DATA data, IntPtr detail, int size, out int required, IntPtr info);
    [DllImport("setupapi.dll")] private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr sec, uint disposition, uint flags, IntPtr template);

    public static SafeFileHandle OpenForRead(string path) =>
        CreateFile(path, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);

    public static List<HidCollection> Enumerate()
    {
        var result = new List<HidCollection>();
        HidD_GetHidGuid(out var guid);
        var set = SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
        if (set == new IntPtr(-1)) return result;
        try
        {
            for (var i = 0; ; i++)
            {
                var data = new SP_DEVICE_INTERFACE_DATA { cbSize = Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };
                if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, i, ref data)) break;
                SetupDiGetDeviceInterfaceDetail(set, ref data, IntPtr.Zero, 0, out var size, IntPtr.Zero);
                if (size <= 0) continue;
                var buf = Marshal.AllocHGlobal(size);
                try
                {
                    Marshal.WriteInt32(buf, IntPtr.Size == 8 ? 8 : 6);
                    if (!SetupDiGetDeviceInterfaceDetail(set, ref data, buf, size, out _, IntPtr.Zero)) continue;
                    var path = Marshal.PtrToStringUni(buf + 4) ?? "";
                    if (Inspect(path, result.Count) is { } c) result.Add(c);
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        return result;
    }

    private static HidCollection? Inspect(string path, int index)
    {
        using var h = CreateFile(path, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        if (h.IsInvalid) return null;
        var attrs = new HIDD_ATTRIBUTES { Size = Marshal.SizeOf<HIDD_ATTRIBUTES>() };
        if (!HidD_GetAttributes(h, ref attrs)) return null;
        string? product = null;
        var name = new byte[256];
        if (HidD_GetProductString(h, name, name.Length)) product = Encoding.Unicode.GetString(name).TrimEnd('\0');
        if (!HidD_GetPreparsedData(h, out var pre)) return null;
        try
        {
            if (HidP_GetCaps(pre, out var caps) != 0x00110000) return null; // HIDP_STATUS_SUCCESS
            bool readable;
            using (var r = OpenForRead(path)) readable = !r.IsInvalid;
            var lower = path.ToLowerInvariant();
            var connection = lower.Contains("{00001124-") || lower.Contains("{00001812-") ? "Bluetooth" : lower.Contains("hid#vid_") ? "USB / receiver" : "other";
            return new HidCollection(index, path, attrs.VendorID, attrs.ProductID, string.IsNullOrWhiteSpace(product) ? null : product, connection,
                caps.UsagePage, caps.Usage, caps.InputReportByteLength, caps.OutputReportByteLength, caps.FeatureReportByteLength, readable);
        }
        finally { HidD_FreePreparsedData(pre); }
    }

    public static string Describe(ushort page, ushort usage) => (page, usage) switch
    {
        (0x01, 0x02) => "Mouse",
        (0x01, 0x06) => "Keyboard",
        (0x01, 0x01) => "Pointer",
        (0x01, 0x80) => "System control",
        (0x0C, 0x01) => "Consumer control",
        ( >= 0xFF00, _) => "Vendor (SteelSeries)",
        _ => "Other"
    };
}
