using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace Clicksmith.App;

/// <summary>Real app icons for per-app rules. Looks for the program via its saved path, a running
/// process or the App Paths registry, then caches the result (including "not found").</summary>
internal static class AppIcons
{
    private static readonly Dictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static ImageSource? For(string exe, string? path = null)
    {
        if (string.IsNullOrWhiteSpace(exe)) return null;
        if (Cache.TryGetValue(exe, out var cached)) return cached;
        ImageSource? icon = null;
        try
        {
            var file = Locate(exe, path);
            if (file is not null) icon = FromFile(file);
        }
        catch { icon = null; }
        Cache[exe] = icon;
        return icon;
    }

    /// <summary>Full path to the program, if it can be found.</summary>
    public static string? Locate(string exe, string? path = null)
    {
        if (path is not null && File.Exists(path)) return path;
        var name = Path.GetFileNameWithoutExtension(exe);
        foreach (var p in Process.GetProcessesByName(name))
        {
            try { var f = p.MainModule?.FileName; if (f is not null && File.Exists(f)) return f; }
            catch { /* elevated or exited */ }
            finally { p.Dispose(); }
        }
        foreach (var root in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            try
            {
                using var key = root.OpenSubKey($@"Software\Microsoft\Windows\CurrentVersion\App Paths\{exe}");
                if (key?.GetValue(null) is string f)
                {
                    f = f.Trim('"');
                    if (File.Exists(f)) return f;
                }
            }
            catch { }
        }
        return null;
    }

    private static ImageSource? FromFile(string file)
    {
        using var icon = System.Drawing.Icon.ExtractAssociatedIcon(file);
        if (icon is null) return null;
        var src = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        src.Freeze();
        return src;
    }
}
