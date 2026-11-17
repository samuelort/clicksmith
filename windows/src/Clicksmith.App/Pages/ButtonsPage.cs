using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Clicksmith.Core;

namespace Clicksmith.App;

internal sealed class ButtonsPage
{
    private readonly MainWindow _w;
    private readonly AppState _s = AppState.Shared;
    public ButtonsPage(MainWindow w) => _w = w;

    public UIElement Build() => Ui.Col(Ui.L,
        Ui.Fill(Ui.L, 0,
            Ui.Col(4, Ui.Title("Buttons"), Ui.T("Click a button on the mouse, or press it, to choose what it does.", 13, brush: Theme.Sub)),
            Ui.Btn("Press a button…", _w.NewMapping, BtnKind.Primary, true, "\uE962", "press")),
        DiagramCard(), ExtraCard(), IdentifyCard(), PresetsCard(), TroubleshootCard());

    // ---------- Diagram + legend ----------

    private UIElement DiagramCard()
    {
        var legend = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        foreach (var b in new[] { ClickButton.Left, ClickButton.Right, ClickButton.Middle, ClickButton.Forward, ClickButton.Back })
            legend.Children.Add(LegendRow(b));
        var body = Ui.Fill(Ui.XL + 4, 1, Diagram(), legend);
        return Ui.Card("Your mouse", "\uE962", Ui.Caption("The five buttons Windows reports for every mouse. Copper parts are mapped."), body);
    }

    private UIElement LegendRow(ClickButton b)
    {
        var m = _w.MappingFor(b);
        var locked = ClickButtons.IsPrimary(b) && !_s.Config.Settings.AllowPrimarySuppression;
        UIElement value = m is not null ? Ui.ActionsSummary(m) : Ui.T(locked ? "Normal click · change in Settings" : "—", 12.5, brush: Theme.Sub);
        var dot = new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Background = m is not null && m.Enabled ? Theme.Copper : Theme.Soft(Theme.Stone, 0.25) };
        var row = Ui.Fill(10, 2, dot, new Border { Width = 110, Child = Ui.T(ClickButtons.Name(b), 13.5) }, value);
        var name = ClickButtons.Name(b) + (m is null ? (locked ? ", normal click" : ", not mapped") : ", " + m.Scope);
        var button = Ui.RowButton(row, () => Open(b), name, "legend:" + b, new Thickness(8, 7, 8, 7));
        button.Opacity = m is null ? 0.85 : 1;
        return button;
    }

    private void Open(ClickButton b)
    {
        if (ClickButtons.IsPrimary(b) && !_s.Config.Settings.AllowPrimarySuppression && _w.MappingFor(b) is null)
        {
            _w.OpenSettings(0);
            return;
        }
        _w.EditMapping(_w.MappingFor(b), b);
    }

    /// <summary>Top-down mouse in the icon's materials; every part is a focusable button.</summary>
    private UIElement Diagram()
    {
        var canvas = new Canvas { Width = 200, Height = 290 };
        var body = new Border
        {
            Width = 150, Height = 250, CornerRadius = new CornerRadius(72),
            Background = new LinearGradientBrush(Theme.Card.Color, Theme.Sidebar.Color, 90),
            BorderBrush = Theme.Soft(Theme.Stone, 0.25), BorderThickness = new Thickness(1)
        };
        Put(canvas, body, 25, 20);
        Put(canvas, Part(ClickButton.Left, 73, 104, new CornerRadius(72, 0, 0, 0)), 26, 21);
        Put(canvas, Part(ClickButton.Right, 73, 104, new CornerRadius(0, 72, 0, 0)), 101, 21);
        Put(canvas, new Border { Width = 148, Height = 1, Background = Theme.Soft(Theme.Stone, 0.25), IsHitTestVisible = false }, 26, 125);
        Put(canvas, Part(ClickButton.Middle, 18, 40, new CornerRadius(9), true), 91, 44);
        Put(canvas, Part(ClickButton.Forward, 11, 34, new CornerRadius(5.5), true), 12, 120);
        Put(canvas, Part(ClickButton.Back, 11, 34, new CornerRadius(5.5), true), 12, 160);
        return canvas;
    }

    private static void Put(Canvas c, UIElement e, double x, double y)
    {
        Canvas.SetLeft(e, x);
        Canvas.SetTop(e, y);
        c.Children.Add(e);
    }

    private UIElement Part(ClickButton b, double w, double h, CornerRadius r, bool outlined = false)
    {
        var m = _w.MappingFor(b);
        var mapped = m is not null && m.Enabled;
        Brush normal = mapped ? Theme.Soft(Theme.Copper, 0.85) : outlined ? Theme.Soft(Theme.Stone, 0.14) : Brushes.Transparent;
        var face = new Border
        {
            Width = w, Height = h, CornerRadius = r, Background = normal,
            BorderBrush = outlined ? Theme.Soft(Theme.Stone, 0.40) : Brushes.Transparent, BorderThickness = new Thickness(outlined ? 1 : 0)
        };
        var button = Ui.BareButton(face, () => Open(b), ClickButtons.Name(b) + ": " + (m?.Scope ?? "not mapped"), "part:" + b);
        button.ToolTip = ClickButtons.Name(b) + ": " + (m?.Scope ?? "not mapped");
        button.MouseEnter += (_, _) => face.Background = mapped ? Theme.Copper : Theme.Soft(Theme.Copper, 0.22);
        button.MouseLeave += (_, _) => face.Background = normal;
        return button;
    }

    // ---------- Extra buttons (F13–F24): the route to buttons Windows can't see, such as DPI ----------

    private UIElement ExtraCard()
    {
        var extras = _s.ActiveProfile.Mappings.Where(m => m.Button == ClickButton.Key).OrderBy(m => m.TriggerVk).ToList();
        var list = new StackPanel();
        foreach (var m in extras)
        {
            var row = Ui.Fill(Ui.M, 1, Ui.Badge(KeyNames.Name(m.TriggerVk), Theme.Copper),
                Ui.Col(1, Ui.T(m.Label, 13.5, FontWeights.Medium), Ui.T(m.Scope, 12, brush: Theme.Sub)), Ui.ActionsSummary(m));
            var open = Ui.RowButton(row, () => _w.EditMapping(m, m.Trigger), $"{m.Label}, {KeyNames.Name(m.TriggerVk)}. Edit", "extra:" + m.Id, new Thickness(6, 6, 6, 6));
            open.Margin = new Thickness(0, list.Children.Count > 0 ? 4 : 0, 0, 0);
            list.Children.Add(open);
        }
        var used = extras.Select(m => m.TriggerVk).ToHashSet();
        ushort? free = null;
        for (var vk = ClickButtons.FirstExtraKey; vk <= ClickButtons.LastExtraKey; vk++) if (!used.Contains(vk)) { free = vk; break; }
        var intro = Ui.Caption("Windows only reports five mouse buttons. Buttons like DPI are handled inside the mouse. If the mouse's own software " +
                               "(SteelSeries GG, Logitech Options+, …) can make such a button send F13–F24, Clicksmith turns that key into a full button — " +
                               "with app rules and hold actions.");
        var actions = Ui.Row(Ui.M,
            Ui.Btn("Add extra button", () => { if (free is { } k) _w.EditMapping(null, new Trigger(ClickButton.Key, k)); }, BtnKind.Secondary, free is not null, "\uE710", "addextra"),
            Ui.Link("Detect it", () => _s.Send(RequestKind.Identify)),
            _s.HasSteelSeries ? Ui.Link("Run DPI probe", SettingsPage.RunProbe) : null);
        return Ui.Card("Extra buttons", "\uE765", intro, extras.Count > 0 ? list : null, actions);
    }

    // ---------- 2.1 guided learning ----------

    private UIElement IdentifyCard()
    {
        var waiting = _s.IdentifyStarted is not null;
        var found = _s.Identified;
        UIElement status;
        if (waiting)
            status = Ui.Row(Ui.S, Ui.Spinner(), Ui.T("Press a side, middle or extra button now…", 13, brush: Theme.Copper));
        else if (found is { } f)
        {
            var name = f.IsKey ? $"{KeyNames.Name(f.Vk)} — an extra button" : ClickButtons.Name(f.Button);
            var existing = _s.MappingFor(f);
            status = Ui.Row(Ui.M, Ui.Glyph("\uE73E", 14, Theme.Sage), Ui.T("That was " + name + ".", 13, FontWeights.SemiBold),
                Ui.Link(existing is null ? "Map it" : "Edit " + existing.Label, () => _w.EditMapping(existing, f)));
        }
        else
            status = Ui.Caption("Not sure which button is which? Press it and Clicksmith tells you. Nothing happens in the app in front.");
        var action = waiting
            ? Ui.Btn("Cancel", _s.CancelIdentify, key: "identify")
            : Ui.Btn("Identify a button", () => _s.Send(RequestKind.Identify), BtnKind.Secondary, _s.EngineRunning, "\uE962", "identify");
        return Ui.Card("Find a button", "\uE721", status, action);
    }

    // ---------- 2.1 suggestions ----------

    private UIElement PresetsCard()
    {
        var list = new StackPanel();
        foreach (var p in Presets.All)
        {
            var preset = p;
            var row = Ui.Fill(Ui.M, 0, Ui.Col(1, Ui.T(p.Title, 13.5, FontWeights.Medium), Ui.Caption(p.Detail)), Ui.Link("Use…", () => Apply(preset), key: "preset:" + p.Title));
            row.Margin = new Thickness(0, list.Children.Count > 0 ? Ui.M : 0, 0, 0);
            list.Children.Add(row);
        }
        return Ui.Card("Suggestions", "\uE734", Ui.Caption("Each suggestion opens in the editor first. Nothing changes until you save."), list);
    }

    private void Apply(Preset p)
    {
        _w.EditMapping(_w.MappingFor(p.Button), p.Button, m =>
        {
            if (p.Default is not null) m.DefaultAction = AppState.Clone(p.Default);
            else if (m.DefaultAction is { } d && d.Problem() is not null) m.DefaultAction = null;
            if (p.App is not null && p.AppAction is not null && m.Overrides.All(o => !o.App.Equals(p.App, StringComparison.OrdinalIgnoreCase)))
                m.Overrides.Add(new AppOverride { App = p.App, AppName = p.AppName ?? p.App, Action = AppState.Clone(p.AppAction) });
        });
    }

    // ---------- 2.1 troubleshooting ----------

    private UIElement TroubleshootCard()
    {
        var st = _s.Status;
        var list = new StackPanel();
        void Item(bool? ok, string title, string detail)
        {
            var glyph = ok switch { true => "\uE73E", false => "\uE711", _ => "\uE946" };
            var brush = ok switch { true => Theme.Sage, false => Theme.Clay, _ => Theme.Amber };
            var row = Ui.Fill(Ui.M, 1, Ui.Glyph(glyph, 13, brush), Ui.Col(1, Ui.T(title, 13.5, FontWeights.Medium), Ui.Caption(detail)));
            row.Margin = new Thickness(0, list.Children.Count > 0 ? Ui.M : 0, 0, 0);
            System.Windows.Automation.AutomationProperties.SetName(row, (ok switch { true => "OK: ", false => "Problem: ", _ => "Note: " }) + title + ". " + detail);
            list.Children.Add(row);
        }
        Item(_s.EngineRunning, "Engine is running", _s.EngineRunning ? "Clicksmith Engine is reading your mouse in the background." : "Start it from the Overview.");
        Item(st?.HookActive == true, "Windows sends mouse events", st?.HookActive == true ? "The mouse hook is active." : "Restart the engine. Windows can drop the hook after a long freeze.");
        var active = !_s.Config.Settings.Paused && st?.EmergencyPaused != true;
        Item(active, "Mappings are active", active ? "Not paused." : "Mappings are paused — resume from the Overview or the tray icon.");
        var count = _s.ActiveProfile.Mappings.Count(m => m.Enabled);
        Item(count > 0, "Buttons are mapped in this profile", count > 0 ? $"{AppState.Plural(count, "button")} in “{_s.ActiveProfile.Name}”." : $"“{_s.ActiveProfile.Name}” has no active buttons. Check the profile in use.");
        if (_s.ActiveProfile.Mappings.Any(m => m.Button == ClickButton.Key))
        {
            var busy = st?.ExtraKeysUnavailable ?? new();
            Item(busy.Count == 0, "Extra buttons can be read", busy.Count == 0 ? "Clicksmith listens for your F13–F24 keys." : $"{string.Join(", ", busy)} is taken by another app. Pick another key in the mouse software and here.");
        }
        if (st?.LastSkipped is { } skipped && st.LastSkippedAt is { } skippedAt && DateTimeOffset.UtcNow - skippedAt < TimeSpan.FromHours(24))
            Item(false, "An action was skipped " + Ui.Relative(skippedAt), skipped);
        var app = st?.LastExternalApp;
        var rule = app is null ? null : _s.ActiveProfile.Mappings.FirstOrDefault(m => m.Overrides.Any(o => o.Enabled && o.App.Equals(app, StringComparison.OrdinalIgnoreCase)));
        Item(null, "Rule for the last app", app is null ? "No app seen yet." : rule is null ? $"{app} uses the default actions." : $"{app} has its own action for {rule.Label}.");
        Item(null, "One app ignores Clicksmith?", "If that app runs as administrator, Windows blocks input from normal apps. Run it normally, or check with “Test a button” on the Overview.");
        return Ui.Card("Why isn't my button working?", "\uE897", list);
    }
}
