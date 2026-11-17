using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Clicksmith.Core;

namespace Clicksmith.App;

internal enum BtnKind { Secondary, Primary, Destructive }
internal enum BannerKind { Info, Warning, Error }

/// <summary>Small UI kit so every page uses the same cards, spacing, buttons and type.</summary>
internal static class Ui
{
    // 8-pt spacing scale.
    public const double XS = 4, S = 8, M = 12, L = 16, XL = 24, Page = 32;
    public const double MaxContent = 1040;

    // ---------- Type ----------

    public static TextBlock T(string text, double size = 13, FontWeight? weight = null, Brush? brush = null, bool wrap = false)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = size,
            FontWeight = weight ?? FontWeights.Normal,
            Foreground = brush ?? Theme.Text,
            FontFamily = size >= 20 ? Theme.Display : Theme.Font,
            TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
            TextTrimming = wrap ? TextTrimming.None : TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    public static TextBlock Title(string text) => T(text, 28, FontWeights.SemiBold);
    public static TextBlock Caption(string text, bool wrap = true) => T(text, 12, brush: Theme.Sub, wrap: wrap);

    public static TextBlock Glyph(string glyph, double size = 14, Brush? brush = null) => new()
    {
        Text = glyph,
        FontFamily = Theme.Icons,
        FontSize = size,
        Foreground = brush ?? Theme.Copper,
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center
    };

    public static Image? Logo(double size)
    {
        if (Theme.Logo is null) return null;
        var img = new Image { Source = Theme.Logo, Width = size, Height = size };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
        return img;
    }

    // ---------- Layout ----------

    public static StackPanel Col(double spacing, params UIElement?[] children)
    {
        var p = new StackPanel { Orientation = Orientation.Vertical };
        var first = true;
        foreach (var c in children.Where(c => c is not null))
        {
            if (!first && c is FrameworkElement fe) fe.Margin = new Thickness(fe.Margin.Left, fe.Margin.Top + spacing, fe.Margin.Right, fe.Margin.Bottom);
            p.Children.Add(c!);
            first = false;
        }
        return p;
    }

    /// <summary>Horizontal row. Elements after a null "spacer" are pushed to the right.</summary>
    public static Grid Row(double spacing, params UIElement?[] children)
    {
        var g = new Grid();
        var col = 0;
        var pushed = false;
        foreach (var c in children)
        {
            if (c is null)
            {
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                col++;
                pushed = true;
                continue;
            }
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            if (c is FrameworkElement fe && col > 0) fe.Margin = new Thickness(fe.Margin.Left + spacing, fe.Margin.Top, fe.Margin.Right, fe.Margin.Bottom);
            Grid.SetColumn(c, col++);
            g.Children.Add(c);
        }
        if (!pushed)
        {
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }
        return g;
    }

    /// <summary>Row where one element (index) takes the remaining width.</summary>
    public static Grid Fill(double spacing, int fillIndex, params UIElement[] children)
    {
        var g = new Grid();
        for (var i = 0; i < children.Length; i++)
        {
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = i == fillIndex ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
            if (children[i] is FrameworkElement fe && i > 0) fe.Margin = new Thickness(fe.Margin.Left + spacing, fe.Margin.Top, fe.Margin.Right, fe.Margin.Bottom);
            Grid.SetColumn(children[i], i);
            g.Children.Add(children[i]);
        }
        return g;
    }

    public static Border Divider() => new() { Height = 1, Background = Theme.Line, Margin = new Thickness(0, S, 0, S) };

    // ---------- Surfaces ----------

    public static Border Surface(UIElement content, double padding = 18)
    {
        return new Border
        {
            Background = Theme.Card,
            BorderBrush = Theme.Soft(Theme.Stone, 0.14),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(padding),
            Child = content,
            Effect = new DropShadowEffect { BlurRadius = 10, ShadowDepth = 1, Opacity = Theme.Dark ? 0.25 : 0.05, Direction = 270, Color = Colors.Black }
        };
    }

    /// <summary>Card with the uniform header: copper glyph + medium secondary title.</summary>
    public static Border Card(string title, string glyph, params UIElement?[] content)
    {
        var header = Row(7, Glyph(glyph, 13), T(title, 13, FontWeights.Medium, Theme.Sub));
        var body = Col(M, new UIElement?[] { header }.Concat(content).ToArray());
        var b = Surface(body);
        b.VerticalAlignment = VerticalAlignment.Stretch;
        return b;
    }

    public static Border Banner(BannerKind kind, string text, string? actionText = null, Action? action = null)
    {
        var tint = kind switch { BannerKind.Warning => Theme.Amber, BannerKind.Error => Theme.Clay, _ => Theme.Copper };
        var glyph = kind switch { BannerKind.Warning => "\uE7BA", BannerKind.Error => "\uEA39", _ => "\uE946" };
        var row = Fill(10, 1, Glyph(glyph, 14, tint), T(text, 13, wrap: true),
            actionText is not null && action is not null ? Btn(actionText, action) : new Border());
        return new Border
        {
            Background = Theme.Soft(tint, 0.09),
            BorderBrush = Theme.Soft(tint, 0.22),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14, 10, 14, 10),
            Child = row
        };
    }

    // ---------- Controls ----------

    public static FrameworkElement Btn(string text, Action onClick, BtnKind kind = BtnKind.Secondary, bool enabled = true, string? glyph = null)
    {
        var fg = kind == BtnKind.Primary ? Theme.OnCopper : kind == BtnKind.Destructive ? Theme.Clay : Theme.Text;
        var bg = kind == BtnKind.Primary ? Theme.Copper : Theme.Card;
        var label = glyph is null ? (UIElement)T(text, 13, kind == BtnKind.Primary ? FontWeights.SemiBold : FontWeights.Normal, fg)
                                  : Row(6, Glyph(glyph, 12, fg), T(text, 13, kind == BtnKind.Primary ? FontWeights.SemiBold : FontWeights.Normal, fg));
        var b = new Border
        {
            Background = bg,
            BorderBrush = kind == BtnKind.Primary ? Theme.Copper : Theme.Line,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(12, 5, 12, 6),
            Child = label,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = enabled ? 1 : 0.45
        };
        if (enabled) MakeClickable(b, onClick, kind == BtnKind.Primary ? Theme.Soft(Theme.Copper, 0.88) : Theme.Soft(Theme.Stone, 0.10), bg);
        AutomationName(b, text);
        return b;
    }

    public static FrameworkElement Link(string text, Action onClick, double size = 13)
    {
        var t = T(text, size, brush: Theme.Copper);
        t.Cursor = Cursors.Hand;
        t.Focusable = true;
        t.MouseLeftButtonUp += (_, e) => { onClick(); e.Handled = true; };
        t.KeyDown += (_, e) => { if (e.Key is Key.Enter or Key.Space) { onClick(); e.Handled = true; } };
        t.MouseEnter += (_, _) => t.TextDecorations = TextDecorations.Underline;
        t.MouseLeave += (_, _) => t.TextDecorations = null;
        return t;
    }

    /// <summary>Pill-shaped switch in the copper accent.</summary>
    public static FrameworkElement Toggle(bool value, Action<bool> onChange, string? label = null, bool enabled = true)
    {
        var track = new Border
        {
            Width = 36, Height = 20, CornerRadius = new CornerRadius(10),
            Background = value ? Theme.Copper : Theme.Soft(Theme.Stone, 0.30),
            VerticalAlignment = VerticalAlignment.Center
        };
        var knob = new Border
        {
            Width = 16, Height = 16, CornerRadius = new CornerRadius(8), Background = Brushes.White,
            HorizontalAlignment = value ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            Margin = new Thickness(2)
        };
        track.Child = knob;
        UIElement content = label is null ? track : Fill(Ui.M, 0, T(label, 13, wrap: true), track);
        var host = new Border { Background = Brushes.Transparent, Child = content, Opacity = enabled ? 1 : 0.45 };
        if (enabled) MakeClickable(host, () => onChange(!value), null, null);
        AutomationName(host, (label ?? "Switch") + (value ? ", on" : ", off"));
        return host;
    }

    public static Border Badge(string text, Brush? tint = null)
    {
        return new Border
        {
            Background = tint is SolidColorBrush s ? Theme.Soft(s, 0.13) : Theme.Soft(Theme.Stone, 0.11),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 2, 8, 3),
            Child = T(text, 12, FontWeights.SemiBold, tint ?? Theme.Text),
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    public static Border Pill(HealthState health, string text)
    {
        var dot = new Border { Width = 7, Height = 7, CornerRadius = new CornerRadius(3.5), Background = HealthBrush(health), VerticalAlignment = VerticalAlignment.Center };
        return new Border
        {
            Background = Theme.Soft(Theme.Stone, 0.12),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(9, 3, 10, 4),
            Child = Row(6, dot, T(text, 12, FontWeights.SemiBold)),
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    public static SolidColorBrush HealthBrush(HealthState h) => h switch
    {
        HealthState.Ready => Theme.Sage,
        HealthState.Stopped => Theme.Clay,
        _ => Theme.Amber
    };

    /// <summary>Segmented control used for the Settings tabs.</summary>
    public static Border Segmented(string[] options, int selected, Action<int> onSelect)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        for (var i = 0; i < options.Length; i++)
        {
            var index = i;
            var on = i == selected;
            var seg = new Border
            {
                Background = on ? Theme.Card : Brushes.Transparent,
                BorderBrush = on ? Theme.Line : Brushes.Transparent,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 4, 12, 5),
                Margin = new Thickness(2),
                Child = T(options[i], 12.5, on ? FontWeights.SemiBold : FontWeights.Normal)
            };
            if (!on) MakeClickable(seg, () => onSelect(index), Theme.Soft(Theme.Stone, 0.08), Brushes.Transparent);
            AutomationName(seg, options[i]);
            row.Children.Add(seg);
        }
        return new Border { Background = Theme.Soft(Theme.Stone, 0.10), CornerRadius = new CornerRadius(8), Child = row, HorizontalAlignment = HorizontalAlignment.Right };
    }

    public static TextBox Input(string value, Action<string> changed, double width = double.NaN, bool multiline = false)
    {
        var tb = new TextBox
        {
            Text = value,
            FontFamily = Theme.Font,
            FontSize = 13,
            Padding = new Thickness(8, 5, 8, 5),
            Background = Theme.Card,
            Foreground = Theme.Text,
            BorderBrush = Theme.Line,
            CaretBrush = Theme.Text,
            Width = width,
            AcceptsReturn = multiline,
            TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
            MinHeight = multiline ? 64 : 0
        };
        tb.TextChanged += (_, _) => changed(tb.Text);
        return tb;
    }

    public static ComboBox Combo<T>(IEnumerable<(string Title, T Value)> items, T selected, Action<T> onChange, double width = 200)
    {
        var cb = new ComboBox { Width = width, FontFamily = Theme.Font, FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
        var list = items.ToList();
        foreach (var (title, _) in list) cb.Items.Add(title);
        cb.SelectedIndex = Math.Max(0, list.FindIndex(i => EqualityComparer<T>.Default.Equals(i.Value, selected)));
        cb.SelectionChanged += (_, _) => { if (cb.SelectedIndex >= 0) onChange(list[cb.SelectedIndex].Value); };
        return cb;
    }

    /// <summary>"⋯" button that opens a menu.</summary>
    public static FrameworkElement MoreMenu(string name, params (string Title, Action Action, bool Destructive)[] items)
    {
        var host = new Border
        {
            Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Background = Brushes.Transparent,
            Child = Glyph("\uE712", 13, Theme.Sub), VerticalAlignment = VerticalAlignment.Center
        };
        MakeClickable(host, () =>
        {
            var menu = new ContextMenu { PlacementTarget = host, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
            foreach (var (title, action, destructive) in items)
            {
                if (title == "-") { menu.Items.Add(new Separator()); continue; }
                var mi = new MenuItem { Header = title, Foreground = destructive ? Theme.Clay : Brushes.Black };
                mi.Click += (_, _) => action();
                menu.Items.Add(mi);
            }
            menu.IsOpen = true;
        }, Theme.Soft(Theme.Stone, 0.12), Brushes.Transparent);
        AutomationName(host, "More for " + name);
        host.ToolTip = "More";
        return host;
    }

    /// <summary>Hover, click and keyboard (Enter/Space) behaviour for a Border.</summary>
    public static void MakeClickable(Border b, Action onClick, Brush? hover, Brush? normal)
    {
        b.Cursor = Cursors.Hand;
        b.Focusable = true;
        b.FocusVisualStyle = null;
        if (hover is not null)
        {
            b.MouseEnter += (_, _) => b.Background = hover;
            b.MouseLeave += (_, _) => b.Background = normal ?? Brushes.Transparent;
        }
        b.MouseLeftButtonUp += (_, e) => { onClick(); e.Handled = true; };
        b.KeyDown += (_, e) => { if (e.Key is Key.Enter or Key.Space) { onClick(); e.Handled = true; } };
        b.GotKeyboardFocus += (_, _) => b.BorderBrush = Theme.Copper;
        var original = b.BorderBrush;
        b.LostKeyboardFocus += (_, _) => b.BorderBrush = original;
    }

    public static void AutomationName(DependencyObject d, string name) =>
        System.Windows.Automation.AutomationProperties.SetName(d, name);

    /// <summary>App tile (letter on a soft copper square) for apps referenced by exe name.</summary>
    public static Border AppTile(string appName, double size = 22)
    {
        var letter = string.IsNullOrWhiteSpace(appName) ? "?" : appName.Trim()[0].ToString().ToUpperInvariant();
        return new Border
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(size * 0.24),
            Background = Theme.Soft(Theme.Copper, 0.16),
            Child = T(letter, size * 0.5, FontWeights.SemiBold, Theme.Copper),
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    /// <summary>The real result of a mapping: default + per-app actions + hold.</summary>
    public static StackPanel ActionsSummary(ButtonMapping m)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Opacity = m.Enabled ? 1 : 0.4 };
        void Add(UIElement e) { if (e is FrameworkElement fe && p.Children.Count > 0) fe.Margin = new Thickness(6, 0, 0, 0); p.Children.Add(e); }
        if (m.DefaultAction is { } d) Add(Badge(d.Display));
        foreach (var o in m.Overrides.Where(o => o.Enabled).Take(2))
            Add(new Border
            {
                Background = Theme.Soft(Theme.Stone, 0.11), CornerRadius = new CornerRadius(6), Padding = new Thickness(4, 2, 8, 3),
                Child = Row(5, AppTile(o.AppName, 15), T(o.Action.Display, 12, FontWeights.SemiBold)),
                ToolTip = $"In {o.AppName}: {o.Action.Display}"
            });
        var more = m.Overrides.Count(o => o.Enabled) - 2;
        if (more > 0) Add(T($"+{more}", 12, FontWeights.SemiBold, Theme.Sub));
        if (m.HasLongPress) Add(new Border
        {
            Background = Theme.Soft(Theme.Copper, 0.12), CornerRadius = new CornerRadius(6), Padding = new Thickness(6, 2, 8, 3),
            Child = Row(4, Glyph("\uE916", 11), T("Hold: " + m.LongPressAction!.Display, 12, FontWeights.SemiBold, Theme.Copper)),
            ToolTip = $"Hold {m.LongPressMs} ms: {m.LongPressAction.Display}"
        });
        if (p.Children.Count == 0) Add(T("No action", 12, brush: Theme.Sub));
        return p;
    }

    /// <summary>Default and hold actions only (used on the Apps page's Default card).</summary>
    public static StackPanel ActionsSummaryDefault(ButtonMapping m)
    {
        var copy = AppState.Clone(m);
        copy.Overrides.Clear();
        return ActionsSummary(copy);
    }

    public static string Relative(DateTimeOffset? d)
    {
        if (d is not { } v || v == DateTimeOffset.MinValue) return "—";
        var span = DateTimeOffset.UtcNow - v;
        if (span < TimeSpan.FromSeconds(45)) return "just now";
        if (span < TimeSpan.FromHours(1)) return $"{(int)span.TotalMinutes} min ago";
        if (span < TimeSpan.FromDays(1)) return $"{(int)span.TotalHours} h ago";
        return v.ToLocalTime().ToString("d MMM HH:mm");
    }

    public static string Duration(DateTimeOffset? since)
    {
        if (since is not { } s) return "—";
        var span = DateTimeOffset.UtcNow - s;
        if (span < TimeSpan.FromMinutes(1)) return "< 1 min";
        if (span < TimeSpan.FromHours(1)) return $"{(int)span.TotalMinutes} min";
        if (span < TimeSpan.FromDays(1)) return $"{(int)span.TotalHours} h {span.Minutes} min";
        return $"{(int)span.TotalDays} d {span.Hours} h";
    }
}
