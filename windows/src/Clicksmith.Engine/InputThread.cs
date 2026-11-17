using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Forms;
using Clicksmith.Core;

namespace Clicksmith.Engine;

/// <summary>The input thread: owns the low-level mouse hook, the extra-key hot keys (F13–F24),
/// the long-press timer and the <see cref="ButtonRouter"/>. It does no file I/O, logging or
/// process lookups — it reads an immutable <see cref="RouterSnapshot"/> published by the control
/// thread and hands actions to <see cref="ActionSender"/>. That keeps the hook callback fast enough
/// that Windows never has a reason to drop it.</summary>
internal sealed class InputThread : IDisposable
{
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new();
    private readonly ActionSender _sender;
    private InputWindow? _window;
    private RouterSnapshot _snapshot = new();

    /// <summary>Called on the input thread; must only post (never block).</summary>
    public Action<EngineResult>? Result { get; set; }
    public Action<bool>? HookChanged { get; set; }
    public Action<List<string>, List<string>>? ExtraKeysChanged { get; set; }

    public InputThread(ActionSender sender)
    {
        _sender = sender;
        _thread = new Thread(Run) { IsBackground = true, Name = "Clicksmith input", Priority = ThreadPriority.AboveNormal };
        _thread.SetApartmentState(ApartmentState.STA);
    }

    public RouterSnapshot Snapshot => Volatile.Read(ref _snapshot);

    public void Start(RouterSnapshot first)
    {
        Volatile.Write(ref _snapshot, first);
        _thread.Start();
        _ready.Wait(5000);
    }

    public void Publish(RouterSnapshot s)
    {
        Volatile.Write(ref _snapshot, s);
        Post(w => w.SyncHotKeys());
    }

    public void Cancel() => Post(w => w.Router.Cancel());
    public void Reinstall() => Post(w => w.Reinstall());

    private void Post(Action<InputWindow> action)
    {
        var w = _window;
        if (w is null || w.IsDisposed || !w.IsHandleCreated) return;
        try { w.BeginInvoke(new Action(() => action(w))); } catch (InvalidOperationException) { }
    }

    private void Run()
    {
        _window = new InputWindow(this);
        _window.HandleCreatedOnce += () => _ready.Set();
        Application.Run(_window);
    }

    public void Dispose()
    {
        var w = _window;
        if (w is not null && w.IsHandleCreated && !w.IsDisposed)
        {
            try { w.Invoke(new Action(w.Close)); } catch { }
        }
        _thread.Join(2000);
    }

    /// <summary>Hidden window on the input thread (needed for the message loop and WM_HOTKEY).</summary>
    private sealed class InputWindow : Form
    {
        private readonly InputThread _owner;
        private readonly MouseHook _hook = new();
        private readonly System.Windows.Forms.Timer _timer = new() { Interval = 15 };
        private readonly System.Windows.Forms.Timer _refresh = new() { Interval = 15 * 60_000 };
        private readonly HashSet<ushort> _registered = new();
        private readonly HashSet<ushort> _unavailable = new();
        private readonly HashSet<ushort> _heldKeys = new();
        public ButtonRouter Router { get; } = new();
        public event Action? HandleCreatedOnce;

        public InputWindow(InputThread owner)
        {
            _owner = owner;
            ShowInTaskbar = false;
            FormBorderStyle = FormBorderStyle.None;
            Text = "Clicksmith Input";
            _timer.Tick += (_, _) => OnTick();
            _refresh.Tick += (_, _) => { if (Router.IsIdle && _heldKeys.Count == 0) InstallHook(); };
        }

        protected override void SetVisibleCore(bool value)
        {
            if (!IsHandleCreated) CreateHandle();
            base.SetVisibleCore(false);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            _hook.Handler = OnMouse;
            InstallHook();
            SyncHotKeys();
            _refresh.Start();
            HandleCreatedOnce?.Invoke();
        }

        // ---------- Hot path ----------

        private bool OnMouse(ClickButton button, bool down)
        {
            var s = _owner.Snapshot;
            var d = Router.OnInput(new Trigger(button), down, Environment.TickCount64, s);
            Apply(d, s);
            return d.Swallow;
        }

        private void Apply(Decision d, RouterSnapshot s)
        {
            if (d.Action is { } a) _owner._sender.Enqueue(a, s.App);
            if (d.Result is { } r) _owner.Result?.Invoke(r);
            if ((Router.HasPending || _heldKeys.Count > 0) && !_timer.Enabled) _timer.Start();
        }

        private void OnTick()
        {
            var now = Environment.TickCount64;
            var s = _owner.Snapshot;
            if (Router.Tick(now) is { } a) _owner._sender.Enqueue(a, s.App);
            if (_heldKeys.Count > 0)
            {
                // Hot keys only report the press; the release is read from the key state.
                foreach (var vk in new List<ushort>(_heldKeys))
                {
                    if ((Native.GetAsyncKeyState(vk) & 0x8000) != 0) continue;
                    _heldKeys.Remove(vk);
                    Apply(Router.OnInput(new Trigger(ClickButton.Key, vk), false, now, s), s);
                }
            }
            if (!Router.HasPending && _heldKeys.Count == 0) _timer.Stop();
        }

        // ---------- Extra buttons (F13–F24) ----------

        /// <summary>Registers the F-keys used by extra buttons, plus all of them while "Find a
        /// button" is waiting. Hot keys are system-wide but see only these keys.</summary>
        public void SyncHotKeys()
        {
            if (!IsHandleCreated) return;
            var s = _owner.Snapshot;
            var want = new HashSet<ushort>(s.Config.ExtraKeys);
            if (s.IdentifyId is not null && Environment.TickCount64 <= s.IdentifyUntilMs)
                for (var vk = ClickButtons.FirstExtraKey; vk <= ClickButtons.LastExtraKey; vk++) want.Add(vk);
            var changed = false;
            foreach (var vk in new List<ushort>(_registered))
            {
                if (want.Contains(vk)) continue;
                Native.UnregisterHotKey(Handle, vk);
                _registered.Remove(vk);
                changed = true;
            }
            foreach (var vk in want)
            {
                if (_registered.Contains(vk)) continue;
                if (Native.RegisterHotKey(Handle, vk, Native.MOD_NOREPEAT, vk)) { _registered.Add(vk); _unavailable.Remove(vk); }
                else _unavailable.Add(vk);
                changed = true;
            }
            _unavailable.IntersectWith(want);
            if (changed)
            {
                var ok = new List<string>(); var bad = new List<string>();
                foreach (var vk in s.Config.ExtraKeys) (_registered.Contains(vk) ? ok : bad).Add(KeyNames.Name(vk));
                _owner.ExtraKeysChanged?.Invoke(ok, bad);
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_HOTKEY)
            {
                var vk = (ushort)m.WParam.ToInt32();
                if (ClickButtons.IsExtraKey(vk) && _heldKeys.Add(vk))
                {
                    var s = _owner.Snapshot;
                    Apply(Router.OnInput(new Trigger(ClickButton.Key, vk), true, Environment.TickCount64, s), s);
                    if (!_timer.Enabled) _timer.Start();
                }
                return;
            }
            base.WndProc(ref m);
        }

        // ---------- Hook lifecycle ----------

        public void Reinstall()
        {
            Router.Reset();
            _heldKeys.Clear();
            _timer.Stop();
            InstallHook();
            // Re-register hot keys too: Windows can drop them across a session change.
            foreach (var vk in _registered) Native.UnregisterHotKey(Handle, vk);
            _registered.Clear();
            SyncHotKeys();
        }

        private void InstallHook() => _owner.HookChanged?.Invoke(_hook.Reinstall());

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _timer.Stop();
            _refresh.Stop();
            _hook.Uninstall();
            foreach (var vk in _registered) Native.UnregisterHotKey(Handle, vk);
            _registered.Clear();
            base.OnFormClosed(e);
        }
    }
}
