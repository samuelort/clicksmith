using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Clicksmith.Core;

namespace Clicksmith.App;

internal sealed class AppsPage
{
    private readonly MainWindow _w;
    private readonly AppState _s = AppState.Shared;
    public AppsPage(MainWindow w) => _w = w;

    private sealed record Item(ButtonMapping Mapping, AppOverride Override);

    public UIElement Build()
    {
        var groups = _s.ActiveProfile.Mappings
            .SelectMany(m => m.Overrides.Select(o => new Item(m, o)))
            .GroupBy(i => i.Override.App.ToLowerInvariant())
            .OrderBy(g => g.First().Override.AppName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var header = Ui.Fill(Ui.L, 0,
            Ui.Col(4, Ui.Title("Apps"), Ui.T($"Give buttons a different job in specific apps. Profile: {_s.ActiveProfile.Name}.", 13, brush: Theme.Sub)),
            Ui.Btn("Add app", AddApp, BtnKind.Secondary, true, "\uE710"));

        var parts = new List<UIElement?> { header, DefaultCard() };
        parts.AddRange(groups.Select(g => AppCard(g.ToList())));
        if (groups.Count == 0) parts.Add(EmptyCard());
        if (_s.Status?.LastExternalApp is { } last)
            parts.Add(Ui.Caption("Last app in front: " + last));
        return Ui.Col(Ui.L, parts.ToArray());
    }

    private UIElement DefaultCard()
    {
        var list = new StackPanel();
        var defaults = _s.ActiveProfile.Mappings.Where(m => m.DefaultAction is not null || m.HasLongPress).OrderBy(m => m.Button).ToList();
        if (defaults.Count == 0) list.Children.Add(Ui.Caption("No button has a default action yet."));
        foreach (var m in defaults)
        {
            var row = Ui.Fill(Ui.M, 0, Ui.T(m.Label, 13.5, FontWeights.Medium), Ui.ActionsSummaryDefault(m), Ui.Link("Edit", () => _w.EditMapping(m, m.Button)));
            row.Margin = new Thickness(0, list.Children.Count > 0 ? Ui.S + 2 : 0, 0, 0);
            list.Children.Add(row);
        }
        return Ui.Card("Default · all other apps", "\uE774", list);
    }

    private UIElement AppCard(List<Item> items)
    {
        var first = items[0].Override;
        var header = Ui.Fill(Ui.M, 1, Ui.AppTile(first.AppName, 32),
            Ui.Col(1, Ui.T(first.AppName, 16, FontWeights.SemiBold), Ui.T($"{AppState.Plural(items.Count, "button")} changed · {first.App}", 12, brush: Theme.Sub)),
            Ui.MoreMenu(first.AppName,
                ("Change another button…", () => ChooseButton(first.App, first.AppName), false),
                ("-", () => { }, false),
                ("Remove app…", () => { if (Dialogs.Confirm(_w, $"Remove {first.AppName}?", "Its buttons go back to the default actions.", "Remove", true)) _s.RemoveApp(first.App); }, true)));
        var rows = new StackPanel();
        foreach (var it in items)
        {
            var m = it.Mapping;
            var o = it.Override;
            var instead = m.DefaultAction is { } d ? Ui.T("instead of " + d.Display, 12, brush: Theme.Sub) : (UIElement)new Border();
            var badge = Ui.Badge(o.Action.Display);
            badge.Opacity = o.Enabled ? 1 : 0.4;
            var row = Ui.Fill(Ui.M, 0, Ui.T(m.Label, 13.5, FontWeights.Medium), instead, badge,
                Ui.Toggle(o.Enabled, on => _s.SetOverrideEnabled(m.Id, o.Id, on)), Ui.Link("Edit", () => _w.EditMapping(m, m.Button)));
            row.Margin = new Thickness(0, rows.Children.Count > 0 ? Ui.S + 2 : 0, 0, 0);
            rows.Children.Add(row);
        }
        return Ui.Surface(Ui.Col(Ui.M, header, Ui.Divider(), rows));
    }

    private UIElement EmptyCard()
    {
        var col = Ui.Col(Ui.S, Ui.Glyph("\uE71D", 30, Theme.Copper), Ui.T("No app-specific buttons yet", 17, FontWeights.SemiBold),
            Ui.T("Example: make Back mute your microphone (Ctrl+Shift+M) only in Microsoft Teams.", 13, brush: Theme.Sub, wrap: true));
        col.HorizontalAlignment = HorizontalAlignment.Center;
        foreach (var c in col.Children.OfType<FrameworkElement>()) c.HorizontalAlignment = HorizontalAlignment.Center;
        var s = Ui.Surface(col, 28);
        return s;
    }

    // ---------- Add an app ----------

    private void AddApp()
    {
        var running = RunningApps();
        var choice = Dialogs.ChooseApp(_w, running);
        if (choice is null) return;
        ChooseButton(choice.Value.Exe, choice.Value.Name);
    }

    private void ChooseButton(string exe, string name)
    {
        var buttons = ClickButtons.All.Where(b => !ClickButtons.IsPrimary(b) || _s.Config.Settings.AllowPrimarySuppression).ToList();
        var picked = Dialogs.ChooseButton(_w, $"Change a button in {name}", buttons, candidate =>
        {
            var m = _w.MappingFor(candidate);
            if (m is null) return "Not mapped yet";
            return m.Overrides.Any(o => o.App.Equals(exe, StringComparison.OrdinalIgnoreCase)) ? "Already changed here" : m.Scope;
        });
        if (picked is not { } chosen) return;
        var existing = _w.MappingFor(chosen);
        _w.EditMapping(existing, chosen, m =>
        {
            if (existing is null) m.DefaultAction = null; // a new button only acts in this app
            if (m.Overrides.All(o => !o.App.Equals(exe, StringComparison.OrdinalIgnoreCase)))
                m.Overrides.Add(new AppOverride { App = exe, AppName = name, Action = new ButtonAction { Kind = ActionKind.Shortcut } });
        });
    }

    /// <summary>Apps with a window, by executable name. Clicksmith itself is excluded.</summary>
    public static List<(string Exe, string Name)> RunningApps()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (p.MainWindowHandle == IntPtr.Zero) continue;
                var exe = p.ProcessName + ".exe";
                if (exe.StartsWith("Clicksmith", StringComparison.OrdinalIgnoreCase) || exe.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(p.MainWindowTitle)) continue;
                string name;
                try { name = p.MainModule?.FileVersionInfo.FileDescription ?? ""; } catch { name = ""; }
                if (string.IsNullOrWhiteSpace(name)) name = p.ProcessName;
                result.TryAdd(exe.ToLowerInvariant(), name.Trim());
            }
            catch { /* process exited or access denied */ }
            finally { p.Dispose(); }
        }
        return result.Select(kv => (kv.Key, kv.Value)).OrderBy(x => x.Value, StringComparer.CurrentCultureIgnoreCase).ToList();
    }
}
