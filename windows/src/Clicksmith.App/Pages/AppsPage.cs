using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using Clicksmith.Core;

namespace Clicksmith.App;

internal sealed record RunningApp(string Exe, string Name, string? Path);

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
            Ui.Btn("Add app", AddApp, BtnKind.Secondary, true, "\uE710", "addapp"));

        var parts = new List<UIElement?> { header, DefaultCard() };
        parts.AddRange(groups.Select(g => AppCard(g.ToList())));
        if (groups.Count == 0) parts.Add(EmptyCard());
        if (_s.Status?.LastExternalApp is { } last) parts.Add(Ui.Caption("Last app in front: " + last));
        return Ui.Col(Ui.L, parts.ToArray());
    }

    private UIElement DefaultCard()
    {
        var list = new System.Windows.Controls.StackPanel();
        var defaults = _s.ActiveProfile.Mappings.Where(m => m.DefaultAction is not null || m.HasLongPress).OrderBy(m => m.Button).ThenBy(m => m.TriggerVk).ToList();
        if (defaults.Count == 0) list.Children.Add(Ui.Caption("No button has a default action yet."));
        foreach (var m in defaults)
        {
            var row = Ui.Fill(Ui.M, 0, Ui.T(m.Label, 13.5, FontWeights.Medium), Ui.ActionsSummary(m, includeApps: false),
                Ui.Link("Edit", () => _w.EditMapping(m, m.Trigger), key: "def:" + m.Id));
            row.Margin = new Thickness(0, list.Children.Count > 0 ? Ui.S + 2 : 0, 0, 0);
            list.Children.Add(row);
        }
        return Ui.Card("Default · all other apps", "\uE774", list);
    }

    private UIElement AppCard(List<Item> items)
    {
        var first = items[0].Override;
        var path = items.Select(i => i.Override.Path).FirstOrDefault(p => p is not null);
        var header = Ui.Fill(Ui.M, 1, Ui.AppTile(first.AppName, 32, first.App, path),
            Ui.Col(1, Ui.T(first.AppName, 16, FontWeights.SemiBold), Ui.T($"{AppState.Plural(items.Count, "button")} changed · {first.App}", 12, brush: Theme.Sub)),
            Ui.MoreMenu(first.AppName,
                ("Change another button…", () => ChooseButton(new RunningApp(first.App, first.AppName, path)), false),
                ("-", () => { }, false),
                ("Remove app…", () => { if (Dialogs.Confirm(_w, $"Remove {first.AppName}?", "Its buttons go back to the default actions.", "Remove", true)) _s.RemoveApp(first.App); }, true)));
        var rows = new System.Windows.Controls.StackPanel();
        foreach (var it in items)
        {
            var m = it.Mapping;
            var o = it.Override;
            UIElement instead = m.DefaultAction is { } d ? Ui.T("instead of " + d.Display, 12, brush: Theme.Sub) : new System.Windows.Controls.Border();
            var badge = Ui.Badge(o.Action.Display);
            badge.Opacity = o.Enabled ? 1 : 0.4;
            var row = Ui.Fill(Ui.M, 0, Ui.T(m.Label, 13.5, FontWeights.Medium), instead, badge,
                Ui.Toggle(o.Enabled, on => _s.SetOverrideEnabled(m.Id, o.Id, on), key: "ov:" + o.Id),
                Ui.Link("Edit", () => _w.EditMapping(m, m.Trigger), key: "ovedit:" + o.Id));
            System.Windows.Automation.AutomationProperties.SetName(row, $"{m.Label} in {first.AppName}: {o.Action.Display}{(o.Enabled ? "" : ", off")}");
            row.Margin = new Thickness(0, rows.Children.Count > 0 ? Ui.S + 2 : 0, 0, 0);
            rows.Children.Add(row);
        }
        var card = Ui.Surface(Ui.Col(Ui.M, header, Ui.Divider(), rows));
        System.Windows.Automation.AutomationProperties.SetName(card, first.AppName);
        return card;
    }

    private UIElement EmptyCard()
    {
        var col = Ui.Col(Ui.S, Ui.Glyph("\uE71D", 30, Theme.Copper), Ui.T("No app-specific buttons yet", 17, FontWeights.SemiBold),
            Ui.T("Example: make Back mute your microphone (Ctrl+Shift+M) only in Microsoft Teams.", 13, brush: Theme.Sub, wrap: true),
            Ui.Btn("Add app", AddApp, BtnKind.Primary, true, "\uE710", "addapp2"));
        col.HorizontalAlignment = HorizontalAlignment.Center;
        foreach (var c in col.Children.OfType<FrameworkElement>()) c.HorizontalAlignment = HorizontalAlignment.Center;
        return Ui.Surface(col, 28);
    }

    // ---------- Add an app ----------

    private void AddApp()
    {
        var choice = Dialogs.ChooseApp(_w, RunningApps());
        if (choice is null) return;
        ChooseButton(choice);
    }

    private void ChooseButton(RunningApp app)
    {
        var triggers = ClickButtons.All.Where(b => !ClickButtons.IsPrimary(b) || _s.Config.Settings.AllowPrimarySuppression)
            .Select(b => new Trigger(b))
            .Concat(_s.ActiveProfile.Mappings.Where(m => m.Button == ClickButton.Key).Select(m => m.Trigger))
            .ToList();
        var picked = Dialogs.ChooseTrigger(_w, $"Change a button in {app.Name}", triggers, candidate =>
        {
            var m = _s.MappingFor(candidate);
            if (m is null) return "Not mapped yet";
            return m.Overrides.Any(o => o.App.Equals(app.Exe, StringComparison.OrdinalIgnoreCase)) ? "Already changed here" : m.Scope;
        });
        if (picked is not { } chosen) return;
        var existing = _s.MappingFor(chosen);
        _w.EditMapping(existing, chosen, m =>
        {
            if (existing is null) m.DefaultAction = null; // a new button only acts in this app
            if (m.Overrides.All(o => !o.App.Equals(app.Exe, StringComparison.OrdinalIgnoreCase)))
                m.Overrides.Add(new AppOverride { App = app.Exe, AppName = app.Name, Path = app.Path, Action = new ButtonAction { Kind = ActionKind.Shortcut } });
        });
    }

    /// <summary>Apps with a window, by executable name (with path, for the real icon). Clicksmith excluded.</summary>
    public static List<RunningApp> RunningApps()
    {
        var result = new Dictionary<string, RunningApp>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (p.MainWindowHandle == IntPtr.Zero || string.IsNullOrEmpty(p.MainWindowTitle)) continue;
                var exe = (p.ProcessName + ".exe").ToLowerInvariant();
                if (exe.StartsWith("clicksmith", StringComparison.OrdinalIgnoreCase)) continue;
                string? path = null;
                string name = "";
                try { path = p.MainModule?.FileName; name = p.MainModule?.FileVersionInfo.FileDescription ?? ""; } catch { }
                if (string.IsNullOrWhiteSpace(name)) name = p.ProcessName;
                result.TryAdd(exe, new RunningApp(exe, name.Trim(), path));
            }
            catch { /* exited or access denied */ }
            finally { p.Dispose(); }
        }
        return result.Values.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }
}
