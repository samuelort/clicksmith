using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Clicksmith.Core;

namespace Clicksmith.App;

internal sealed class OverviewPage
{
    private readonly MainWindow _w;
    private readonly AppState _s = AppState.Shared;
    public OverviewPage(MainWindow w) => _w = w;

    public UIElement Build()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Ui.L) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(Ui.L) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Place(grid, ButtonsCard(), 0, 0);
        Place(grid, ProfilesCard(), 0, 2);
        Place(grid, EngineCard(), 2, 0);
        Place(grid, TestCard(), 2, 2);

        return Ui.Col(Ui.XL, Header(), Banners(), grid);
    }

    private static void Place(Grid g, UIElement e, int row, int col)
    {
        Grid.SetRow(e, row);
        Grid.SetColumn(e, col);
        g.Children.Add(e);
    }

    private UIElement Header()
    {
        var titleBlock = Ui.Col(6, Ui.Title("Clicksmith"),
            Ui.Row(Ui.S, Ui.Pill(_s.Health, _s.HealthTitle), Ui.T(_s.HealthDetail, 13, brush: Theme.Sub)));
        var profiles = _s.Config.Profiles.Select(p => (p.Name, p.Id));
        var profilePicker = _s.Config.Profiles.Count > 1
            ? Ui.Row(Ui.S, Ui.T("Profile", 13, brush: Theme.Sub), Ui.Combo(profiles, _s.Config.ActiveProfileId, id => _s.ActivateProfile(id), 160))
            : null;
        var pause = Ui.Toggle(_s.Config.Settings.Paused, v => _s.SetPaused(v), "Pause all");
        ((FrameworkElement)pause).ToolTip = "Pause every mapping. Ctrl+Alt+Shift+F12 is the emergency pause.";
        var right = Ui.Row(Ui.XL, profilePicker, pause);
        return Ui.Fill(Ui.L, 1, (UIElement?)Ui.Logo(56) ?? new Border(), titleBlock, right);
    }

    private UIElement Banners()
    {
        var p = new StackPanel();
        void Add(UIElement e) { if (e is FrameworkElement fe && p.Children.Count > 0) fe.Margin = new Thickness(0, Ui.S, 0, 0); p.Children.Add(e); }
        if (_s.Status?.EmergencyPaused == true)
            Add(Ui.Banner(BannerKind.Warning, "Emergency pause is on (Ctrl+Alt+Shift+F12). Mappings and button blocking are off.", "Resume", () => _s.Send(RequestKind.Resume)));
        if (_s.Status is { } st && st.Version != ClicksmithInfo.Version && _s.EngineRunning)
            Add(Ui.Banner(BannerKind.Info, $"The engine is still version {st.Version}. Restart it to use {ClicksmithInfo.DisplayVersion}.", "Restart", _s.RestartEngine));
        if (_s.LoadError is { } le) Add(Ui.Banner(BannerKind.Error, le));
        if (_s.SaveError is { } se) Add(Ui.Banner(BannerKind.Error, se));
        if (_s.EngineMessage is { } em) Add(Ui.Banner(BannerKind.Error, em));
        if (_s.Status?.LastError is { } er && _s.EngineRunning) Add(Ui.Banner(BannerKind.Warning, er, "Diagnostics", () => { _s.SettingsTab = 4; _w.Select(Section.Settings); }));
        if (_s.LastDeleted is { } d)
            Add(Ui.Banner(BannerKind.Info, $"{d.Mapping.Label} was deleted.", "Undo", _s.UndoDelete));
        return p;
    }

    private UIElement ButtonsCard()
    {
        var maps = _s.ActiveProfile.Mappings;
        UIElement body;
        if (maps.Count == 0)
        {
            body = Ui.Col(6, Ui.T("Nothing forged yet.", 17, System.Windows.FontWeights.SemiBold),
                Ui.Caption("Give a mouse button a shortcut, a media key, an app or a text snippet."),
                Ui.Link("Start with a suggestion", () => _w.Select(Section.Buttons)));
        }
        else
        {
            var list = new StackPanel();
            foreach (var m in maps.OrderBy(x => x.Button))
            {
                if (list.Children.Count > 0) list.Children.Add(new Border { Height = 1, Background = Theme.Line, Margin = new Thickness(28, 0, 0, 0) });
                list.Children.Add(MappingRow(m));
            }
            body = list;
        }
        var footer = Ui.Row(Ui.M, Ui.Btn("New mapping", () => NewMapping(), BtnKind.Primary, true, "\uE710"), null,
            maps.Count > 0 ? Ui.Link("Open Buttons", () => _w.Select(Section.Buttons)) : null);
        return Ui.Card("Your buttons", "\uE962", body, footer);
    }

    private void NewMapping()
    {
        var free = ClickButtons.All.Where(b => !ClickButtons.IsPrimary(b)).FirstOrDefault(b => _w.MappingFor(b) is null);
        if (_w.MappingFor(free) is not null || ClickButtons.IsPrimary(free)) { _w.Select(Section.Buttons); return; }
        _w.EditMapping(null, free);
    }

    public UIElement MappingRow(ButtonMapping m)
    {
        var icon = Ui.Glyph("\uE962", 14, m.Enabled ? Theme.Copper : Theme.Soft(Theme.Stone, 0.6));
        var names = Ui.Col(2, Ui.T(m.Label, 13.5, System.Windows.FontWeights.Medium), Ui.T(m.Scope, 12, brush: Theme.Sub));
        names.Opacity = m.Enabled ? 1 : 0.6;
        var more = Ui.MoreMenu(m.Label,
            (m.Enabled ? "Turn off" : "Turn on", () => _s.SetEnabled(m, !m.Enabled), false),
            ("Edit…", () => _w.EditMapping(m, m.Button), false),
            ("-", () => { }, false),
            ("Delete", () => _s.Delete(m), true));
        var row = Ui.Fill(Ui.M, 1, icon, names, Ui.ActionsSummary(m), more);
        var host = new Border { Padding = new Thickness(0, 8, 0, 8), Background = System.Windows.Media.Brushes.Transparent, Child = row, ToolTip = "Double-click to edit" };
        host.MouseLeftButtonDown += (_, e) => { if (e.ClickCount == 2) { _w.EditMapping(m, m.Button); e.Handled = true; } };
        return host;
    }

    private UIElement ProfilesCard()
    {
        var list = new StackPanel();
        foreach (var p in _s.Config.Profiles)
        {
            var active = p.Id == _s.Config.ActiveProfileId;
            var apps = p.Mappings.SelectMany(m => m.Overrides).Select(o => o.App.ToLowerInvariant()).Distinct().Count();
            var detail = $"{AppState.Plural(p.Mappings.Count, "button")}" + (apps > 0 ? $" · {AppState.Plural(apps, "app rule")}" : "");
            var row = Ui.Fill(Ui.M, 1, Ui.Glyph(active ? "\uE73E" : "\uE8F1", 14, active ? Theme.Copper : Theme.Stone),
                Ui.Col(1, Ui.T(p.Name, 13.5, System.Windows.FontWeights.Medium), Ui.T(detail, 12, brush: Theme.Sub)),
                active ? Ui.Badge("In use", Theme.Copper) : Ui.Link("Use", () => _s.ActivateProfile(p.Id), 12.5));
            row.Margin = new Thickness(0, list.Children.Count > 0 ? Ui.M : 0, 0, 0);
            list.Children.Add(row);
        }
        var last = _s.Status?.LastExternalApp;
        var footer = Ui.Row(Ui.M, Ui.Link("Manage profiles", () => { _s.SettingsTab = 1; _w.Select(Section.Settings); }), null,
            last is null ? null : Ui.T("Last app: " + last, 12, brush: Theme.Sub));
        return Ui.Card("Profiles", "\uE8F1", list, footer);
    }

    private UIElement EngineCard()
    {
        UIElement body;
        if (_s.EngineRunning && _s.Status is { } st)
        {
            body = Ui.Col(Ui.M,
                Ui.Row(Ui.XL, Metric(st.HookActive ? "Listening" : "No hook", "Mouse"), Metric(Ui.Duration(st.StartedAt), "Running"),
                    Metric(st.LastRecovery is null ? "Ready" : "Recovered", "After sleep")),
                Ui.Caption(st.LastRecovery is { } r ? $"Last refresh {Ui.Relative(r)} · profile {st.ActiveProfile}" : $"Profile {st.ActiveProfile} · refreshes itself after sleep and unlock"));
        }
        else
        {
            body = Ui.T(_s.Config.Settings.EngineEnabled ? "The engine isn't running." : "The engine is switched off.", 13, brush: Theme.Clay);
        }
        var actions = Ui.Row(Ui.M,
            _s.EngineRunning ? Ui.Btn("Restart engine", _s.RestartEngine) : Ui.Btn("Start engine", _s.StartEngine, BtnKind.Primary),
            Ui.Link("Diagnostics", () => { _s.SettingsTab = 4; _w.Select(Section.Settings); }));
        return Ui.Card("Engine", "\uE945", body, actions);
    }

    private static UIElement Metric(string value, string label) =>
        Ui.Col(2, Ui.T(value, 14, System.Windows.FontWeights.SemiBold), Ui.T(label, 11, brush: Theme.Sub));

    /// <summary>2.1: test mode shows what a press would do, without doing it.</summary>
    private UIElement TestCard()
    {
        var testing = _s.TestStarted is not null;
        var result = _s.LastResult is { Kind: "test" } r ? r.Summary : null;
        var body = Ui.Col(Ui.M,
            Ui.Caption(testing ? "Press any mouse button. Clicksmith shows what it would do — nothing is sent while testing."
                               : "See exactly what a button would do in the app in front, without running it."),
            result is null ? null : new Border
            {
                Background = Theme.Soft(Theme.Copper, 0.08), CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 8, 10, 8),
                Child = Ui.T(result, 13, brush: Theme.Text, wrap: true)
            });
        var actions = Ui.Row(Ui.M,
            testing ? Ui.Btn("Stop test", () => _s.Send(RequestKind.StopTest)) : Ui.Btn("Start test", () => _s.Send(RequestKind.StartTest), BtnKind.Secondary, _s.EngineRunning, "\uE768"),
            Ui.Link("Why isn't my button working?", () => _w.Select(Section.Buttons)));
        return Ui.Card(testing ? "Test mode · 60 s" : "Test a button", "\uE9D9", body, actions);
    }
}
