using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Clicksmith.Core;

namespace Clicksmith.App;

/// <summary>Small dialogs in the Clicksmith style: themed title bar, real buttons, Esc to cancel,
/// Enter for the main action.</summary>
internal static class Dialogs
{
    private static Window Shell(Window owner, string title, double width)
    {
        var w = new Window
        {
            Owner = owner,
            Title = title,
            Width = width,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Theme.Canvas,
            Foreground = Theme.Text,
            FontFamily = Theme.Font,
            ShowInTaskbar = false,
            UseLayoutRounding = true
        };
        Chrome.Apply(w);
        return w;
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
        var okButton = Ui.Btn(ok, () => { result = true; w.Close(); }, destructive ? BtnKind.Destructive : BtnKind.Primary);
        var cancel = Ui.Btn("Cancel", () => w.Close());
        cancel.IsCancel = true;
        okButton.IsDefault = !destructive; // destructive actions need a deliberate click
        w.Content = Body(title, message, null, Buttons(cancel, okButton));
        w.Loaded += (_, _) => (destructive ? cancel : okButton).Focus();
        w.ShowDialog();
        return result;
    }

    public static void Info(Window owner, string title, string message)
    {
        var w = Shell(owner, title, 460);
        var ok = Ui.Btn("OK", () => w.Close(), BtnKind.Primary);
        ok.IsDefault = true;
        ok.IsCancel = true;
        w.Content = Body(title, message, null, Buttons(ok));
        w.Loaded += (_, _) => ok.Focus();
        w.ShowDialog();
    }

    public static string? Prompt(Window owner, string title, string label, string initial)
    {
        var w = Shell(owner, title, 420);
        string? result = null;
        var text = initial;
        var box = Ui.Input(initial, t => text = t, name: label);
        box.MaxLength = 40;
        void Ok() { if (!string.IsNullOrWhiteSpace(text)) { result = text.Trim(); w.Close(); } }
        var save = Ui.Btn("Save", Ok, BtnKind.Primary);
        save.IsDefault = true;
        var cancel = Ui.Btn("Cancel", () => w.Close());
        cancel.IsCancel = true;
        w.Content = Body(title, null, Ui.Col(6, Ui.T(label, 12, brush: Theme.Sub), box), Buttons(cancel, save));
        w.Loaded += (_, _) => { box.Focus(); box.SelectAll(); };
        w.ShowDialog();
        return result;
    }

    private static Button ListRow(UIElement icon, string title, string detail, Action onClick, string key)
    {
        var content = Ui.Fill(Ui.M, 1, icon, Ui.Col(1, Ui.T(title, 13.5, FontWeights.Medium), Ui.Caption(detail, false)), Ui.Glyph("\uE76C", 11, Theme.Sub));
        var row = Ui.RowButton(content, onClick, $"{title}, {detail}", key);
        row.Background = Theme.Soft(Theme.Stone, 0.06);
        row.Margin = new Thickness(0, 0, 0, 4);
        return row;
    }

    /// <summary>Pick a running app, or browse for an .exe.</summary>
    public static RunningApp? ChooseApp(Window owner, List<RunningApp> running)
    {
        var w = Shell(owner, "Add app", 480);
        RunningApp? result = null;
        var list = new StackPanel();
        foreach (var app in running)
        {
            var a = app;
            list.Children.Add(ListRow(Ui.AppTile(app.Name, 24, app.Exe, app.Path), app.Name, app.Exe, () => { result = a; w.Close(); }, "app:" + app.Exe));
        }
        if (running.Count == 0) list.Children.Add(Ui.Caption("No apps with a window are running."));
        var scroll = new ScrollViewer { Content = list, MaxHeight = 360, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var cancel = Ui.Btn("Cancel", () => w.Close());
        cancel.IsCancel = true;
        w.Content = Body("Which app?", "Running apps are listed. Clicksmith recognises apps by their program file.", scroll, Buttons(
            cancel,
            Ui.Btn("Browse…", () =>
            {
                var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Programs (*.exe)|*.exe" };
                if (dlg.ShowDialog(w) != true) return;
                var exe = System.IO.Path.GetFileName(dlg.FileName).ToLowerInvariant();
                string name;
                try { name = System.Diagnostics.FileVersionInfo.GetVersionInfo(dlg.FileName).FileDescription ?? ""; } catch { name = ""; }
                if (string.IsNullOrWhiteSpace(name)) name = System.IO.Path.GetFileNameWithoutExtension(exe);
                result = new RunningApp(exe, name.Trim(), dlg.FileName);
                w.Close();
            })));
        w.ShowDialog();
        return result;
    }

    public static Trigger? ChooseTrigger(Window owner, string title, List<Trigger> triggers, Func<Trigger, string> describe)
    {
        var w = Shell(owner, title, 440);
        Trigger? result = null;
        var list = new StackPanel();
        foreach (var t in triggers)
        {
            var trigger = t;
            var name = t.IsKey ? $"Extra button · {KeyNames.Name(t.Vk)}" : ClickButtons.Name(t.Button);
            list.Children.Add(ListRow(Ui.Glyph(t.IsKey ? "\uE765" : "\uE962", 14), name, describe(t), () => { result = trigger; w.Close(); }, "trig:" + t));
        }
        var cancel = Ui.Btn("Cancel", () => w.Close());
        cancel.IsCancel = true;
        w.Content = Body(title, "Choose the button. Next you pick what it does.", list, Buttons(cancel));
        w.ShowDialog();
        return result;
    }

    /// <summary>2.1 flow for "New mapping": press the physical button; Clicksmith recognises it
    /// (mouse buttons and extra F13–F24 buttons). Falls back to a list.</summary>
    public static Trigger? PressButton(Window owner)
    {
        var s = AppState.Shared;
        var w = Shell(owner, "New mapping", 460);
        Trigger? result = null;
        var listening = s.EngineRunning;

        void ChooseFromList()
        {
            var triggers = ClickButtons.All.Where(b => !ClickButtons.IsPrimary(b) || s.Config.Settings.AllowPrimarySuppression).Select(b => new Trigger(b)).ToList();
            var used = s.ActiveProfile.Mappings.Where(m => m.Button == ClickButton.Key).Select(m => m.TriggerVk).ToHashSet();
            for (var vk = ClickButtons.FirstExtraKey; vk <= ClickButtons.LastExtraKey; vk++)
                if (!used.Contains(vk)) { triggers.Add(new Trigger(ClickButton.Key, vk)); break; }
            var picked = ChooseTrigger(w, "Which button?", triggers, t => s.MappingFor(t) is { } m ? m.Scope : t.IsKey ? "For mice whose software can send F13–F24" : "Not mapped yet");
            if (picked is { } p) { result = p; w.Close(); }
        }

        var cancel = Ui.Btn("Cancel", () => w.Close());
        cancel.IsCancel = true;
        var status = listening
            ? Ui.Row(Ui.S, Ui.Spinner(), Ui.T("Press a side, middle or extra button on your mouse now…", 13, brush: Theme.Copper))
            : (UIElement)Ui.T("The engine isn't running, so Clicksmith can't listen. Choose the button from the list.", 13, brush: Theme.Sub, wrap: true);
        w.Content = Body("Press the button you want to change", "Nothing happens in the app in front while Clicksmith listens. Left and right click are skipped.",
            status, Buttons(Ui.Link("Choose from a list", ChooseFromList), cancel));

        void OnChanged()
        {
            if (s.Identified is { } t && result is null && w.IsVisible)
            {
                result = t;
                w.Dispatcher.InvokeAsync(w.Close);
            }
        }
        if (listening)
        {
            s.Send(RequestKind.Identify);
            s.Changed += OnChanged;
        }
        w.Closed += (_, _) =>
        {
            s.Changed -= OnChanged;
            if (result is null && listening) s.CancelIdentify();
        };
        w.ShowDialog();
        return result;
    }
}
