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
        // Two aligned rows of two cards, then the test card across the full width.
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Ui.L) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < 5; i++) grid.RowDefinitions.Add(new RowDefinition { Height = i % 2 == 1 ? new GridLength(Ui.L) : GridLength.Auto });
        Place(grid, ButtonsCard(), 0, 0);
        Place(grid, ProfilesCard(), 0, 2);
        Place(grid, EngineCard(), 2, 0);
        Place(grid, MiceCard(), 2, 2);
        var test = TestCard();
        Grid.SetColumnSpan(test, 3);
        Place(grid, test, 4, 0);
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
            ? Ui.Row(Ui.S, Ui.T("Profile", 13, brush: Theme.Sub), Ui.Combo(profiles, _s.Config.ActiveProfileId, id => _s.ActivateProfile(id), 160, "Profile in use"))
            : null;
        var pause = Ui.Toggle(_s.Config.Settings.Paused, v => _s.SetPaused(v), "Pause all", key: "pause");
        pause.HorizontalAlignment = HorizontalAlignment.Right;
        pause.ToolTip = "Pause every mapping. Ctrl+Alt+Shift+F12 is the emergency pause.";
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
        if (_s.Status?.LastError is { } er && _s.EngineRunning) Add(Ui.Banner(BannerKind.Warning, er, "Diagnostics", () => _w.OpenSettings(4)));
        if (_s.Status?.ExtraKeysUnavailable is { Count: > 0 } busy && _s.EngineRunning)
            Add(Ui.Banner(BannerKind.Warning, $"{string.Join(", ", busy)} is used by another app, so that extra button can't be read."));
        if (SettingsPage.UpdateAvailable is { } upd)
            Add(Ui.Banner(BannerKind.Info, upd, "Update", () => _w.OpenSettings(3)));
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
            body = Ui.Col(6, Ui.T("Nothing forged yet.", 17, FontWeights.SemiBold),
                Ui.Caption("Give a mouse button a shortcut, a media key, an app or a text snippet."),
                Ui.Link("Start with a suggestion", () => _w.Select(Section.Buttons)));
        }
        else
        {
            var list = new StackPanel();
            foreach (var m in maps.OrderBy(x => x.Button).ThenBy(x => x.TriggerVk))
            {
                if (list.Children.Count > 0) list.Children.Add(new Border { Height = 1, Background = Theme.Line, Margin = new Thickness(36, 0, 0, 0) });
                list.Children.Add(MappingRow(m));
            }
            body = list;
        }
        var footer = Ui.Row(Ui.M, Ui.Btn("New mapping", _w.NewMapping, BtnKind.Primary, true, "\uE710"), null,
            maps.Count > 0 ? Ui.Link("Open Buttons", () => _w.Select(Section.Buttons)) : null);
        return Ui.CardWithFooter("Your buttons", "\uE962", footer, body);
    }

    public UIElement MappingRow(ButtonMapping m)
    {
        var icon = Ui.Glyph(m.Button == ClickButton.Key ? "\uE765" : "\uE962", 14, m.Enabled ? Theme.Copper : Theme.Soft(Theme.Stone, 0.6));
        var names = Ui.Col(2, Ui.T(m.Label, 13.5, FontWeights.Medium), Ui.T(m.Scope, 12, brush: Theme.Sub));
        names.Opacity = m.Enabled ? 1 : 0.6;
        var row = Ui.Fill(Ui.M, 1, icon, names, Ui.ActionsSummary(m));
        var open = Ui.RowButton(row, () => _w.EditMapping(m, m.Trigger), $"{m.Label}: {m.Scope}. Edit", "map:" + m.Id, new Thickness(6, 8, 6, 8));
        var more = Ui.MoreMenu(m.Label,
            (m.Enabled ? "Turn off" : "Turn on", () => _s.SetEnabled(m, !m.Enabled), false),
            ("Edit…", () => _w.EditMapping(m, m.Trigger), false),
            ("-", () => { }, false),
            ("Delete", () => _s.Delete(m), true));
        return Ui.Fill(4, 0, open, more);
    }

    private UIElement ProfilesCard()
    {
        var list = new StackPanel();
        foreach (var p in _s.Config.Profiles)
        {
            var active = p.Id == _s.Config.ActiveProfileId;
            var apps = p.Mappings.SelectMany(m => m.Overrides).Select(o => o.App.ToLowerInvariant()).Distinct().Count();
            var detail = AppState.Plural(p.Mappings.Count, "button") + (apps > 0 ? $" · {AppState.Plural(apps, "app rule")}" : "");
            var id = p.Id;
            UIElement action = active ? Ui.Badge("In use", Theme.Copper) : Ui.Link("Use", () => _s.ActivateProfile(id), 12.5, "use:" + p.Id);
            var row = Ui.Fill(Ui.M, 1, Ui.Glyph(active ? "\uE73E" : "\uE8F1", 14, active ? Theme.Copper : Theme.Stone),
                Ui.Col(1, Ui.T(p.Name, 13.5, FontWeights.Medium), Ui.T(detail, 12, brush: Theme.Sub)), action);
            row.Margin = new Thickness(0, list.Children.Count > 0 ? Ui.M : 0, 0, 0);
            list.Children.Add(row);
        }
        var last = _s.Status?.LastExternalApp;
        var footer = Ui.Row(Ui.M, Ui.Link("Manage profiles", () => _w.OpenSettings(1)), null,
            last is null ? null : Ui.T("Last app: " + last, 12, brush: Theme.Sub));
        return Ui.CardWithFooter("Profiles", "\uE8F1", footer, list);
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
        var footer = Ui.Row(Ui.M,
            _s.EngineRunning ? Ui.Btn("Restart engine", _s.RestartEngine) : Ui.Btn("Start engine", _s.StartEngine, BtnKind.Primary),
            Ui.Link("Diagnostics", () => _w.OpenSettings(4)));
        return Ui.CardWithFooter("Engine", "\uE945", footer, body);
    }

    private static UIElement Metric(string value, string label) =>
        Ui.Col(2, Ui.T(value, 14, FontWeights.SemiBold), Ui.T(label, 11, brush: Theme.Sub));

    /// <summary>The mice Windows sees right now.</summary>
    private UIElement MiceCard()
    {
        var list = new StackPanel();
        var mice = _s.Mice.Where(m => m.Connection != "Built-in").ToList();
        var builtIn = _s.Mice.Count - mice.Count;
        if (mice.Count == 0) list.Children.Add(Ui.Caption(builtIn > 0 ? "Only the built-in touchpad is connected." : "No mouse connected."));
        foreach (var m in mice)
        {
            var row = Ui.Fill(Ui.M, 1, Ui.Glyph(m.Connection == "Bluetooth" ? "\uE702" : "\uE962", 14, Theme.Stone),
                Ui.Col(1, Ui.T(m.Name, 13.5, FontWeights.Medium), Ui.T(m.Connection, 12, brush: Theme.Sub)),
                new Border { Width = 7, Height = 7, CornerRadius = new CornerRadius(3.5), Background = _s.EngineRunning ? Theme.Sage : Theme.Line, ToolTip = _s.EngineRunning ? "Clicksmith is listening" : "Engine stopped" });
            row.Margin = new Thickness(0, list.Children.Count > 0 ? Ui.M : 0, 0, 0);
            list.Children.Add(row);
        }
        UIElement? footer = null;
        if (_s.HasSteelSeries)
            footer = Ui.Col(6, Ui.Caption("The DPI button isn't one of Windows' five buttons. Two ways to use it:"),
                Ui.Row(Ui.M, Ui.Link("Set up as extra button", () => _w.Select(Section.Buttons)), Ui.Link("Run DPI probe", SettingsPage.RunProbe)));
        else if (builtIn > 0 && mice.Count > 0)
            footer = Ui.Caption($"+ {AppState.Plural(builtIn, "built-in pointing device")}");
        return Ui.CardWithFooter("Mice", "\uE962", footer, list);
    }

    /// <summary>2.1: test mode shows what a press would do, without doing it.</summary>
    private UIElement TestCard()
    {
        var testing = _s.TestStarted is not null;
        var result = _s.LastResult is { Kind: "test" } r ? r.Summary : null;
        var text = Ui.Caption(testing ? "Press any mouse button. Clicksmith shows what it would do — nothing is sent while testing."
                                      : "See exactly what a button would do in the app in front, without running it.");
        var resultBox = result is null ? null : new Border
        {
            Background = Theme.Soft(Theme.Copper, 0.08), CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 8, 10, 8),
            Child = Ui.T(result, 13, brush: Theme.Text, wrap: true)
        };
        if (resultBox is not null) System.Windows.Automation.AutomationProperties.SetLiveSetting(resultBox, System.Windows.Automation.AutomationLiveSetting.Polite);
        var actions = Ui.Row(Ui.M,
            testing ? Ui.Btn("Stop test", () => _s.Send(RequestKind.StopTest), key: "test")
                    : Ui.Btn("Start test", () => _s.Send(RequestKind.StartTest), BtnKind.Secondary, _s.EngineRunning, "\uE768", "test"),
            Ui.Link("Why isn't my button working?", () => _w.Select(Section.Buttons)));
        var left = Ui.Col(Ui.M, text, actions);
        UIElement body = resultBox is null ? left : Ui.Fill(Ui.XL, 0, left, new Border { Width = 420, Child = resultBox });
        return Ui.Card(testing ? "Test mode · 60 s" : "Test a button", "\uE9D9", body);
    }
}
