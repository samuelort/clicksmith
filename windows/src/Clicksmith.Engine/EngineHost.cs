using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;
using Clicksmith.Core;

namespace Clicksmith.Engine;

/// <summary>Control thread: a hidden top-level window (so it receives power and session messages)
/// that owns configuration, the app in front, requests from the settings app, the emergency
/// shortcut, sleep/wake recovery and status. It never handles mouse input itself — that is the
/// <see cref="InputThread"/> — so nothing here can slow the hook down.</summary>
internal sealed class EngineHost : Form
{
    private const int EmergencyHotKeyId = 0x434C;

    private readonly ActionSender _sender = new();
    private readonly InputThread _input;
    private readonly System.Windows.Forms.Timer _reloadDebounce = new() { Interval = 150 };
    private readonly System.Windows.Forms.Timer _statusDebounce = new() { Interval = 250 };
    private readonly System.Windows.Forms.Timer _heartbeat = new() { Interval = 10_000 };
    private readonly System.Windows.Forms.Timer _recoveryTimer = new();
    private readonly System.Windows.Forms.Timer _identifyTimeout = new() { Interval = 20_000 };
    private readonly EngineStatus _status = new();
    private readonly Native.WinEventProc _foregroundProc;
    private readonly Dictionary<uint, string?> _exeCache = new();

    private ClicksmithConfig _config = ClicksmithConfig.CreateDefault();
    private CompiledConfig _compiled;
    private FileSystemWatcher? _watcher;
    private IntPtr _foregroundHook;
    private string? _foregroundApp;
    private bool _emergencyPaused;
    private bool _hotKeyRegistered;
    private Guid? _identifyId;
    private long _identifyUntil;
    private Guid? _testId;
    private long _testUntil;
    private Guid _lastRequest;
    private int _loadGeneration;

    public EngineHost()
    {
        ShowInTaskbar = false;
        FormBorderStyle = FormBorderStyle.None;
        Text = "Clicksmith Engine";
        _compiled = new CompiledConfig(_config);
        _foregroundProc = OnForeground;
        _input = new InputThread(_sender)
        {
            Result = r => PostToControl(() => OnResult(r)),
            HookChanged = ok => PostToControl(() => OnHookChanged(ok)),
            ExtraKeysChanged = (ok, bad) => PostToControl(() => { _status.ExtraKeys = ok; _status.ExtraKeysUnavailable = bad; ScheduleStatus(); })
        };
        _sender.Skipped = message => PostToControl(() =>
        {
            _status.LastSkipped = message;
            _status.LastSkippedAt = DateTimeOffset.UtcNow;
            ScheduleStatus();
        });
        _reloadDebounce.Tick += (_, _) => { _reloadDebounce.Stop(); LoadConfigAsync(); };
        _statusDebounce.Tick += (_, _) => { _statusDebounce.Stop(); WriteStatus(); };
        _heartbeat.Tick += (_, _) => WriteStatus();
        _recoveryTimer.Tick += (_, _) => { _recoveryTimer.Stop(); Recover("wake"); };
        _identifyTimeout.Tick += (_, _) => { _identifyTimeout.Stop(); _identifyId = null; Publish(); };
    }

    protected override void SetVisibleCore(bool value)
    {
        if (!IsHandleCreated) CreateHandle();
        base.SetVisibleCore(false);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Start();
    }

    private void PostToControl(Action a)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try { BeginInvoke(a); } catch (InvalidOperationException) { }
    }

    private void Start()
    {
        ClicksmithPaths.EnsureDirectories();
        _status.Pid = Environment.ProcessId;
        _status.StartedAt = DateTimeOffset.UtcNow;
        LoadConfigNow();
        Native.WTSRegisterSessionNotification(Handle, 0);
        _foregroundHook = Native.SetWinEventHook(Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, _foregroundProc, 0, 0, Native.WINEVENT_OUTOFCONTEXT);
        UpdateForeground(Native.GetForegroundWindow());
        _input.Start(BuildSnapshot());
        _watcher = new FileSystemWatcher(ClicksmithPaths.Root)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime,
            SynchronizingObject = this,
            EnableRaisingEvents = true
        };
        _watcher.Changed += OnFileEvent;
        _watcher.Created += OnFileEvent;
        _watcher.Renamed += (s, e) => OnFileEvent(s, e);
        _heartbeat.Start();
        Log.Write($"Clicksmith Engine {ClicksmithInfo.Version} started");
        WriteStatus();
    }

    // ---------- Snapshot for the input thread ----------

    private bool Active => !_config.Settings.Paused && !_emergencyPaused && _config.Settings.EngineEnabled;

    private RouterSnapshot BuildSnapshot() => new()
    {
        Config = _compiled,
        Active = Active,
        App = _foregroundApp,
        IdentifyId = _identifyId,
        IdentifyUntilMs = _identifyUntil,
        TestId = _testId,
        TestUntilMs = _testUntil
    };

    private void Publish() => _input.Publish(BuildSnapshot());

    // ---------- Configuration (parsed off the control thread) ----------

    private void LoadConfigNow()
    {
        try { _config = ConfigStore.Load(); _status.LastError = null; }
        catch (Exception e) { _status.LastError = "Configuration not applied: " + e.Message; Log.Write(_status.LastError); }
        _compiled = new CompiledConfig(_config);
        UpdateHotKey();
    }

    private void LoadConfigAsync()
    {
        var generation = ++_loadGeneration;
        Task.Run(() =>
        {
            try { var c = ConfigStore.Load(); return (Config: c, Compiled: new CompiledConfig(c), Error: (string?)null); }
            catch (Exception e) { return (Config: (ClicksmithConfig?)null, Compiled: (CompiledConfig?)null, Error: e.Message); }
        }).ContinueWith(t => PostToControl(() =>
        {
            if (generation != _loadGeneration) return; // a newer load is on its way
            var (config, compiled, error) = t.Result;
            if (config is null || compiled is null)
            {
                // Keep running with the last good configuration.
                _status.LastError = "Configuration not applied: " + error;
                Log.Write(_status.LastError);
            }
            else
            {
                _config = config;
                _compiled = compiled;
                _status.LastError = null;
                UpdateHotKey();
                _input.Cancel();
            }
            Publish();
            ScheduleStatus();
        }), TaskScheduler.Default);
    }

    private void UpdateHotKey()
    {
        var want = _config.Settings.EmergencyHotKeyEffective;
        if (want && !_hotKeyRegistered)
        {
            // Ctrl+Alt+Shift+F12 pauses every mapping and every blocked button.
            _hotKeyRegistered = Native.RegisterHotKey(Handle, EmergencyHotKeyId,
                Native.MOD_CONTROL | Native.MOD_ALT | Native.MOD_SHIFT | Native.MOD_NOREPEAT, Native.VK_F12);
            if (!_hotKeyRegistered) Log.Write("Emergency shortcut unavailable (another app uses Ctrl+Alt+Shift+F12)");
        }
        else if (!want && _hotKeyRegistered)
        {
            Native.UnregisterHotKey(Handle, EmergencyHotKeyId);
            _hotKeyRegistered = false;
        }
        _status.EmergencyHotKeyRegistered = _hotKeyRegistered;
    }

    // ---------- App in front ----------

    private void OnForeground(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time) => UpdateForeground(hwnd);

    private void UpdateForeground(IntPtr hwnd)
    {
        var app = ExeName(hwnd);
        if (app == _foregroundApp) return;
        var previous = _foregroundApp;
        _foregroundApp = app;
        if (app is not null && app != "clicksmith.exe" && app != "clicksmith.engine.exe") _status.LastExternalApp = app;
        if (_compiled.ProfileDiffers(previous, app)) _input.Cancel();
        Publish();
        ScheduleStatus();
    }

    /// <summary>Process lookups are cached per process id, so switching windows stays cheap.</summary>
    private string? ExeName(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return null;
        Native.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0) return null;
        if (_exeCache.TryGetValue(pid, out var cached)) return cached;
        if (_exeCache.Count > 256) _exeCache.Clear();
        var name = Native.ProcessExeName(hwnd);
        _exeCache[pid] = name;
        return name;
    }

    // ---------- Requests from the settings app ----------

    private void OnFileEvent(object sender, FileSystemEventArgs e)
    {
        var name = Path.GetFileName(e.FullPath);
        if (string.Equals(name, "config.json", StringComparison.OrdinalIgnoreCase))
        {
            _reloadDebounce.Stop();
            _reloadDebounce.Start();
        }
        else if (string.Equals(name, "engine-request.json", StringComparison.OrdinalIgnoreCase))
        {
            // Read off the control thread; handle on it.
            Task.Run(() => IpcFiles.Read<EngineRequest>(ClicksmithPaths.Request))
                .ContinueWith(t => PostToControl(() => HandleRequest(t.Result)), TaskScheduler.Default);
        }
    }

    private void HandleRequest(EngineRequest? req)
    {
        if (req is null || !req.IsFresh || req.Id == _lastRequest) return;
        _lastRequest = req.Id;
        var now = Environment.TickCount64;
        switch (req.Kind)
        {
            case RequestKind.Identify:
                _identifyId = req.Id;
                _identifyUntil = now + 20_000;
                _identifyTimeout.Stop();
                _identifyTimeout.Start();
                break;
            case RequestKind.StartTest:
                _input.Cancel();
                _testId = req.Id;
                _testUntil = now + 60_000;
                break;
            case RequestKind.StopTest:
                _testId = null;
                break;
            case RequestKind.Resume:
                _emergencyPaused = false;
                Log.Write("Resumed after emergency pause");
                break;
            case RequestKind.Reload:
                LoadConfigAsync();
                break;
            case RequestKind.Quit:
                Log.Write("Quit requested");
                Close();
                return;
        }
        Publish();
        ScheduleStatus();
    }

    private void OnResult(EngineResult r)
    {
        if (r.Kind == "identified")
        {
            if (r.RequestId != _identifyId) return; // a second press before the snapshot updated
            _identifyId = null;
            _identifyTimeout.Stop();
            Publish();
        }
        BackgroundIo.Write(ClicksmithPaths.Result, JsonSerializer.SerializeToUtf8Bytes(r, Json.Options));
    }

    private void OnHookChanged(bool ok)
    {
        _status.HookActive = ok;
        if (ok) _status.HookInstalledAt = DateTimeOffset.UtcNow;
        else
        {
            _status.LastError = "Windows refused the mouse hook. Restart the engine.";
            Log.Write(_status.LastError);
        }
        ScheduleStatus();
    }

    // ---------- Emergency, sleep, wake, unlock ----------

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case Native.WM_HOTKEY when m.WParam.ToInt32() == EmergencyHotKeyId:
                _emergencyPaused = true;
                _input.Cancel();
                Publish();
                Log.Write("Emergency pause (Ctrl+Alt+Shift+F12)");
                ScheduleStatus();
                break;
            case Native.WM_POWERBROADCAST:
                var evt = m.WParam.ToInt32();
                if (evt == Native.PBT_APMSUSPEND)
                {
                    _input.Cancel();
                    _status.LastSleep = DateTimeOffset.UtcNow;
                    WriteStatus();
                }
                else if (evt is Native.PBT_APMRESUMEAUTOMATIC or Native.PBT_APMRESUMESUSPEND)
                {
                    _status.LastResume = DateTimeOffset.UtcNow;
                    _recoveryTimer.Interval = Math.Clamp(_config.Settings.WakeRecoveryDelayMs, 500, 10_000);
                    _recoveryTimer.Stop();
                    _recoveryTimer.Start();
                }
                break;
            case Native.WM_WTSSESSION_CHANGE when m.WParam.ToInt32() == Native.WTS_SESSION_UNLOCK:
                Recover("unlock");
                break;
        }
        base.WndProc(ref m);
    }

    /// <summary>Windows can silently drop a hook after sleep. A fresh hook, fresh hot keys and a
    /// fresh look at the app in front make buttons work again without a restart.</summary>
    private void Recover(string reason)
    {
        _input.Reinstall();
        if (_hotKeyRegistered) { Native.UnregisterHotKey(Handle, EmergencyHotKeyId); _hotKeyRegistered = false; }
        UpdateHotKey();
        _exeCache.Clear();
        _foregroundApp = null;
        UpdateForeground(Native.GetForegroundWindow());
        _status.LastRecovery = DateTimeOffset.UtcNow;
        Log.Write($"Refreshed after {reason}");
        WriteStatus();
    }

    // ---------- Status ----------

    private void ScheduleStatus()
    {
        _statusDebounce.Stop();
        _statusDebounce.Start();
    }

    private byte[] StatusBytes()
    {
        _status.Heartbeat = DateTimeOffset.UtcNow;
        _status.Paused = _config.Settings.Paused;
        _status.EmergencyPaused = _emergencyPaused;
        _status.ForegroundApp = _foregroundApp;
        _status.ActiveProfile = _compiled.ProfileName;
        _status.ActiveMappings = _compiled.ActiveMappings;
        _status.TestMode = _testId is not null && Environment.TickCount64 <= _testUntil;
        return JsonSerializer.SerializeToUtf8Bytes(_status, Json.Options);
    }

    private void WriteStatus() => BackgroundIo.Write(ClicksmithPaths.Status, StatusBytes());

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _input.Dispose();
        if (_foregroundHook != IntPtr.Zero) Native.UnhookWinEvent(_foregroundHook);
        if (_hotKeyRegistered) Native.UnregisterHotKey(Handle, EmergencyHotKeyId);
        Native.WTSUnRegisterSessionNotification(Handle);
        _watcher?.Dispose();
        _sender.Dispose();
        _status.HookActive = false;
        StatusBytes();
        _status.Heartbeat = DateTimeOffset.MinValue;
        BackgroundIo.WriteNow(ClicksmithPaths.Status, JsonSerializer.SerializeToUtf8Bytes(_status, Json.Options));
        Log.Write("Clicksmith Engine stopped");
        System.Threading.Thread.Sleep(100); // let the log line out
        base.OnFormClosed(e);
    }
}
