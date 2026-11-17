using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;
using Clicksmith.Core;

namespace Clicksmith.Engine;

/// <summary>Hidden top-level window (so it receives power broadcasts) that owns the engine.
/// Everything — hook callbacks, timers, file events, foreground changes — runs on this one
/// thread, so no locks are needed and the hook path never waits.</summary>
internal sealed class EngineHost : Form
{
    private const int EmergencyHotKeyId = 0x434C;

    private readonly MouseHook _hook = new();
    private readonly ActionSender _sender = new();
    private readonly Dictionary<ClickButton, LongPressTracker<ButtonAction>> _pending = new();
    private readonly HashSet<ClickButton> _swallowedDown = new();
    private readonly Timer _longPressTimer = new() { Interval = 15 };
    private readonly Timer _reloadDebounce = new() { Interval = 150 };
    private readonly Timer _statusDebounce = new() { Interval = 250 };
    private readonly Timer _heartbeat = new() { Interval = 10_000 };
    private readonly Timer _recoveryTimer = new();
    private readonly Timer _hookRefresh = new() { Interval = 15 * 60_000 };
    private readonly EngineStatus _status = new();
    private readonly Native.WinEventProc _foregroundProc;

    private ClicksmithConfig _config = ClicksmithConfig.CreateDefault();
    private CompiledConfig _compiled;
    private PressGuard _guard = new(150);
    private FileSystemWatcher? _watcher;
    private IntPtr _foregroundHook;
    private string? _foregroundApp;
    private bool _emergencyPaused;
    private bool _hotKeyRegistered;
    private Guid? _identifyRequest;
    private DateTimeOffset _identifyUntil;
    private Guid? _testRequest;
    private DateTimeOffset _testUntil;
    private Guid _lastRequest;

    public EngineHost()
    {
        ShowInTaskbar = false;
        FormBorderStyle = FormBorderStyle.None;
        Text = "Clicksmith Engine";
        _compiled = new CompiledConfig(_config);
        _foregroundProc = OnForeground;
        _longPressTimer.Tick += (_, _) => OnLongPressTick();
        _reloadDebounce.Tick += (_, _) => { _reloadDebounce.Stop(); LoadConfig(); };
        _statusDebounce.Tick += (_, _) => { _statusDebounce.Stop(); WriteStatus(); };
        _heartbeat.Tick += (_, _) => WriteStatus();
        _recoveryTimer.Tick += (_, _) => { _recoveryTimer.Stop(); Recover("wake"); };
        _hookRefresh.Tick += (_, _) => RefreshHook();
    }

    // Keep the window hidden while still creating its handle.
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

    private void Start()
    {
        ClicksmithPaths.EnsureDirectories();
        _status.Pid = Environment.ProcessId;
        _status.StartedAt = DateTimeOffset.UtcNow;
        LoadConfig();
        _hook.Handler = OnButton;
        InstallHook();
        Native.WTSRegisterSessionNotification(Handle, 0);
        _foregroundHook = Native.SetWinEventHook(Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, _foregroundProc, 0, 0, Native.WINEVENT_OUTOFCONTEXT);
        UpdateForeground(Native.GetForegroundWindow());
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
        _hookRefresh.Start();
        Log.Write($"Clicksmith Engine {ClicksmithInfo.Version} started");
        WriteStatus();
    }

    // ---------- Configuration ----------

    private void LoadConfig()
    {
        try
        {
            _config = ConfigStore.Load();
            _status.LastError = null;
        }
        catch (Exception e)
        {
            // Keep running with the last good configuration.
            _status.LastError = "Configuration not applied: " + e.Message;
            Log.Write(_status.LastError);
        }
        _compiled = new CompiledConfig(_config);
        _guard = new PressGuard(_config.Settings.DuplicateGuardMs);
        CancelPending();
        UpdateHotKey();
        ScheduleStatus();
    }

    private void UpdateHotKey()
    {
        var want = _config.Settings.EmergencyHotKeyEffective;
        if (want && !_hotKeyRegistered)
        {
            // Ctrl+Alt+Shift+F12: pauses every mapping and every blocked button.
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

    // ---------- Hot path ----------

    private bool Active => !_config.Settings.Paused && !_emergencyPaused && _config.Settings.EngineEnabled;

    private bool OnButton(ClickButton button, bool down)
    {
        var now = Environment.TickCount64;

        if (down && _identifyRequest is { } id && !ClickButtons.IsPrimary(button))
        {
            if (DateTimeOffset.UtcNow <= _identifyUntil)
            {
                _identifyRequest = null;
                _swallowedDown.Add(button);
                var identified = new EngineResult
                {
                    RequestId = id, Kind = "identified", Button = button, App = _foregroundApp,
                    Summary = ClickButtons.Name(button)
                };
                BeginInvoke(new Action(() => WriteResult(identified)));
                return true;
            }
            _identifyRequest = null;
        }

        if (!down)
        {
            if (_pending.Remove(button, out var tracker))
            {
                if (tracker.Release(now) is { } action && _guard.Allow(button, now)) _sender.Enqueue(action, _foregroundApp);
                _swallowedDown.Remove(button);
                StopLongPressTimerIfIdle();
                return true;
            }
            return _swallowedDown.Remove(button);
        }

        var testing = _testRequest is not null && DateTimeOffset.UtcNow <= _testUntil;
        if (!Active && !testing) return false;
        var resolved = _compiled.Resolve(button, _foregroundApp);

        if (testing)
        {
            var result = TestResult(button, resolved);
            BeginInvoke(new Action(() => WriteResult(result)));
            if (resolved is { Block: true }) { _swallowedDown.Add(button); return true; }
            return false; // nothing is executed in test mode
        }

        if (resolved is null) return false;

        if (resolved.Long is not null)
        {
            var t = new LongPressTracker<ButtonAction>(resolved.ThresholdMs, resolved.Short, resolved.Long);
            t.Press(now);
            _pending[button] = t;
            _swallowedDown.Add(button);
            if (!_longPressTimer.Enabled) _longPressTimer.Start();
            return true;
        }

        if (resolved.Short is { } shortAction && _guard.Allow(button, now)) _sender.Enqueue(shortAction, _foregroundApp);
        if (resolved.Block) { _swallowedDown.Add(button); return true; }
        return false;
    }

    private void OnLongPressTick()
    {
        var now = Environment.TickCount64;
        foreach (var (button, tracker) in _pending)
        {
            if (tracker.Tick(now) is { } action && _guard.Allow(button, now)) _sender.Enqueue(action, _foregroundApp);
        }
        StopLongPressTimerIfIdle();
    }

    private void StopLongPressTimerIfIdle()
    {
        var anyWaiting = false;
        foreach (var t in _pending.Values) if (t.IsPending) { anyWaiting = true; break; }
        if (!anyWaiting && _pending.Count == 0) _longPressTimer.Stop();
    }

    /// <summary>Pause, profile change, app switch or emergency: drop pending presses without firing.
    /// Their release is still swallowed, so no half-click reaches the app.</summary>
    private void CancelPending()
    {
        foreach (var t in _pending.Values) t.Cancel();
        _pending.Clear();
        _longPressTimer.Stop();
    }

    private EngineResult TestResult(ClickButton button, ResolvedButton? r)
    {
        var name = ClickButtons.Name(button);
        var where = _foregroundApp is null ? "" : $" in {_foregroundApp}";
        string summary;
        if (r is null) summary = $"{name}{where}: not mapped — Windows handles it normally.";
        else if (r.Long is not null) summary = $"{name}{where}: tap → {r.Short?.Display ?? "nothing"} · hold {r.ThresholdMs} ms → {r.Long.Display}";
        else if (r.Short is not null) summary = $"{name}{where}: would run {r.Short.Display}" + (r.Block ? " (normal action blocked)" : "");
        else summary = $"{name}{where}: disabled — the normal action is blocked.";
        return new EngineResult
        {
            RequestId = _testRequest ?? Guid.Empty, Kind = "test", Button = button, App = _foregroundApp,
            MappingLabel = r?.Label, ShortDisplay = r?.Short?.Display, LongDisplay = r?.Long?.Display,
            WouldBlock = r?.Block ?? false, Summary = summary
        };
    }

    // ---------- Foreground app ----------

    private void OnForeground(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time) => UpdateForeground(hwnd);

    private void UpdateForeground(IntPtr hwnd)
    {
        var app = Native.ProcessExeName(hwnd);
        if (app == _foregroundApp) return;
        var previous = _foregroundApp;
        _foregroundApp = app;
        if (app is not null && app != "clicksmith.exe" && app != "clicksmith.engine.exe") _status.LastExternalApp = app;
        if (_compiled.ProfileDiffers(previous, app)) CancelPending();
        ScheduleStatus();
    }

    // ---------- Requests from the app ----------

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
            HandleRequest();
        }
    }

    private void HandleRequest()
    {
        var req = IpcFiles.Read<EngineRequest>(ClicksmithPaths.Request);
        if (req is null || !req.IsFresh || req.Id == _lastRequest) return;
        _lastRequest = req.Id;
        switch (req.Kind)
        {
            case RequestKind.Identify:
                _identifyRequest = req.Id;
                _identifyUntil = DateTimeOffset.UtcNow.AddSeconds(20);
                break;
            case RequestKind.StartTest:
                CancelPending();
                _testRequest = req.Id;
                _testUntil = DateTimeOffset.UtcNow.AddSeconds(60);
                break;
            case RequestKind.StopTest:
                _testRequest = null;
                break;
            case RequestKind.Resume:
                _emergencyPaused = false;
                Log.Write("Resumed after emergency pause");
                break;
            case RequestKind.Reload:
                LoadConfig();
                break;
            case RequestKind.Quit:
                Log.Write("Quit requested");
                Close();
                return;
        }
        ScheduleStatus();
    }

    private void WriteResult(EngineResult r)
    {
        try { IpcFiles.Write(ClicksmithPaths.Result, r); } catch (Exception e) { Log.Write("Result write failed: " + e.Message); }
    }

    // ---------- Sleep, wake, unlock ----------

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case Native.WM_HOTKEY when m.WParam.ToInt32() == EmergencyHotKeyId:
                _emergencyPaused = true;
                CancelPending();
                Log.Write("Emergency pause (Ctrl+Alt+Shift+F12)");
                ScheduleStatus();
                break;
            case Native.WM_POWERBROADCAST:
                var evt = m.WParam.ToInt32();
                if (evt == Native.PBT_APMSUSPEND)
                {
                    CancelPending();
                    _swallowedDown.Clear();
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

    /// <summary>Windows can silently drop a hook after sleep or a slow moment. A fresh hook,
    /// hotkey and foreground lookup make buttons work again without a restart.</summary>
    private void Recover(string reason)
    {
        CancelPending();
        _swallowedDown.Clear();
        InstallHook();
        if (_hotKeyRegistered) { Native.UnregisterHotKey(Handle, EmergencyHotKeyId); _hotKeyRegistered = false; }
        UpdateHotKey();
        UpdateForeground(Native.GetForegroundWindow());
        _status.LastRecovery = DateTimeOffset.UtcNow;
        Log.Write($"Recovered after {reason}: hook {(_hook.IsInstalled ? "active" : "FAILED")}");
        WriteStatus();
    }

    private void RefreshHook()
    {
        if (_pending.Count == 0 && _swallowedDown.Count == 0) InstallHook();
    }

    private void InstallHook()
    {
        var ok = _hook.Reinstall();
        _status.HookActive = ok;
        if (ok) _status.HookInstalledAt = DateTimeOffset.UtcNow;
        else
        {
            _status.LastError = "Windows refused the mouse hook. Restart the engine.";
            Log.Write(_status.LastError);
        }
    }

    // ---------- Status ----------

    private void ScheduleStatus()
    {
        _statusDebounce.Stop();
        _statusDebounce.Start();
    }

    private void WriteStatus()
    {
        _status.Heartbeat = DateTimeOffset.UtcNow;
        _status.Paused = _config.Settings.Paused;
        _status.EmergencyPaused = _emergencyPaused;
        _status.ForegroundApp = _foregroundApp;
        _status.ActiveProfile = _compiled.ProfileName;
        _status.ActiveMappings = _compiled.ActiveMappings;
        _status.TestMode = _testRequest is not null && DateTimeOffset.UtcNow <= _testUntil;
        try { IpcFiles.Write(ClicksmithPaths.Status, _status); }
        catch (Exception e) { Log.Write("Status write failed: " + e.Message); }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _hook.Uninstall();
        if (_foregroundHook != IntPtr.Zero) Native.UnhookWinEvent(_foregroundHook);
        if (_hotKeyRegistered) Native.UnregisterHotKey(Handle, EmergencyHotKeyId);
        Native.WTSUnRegisterSessionNotification(Handle);
        _watcher?.Dispose();
        _sender.Dispose();
        _status.HookActive = false;
        _status.Heartbeat = DateTimeOffset.MinValue;
        try { IpcFiles.Write(ClicksmithPaths.Status, _status); } catch { }
        Log.Write("Clicksmith Engine stopped");
        base.OnFormClosed(e);
    }
}
