namespace Clicksmith.Core;

/// <summary>Everything the input thread needs, published as one immutable snapshot by the control
/// thread. Swapping a reference is atomic, so the input thread never waits for a lock.</summary>
public sealed class RouterSnapshot
{
    public CompiledConfig Config { get; init; } = new(ClicksmithConfig.CreateDefault());
    public bool Active { get; init; } = true;
    public string? App { get; init; }
    public Guid? IdentifyId { get; init; }
    public long IdentifyUntilMs { get; init; }
    public Guid? TestId { get; init; }
    public long TestUntilMs { get; init; }
}

/// <summary>What to do with one input event.</summary>
public readonly record struct Decision(bool Swallow, ButtonAction? Action, EngineResult? Result)
{
    public static readonly Decision PassThrough = new(false, null, null);
}

/// <summary>Turns physical presses into actions: mapping lookup, repeat guard, 2.2 short/long press,
/// 2.1 test mode and button identification. Pure logic — no Windows calls, no I/O, no allocation
/// beyond the occasional tracker — so it is fully covered by the self-tests with simulated presses.
/// Used from a single thread (the engine's input thread).</summary>
public sealed class ButtonRouter
{
    private readonly Dictionary<Trigger, LongPressTracker<ButtonAction>> _pending = new();
    private readonly HashSet<Trigger> _swallowedDown = new();
    private PressGuard _guard;
    private int _guardMs;
    private Guid _lastIdentified;
    private Guid _lastTest;

    public ButtonRouter(int guardMs = 150)
    {
        _guardMs = guardMs;
        _guard = new PressGuard(guardMs);
    }

    public bool HasPending => _pending.Count > 0;
    /// <summary>Nothing pending and no held press: safe to replace the hook.</summary>
    public bool IsIdle => _pending.Count == 0 && _swallowedDown.Count == 0;
    public IEnumerable<Trigger> PendingTriggers => _pending.Keys;
    /// <summary>A press is held back (or pending) and its release must be swallowed too.</summary>
    public bool IsHeld(Trigger t) => _swallowedDown.Contains(t) || _pending.ContainsKey(t);

    public Decision OnInput(Trigger t, bool down, long nowMs, RouterSnapshot s)
    {
        if (s.Config.Settings.DuplicateGuardMs != _guardMs)
        {
            _guardMs = s.Config.Settings.DuplicateGuardMs;
            _guard = new PressGuard(_guardMs);
        }

        if (!down) return OnRelease(t, nowMs, s);

        // 2.1: "Find a button" — the next press is reported and swallowed, nothing runs.
        if (s.IdentifyId is { } id && id != _lastIdentified && nowMs <= s.IdentifyUntilMs &&
            (t.IsKey || !ClickButtons.IsPrimary(t.Button)))
        {
            _lastIdentified = id;
            _swallowedDown.Add(t);
            return new Decision(true, null, new EngineResult
            {
                RequestId = id, Kind = "identified", Button = t.Button, Vk = t.Vk, App = s.App, Summary = t.ToString()
            });
        }

        var testing = s.TestId is not null && nowMs <= s.TestUntilMs;
        if (!s.Active && !testing) return Decision.PassThrough;
        var r = s.Config.Resolve(t, s.App);

        // 2.1: test mode shows what would happen and never runs anything.
        if (testing)
        {
            _lastTest = s.TestId!.Value;
            var result = TestResult(t, r, s);
            if (r is { Block: true }) { _swallowedDown.Add(t); return new Decision(true, null, result); }
            return new Decision(false, null, result);
        }

        if (r is null) return Decision.PassThrough;

        // 2.2: with a hold action, wait to see whether this is a tap or a hold.
        if (r.Long is not null)
        {
            if (_pending.ContainsKey(t)) return new Decision(true, null, null); // key auto-repeat
            var tracker = new LongPressTracker<ButtonAction>(r.ThresholdMs, r.Short, r.Long);
            tracker.Press(nowMs);
            _pending[t] = tracker;
            _swallowedDown.Add(t);
            return new Decision(true, null, null);
        }

        ButtonAction? action = r.Short is { } a && _guard.Allow(t, nowMs) ? a : null;
        if (r.Block) _swallowedDown.Add(t);
        return new Decision(r.Block, action, null);
    }

    private Decision OnRelease(Trigger t, long nowMs, RouterSnapshot s)
    {
        if (_pending.Remove(t, out var tracker))
        {
            _swallowedDown.Remove(t);
            var a = tracker.Release(nowMs);
            return new Decision(true, a is not null && _guard.Allow(t, nowMs) ? a : null, null);
        }
        return new Decision(_swallowedDown.Remove(t), null, null);
    }

    /// <summary>Call every ~15 ms while <see cref="HasPending"/>. Returns hold actions that are due.</summary>
    public ButtonAction? Tick(long nowMs)
    {
        foreach (var (t, tracker) in _pending)
        {
            if (tracker.Tick(nowMs) is { } a && _guard.Allow(t, nowMs)) return a; // at most one per tick
        }
        return null;
    }

    /// <summary>Pause, app/profile switch, emergency: drop pending presses without firing. Their
    /// release is still swallowed, so no half-click reaches the app.</summary>
    public void Cancel()
    {
        foreach (var t in _pending.Values) t.Cancel();
        _pending.Clear();
    }

    /// <summary>After sleep or a fresh hook: forget everything, including held buttons.</summary>
    public void Reset()
    {
        Cancel();
        _swallowedDown.Clear();
    }

    private static EngineResult TestResult(Trigger t, ResolvedButton? r, RouterSnapshot s)
    {
        var name = t.IsKey ? $"Extra button ({KeyNames.Name(t.Vk)})" : ClickButtons.Name(t.Button);
        var where = s.App is null ? "" : $" in {s.App}";
        string summary;
        if (r is null) summary = $"{name}{where}: not mapped — Windows handles it normally.";
        else if (r.Long is not null) summary = $"{name}{where}: tap → {r.Short?.Display ?? "nothing"} · hold {r.ThresholdMs} ms → {r.Long.Display}";
        else if (r.Short is not null) summary = $"{name}{where}: would run {r.Short.Display}" + (r.Block && !t.IsKey ? " (normal action blocked)" : "");
        else summary = $"{name}{where}: disabled — the normal action is blocked.";
        if (r?.Short?.Kind == ActionKind.TypeText || r?.Long?.Kind == ActionKind.TypeText)
            summary += " Text is never typed into password fields or sign-in prompts.";
        return new EngineResult
        {
            RequestId = s.TestId ?? Guid.Empty, Kind = "test", Button = t.Button, Vk = t.Vk, App = s.App,
            MappingLabel = r?.Label, ShortDisplay = r?.Short?.Display, LongDisplay = r?.Long?.Display,
            WouldBlock = r?.Block ?? false, Summary = summary
        };
    }
}
