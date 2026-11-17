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
        Ui.Col(4, Ui.Title("Buttons"), Ui.T("Windows reads five buttons on every mouse. Click one to choose what it does.", 13, brush: Theme.Sub)),
        DiagramCard(), IdentifyCard(), PresetsCard(), TroubleshootCard());

    // ---------- Diagram + legend ----------

    private UIElement DiagramCard()
    {
        var legend = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        foreach (var b in new[] { ClickButton.Left, ClickButton.Right, ClickButton.Middle, ClickButton.Forward, ClickButton.Back })
            legend.Children.Add(LegendRow(b));
        var body = Ui.Fill(Ui.XL + 4, 1, Diagram(), legend);
        return Ui.Card("Your mouse", "\uE962", Ui.Caption("Click a part of the mouse or a row to map it. Copper parts are mapped."), body);
    }

    private UIElement LegendRow(ClickButton b)
    {
        var m = _w.MappingFor(b);
        var locked = ClickButtons.IsPrimary(b) && !_s.Config.Settings.AllowPrimarySuppression;
        UIElement value = m is not null ? Ui.ActionsSummary(m)
            : Ui.T(locked ? "Normal click · change in Settings" : "—", 12.5, brush: Theme.Sub);
        var dot = new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Background = m is not null && m.Enabled ? Theme.Copper : Theme.Soft(Theme.Stone, 0.25) };
        var row = Ui.Fill(10, 2, dot, new Border { Width = 110, Child = Ui.T(ClickButtons.Name(b), 13.5) }, value);
        var host = new Border { Padding = new Thickness(8, 7, 8, 7), CornerRadius = new CornerRadius(7), Background = Brushes.Transparent, Child = row, Opacity = m is null ? 0.8 : 1 };
        Ui.MakeClickable(host, () => Open(b), Theme.Soft(Theme.Copper, 0.08), Brushes.Transparent);
        Ui.AutomationName(host, ClickButtons.Name(b) + (m is null ? ", not mapped" : ", " + m.Scope));
        return host;
    }

    private void Open(ClickButton b)
    {
        if (ClickButtons.IsPrimary(b) && !_s.Config.Settings.AllowPrimarySuppression && _w.MappingFor(b) is null)
        {
            _s.SettingsTab = 0;
            _w.Select(Section.Settings);
            return;
        }
        _w.EditMapping(_w.MappingFor(b), b);
    }

    /// <summary>Top-down mouse in the icon's materials; every part is a button.</summary>
    private UIElement Diagram()
    {
        var canvas = new Canvas { Width = 200, Height = 290 };
        var body = new Border
        {
            Width = 150, Height = 250, CornerRadius = new CornerRadius(72),
            Background = new LinearGradientBrush(Theme.Card.Color, Theme.Sidebar.Color, 90),
            BorderBrush = Theme.Soft(Theme.Stone, 0.25), BorderThickness = new Thickness(1),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 6, Direction = 270, Opacity = Theme.Dark ? 0.35 : 0.10 }
        };
        Put(canvas, body, 25, 20);
        Put(canvas, Part(ClickButton.Left, 73, 104, new CornerRadius(72, 0, 0, 0)), 26, 21);
        Put(canvas, Part(ClickButton.Right, 73, 104, new CornerRadius(0, 72, 0, 0)), 101, 21);
        Put(canvas, new Border { Width = 148, Height = 1, Background = Theme.Soft(Theme.Stone, 0.25) }, 26, 125);
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

    private Border Part(ClickButton b, double w, double h, CornerRadius r, bool outlined = false)
    {
        var m = _w.MappingFor(b);
        var mapped = m is not null && m.Enabled;
        Brush normal = mapped ? Theme.Soft(Theme.Copper, 0.85) : outlined ? Theme.Soft(Theme.Stone, 0.14) : Brushes.Transparent;
        var part = new Border
        {
            Width = w, Height = h, CornerRadius = r, Background = normal,
            BorderBrush = outlined ? Theme.Soft(Theme.Stone, 0.40) : Brushes.Transparent, BorderThickness = new Thickness(outlined ? 1 : 0),
            ToolTip = ClickButtons.Name(b) + ": " + (m?.Scope ?? "not mapped")
        };
        Ui.MakeClickable(part, () => Open(b), mapped ? Theme.Copper : Theme.Soft(Theme.Copper, 0.22), normal);
        Ui.AutomationName(part, ClickButtons.Name(b));
        return part;
    }

    // ---------- 2.1 guided learning ----------

    private UIElement IdentifyCard()
    {
        var waiting = _s.IdentifyStarted is not null;
        var found = _s.LastResult is { Kind: "identified", Button: { } b } ? b : (ClickButton?)null;
        UIElement status;
        if (waiting)
            status = Ui.Row(Ui.S, new ProgressBar { IsIndeterminate = true, Width = 80, Height = 4, Foreground = Theme.Copper, Background = Theme.Soft(Theme.Stone, 0.15), BorderThickness = new Thickness(0) },
                Ui.T("Press a side or middle button on your mouse now…", 13, brush: Theme.Copper));
        else if (found is { } f)
            status = Ui.Row(Ui.M, Ui.Glyph("\uE73E", 14, Theme.Sage), Ui.T("That was " + ClickButtons.Name(f) + ".", 13, System.Windows.FontWeights.SemiBold),
                Ui.Link("Map " + ClickButtons.Name(f), () => Open(f)));
        else
            status = Ui.Caption("Not sure which button is which? Press it and Clicksmith tells you. Nothing happens in the app in front.");
        var action = waiting ? Ui.Btn("Cancel", _s.CancelIdentify) : Ui.Btn("Identify a button", () => _s.Send(RequestKind.Identify), BtnKind.Secondary, _s.EngineRunning, "\uE962");
        return Ui.Card("Find a button", "\uE721", status, action);
    }

    // ---------- 2.1 suggestions ----------

    private UIElement PresetsCard()
    {
        var list = new StackPanel();
        foreach (var p in Presets.All)
        {
            var row = Ui.Fill(Ui.M, 0, Ui.Col(1, Ui.T(p.Title, 13.5, System.Windows.FontWeights.Medium), Ui.Caption(p.Detail)),
                Ui.Link("Use…", () => Apply(p)));
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
            var row = Ui.Fill(Ui.M, 1, Ui.Glyph(glyph, 13, brush), Ui.Col(1, Ui.T(title, 13.5, System.Windows.FontWeights.Medium), Ui.Caption(detail)));
            row.Margin = new Thickness(0, list.Children.Count > 0 ? Ui.M : 0, 0, 0);
            list.Children.Add(row);
        }
        Item(_s.EngineRunning, "Engine is running", _s.EngineRunning ? "Clicksmith Engine is reading your mouse in the background." : "Start it from the Overview.");
        Item(st?.HookActive == true, "Windows sends mouse events", st?.HookActive == true ? "The mouse hook is active." : "Restart the engine. Windows can drop the hook after a long freeze.");
        var active = !_s.Config.Settings.Paused && st?.EmergencyPaused != true;
        Item(active, "Mappings are active", active ? "Not paused." : "Mappings are paused — resume from the Overview or the tray icon.");
        var count = _s.ActiveProfile.Mappings.Count(m => m.Enabled);
        Item(count > 0, "Buttons are mapped in this profile", count > 0 ? $"{AppState.Plural(count, "button")} in “{_s.ActiveProfile.Name}”." : $"“{_s.ActiveProfile.Name}” has no active buttons. Check the profile in use.");
        var app = st?.LastExternalApp;
        var rule = app is null ? null : _s.ActiveProfile.Mappings.FirstOrDefault(m => m.Overrides.Any(o => o.Enabled && o.App.Equals(app, StringComparison.OrdinalIgnoreCase)));
        Item(null, "Rule for the last app", app is null ? "No app seen yet." : rule is null ? $"{app} uses the default actions." : $"{app} has its own action for {rule.Label}.");
        Item(null, "One app ignores Clicksmith?", "If that app runs as administrator, Windows blocks input from normal apps. Run it normally, or test with “Test a button” on the Overview.");
        return Ui.Card("Why isn't my button working?", "\uE897", list);
    }
}
