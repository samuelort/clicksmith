using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Clicksmith.Core;

namespace Clicksmith.App;

internal sealed class SettingsPage
{
    private static readonly string[] Tabs = { "General", "Profiles", "Backups", "Updates", "Diagnostics", "About" };
    private readonly MainWindow _w;
    private readonly AppState _s = AppState.Shared;
    public SettingsPage(MainWindow w) => _w = w;

    private EngineSettings S => _s.Config.Settings;

    public UIElement Build()
    {
        var tab = Math.Clamp(_s.SettingsTab, 0, Tabs.Length - 1);
        var header = Ui.Fill(Ui.L, 0, Ui.Title("Settings"), Ui.Segmented(Tabs, tab, i => { _s.SettingsTab = i; _w.Render(); }, "settings"));
        UIElement body = tab switch
        {
            1 => Profiles(),
            2 => Backups(),
            3 => Updates(),
            4 => Diagnostics(),
            5 => About(),
            _ => General()
        };
        return Ui.Col(Ui.L, header, _s.SaveError is { } e ? Ui.Banner(BannerKind.Error, e) : null, _s.EngineMessage is { } m ? Ui.Banner(BannerKind.Error, m) : null, body);
    }

    private static Border Group(string title, params UIElement?[] rows)
    {
        var col = new StackPanel();
        foreach (var r in rows.Where(r => r is not null))
        {
            if (col.Children.Count > 0) col.Children.Add(new Border { Height = 1, Background = Theme.Line, Margin = new Thickness(0, 10, 0, 10) });
            col.Children.Add(r!);
        }
        var card = Ui.Surface(col, 16);
        System.Windows.Automation.AutomationProperties.SetName(card, title);
        return new Border { Child = Ui.Col(6, Ui.T(title.ToUpperInvariant(), 11.5, FontWeights.SemiBold, Theme.Sub), card) };
    }

    private static UIElement Line(string label, string value)
    {
        var row = Ui.Fill(Ui.M, 0, Ui.T(label, 13), Ui.T(value, 13, brush: Theme.Sub));
        System.Windows.Automation.AutomationProperties.SetName(row, label + ": " + value);
        return row;
    }

    // ---------- General ----------

    private UIElement General()
    {
        var engineAtLogin = EngineControl.RunsAtLogin("Clicksmith Engine");
        var appAtLogin = EngineControl.RunsAtLogin("Clicksmith");
        var startup = Group("Startup",
            Ui.Toggle(engineAtLogin, on =>
            {
                _s.EngineMessage = EngineControl.SetRunAtLogin("Clicksmith Engine", EngineControl.EnginePath, on);
                _s.Update(c => c.Settings.LaunchEngineAtLogin = on);
            }, "Start Clicksmith Engine when I sign in", key: "engineatlogin"),
            Ui.Toggle(appAtLogin, on =>
            {
                _s.EngineMessage = EngineControl.SetRunAtLogin("Clicksmith", EngineControl.AppPath, on, "--tray");
                _s.Update(c => c.Settings.LaunchAppAtLogin = on);
            }, "Show Clicksmith in the system tray when I sign in", key: "appatlogin"),
            Ui.Caption("The engine keeps your buttons working even when this window and the tray icon are closed."));

        var behaviour = Group("Behaviour",
            Ui.Toggle(S.Paused, v => _s.SetPaused(v), "Pause all mappings", key: "pause"),
            Ui.Fill(Ui.M, 0, Ui.T("Ignore repeat presses within", 13),
                Ui.Combo(new[] { 0, 50, 100, 150, 250, 400, 600 }.Select(v => ($"{v} ms", v)), S.DuplicateGuardMs, v => _s.Update(c => c.Settings.DuplicateGuardMs = v), 110, "Repeat guard")),
            Ui.Fill(Ui.M, 0, Ui.T("Refresh after sleep (delay)", 13),
                Ui.Combo(new[] { 1000, 2000, 3000, 5000 }.Select(v => ($"{v / 1000.0:0.#} s", v)), S.WakeRecoveryDelayMs, v => _s.Update(c => c.Settings.WakeRecoveryDelayMs = v), 110, "Wake delay")));

        var safety = Group("Safety",
            Ui.Toggle(S.EmergencyHotKeyEffective, v => _s.Update(c => c.Settings.EmergencyHotKeyEnabled = v), "Emergency pause with Ctrl+Alt+Shift+F12", !S.AllowPrimarySuppression, "emergency"),
            Ui.Toggle(S.AllowPrimarySuppression, v =>
            {
                if (v && !Dialogs.Confirm(_w, "Allow changing left and right click?",
                        "A wrong mapping could leave you without a working click. Ctrl+Alt+Shift+F12 always pauses Clicksmith and stays on while this is allowed. Stopping the engine also restores normal clicks.",
                        "Allow", true)) { _w.Render(); return; }
                _s.Update(c =>
                {
                    c.Settings.AllowPrimarySuppression = v;
                    if (!v) foreach (var p in c.Profiles) p.Mappings.RemoveAll(m => ClickButtons.IsPrimary(m.Button));
                });
            }, "Allow changing left and right click", key: "primary"),
            Ui.Caption(S.AllowPrimarySuppression
                ? "The emergency shortcut stays on while this is allowed, so you can't lock yourself out. Turning this off removes left/right mappings."
                : "Left and right click always keep working normally."));

        var appearance = Group("Appearance",
            Ui.Toggle(S.ShowTrayIcon, v => _s.Update(c => c.Settings.ShowTrayIcon = v), "Show Clicksmith in the system tray", key: "tray"),
            Ui.Caption("Without the tray icon, closing this window quits the app. The engine keeps running. Light and dark mode follow Windows."));

        var data = Group("Configuration",
            Ui.Row(Ui.S, Ui.Btn("Export…", Export), Ui.Btn("Import…", Import), Ui.Btn("Open folder", () => OpenFolder(ClicksmithPaths.Root)), null,
                Ui.Btn("Reset all mappings…", () =>
                {
                    if (Dialogs.Confirm(_w, "Delete all mappings in every profile?", "Settings are kept, and the current setup is saved in Backups first.", "Delete all", true))
                        _s.Update(c => { foreach (var p in c.Profiles) p.Mappings.Clear(); });
                }, BtnKind.Destructive)),
            Ui.Caption("Every change is backed up automatically (last 20). See the Backups tab."));

        return Ui.Col(Ui.L, startup, behaviour, safety, appearance, data);
    }

    private void Export()
    {
        var dlg = new Microsoft.Win32.SaveFileDialog { FileName = "Clicksmith-config.json", Filter = "Clicksmith configuration (*.json)|*.json" };
        if (dlg.ShowDialog(_w) != true) return;
        try { ConfigStore.Export(_s.Config, dlg.FileName); }
        catch (Exception e) { Dialogs.Info(_w, "Export failed", e.Message); }
    }

    private void Import()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Clicksmith configuration (*.json)|*.json" };
        if (dlg.ShowDialog(_w) != true) return;
        try
        {
            var imported = ConfigStore.ReadForImport(dlg.FileName);
            if (Dialogs.Confirm(_w, "Replace your setup with this file?", Summary(imported) + "\n\nYour current setup is saved in Backups first.", "Replace", false))
                _s.ReplaceConfig(imported);
        }
        catch (ConfigException e) { Dialogs.Info(_w, "This file can't be imported", e.Message + "\nNothing was changed."); }
        catch (Exception e) { Dialogs.Info(_w, "Import failed", e.Message); }
    }

    public static string Summary(ClicksmithConfig c)
    {
        var lines = c.Profiles.Select(p =>
        {
            var apps = p.Mappings.SelectMany(m => m.Overrides).Select(o => o.AppName).Distinct().ToList();
            return $"• {p.Name}{(p.Id == c.ActiveProfileId ? " (in use)" : "")}: {AppState.Plural(p.Mappings.Count, "button")}" +
                   (apps.Count > 0 ? " · " + string.Join(", ", apps.Take(4)) + (apps.Count > 4 ? "…" : "") : "");
        });
        return string.Join("\n", lines);
    }

    private static void OpenFolder(string path)
    {
        try { Directory.CreateDirectory(path); Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true })?.Dispose(); }
        catch { }
    }

    // ---------- 2.3 Profiles ----------

    private UIElement Profiles()
    {
        var list = new StackPanel();
        foreach (var p in _s.Config.Profiles)
        {
            var active = p.Id == _s.Config.ActiveProfileId;
            var id = p.Id;
            var name = p.Name;
            var row = Ui.Fill(Ui.M, 1,
                Ui.Glyph(active ? "\uE73E" : "\uE8F1", 14, active ? Theme.Copper : Theme.Stone),
                Ui.Col(1, Ui.T(p.Name, 14, FontWeights.SemiBold), Ui.Caption(AppState.Plural(p.Mappings.Count, "button"))),
                active ? Ui.Badge("In use", Theme.Copper) : Ui.Btn("Use", () => _s.ActivateProfile(id), key: "use:" + id),
                Ui.MoreMenu(p.Name,
                    ("Rename…", () => { var n = Dialogs.Prompt(_w, "Rename profile", "Name", name); if (!string.IsNullOrWhiteSpace(n)) _s.RenameProfile(id, n); }, false),
                    ("Duplicate", () => _s.DuplicateProfile(id), false),
                    ("-", () => { }, false),
                    ("Delete…", () =>
                    {
                        if (_s.Config.Profiles.Count <= 1) { Dialogs.Info(_w, "Keep at least one profile", "Clicksmith needs one profile to work with."); return; }
                        if (Dialogs.Confirm(_w, $"Delete “{name}”?", "Its buttons are removed. The current setup is saved in Backups first.", "Delete", true)) _s.DeleteProfile(id);
                    }, true)));
            row.Margin = new Thickness(0, list.Children.Count > 0 ? Ui.M : 0, 0, 0);
            list.Children.Add(row);
        }
        var actions = Ui.Row(Ui.S,
            Ui.Btn("New profile", () => { var n = Dialogs.Prompt(_w, "New profile", "Name", "Work"); if (!string.IsNullOrWhiteSpace(n)) _s.AddProfile(n); }, BtnKind.Primary, _s.Config.Profiles.Count < 20, "\uE710"),
            Ui.Btn("Export…", Export), Ui.Btn("Import…", Import));
        return Ui.Col(Ui.L,
            Ui.Caption("A profile is a complete set of buttons — for example Work, Gaming or Presenting. One profile is in use at a time; switch from the Overview or the tray icon."),
            Ui.Surface(Ui.Col(Ui.L, list, actions)));
    }

    // ---------- 2.1 Backups ----------

    private UIElement Backups()
    {
        var entries = ConfigurationHistory.List();
        var list = new StackPanel();
        if (entries.Count == 0) list.Children.Add(Ui.Caption("No backups yet. One is made automatically before every change."));
        foreach (var e in entries)
        {
            var entry = e;
            var when = e.CreatedUtc.ToLocalTime().ToString("ddd d MMM yyyy · HH:mm:ss");
            var row = Ui.Fill(Ui.M, 1, Ui.Glyph("\uE81C", 14, Theme.Stone), Ui.Col(1, Ui.T(when, 13.5), Ui.Caption($"{e.Bytes / 1024.0:0.#} KB")),
                Ui.Link("Preview", () =>
                {
                    try { Dialogs.Info(_w, "Backup from " + when, Summary(ConfigurationHistory.Read(entry))); }
                    catch (Exception ex) { Dialogs.Info(_w, "Backup can't be read", ex.Message); }
                }, key: "prev:" + e.Path),
                Ui.Btn("Restore…", () =>
                {
                    try
                    {
                        var c = ConfigurationHistory.Read(entry);
                        if (!Dialogs.Confirm(_w, "Restore this backup?", Summary(c) + "\n\nYour current setup is backed up first, so you can go back.", "Restore", false)) return;
                        _s.ReplaceConfig(c);
                    }
                    catch (Exception ex) { Dialogs.Info(_w, "Backup can't be restored", ex.Message + "\nNothing was changed."); }
                }, key: "restore:" + e.Path));
            row.Margin = new Thickness(0, list.Children.Count > 0 ? Ui.M : 0, 0, 0);
            list.Children.Add(row);
        }
        return Ui.Col(Ui.L,
            Ui.Fill(Ui.M, 0, Ui.Caption($"Clicksmith keeps the last {ConfigurationHistory.Limit} versions of your setup on this PC. Nothing leaves your computer."),
                Ui.Btn("Open folder", () => OpenFolder(ClicksmithPaths.Backups))),
            Ui.Surface(list));
    }

    // ---------- 2.4 Updates ----------

    private static string? _updateMessage;
    private static string? _updateUrl;
    private static string? _updateTag;
    private static string? _progress;
    private static bool _busy;

    /// <summary>Shown as a banner on the Overview when a start-up check found a newer version.</summary>
    public static string? UpdateAvailable { get; private set; }

    private UIElement Updates()
    {
        UIElement status = _busy ? Ui.Row(Ui.S, Ui.Spinner(), Ui.T(_progress ?? "Checking GitHub…", 13, brush: Theme.Sub))
            : _updateMessage is { } m ? Ui.T(m, 13, wrap: true)
            : Ui.Caption("Clicksmith only contacts GitHub when you press Check, or at start-up if you turn that on below.");
        var check = Group("Version",
            Line("Clicksmith", ClicksmithInfo.DisplayVersion + $" ({ClicksmithInfo.Version}, {UpdateChecker.Arch})"),
            Line("Engine", _s.Status?.Version ?? "not running"),
            status,
            Ui.Row(Ui.S,
                _updateTag is not null
                    ? Ui.Btn("Download and install", InstallNow, BtnKind.Primary, !_busy, "\uE896", "install")
                    : Ui.Btn("Check for updates", CheckNow, BtnKind.Primary, !_busy, "\uE895", "check"),
                _updateUrl is { } u ? Ui.Btn("Release notes", () => OpenUrl(u)) : null),
            Ui.Toggle(S.CheckForUpdatesOnStart, v => _s.Update(c => c.Settings.CheckForUpdatesOnStart = v), "Check for updates when Clicksmith starts", key: "autocheck"));
        var how = Group("How updating works",
            Ui.Caption("1. Clicksmith downloads the package for your PC from the project's GitHub release."),
            Ui.Caption("2. It checks the package's SHA-256 against the release's list before anything runs."),
            Ui.Caption("3. The installer backs up your setup, keeps the current version, installs, and puts the previous version back automatically if anything fails. Your buttons and profiles are kept."),
            Ui.Caption("Without code signing, Windows may show a SmartScreen notice for downloaded installers. Prefer the terminal or in-app update."));
        return Ui.Col(Ui.L, check, how);
    }

    private async void CheckNow()
    {
        _busy = true; _progress = null;
        _w.Render();
        var r = await UpdateChecker.CheckAsync();
        _busy = false;
        Apply(r);
        _w.Render();
    }

    private static void Apply(UpdateChecker.Result r)
    {
        _updateMessage = r.Message;
        _updateUrl = r.Url;
        _updateTag = r.Newer ? r.Tag : null;
        UpdateAvailable = r.Newer ? r.Message : null;
    }

    private async void InstallNow()
    {
        if (_updateTag is not { } tag) return;
        if (!Dialogs.Confirm(_w, "Install the update?", "Clicksmith closes, the installer opens in a terminal window, and Clicksmith starts again when it's done. Your setup is backed up first.", "Install", false)) return;
        _busy = true;
        var progress = new Progress<string>(p => { _progress = p; _w.Render(); });
        var error = await UpdateChecker.InstallAsync(tag, progress);
        _busy = false;
        if (error is not null)
        {
            _updateMessage = error;
            _w.Render();
            return;
        }
        _w.AllowClose = true;
        Application.Current.Shutdown();
    }

    public static void ApplyStartupCheck(UpdateChecker.Result r)
    {
        if (r.Newer) Apply(r);
    }

    private static void OpenUrl(string url)
    {
        if (!url.StartsWith("https://github.com/", StringComparison.Ordinal)) return;
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose(); } catch { }
    }

    // ---------- Diagnostics ----------

    public static void RunProbe()
    {
        var exe = Path.Combine(AppContext.BaseDirectory, "Clicksmith.DpiProbe.exe");
        if (!File.Exists(exe)) return;
        try { Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = AppContext.BaseDirectory })?.Dispose(); } catch { }
    }

    private UIElement Diagnostics()
    {
        var st = _s.Status;
        var versions = Group("Versions",
            Line("Clicksmith", ClicksmithInfo.Version),
            Line("Engine", (st?.Version ?? "—") + (st is not null && st.Version != ClicksmithInfo.Version ? " (restart to update)" : "")),
            Line("Windows", Environment.OSVersion.VersionString),
            Line(".NET", Environment.Version.ToString()));
        var engine = Group("Engine",
            Line("State", _s.HealthTitle),
            Line("Process ID", st?.Pid.ToString() ?? "—"),
            Line("Started", Ui.Relative(st?.StartedAt)),
            Line("Mouse hook", st?.HookActive == true ? "Active since " + Ui.Relative(st.HookInstalledAt) : "Not active"),
            Line("Last sleep", Ui.Relative(st?.LastSleep)),
            Line("Last wake", Ui.Relative(st?.LastResume)),
            Line("Last refresh", Ui.Relative(st?.LastRecovery)),
            Line("Emergency shortcut", st?.EmergencyHotKeyRegistered == true ? "Ctrl+Alt+Shift+F12" : "Off or unavailable"),
            Line("Extra buttons", st is null ? "—" : st.ExtraKeys.Count == 0 && st.ExtraKeysUnavailable.Count == 0 ? "None" :
                string.Join(", ", st.ExtraKeys) + (st.ExtraKeysUnavailable.Count > 0 ? $" · taken: {string.Join(", ", st.ExtraKeysUnavailable)}" : "")),
            Line("Profile in effect", st?.ActiveProfile ?? "—"),
            Line("Active buttons", st?.ActiveMappings.ToString() ?? "—"),
            Line("App in front", st?.ForegroundApp ?? "—"),
            Line("Last error", st?.LastError ?? "None"));
        var mice = Group("Mice Windows sees", _s.Mice.Count == 0 ? Ui.Caption("None") : null,
            _s.Mice.Count == 0 ? null : Ui.Col(6, _s.Mice.Select(m => (UIElement)Line(m.Name, $"{m.Connection}{(m.Vid != 0 ? $" · {m.Vid:X4}:{m.Pid:X4}" : "")}")).ToArray()));
        var probeExists = File.Exists(Path.Combine(AppContext.BaseDirectory, "Clicksmith.DpiProbe.exe"));
        var probe = Group("DPI probe",
            Ui.Caption("Read-only: lists your mouse's HID parts and listens while you press the DPI button, to see if Clicksmith can read it directly. Nothing is written to the mouse. The report is saved on your desktop."),
            Ui.Btn("Run DPI probe", RunProbe, BtnKind.Secondary, probeExists, "\uE9D9", "probe"));
        var actions = Ui.Row(Ui.S, Ui.Btn("Restart engine", _s.RestartEngine), Ui.Btn("Open logs", () => OpenFolder(ClicksmithPaths.Logs)), Ui.Btn("Export report…", ExportReport));
        return Ui.Col(Ui.L, versions, engine, mice, probe, actions,
            Ui.Caption("Logs contain start-ups, refreshes and errors only — never which buttons you press or what you type."));
    }

    private void ExportReport()
    {
        var dlg = new Microsoft.Win32.SaveFileDialog { FileName = "Clicksmith-diagnostics.json", Filter = "JSON (*.json)|*.json" };
        if (dlg.ShowDialog(_w) != true) return;
        var report = new
        {
            generated = DateTimeOffset.UtcNow,
            app = ClicksmithInfo.Version,
            arch = UpdateChecker.Arch,
            windows = Environment.OSVersion.VersionString,
            dotnet = Environment.Version.ToString(),
            engineRunning = _s.EngineRunning,
            status = _s.Status,
            settings = _s.Config.Settings,
            mice = _s.Mice,
            profiles = _s.Config.Profiles.Count,
            buttons = _s.ActiveProfile.Mappings.Count,
            appRules = _s.ActiveProfile.Mappings.Sum(m => m.Overrides.Count)
        };
        try { File.WriteAllText(dlg.FileName, JsonSerializer.Serialize(report, Json.Options)); }
        catch (Exception e) { Dialogs.Info(_w, "Export failed", e.Message); }
    }

    // ---------- About ----------

    private static UIElement About()
    {
        var col = Ui.Col(Ui.M,
            (UIElement?)Ui.Logo(128) ?? new Border(),
            Ui.T("Clicksmith", 34, FontWeights.SemiBold),
            Ui.T("Small clicks. A calmer workflow.", 17, brush: Theme.Sub),
            Ui.T("Version " + ClicksmithInfo.DisplayVersion + " for Windows", 13, brush: Theme.Sub),
            new Border { Width = 320, Height = 1, Background = Theme.Line, Margin = new Thickness(0, 6, 0, 6) },
            Ui.T("Local only · no telemetry · no keystroke logging", 13),
            Ui.T("Network is only used when you check for or install updates. MIT License.", 12, brush: Theme.Sub));
        col.HorizontalAlignment = HorizontalAlignment.Center;
        foreach (var c in col.Children.OfType<FrameworkElement>()) c.HorizontalAlignment = HorizontalAlignment.Center;
        col.Margin = new Thickness(0, 30, 0, 0);
        return col;
    }
}
