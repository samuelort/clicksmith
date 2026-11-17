using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Clicksmith.Core;

namespace Clicksmith.App;

internal enum Section { Overview, Buttons, Apps, Settings }

/// <summary>Single source of truth for the UI (UI thread only). The UI never sits in the input
/// path: it only writes config.json and requests, and reads the engine's status.</summary>
internal sealed class AppState
{
    public static AppState Shared { get; } = new();

    public ClicksmithConfig Config { get; private set; }
    public string? LoadError { get; private set; }
    public string? SaveError { get; private set; }
    public EngineStatus? Status { get; private set; }
    public bool EngineRunning { get; private set; }
    public EngineResult? LastResult { get; private set; }
    public Section Selection { get; set; } = Section.Overview;
    public int SettingsTab { get; set; }
    public (Guid ProfileId, ButtonMapping Mapping)? LastDeleted { get; private set; }
    public DateTimeOffset? IdentifyStarted { get; private set; }
    public DateTimeOffset? TestStarted { get; private set; }
    public string? EngineMessage { get; set; }
    public List<MouseDevice> Mice { get; private set; } = new();
    public readonly EngineControl Engine = new();

    public event Action? Changed;

    private readonly FileSystemWatcher? _watcher;
    private readonly DispatcherTimer _timer;
    private Guid? _pendingRequest;

    private AppState()
    {
        try { Config = ConfigStore.Load(); }
        catch (Exception e)
        {
            Config = ClicksmithConfig.CreateDefault();
            LoadError = e.Message + " Your existing file was left untouched; saving will replace it (a backup is kept).";
        }
        ClicksmithPaths.EnsureDirectories();
        RefreshStatus(notify: false);
        try
        {
            _watcher = new FileSystemWatcher(ClicksmithPaths.Root)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
                EnableRaisingEvents = true
            };
            _watcher.Changed += OnFile;
            _watcher.Created += OnFile;
            _watcher.Renamed += (s, e) => OnFile(s, e);
        }
        catch { /* fall back to the timer */ }
        Mice = Devices.Mice();
        // Upgrading from a version without the welcome guide: skip it if a setup already exists.
        if (!Config.Settings.OnboardingCompleted && Config.Profiles.Any(p => p.Mappings.Count > 0))
            Update(c => c.Settings.OnboardingCompleted = true);
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _timer.Tick += (_, _) => { RefreshDevices(); RefreshStatus(notify: true); };
        _timer.Start();
    }

    private void OnFile(object sender, FileSystemEventArgs e)
    {
        var name = Path.GetFileName(e.FullPath);
        Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            if (name.Equals("engine-status.json", StringComparison.OrdinalIgnoreCase)) RefreshStatus(notify: true);
            else if (name.Equals("engine-result.json", StringComparison.OrdinalIgnoreCase)) ReadResult();
        });
    }

    public void Notify() => Changed?.Invoke();

    // ---------- Engine status ----------

    public void RefreshStatus(bool notify)
    {
        var s = IpcFiles.Read<EngineStatus>(ClicksmithPaths.Status);
        var fresh = s is not null && DateTimeOffset.UtcNow - s.Heartbeat < TimeSpan.FromSeconds(30);
        var running = fresh && EngineControl.IsAlive(s!.Pid);
        var changed = running != EngineRunning || Signature(s) != Signature(Status);
        Status = s;
        EngineRunning = running;
        if (IdentifyStarted is { } i && DateTimeOffset.UtcNow - i > TimeSpan.FromSeconds(20)) { IdentifyStarted = null; changed = true; }
        if (TestStarted is { } t && DateTimeOffset.UtcNow - t > TimeSpan.FromSeconds(60)) { TestStarted = null; changed = true; }
        if (notify && changed) Notify();
    }

    /// <summary>Status without the heartbeat, so the 10-second heartbeat doesn't rebuild the UI.</summary>
    private static string Signature(EngineStatus? s)
    {
        if (s is null) return "";
        var beat = s.Heartbeat;
        s.Heartbeat = default;
        var json = JsonSerializer.Serialize(s, Json.Options);
        s.Heartbeat = beat;
        return json;
    }

    /// <summary>Mouse list; only re-renders when something was connected or removed.</summary>
    public void RefreshDevices()
    {
        var now = Devices.Mice();
        if (now.SequenceEqual(Mice)) return;
        Mice = now;
        Notify();
    }

    public bool HasSteelSeries => Mice.Any(m => m.IsSteelSeries);

    private void ReadResult()
    {
        var r = IpcFiles.Read<EngineResult>(ClicksmithPaths.Result);
        if (r is null || r.RequestId != _pendingRequest) return;
        LastResult = r;
        if (r.Kind == "identified") IdentifyStarted = null;
        Notify();
    }

    public HealthState Health => HealthModel.Evaluate(EngineRunning, Status?.HookActive == true,
        Status?.EmergencyPaused == true, Config.Settings.Paused);

    public string HealthTitle => Health switch
    {
        HealthState.Ready => "Forging clicks",
        HealthState.Paused => "Paused",
        HealthState.EmergencyPaused => "Emergency pause",
        HealthState.HookUnavailable => "Engine needs a restart",
        _ => Config.Settings.EngineEnabled ? "Engine stopped" : "Engine off"
    };

    public string HealthDetail => Health switch
    {
        HealthState.Ready => $"{Plural(Config.ActiveProfile.Mappings.Count(m => m.Enabled), "button")} active · {Config.ActiveProfile.Name}",
        HealthState.Paused => "Mappings are paused",
        HealthState.EmergencyPaused => "Press Resume to continue",
        HealthState.HookUnavailable => "Windows stopped sending mouse events to Clicksmith",
        _ => "Start the engine to use your buttons"
    };

    public static string Plural(int n, string word) => n == 1 ? $"1 {word}" : $"{n} {word}s";

    // ---------- Configuration ----------

    /// <summary>Edits a copy; saves only if it validates (the previous file is backed up first).</summary>
    public bool Update(Action<ClicksmithConfig> mutate)
    {
        var c = Config.Clone();
        mutate(c);
        try
        {
            ConfigStore.Save(c);
            Config = c;
            SaveError = null;
            LoadError = null;
            Notify();
            return true;
        }
        catch (Exception e) when (e is ConfigException or IOException or UnauthorizedAccessException)
        {
            SaveError = e.Message;
            Notify();
            return false;
        }
    }

    public string? Validate(Action<ClicksmithConfig> mutate)
    {
        var c = Config.Clone();
        mutate(c);
        try { ConfigCodec.Validate(c); return null; }
        catch (ConfigException e) { return e.Message; }
    }

    public Profile ActiveProfile => Config.ActiveProfile;

    public void Upsert(ButtonMapping m) => Update(c =>
    {
        if (m.Button != ClickButton.Key) m.TriggerVk = 0;
        var list = c.ActiveProfile.Mappings;
        var i = list.FindIndex(x => x.Id == m.Id);
        if (i >= 0) list[i] = Clone(m); else list.Add(Clone(m));
    });

    public void Delete(ButtonMapping m)
    {
        var profileId = Config.ActiveProfileId;
        if (Update(c => c.ActiveProfile.Mappings.RemoveAll(x => x.Id == m.Id)))
        {
            LastDeleted = (profileId, Clone(m));
            Notify();
        }
    }

    public void UndoDelete()
    {
        if (LastDeleted is not { } d) return;
        LastDeleted = null;
        Update(c =>
        {
            var p = c.Profiles.FirstOrDefault(x => x.Id == d.ProfileId) ?? c.ActiveProfile;
            if (p.Mappings.All(x => x.Trigger != d.Mapping.Trigger)) p.Mappings.Add(d.Mapping);
        });
    }

    public void DismissUndo() { LastDeleted = null; Notify(); }

    public void SetEnabled(ButtonMapping m, bool on) => Update(c =>
    {
        var x = c.ActiveProfile.Mappings.FirstOrDefault(y => y.Id == m.Id);
        if (x is not null) x.Enabled = on;
    });

    public void SetOverrideEnabled(Guid mappingId, Guid overrideId, bool on) => Update(c =>
    {
        var o = c.ActiveProfile.Mappings.FirstOrDefault(y => y.Id == mappingId)?.Overrides.FirstOrDefault(z => z.Id == overrideId);
        if (o is not null) o.Enabled = on;
    });

    public void RemoveApp(string app) => Update(c =>
    {
        foreach (var m in c.ActiveProfile.Mappings) m.Overrides.RemoveAll(o => o.App.Equals(app, StringComparison.OrdinalIgnoreCase));
        c.ActiveProfile.Mappings.RemoveAll(m => m.DefaultAction is null && m.Overrides.Count == 0 && !m.HasLongPress);
    });

    public void SetPaused(bool paused) => Update(c => c.Settings.Paused = paused);

    // ---------- Profiles (2.3) ----------

    public void ActivateProfile(Guid id) => Update(c => { if (c.Profiles.Any(p => p.Id == id)) c.ActiveProfileId = id; });

    public void AddProfile(string name) => Update(c => c.Profiles.Add(new Profile { Name = name.Trim() }));

    public void DuplicateProfile(Guid id) => Update(c =>
    {
        var src = c.Profiles.First(p => p.Id == id);
        var copy = JsonSerializer.Deserialize<Profile>(JsonSerializer.Serialize(src, Json.Options), Json.Options)!;
        copy.Id = Guid.NewGuid();
        copy.Name = (src.Name.Length > 34 ? src.Name[..34] : src.Name) + " copy";
        foreach (var m in copy.Mappings) { m.Id = Guid.NewGuid(); foreach (var o in m.Overrides) o.Id = Guid.NewGuid(); }
        c.Profiles.Add(copy);
    });

    public void RenameProfile(Guid id, string name) => Update(c => c.Profiles.First(p => p.Id == id).Name = name.Trim());

    public void DeleteProfile(Guid id) => Update(c =>
    {
        if (c.Profiles.Count <= 1) return;
        c.Profiles.RemoveAll(p => p.Id == id);
        if (c.Profiles.All(p => p.Id != c.ActiveProfileId)) c.ActiveProfileId = c.Profiles[0].Id;
    });

    public bool ReplaceConfig(ClicksmithConfig imported)
    {
        imported.Settings.OnboardingCompleted = true;
        return Update(c =>
        {
            c.Settings = imported.Settings;
            c.Profiles = imported.Profiles;
            c.ActiveProfileId = imported.ActiveProfileId;
        });
    }

    // ---------- Engine requests (2.1 identify + test mode) ----------

    public void Send(RequestKind kind)
    {
        var req = new EngineRequest { Kind = kind };
        if (kind is RequestKind.Identify or RequestKind.StartTest) _pendingRequest = req.Id;
        if (kind == RequestKind.Identify) { IdentifyStarted = DateTimeOffset.UtcNow; LastResult = null; }
        if (kind == RequestKind.StartTest) { TestStarted = DateTimeOffset.UtcNow; LastResult = null; }
        if (kind == RequestKind.StopTest) TestStarted = null;
        try { IpcFiles.Write(ClicksmithPaths.Request, req); }
        catch (Exception e) { EngineMessage = "Couldn't reach the engine: " + e.Message; }
        Notify();
    }

    public void CancelIdentify() { IdentifyStarted = null; Notify(); }

    /// <summary>The trigger found by the last "Find a button", if any.</summary>
    public Trigger? Identified => LastResult is { Kind: "identified", Button: { } b } r ? new Trigger(b, b == ClickButton.Key ? r.Vk : (ushort)0) : null;

    public ButtonMapping? MappingFor(Trigger t) => ActiveProfile.Mappings.FirstOrDefault(m => m.Trigger == t);

    public void CompleteOnboarding() => Update(c => c.Settings.OnboardingCompleted = true);

    public async void RestartEngine()
    {
        EngineMessage = null;
        Notify();
        var message = await System.Threading.Tasks.Task.Run(() => Engine.Restart());
        EngineMessage = message;
        RefreshStatus(notify: true);
        Notify();
    }

    public void StartEngine()
    {
        EngineMessage = Engine.Start();
        Notify();
    }

    public static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Json.Options), Json.Options)!;
}
