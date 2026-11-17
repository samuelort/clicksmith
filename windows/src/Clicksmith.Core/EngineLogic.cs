namespace Clicksmith.Core;

public static class ClicksmithInfo
{
    public const string Version = "2.5.0-beta.1";
    public const string DisplayVersion = "2.5 Beta 1";
    public const string EngineProcessName = "Clicksmith.Engine";
    public const string AppProcessName = "Clicksmith";
    public const string Repository = "samuelort/clicksmith";
    /// <summary>Windows releases are tagged windows-vX.Y.Z so they never mix with macOS tags.</summary>
    public const string ReleaseTagPrefix = "windows-v";
    /// <summary>Marks Clicksmith's own injected input so the engine never reacts to it.</summary>
    public const long InjectedMarker = 0x434C4B53;
}

/// <summary>What the engine does with one physical button in the current app.</summary>
public sealed record ResolvedButton(Guid MappingId, string Label, ButtonAction? Short, ButtonAction? Long, int ThresholdMs, bool Block);

/// <summary>Resolved once per config load / app switch — never per click. Immutable, so the input
/// thread can read it while the control thread builds the next one.</summary>
public sealed class CompiledConfig
{
    private readonly Dictionary<Trigger, ButtonMapping> _map;
    public EngineSettings Settings { get; }
    public string ProfileName { get; }
    public int ActiveMappings => _map.Count;
    /// <summary>F13–F24 keys used by extra buttons in the active profile.</summary>
    public IReadOnlyList<ushort> ExtraKeys { get; }

    public CompiledConfig(ClicksmithConfig c)
    {
        Settings = c.Settings;
        var p = c.ActiveProfile;
        ProfileName = p.Name;
        _map = new Dictionary<Trigger, ButtonMapping>();
        foreach (var m in p.Mappings.Where(m => m.Enabled))
            _map.TryAdd(m.Trigger, m);
        ExtraKeys = _map.Keys.Where(t => t.IsKey).Select(t => t.Vk).Distinct().OrderBy(v => v).ToList();
    }

    public ResolvedButton? Resolve(ClickButton b, string? app) => Resolve(new Trigger(b), app);

    public ResolvedButton? Resolve(Trigger t, string? app)
    {
        if (!_map.TryGetValue(t, out var m)) return null;
        var a = m.ActionFor(app);
        var shortAction = a is { } x && x.Fires ? x : null;
        var canBlock = t.IsKey || !ClickButtons.IsPrimary(t.Button) || Settings.AllowPrimarySuppression;
        var longAction = m.HasLongPress && canBlock ? m.LongPressAction : null;
        // A long press always needs the original press held back until we know which it was.
        // Extra keys are always consumed (they are registered as hot keys).
        var block = t.IsKey || canBlock && (m.BlockOriginal || longAction is not null);
        if (shortAction is null && longAction is null && !block) return null;
        return new ResolvedButton(m.Id, m.Label, shortAction, longAction, m.LongPressMs, block);
    }

    /// <summary>Does switching between these apps change what any button does?</summary>
    public bool ProfileDiffers(string? a, string? b)
    {
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return false;
        return _map.Values.Any(m => m.Overrides.Any(o => o.Enabled &&
            (string.Equals(o.App, a, StringComparison.OrdinalIgnoreCase) || string.Equals(o.App, b, StringComparison.OrdinalIgnoreCase))));
    }
}

/// <summary>2.2: short vs long press. Pure timing logic; it never sends anything itself.
/// Short fires on release before the threshold. Long fires once at the threshold, and the
/// short action is then suppressed. Cancel drops a pending press without firing.</summary>
public sealed class LongPressTracker<TAction> where TAction : class
{
    private long? _pressedAt;
    private bool _longFired;
    public long ThresholdMs { get; }
    public TAction? ShortAction { get; }
    public TAction? LongAction { get; }

    public LongPressTracker(int thresholdMs, TAction? shortAction, TAction? longAction)
    {
        ThresholdMs = Math.Clamp(thresholdMs, ButtonMapping.MinLongPressMs, ButtonMapping.MaxLongPressMs);
        ShortAction = shortAction;
        LongAction = longAction;
    }

    public bool IsPending => _pressedAt is not null;

    public void Press(long nowMs)
    {
        if (_pressedAt is not null) return;
        _pressedAt = nowMs;
        _longFired = false;
    }

    /// <summary>Call regularly while pending. Returns the long action exactly once.</summary>
    public TAction? Tick(long nowMs)
    {
        if (_pressedAt is not { } start || _longFired || LongAction is null) return null;
        if (nowMs - start < ThresholdMs) return null;
        _longFired = true;
        return LongAction;
    }

    public TAction? Release(long nowMs)
    {
        if (_pressedAt is not { } start) return null;
        _pressedAt = null;
        var fired = _longFired;
        _longFired = false;
        if (fired) return null;
        if (LongAction is not null && nowMs - start >= ThresholdMs) return LongAction; // late release
        return ShortAction;
    }

    public void Cancel()
    {
        _pressedAt = null;
        _longFired = false;
    }
}

/// <summary>Per-button repeat guard.</summary>
public sealed class PressGuard
{
    private readonly long _intervalMs;
    private readonly Dictionary<Trigger, long> _last = new();
    public PressGuard(int intervalMs) => _intervalMs = Math.Max(0, intervalMs);

    public bool Allow(ClickButton b, long nowMs) => Allow(new Trigger(b), nowMs);

    public bool Allow(Trigger t, long nowMs)
    {
        if (_last.TryGetValue(t, out var l) && nowMs - l < _intervalMs) return false;
        _last[t] = nowMs;
        return true;
    }
}

public enum HealthState { Ready, Paused, EmergencyPaused, HookUnavailable, Stopped }

public static class HealthModel
{
    /// <summary>Priority: stopped → hook unavailable → emergency → paused → ready.</summary>
    public static HealthState Evaluate(bool engineRunning, bool hookActive, bool emergencyPaused, bool paused)
    {
        if (!engineRunning) return HealthState.Stopped;
        if (!hookActive) return HealthState.HookUnavailable;
        if (emergencyPaused) return HealthState.EmergencyPaused;
        if (paused) return HealthState.Paused;
        return HealthState.Ready;
    }
}

/// <summary>"Open app" only launches programs installed in the normal program folders, and never
/// shells or script hosts (which would let a snippet of config run arbitrary commands).</summary>
public static class AppLocationPolicy
{
    private static readonly HashSet<string> Denied = new(StringComparer.OrdinalIgnoreCase)
    {
        "cmd.exe", "powershell.exe", "pwsh.exe", "powershell_ise.exe", "wscript.exe", "cscript.exe", "mshta.exe",
        "rundll32.exe", "regsvr32.exe", "msiexec.exe", "wsl.exe", "bash.exe", "conhost.exe", "wt.exe",
        "schtasks.exe", "reg.exe", "certutil.exe", "bitsadmin.exe", "installutil.exe", "msbuild.exe"
    };

    public static string[] DefaultRoots() => new[]
    {
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps")
    }.Where(r => !string.IsNullOrWhiteSpace(r)).ToArray();

    public static bool IsAllowed(string path, IEnumerable<string> roots)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        string full;
        try { full = Path.GetFullPath(path); } catch { return false; }
        if (!full.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return false;
        if (Denied.Contains(Path.GetFileName(full))) return false;
        foreach (var root in roots)
        {
            string r;
            try { r = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar; }
            catch { continue; }
            if (full.StartsWith(r, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
}
