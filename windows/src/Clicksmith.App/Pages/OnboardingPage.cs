using System.Windows;
using System.Windows.Controls;
using Clicksmith.Core;

namespace Clicksmith.App;

/// <summary>First-run guide, shown inside the main window: welcome → engine → first button.</summary>
internal sealed class OnboardingPage
{
    private static int _step;
    private readonly MainWindow _w;
    private readonly AppState _s = AppState.Shared;
    public OnboardingPage(MainWindow w) => _w = w;

    public UIElement Build()
    {
        UIElement body = _step switch { 1 => Engine(), 2 => FirstButton(), _ => Welcome() };
        var dots = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, Ui.XL, 0, 0) };
        for (var i = 0; i < 3; i++)
            dots.Children.Add(new Border { Width = 7, Height = 7, CornerRadius = new CornerRadius(3.5), Margin = new Thickness(4, 0, 4, 0), Background = i == _step ? Theme.Copper : Theme.Soft(Theme.Stone, 0.3) });
        System.Windows.Automation.AutomationProperties.SetName(dots, $"Step {_step + 1} of 3");
        var col = Ui.Col(Ui.L, body, dots);
        col.MaxWidth = 560;
        col.Margin = new Thickness(0, 40, 0, 0);
        col.HorizontalAlignment = HorizontalAlignment.Center;
        return col;
    }

    private void Go(int step) { _step = step; _w.Render(); }

    private static StackPanel Centered(params UIElement?[] items)
    {
        var col = Ui.Col(Ui.M, items);
        foreach (var c in col.Children) if (c is FrameworkElement fe) fe.HorizontalAlignment = HorizontalAlignment.Center;
        return col;
    }

    private UIElement Welcome() => Centered(
        Ui.Logo(112),
        Ui.T("Welcome to Clicksmith", 30, FontWeights.SemiBold),
        Wrap("Give every mouse button a job — a shortcut, a media key, an app or a snippet. Everywhere, or differently in each app."),
        Ui.T("Local only · no account · no telemetry", 12.5, brush: Theme.Sub),
        Ui.Btn("Get started", () => Go(1), BtnKind.Primary, key: "ob:start"));

    private static TextBlock Wrap(string text)
    {
        var t = Ui.T(text, 14, brush: Theme.Sub, wrap: true);
        t.TextAlignment = TextAlignment.Center;
        return t;
    }

    private UIElement Engine()
    {
        var running = _s.EngineRunning;
        var status = running
            ? Ui.Row(Ui.S, Ui.Glyph("\uE73E", 14, Theme.Sage), Ui.T("Clicksmith Engine is running.", 13.5, FontWeights.Medium))
            : Ui.Row(Ui.S, Ui.Glyph("\uE7BA", 14, Theme.Amber), Ui.T("The engine isn't running yet.", 13.5, FontWeights.Medium), Ui.Btn("Start engine", _s.StartEngine, BtnKind.Secondary, key: "ob:engine"));
        var card = Ui.Surface(Ui.Col(Ui.M,
            Ui.T("A small engine in the background", 17, FontWeights.SemiBold),
            Ui.Caption("Clicksmith Engine reads your mouse buttons and runs your actions. It uses almost no CPU, needs no administrator rights and keeps working when this window is closed. It never records what you type."),
            status,
            Ui.Toggle(EngineControl.RunsAtLogin("Clicksmith Engine"), on =>
            {
                _s.EngineMessage = EngineControl.SetRunAtLogin("Clicksmith Engine", EngineControl.EnginePath, on);
                _s.Update(c => c.Settings.LaunchEngineAtLogin = on);
            }, "Start the engine when I sign in", key: "ob:login"),
            Ui.Toggle(EngineControl.RunsAtLogin("Clicksmith"), on =>
            {
                _s.EngineMessage = EngineControl.SetRunAtLogin("Clicksmith", EngineControl.AppPath, on, "--tray");
                _s.Update(c => c.Settings.LaunchAppAtLogin = on);
            }, "Show Clicksmith in the system tray when I sign in", key: "ob:tray")), 22);
        return Ui.Col(Ui.L, card, Ui.Row(Ui.S, Ui.Link("Back", () => Go(0)), null, Ui.Btn("Continue", () => Go(2), BtnKind.Primary, key: "ob:next")));
    }

    private UIElement FirstButton()
    {
        var waiting = _s.IdentifyStarted is not null;
        var found = _s.Identified;
        UIElement status;
        if (waiting) status = Ui.Row(Ui.S, Ui.Spinner(), Ui.T("Press a side or middle button on your mouse now…", 13.5, brush: Theme.Copper));
        else if (found is { } f)
            status = Ui.Row(Ui.M, Ui.Glyph("\uE73E", 14, Theme.Sage), Ui.T("That was " + f + ".", 13.5, FontWeights.SemiBold),
                Ui.Btn("Choose what it does", () => { _s.CompleteOnboarding(); _w.EditMapping(_s.MappingFor(f), f); }, BtnKind.Primary, key: "ob:map"));
        else status = Ui.Btn("Press a button", () => _s.Send(RequestKind.Identify), BtnKind.Primary, _s.EngineRunning, "\uE962", "ob:press");
        var card = Ui.Surface(Ui.Col(Ui.M,
            Ui.T("Your first button", 17, FontWeights.SemiBold),
            Ui.Caption("Press the mouse button you'd like to change. Nothing happens in other apps while Clicksmith listens. Left and right click keep working."),
            status,
            Ui.Caption("Tip: Overview › “Test a button” shows what a button would do, without doing it.")), 22);
        return Ui.Col(Ui.L, card, Ui.Row(Ui.S, Ui.Link("Back", () => Go(1)), null,
            Ui.Link("Start with a suggestion", () => { _s.CompleteOnboarding(); _s.Selection = Section.Buttons; _w.Render(); }),
            Ui.Btn("Skip for now", () => { _s.CompleteOnboarding(); _w.Select(Section.Overview); }, key: "ob:skip")));
    }
}
