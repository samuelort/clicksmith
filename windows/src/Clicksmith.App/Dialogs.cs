using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Clicksmith.Core;

namespace Clicksmith.App;

/// <summary>Small dialogs in the Clicksmith style (no system-blue message boxes).</summary>
internal static class Dialogs
{
    private static Window Shell(Window owner, string title, double width)
    {
        return new Window
        {
            Owner = owner,
            Title = title,
            Width = width,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Theme.Canvas,
            FontFamily = Theme.Font,
            ShowInTaskbar = false
        };
    }

    private static UIElement Body(string title, string? message, UIElement? extra, UIElement buttons) =>
        new Border
        {
            Padding = new Thickness(Ui.XL),
            Child = Ui.Col(Ui.M, Ui.T(title, 17, FontWeights.SemiBold, wrap: true),
                message is null ? null : Ui.T(message, 13, brush: Theme.Sub, wrap: true), extra, buttons)
        };

    private static Grid Buttons(params UIElement?[] right)
    {
        var list = new List<UIElement?> { null };
        list.AddRange(right);
        var g = Ui.Row(Ui.S, list.ToArray());
        g.Margin = new Thickness(0, Ui.S, 0, 0);
        return g;
    }

    public static bool Confirm(Window owner, string title, string message, string ok, bool destructive)
    {
        var w = Shell(owner, title, 440);
        var result = false;
        w.Content = Body(title, message, null, Buttons(
            Ui.Btn("Cancel", () => w.Close()),
            Ui.Btn(ok, () => { result = true; w.Close(); }, destructive ? BtnKind.Destructive : BtnKind.Primary)));
        w.KeyDown += (_, e) => { if (e.Key == Key.Escape) w.Close(); };
        w.ShowDialog();
        return result;
    }

    public static void Info(Window owner, string title, string message)
    {
        var w = Shell(owner, title, 460);
        w.Content = Body(title, message, null, Buttons(Ui.Btn("OK", () => w.Close(), BtnKind.Primary)));
        w.KeyDown += (_, e) => { if (e.Key is Key.Escape or Key.Enter) w.Close(); };
        w.ShowDialog();
    }

    public static string? Prompt(Window owner, string title, string label, string initial)
    {
        var w = Shell(owner, title, 420);
        string? result = null;
        var text = initial;
        var box = Ui.Input(initial, t => text = t);
        box.MaxLength = 40;
        void Ok() { if (!string.IsNullOrWhiteSpace(text)) { result = text.Trim(); w.Close(); } }
        w.Content = Body(title, null, Ui.Col(6, Ui.T(label, 12, brush: Theme.Sub), box),
            Buttons(Ui.Btn("Cancel", () => w.Close()), Ui.Btn("Save", Ok, BtnKind.Primary)));
        w.KeyDown += (_, e) => { if (e.Key == Key.Escape) w.Close(); if (e.Key == Key.Enter) Ok(); };
        w.Loaded += (_, _) => { box.Focus(); box.SelectAll(); };
        w.ShowDialog();
        return result;
    }

    /// <summary>Pick a running app, or browse for an .exe.</summary>
    public static (string Exe, string Name)? ChooseApp(Window owner, List<(string Exe, string Name)> running)
    {
        var w = Shell(owner, "Add app", 460);
        (string, string)? result = null;
        var list = new StackPanel();
        foreach (var (exe, name) in running)
        {
            var row = new Border
            {
                Padding = new Thickness(10, 8, 10, 8), CornerRadius = new CornerRadius(8), Background = Theme.Soft(Theme.Stone, 0.06), Margin = new Thickness(0, 0, 0, 4),
                Child = Ui.Fill(Ui.M, 1, Ui.AppTile(name, 24), Ui.Col(1, Ui.T(name, 13.5, FontWeights.Medium), Ui.Caption(exe, false)), Ui.Glyph("\uE76C", 11, Theme.Sub))
            };
            var e = exe; var n = name;
            Ui.MakeClickable(row, () => { result = (e, n); w.Close(); }, Theme.Soft(Theme.Copper, 0.10), Theme.Soft(Theme.Stone, 0.06));
            list.Children.Add(row);
        }
        if (running.Count == 0) list.Children.Add(Ui.Caption("No apps with a window are running."));
        var scroll = new ScrollViewer { Content = list, MaxHeight = 360, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        w.Content = Body("Which app?", "Running apps are listed. Clicksmith recognises apps by their program file.", scroll, Buttons(
            Ui.Btn("Cancel", () => w.Close()),
            Ui.Btn("Browse…", () =>
            {
                var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Programs (*.exe)|*.exe" };
                if (dlg.ShowDialog(w) != true) return;
                var exe = System.IO.Path.GetFileName(dlg.FileName).ToLowerInvariant();
                string name;
                try { name = System.Diagnostics.FileVersionInfo.GetVersionInfo(dlg.FileName).FileDescription ?? ""; } catch { name = ""; }
                if (string.IsNullOrWhiteSpace(name)) name = System.IO.Path.GetFileNameWithoutExtension(exe);
                result = (exe, name.Trim());
                w.Close();
            })));
        w.KeyDown += (_, e) => { if (e.Key == Key.Escape) w.Close(); };
        w.ShowDialog();
        return result;
    }

    public static ClickButton? ChooseButton(Window owner, string title, List<ClickButton> buttons, Func<ClickButton, string> describe)
    {
        var w = Shell(owner, title, 420);
        ClickButton? result = null;
        var list = new StackPanel();
        foreach (var b in buttons)
        {
            var btn = b;
            var row = new Border
            {
                Padding = new Thickness(10, 8, 10, 8), CornerRadius = new CornerRadius(8), Background = Theme.Soft(Theme.Stone, 0.06), Margin = new Thickness(0, 0, 0, 4),
                Child = Ui.Fill(Ui.M, 1, Ui.Glyph("\uE962", 14), Ui.Col(1, Ui.T(ClickButtons.Name(b), 13.5, FontWeights.Medium), Ui.Caption(describe(b), false)), Ui.Glyph("\uE76C", 11, Theme.Sub))
            };
            Ui.MakeClickable(row, () => { result = btn; w.Close(); }, Theme.Soft(Theme.Copper, 0.10), Theme.Soft(Theme.Stone, 0.06));
            list.Children.Add(row);
        }
        w.Content = Body(title, "Choose the button. Next you pick what it does in this app.", list, Buttons(Ui.Btn("Cancel", () => w.Close())));
        w.KeyDown += (_, e) => { if (e.Key == Key.Escape) w.Close(); };
        w.ShowDialog();
        return result;
    }
}
