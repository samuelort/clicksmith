using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Clicksmith.Core;

namespace Clicksmith.App;

internal enum BtnKind { Secondary, Primary, Destructive }
internal enum BannerKind { Info, Warning, Error }

/// <summary>Small UI kit so every page uses the same cards, spacing, controls and type.
/// Interactive elements are real WPF controls (keyboard focus, Enter/Space, Narrator names) styled
/// by Themes/Nordic.xaml. Each gets a stable Uid so focus survives a page rebuild.</summary>
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
        AutomationProperties.SetName(img, "Clicksmith");
        return img;
    }

    /// <summary>Small pulsing copper dot — "waiting for you".</summary>
    public static FrameworkElement Spinner()
    {
        var dot = new Ellipse { Width = 10, Height = 10, Fill = Theme.Copper, VerticalAlignment = VerticalAlignment.Center };
        dot.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, 0.25, TimeSpan.FromMilliseconds(700))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase()
        });
        return dot;
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
        var placed = 0;
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
            if (c is FrameworkElement fe && placed > 0) fe.Margin = new Thickness(fe.Margin.Left + spacing, fe.Margin.Top, fe.Margin.Right, fe.Margin.Bottom);
            Grid.SetColumn(c, col++);
            g.Children.Add(c);
            placed++;
        }
        if (!pushed) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
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

    // ---------- Surfaces (no drop shadows: hairline borders scroll smoother) ----------

    public static Border Surface(UIElement content, double padding = 18) => new()
    {
        Background = Theme.Card,
        BorderBrush = Theme.Soft(Theme.Stone, Theme.Dark ? 0.22 : 0.16),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(14),
        Padding = new Thickness(padding),
        Child = content,
        SnapsToDevicePixels = true
    };

    /// <summary>Card with the uniform header: copper glyph + medium secondary title. The optional
    /// footer sticks to the bottom, so cards in one row line up.</summary>
    public static Border CardWithFooter(string title, string glyph, UIElement? footer, params UIElement?[] content)
    {
        var header = Row(7, Glyph(glyph, 13), T(title, 13, FontWeights.Medium, Theme.Sub));
        var body = Col(M, new UIElement?[] { header }.Concat(content).ToArray());
        UIElement inner = body;
        if (footer is not null)
        {
            var dock = new DockPanel { LastChildFill = true };
            if (footer is FrameworkElement f) f.Margin = new Thickness(0, M, 0, 0);
            DockPanel.SetDock(footer, Dock.Bottom);
            dock.Children.Add(footer);
            dock.Children.Add(body);
            inner = dock;
        }
        var b = Surface(inner);
        b.VerticalAlignment = VerticalAlignment.Stretch;
        AutomationProperties.SetName(b, title);
        return b;
    }

    public static Border Card(string title, string glyph, params UIElement?[] content) => CardWithFooter(title, glyph, null, content);

    public static Border Banner(BannerKind kind, string text, string? actionText = null, Action? action = null)
    {
        var tint = kind switch { BannerKind.Warning => Theme.Amber, BannerKind.Error => Theme.Clay, _ => Theme.Copper };
        var glyph = kind switch { BannerKind.Warning => "\uE7BA", BannerKind.Error => "\uEA39", _ => "\uE946" };
        var row = Fill(10, 1, Glyph(glyph, 14, tint), T(text, 13, wrap: true),
            actionText is not null && action is not null ? Btn(actionText, action, key: "banner:" + actionText) : new Border());
        var b = new Border
        {
            Background = Theme.Soft(tint, 0.09),
            BorderBrush = Theme.Soft(tint, 0.24),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14, 10, 14, 10),
            Child = row
        };
        AutomationProperties.SetLiveSetting(b, AutomationLiveSetting.Polite);
        AutomationProperties.SetName(b, text);
        return b;
    }

    // ---------- Controls ----------

    private static T Keyed<T>(T e, string kind, string? key, string name) where T : FrameworkElement
    {
        e.Uid = kind + ":" + (key ?? name);
        AutomationProperties.SetName(e, name);
        return e;
    }

    public static Button Btn(string text, Action onClick, BtnKind kind = BtnKind.Secondary, bool enabled = true, string? glyph = null, string? key = null)
    {
        var fgKey = kind switch { BtnKind.Primary => Theme.OnCopper, BtnKind.Destructive => Theme.Clay, _ => Theme.Text };
        object content = glyph is null
            ? text
            : Row(6, Glyph(glyph, 12, fgKey), T(text, 13, kind == BtnKind.Primary ? FontWeights.SemiBold : FontWeights.Normal, fgKey));
        var b = new Button
        {
            Content = content,
            IsEnabled = enabled,
            Style = (Style)Application.Current.FindResource(kind switch
            {
                BtnKind.Primary => "Cs.Button.Primary",
                BtnKind.Destructive => "Cs.Button.Destructive",
                _ => "Cs.Button"
            })
        };
        b.Click += (_, _) => onClick();
        return Keyed(b, "btn", key, text);
    }

    public static Button Link(string text, Action onClick, double size = 13, string? key = null)
    {
        var b = new Button { Content = text, FontSize = size, Style = (Style)Application.Current.FindResource("Cs.Link") };
        b.Click += (_, _) => onClick();
        return Keyed(b, "link", key, text);
    }

    /// <summary>Copper switch. With a label, the label wraps and the switch sits on the right.</summary>
    public static CheckBox Toggle(bool value, Action<bool> onChange, string? label = null, bool enabled = true, string? key = null)
    {
        var cb = new CheckBox
        {
            IsChecked = value,
            IsEnabled = enabled,
            Content = label is null ? null : T(label, 13, wrap: true),
            Style = (Style)Application.Current.FindResource("Cs.Switch"),
            HorizontalAlignment = label is null ? HorizontalAlignment.Left : HorizontalAlignment.Stretch
        };
        cb.Click += (_, _) => onChange(cb.IsChecked == true);
        return Keyed(cb, "sw", key, label ?? "Switch");
    }

    /// <summary>Full-width clickable row with hover, focus ring and Enter/Space.</summary>
    public static Button RowButton(UIElement content, Action onClick, string name, string? key = null, Thickness? padding = null)
    {
        var b = new Button { Content = content, Style = (Style)Application.Current.FindResource("Cs.Row") };
        if (padding is { } p) b.Padding = p;
        b.Click += (_, _) => onClick();
        return Keyed(b, "row", key, name);
    }

    /// <summary>Button with no chrome of its own (diagram shapes).</summary>
    public static Button BareButton(UIElement content, Action onClick, string name, string? key = null)
    {
        var b = new Button { Content = content, Style = (Style)Application.Current.FindResource("Cs.Bare") };
        b.Click += (_, _) => onClick();
        return Keyed(b, "bare", key, name);
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
        var b = new Border
        {
            Background = Theme.Soft(Theme.Stone, 0.12),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(9, 3, 10, 4),
            Child = Row(6, dot, T(text, 12, FontWeights.SemiBold)),
            VerticalAlignment = VerticalAlignment.Center
        };
        AutomationProperties.SetName(b, "Status: " + text);
        AutomationProperties.SetLiveSetting(b, AutomationLiveSetting.Polite);
        return b;
    }

    public static SolidColorBrush HealthBrush(HealthState h) => h switch
    {
        HealthState.Ready => Theme.Sage,
        HealthState.Stopped => Theme.Clay,
        _ => Theme.Amber
    };

    /// <summary>Segmented control (radio buttons; arrow keys move between segments).</summary>
    public static Border Segmented(string[] options, int selected, Action<int> onSelect, string group)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var style = (Style)Application.Current.FindResource("Cs.Segment");
        for (var i = 0; i < options.Length; i++)
        {
            var index = i;
            var rb = new RadioButton { Content = options[i], GroupName = group, IsChecked = i == selected, Style = style };
            rb.Checked += (_, _) => { if (index != selected) onSelect(index); };
            row.Children.Add(Keyed(rb, "seg", group + ":" + options[i], options[i]));
        }
        return new Border { Background = Theme.Soft(Theme.Stone, 0.10), CornerRadius = new CornerRadius(8), Child = row, HorizontalAlignment = HorizontalAlignment.Right };
    }

    public static TextBox Input(string value, Action<string> changed, double width = double.NaN, bool multiline = false, string? name = null)
    {
        var tb = new TextBox
        {
            Text = value,
            Width = width,
            AcceptsReturn = multiline,
            TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
            MinHeight = multiline ? 70 : 30,
            VerticalContentAlignment = multiline ? VerticalAlignment.Top : VerticalAlignment.Center
        };
        tb.TextChanged += (_, _) => changed(tb.Text);
        if (name is not null) Keyed(tb, "in", null, name);
        return tb;
    }

    public static ComboBox Combo<T>(IEnumerable<(string Title, T Value)> items, T selected, Action<T> onChange, double width = 200, string? name = null)
    {
        var cb = new ComboBox { Width = width };
        var list = items.ToList();
        foreach (var (title, _) in list) cb.Items.Add(title);
        cb.SelectedIndex = Math.Max(0, list.FindIndex(i => EqualityComparer<T>.Default.Equals(i.Value, selected)));
        cb.SelectionChanged += (_, _) => { if (cb.SelectedIndex >= 0) onChange(list[cb.SelectedIndex].Value); };
        if (name is not null) Keyed(cb, "combo", null, name);
        return cb;
    }

    /// <summary>"⋯" button that opens a styled menu. "-" adds a separator.</summary>
    public static Button MoreMenu(string name, params (string Title, Action Action, bool Destructive)[] items)
    {
        var b = new Button
        {
            Width = 28, Height = 28,
            Content = Glyph("\uE712", 13, Theme.Sub),
            Style = (Style)Application.Current.FindResource("Cs.Row"),
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Right,
            ToolTip = "More"
        };
        b.Click += (_, _) =>
        {
            var menu = new ContextMenu { PlacementTarget = b, Placement = PlacementMode.Bottom };
            foreach (var (title, action, destructive) in items)
            {
                if (title == "-") { menu.Items.Add(new Separator()); continue; }
                var mi = new MenuItem { Header = title };
                if (destructive) mi.Foreground = Theme.Clay;
                mi.Click += (_, _) => action();
                menu.Items.Add(mi);
            }
            menu.IsOpen = true;
        };
        return Keyed(b, "more", name, "More for " + name);
    }

    /// <summary>Real app icon when the program can be found, otherwise a soft copper letter tile.</summary>
    public static FrameworkElement AppTile(string appName, double size = 22, string? exe = null, string? path = null)
    {
        if (exe is not null && AppIcons.For(exe, path) is { } icon)
        {
            var img = new Image { Source = icon, Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            AutomationProperties.SetName(img, appName);
            return img;
        }
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
    public static StackPanel ActionsSummary(ButtonMapping m, bool includeApps = true)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Opacity = m.Enabled ? 1 : 0.4 };
        void Add(UIElement e) { if (e is FrameworkElement fe && p.Children.Count > 0) fe.Margin = new Thickness(6, 0, 0, 0); p.Children.Add(e); }
        if (m.DefaultAction is { } d) Add(Badge(d.Display));
        if (includeApps)
        {
            foreach (var o in m.Overrides.Where(o => o.Enabled).Take(2))
                Add(new Border
                {
                    Background = Theme.Soft(Theme.Stone, 0.11), CornerRadius = new CornerRadius(6), Padding = new Thickness(4, 2, 8, 3),
                    Child = Row(5, AppTile(o.AppName, 15, o.App, o.Path), T(o.Action.Display, 12, FontWeights.SemiBold)),
                    ToolTip = $"In {o.AppName}: {o.Action.Display}"
                });
            var more = m.Overrides.Count(o => o.Enabled) - 2;
            if (more > 0) Add(T($"+{more}", 12, FontWeights.SemiBold, Theme.Sub));
        }
        if (m.HasLongPress) Add(new Border
        {
            Background = Theme.Soft(Theme.Copper, 0.12), CornerRadius = new CornerRadius(6), Padding = new Thickness(6, 2, 8, 3),
            Child = Row(4, Glyph("\uE916", 11), T("Hold: " + m.LongPressAction!.Display, 12, FontWeights.SemiBold, Theme.Copper)),
            ToolTip = $"Hold {m.LongPressMs} ms: {m.LongPressAction.Display}"
        });
        if (p.Children.Count == 0) Add(T("No action", 12, brush: Theme.Sub));
        return p;
    }

    public static string TriggerName(ButtonMapping m) => m.Button == ClickButton.Key ? $"Extra button · {KeyNames.Name(m.TriggerVk)}" : ClickButtons.Name(m.Button);

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

    /// <summary>Finds an element by Uid in a visual tree (used to restore keyboard focus).</summary>
    public static FrameworkElement? FindByUid(DependencyObject root, string uid)
    {
        if (root is FrameworkElement fe && fe.Uid == uid) return fe;
        var n = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < n; i++)
            if (FindByUid(VisualTreeHelper.GetChild(root, i), uid) is { } hit) return hit;
        return null;
    }
}
