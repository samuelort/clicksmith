using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace Clicksmith.App;

/// <summary>Nordic palette taken from the app icon: warm paper, soft stone, one muted copper accent.
/// Every colour has a light and dark value; the window rebuilds when Windows switches theme.</summary>
internal static class Theme
{
    public static bool Dark { get; private set; }
    public static SolidColorBrush Canvas = null!, Card = null!, Sidebar = null!, Stone = null!, Copper = null!,
        Sage = null!, Amber = null!, Clay = null!, Text = null!, Sub = null!, Line = null!, OnCopper = null!;

    public static readonly FontFamily Font = new("Segoe UI Variable Text, Segoe UI");
    public static readonly FontFamily Display = new("Segoe UI Variable Display, Segoe UI");
    public static readonly FontFamily Icons = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    public static event Action? Changed;

    public static void Apply(bool dark)
    {
        Dark = dark;
        Canvas = B(dark ? (30, 29, 27) : (244, 241, 236));
        Card = B(dark ? (41, 39, 37) : (251, 250, 247));
        Sidebar = B(dark ? (36, 34, 32) : (235, 231, 225));
        Stone = B(dark ? (170, 162, 152) : (120, 112, 102));
        Copper = B(dark ? (212, 152, 112) : (160, 96, 58));
        Sage = B(dark ? (142, 176, 137) : (96, 132, 92));
        Amber = B(dark ? (214, 170, 96) : (168, 120, 44));
        Clay = B(dark ? (214, 128, 112) : (166, 82, 66));
        Text = B(dark ? (236, 232, 226) : (36, 34, 31));
        Sub = B(dark ? (160, 153, 144) : (116, 110, 102));
        Line = B(dark ? (58, 55, 52) : (226, 221, 214));
        OnCopper = B(dark ? (30, 29, 27) : (255, 255, 255));
        Changed?.Invoke();
    }

    public static bool SystemDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch { return false; }
    }

    /// <summary>The brush at a given opacity (for soft tints and hairlines).</summary>
    public static SolidColorBrush Soft(SolidColorBrush b, double opacity)
    {
        var c = b.Color;
        var s = new SolidColorBrush(Color.FromArgb((byte)Math.Round(opacity * 255), c.R, c.G, c.B));
        s.Freeze();
        return s;
    }

    private static SolidColorBrush B((byte R, byte G, byte B) c)
    {
        var s = new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B));
        s.Freeze();
        return s;
    }

    private static ImageSource? _logo;
    public static ImageSource? Logo
    {
        get
        {
            if (_logo is not null) return _logo;
            try
            {
                var path = Path.Combine(AppContext.BaseDirectory, "assets", "logo.png");
                if (!File.Exists(path)) return null;
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(path);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                _logo = bmp;
            }
            catch { _logo = null; }
            return _logo;
        }
    }
}
