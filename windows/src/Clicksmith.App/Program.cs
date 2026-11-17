using System;
using System.Linq;
using System.Threading;
using System.Windows;
using Clicksmith.Core;
using Microsoft.Win32;

namespace Clicksmith.App;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        using var single = new Mutex(true, @"Local\Clicksmith.App", out var first);
        using var showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\Clicksmith.App.Show");
        if (!first)
        {
            showSignal.Set(); // bring the running window to the front
            return 0;
        }

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/Clicksmith;component/Themes/Nordic.xaml", UriKind.Absolute)
        });
        Theme.Apply(Theme.SystemDark());
        var state = AppState.Shared;
        var window = new MainWindow();
        var tray = new Tray(window);

        ThreadPool.RegisterWaitForSingleObject(showSignal, (_, _) => app.Dispatcher.InvokeAsync(window.ShowAndActivate), null, -1, false);
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category != UserPreferenceCategory.General) return;
            var dark = Theme.SystemDark();
            if (dark != Theme.Dark) app.Dispatcher.InvokeAsync(() => Theme.Apply(dark));
        };

        if (state.Config.Settings.EngineEnabled && !state.EngineRunning)
            state.EngineMessage = state.Engine.Start();

        var startHidden = args.Contains("--tray") && state.Config.Settings.ShowTrayIcon && state.Config.Settings.OnboardingCompleted;
        if (!startHidden) window.ShowAndActivate();

        if (state.Config.Settings.CheckForUpdatesOnStart)
        {
            _ = UpdateChecker.CheckAsync().ContinueWith(t =>
            {
                if (t.IsCompletedSuccessfully) app.Dispatcher.InvokeAsync(() => { SettingsPage.ApplyStartupCheck(t.Result); state.Notify(); });
            });
        }

        var code = app.Run();
        tray.Dispose();
        return code;
    }
}
