using System.Text;
using Clicksmith.Core;

// XCTest/xUnit-free self-test, so the installer needs nothing but the .NET SDK.
var results = new List<(string Name, bool Ok)>();
void Check(string name, Func<bool> test)
{
    bool ok;
    try { ok = test(); } catch (Exception e) { ok = false; Console.WriteLine($"  ! {name}: {e.GetType().Name}: {e.Message}"); }
    results.Add((name, ok));
}
bool Throws<T>(Action a) where T : Exception { try { a(); return false; } catch (T) { return true; } }

var home = Path.Combine(Path.GetTempPath(), "clicksmith-selftest-" + Guid.NewGuid().ToString("N"));
Environment.SetEnvironmentVariable("CLICKSMITH_HOME", home);

ButtonAction Keys(ushort vk, string name, bool ctrl = false, bool shift = false) =>
    new() { Kind = ActionKind.Shortcut, Shortcut = new KeyShortcut { Vk = vk, KeyName = name, Ctrl = ctrl, Shift = shift } };

var mute = Keys(0x4D, "M", ctrl: true, shift: true);
var screenshot = Keys(0x53, "S", shift: true);
var back = new ButtonMapping
{
    Label = "Back", Button = ClickButton.Back, DefaultAction = screenshot,
    Overrides = { new AppOverride { App = "ms-teams.exe", AppName = "Teams", Action = mute } }
};
var config = ClicksmithConfig.CreateDefault();
config.ActiveProfile.Mappings.Add(back);

// --- Mapping resolution ---
var compiled = new CompiledConfig(config);
Check("app override wins", () => compiled.Resolve(ClickButton.Back, "MS-Teams.exe")?.Short == mute);
Check("other app uses default", () => compiled.Resolve(ClickButton.Back, "explorer.exe")?.Short == screenshot);
Check("no app uses default", () => compiled.Resolve(ClickButton.Back, null)?.Short == screenshot);
Check("unmapped button passes through", () => compiled.Resolve(ClickButton.Forward, null) is null);
Check("side button blocks original", () => compiled.Resolve(ClickButton.Back, null)!.Block);
Check("switch between default apps changes nothing", () => !compiled.ProfileDiffers("a.exe", "b.exe"));
Check("switch into Teams changes profile", () => compiled.ProfileDiffers("a.exe", "ms-teams.exe"));
Check("disabled override ignored", () =>
{
    var c = config.Clone(); c.ActiveProfile.Mappings[0].Overrides[0].Enabled = false;
    return new CompiledConfig(c).Resolve(ClickButton.Back, "ms-teams.exe")!.Short!.Display == screenshot.Display;
});
Check("disabled mapping ignored", () =>
{
    var c = config.Clone(); c.ActiveProfile.Mappings[0].Enabled = false;
    return new CompiledConfig(c).Resolve(ClickButton.Back, null) is null;
});
Check("left click never blocked unless allowed", () =>
{
    var c = ClicksmithConfig.CreateDefault();
    c.ActiveProfile.Mappings.Add(new ButtonMapping { Label = "Left", Button = ClickButton.Left, DefaultAction = screenshot, BlockOriginal = false });
    var r = new CompiledConfig(c).Resolve(ClickButton.Left, null);
    return r is not null && !r.Block;
});
Check("left click long press dropped unless allowed", () =>
{
    var c = ClicksmithConfig.CreateDefault();
    c.ActiveProfile.Mappings.Add(new ButtonMapping { Label = "Left", Button = ClickButton.Left, BlockOriginal = false, LongPressAction = mute });
    var r = new CompiledConfig(c).Resolve(ClickButton.Left, null);
    return r is null || r.Long is null;
});
Check("long press forces blocking", () =>
{
    var c = ClicksmithConfig.CreateDefault();
    c.ActiveProfile.Mappings.Add(new ButtonMapping { Label = "Fwd", Button = ClickButton.Forward, BlockOriginal = false, DefaultAction = screenshot, LongPressAction = mute });
    var r = new CompiledConfig(c).Resolve(ClickButton.Forward, null)!;
    return r.Block && r.Long == mute;
});

// --- 2.2 long press ---
Check("short press fires on release", () =>
{
    var t = new LongPressTracker<string>(500, "short", "long"); t.Press(1000);
    return t.Tick(1300) is null && t.Release(1300) == "short";
});
Check("long press fires once and suppresses short", () =>
{
    var t = new LongPressTracker<string>(500, "short", "long"); t.Press(1000);
    return t.Tick(1499) is null && t.Tick(1500) == "long" && t.Tick(1600) is null && t.Release(1700) is null;
});
Check("late release still counts as long", () =>
{
    var t = new LongPressTracker<string>(500, "short", "long"); t.Press(1000);
    return t.Release(1800) == "long";
});
Check("cancel fires nothing", () =>
{
    var t = new LongPressTracker<string>(500, "short", "long"); t.Press(1000); t.Cancel();
    return t.Release(3000) is null && !t.IsPending;
});
Check("threshold clamped to 250–1500 ms", () =>
    new LongPressTracker<string>(10, "s", "l").ThresholdMs == 250 && new LongPressTracker<string>(9000, "s", "l").ThresholdMs == 1500);
Check("hold-only mapping: short release fires nothing", () =>
{
    var t = new LongPressTracker<string>(500, null, "long"); t.Press(0);
    return t.Release(100) is null;
});
Check("second press after release works", () =>
{
    var t = new LongPressTracker<string>(500, "short", "long"); t.Press(0); t.Tick(600); t.Release(700);
    t.Press(1000); return t.Release(1100) == "short";
});

// --- Repeat guard ---
Check("repeat guard blocks fast repeats", () => { var g = new PressGuard(150); return g.Allow(ClickButton.Back, 0) && !g.Allow(ClickButton.Back, 100) && g.Allow(ClickButton.Back, 200); });
Check("repeat guard is per button", () => { var g = new PressGuard(150); return g.Allow(ClickButton.Back, 0) && g.Allow(ClickButton.Forward, 10); });

// --- Configuration ---
Check("config round trip", () =>
{
    var d = ConfigCodec.Decode(ConfigCodec.Encode(config));
    return d.ActiveProfile.Mappings[0].Overrides[0].Action.Shortcut!.Display == "Ctrl+Shift+M";
});
Check("enums stored as words", () => Encoding.UTF8.GetString(ConfigCodec.Encode(config)).Contains("\"back\""));
Check("reject non-JSON", () => Throws<ConfigException>(() => ConfigCodec.Decode("hello"u8.ToArray())));
Check("reject future schema", () => Throws<ConfigException>(() => ConfigCodec.Decode("{\"schemaVersion\":99}"u8.ToArray())));
Check("reject missing schema", () => Throws<ConfigException>(() => ConfigCodec.Decode("{\"settings\":{}}"u8.ToArray())));
Check("reject macOS config", () => Throws<ConfigException>(() => ConfigCodec.Decode("{\"schemaVersion\":2,\"platform\":\"macos\"}"u8.ToArray())));
Check("migrate schema 1 to profiles", () =>
{
    var v1 = "{\"schemaVersion\":1,\"settings\":{\"paused\":true},\"mappings\":[{\"label\":\"Back\",\"button\":\"back\",\"defaultAction\":{\"kind\":\"mediaKey\",\"media\":\"playPause\"}}]}";
    var c = ConfigCodec.Decode(Encoding.UTF8.GetBytes(v1));
    return c.SchemaVersion == 2 && c.Profiles.Count == 1 && c.Settings.Paused && c.ActiveProfile.Mappings[0].DefaultAction!.Media == MediaKey.PlayPause;
});
Check("old settings get defaults", () => ConfigCodec.Decode("{\"schemaVersion\":1,\"settings\":{}}"u8.ToArray()).Settings.DuplicateGuardMs == 150);
Check("reject duplicate button per profile", () =>
{
    var c = config.Clone(); c.ActiveProfile.Mappings.Add(new ButtonMapping { Label = "Again", Button = ClickButton.Back, DefaultAction = screenshot });
    return Throws<ConfigException>(() => ConfigCodec.Validate(c));
});
Check("reject bad app name", () =>
{
    var c = config.Clone(); c.ActiveProfile.Mappings[0].Overrides[0].App = "..\\evil.exe";
    return Throws<ConfigException>(() => ConfigCodec.Validate(c));
});
Check("reject missing shortcut", () =>
{
    var c = config.Clone(); c.ActiveProfile.Mappings[0].DefaultAction = new ButtonAction { Kind = ActionKind.Shortcut };
    return Throws<ConfigException>(() => ConfigCodec.Validate(c));
});
Check("reject hold time out of range", () =>
{
    var c = config.Clone(); c.ActiveProfile.Mappings[0].LongPressMs = 50;
    return Throws<ConfigException>(() => ConfigCodec.Validate(c));
});
Check("reject left click change unless allowed", () =>
{
    var c = ClicksmithConfig.CreateDefault();
    c.ActiveProfile.Mappings.Add(new ButtonMapping { Label = "Left", Button = ClickButton.Left, DefaultAction = screenshot });
    return Throws<ConfigException>(() => ConfigCodec.Validate(c));
});
Check("reject missing active profile", () => { var c = config.Clone(); c.ActiveProfileId = Guid.NewGuid(); return Throws<ConfigException>(() => ConfigCodec.Validate(c)); });

// --- Actions ---
Check("type text rejects control characters", () => new ButtonAction { Kind = ActionKind.TypeText, Text = "a\u001b[2J" }.Problem() is not null);
Check("type text allows newline and ÅÄÖ", () => new ButtonAction { Kind = ActionKind.TypeText, Text = "Hej!\nÅÄÖ" }.Problem() is null);
Check("type text max 500", () => new ButtonAction { Kind = ActionKind.TypeText, Text = new string('a', 501) }.Problem() is not null);
Check("open app needs .exe", () => new ButtonAction { Kind = ActionKind.OpenApp, AppPath = "C:\\x\\script.ps1" }.Problem() is not null);
Check("media key valid", () => new ButtonAction { Kind = ActionKind.MediaKey, Media = MediaKey.Mute }.Problem() is null);
Check("shortcut display", () => mute.Display == "Ctrl+Shift+M");
Check("Alt+F4 warns", () => KeyNames.Warning(new KeyShortcut { Vk = 0x73, Alt = true }) is not null);
Check("Win+Shift+S no warning", () => KeyNames.Warning(new KeyShortcut { Vk = 0x53, Shift = true, Win = true }) is null);
Check("presets are valid", () => Presets.All.All(p => (p.Default?.Problem() ?? null) is null && (p.AppAction?.Problem() ?? null) is null));

// --- Open-app policy ---
var pf = Path.Combine(Path.GetTempPath(), "cs-pf");
Check("open app allowed in Program Files", () => AppLocationPolicy.IsAllowed(Path.Combine(pf, "Tool", "tool.exe"), new[] { pf }));
Check("open app refused in Downloads", () => !AppLocationPolicy.IsAllowed(Path.Combine(Path.GetTempPath(), "Downloads", "Teams.exe"), new[] { pf }));
Check("open app refuses traversal", () => !AppLocationPolicy.IsAllowed(Path.Combine(pf, "..", "evil.exe"), new[] { pf }));
Check("open app refuses prefix trick", () => !AppLocationPolicy.IsAllowed(pf + "-evil" + Path.DirectorySeparatorChar + "x.exe", new[] { pf }));
Check("open app refuses non-exe", () => !AppLocationPolicy.IsAllowed(Path.Combine(pf, "x.bat"), new[] { pf }));

// --- Health ---
Check("health ready", () => HealthModel.Evaluate(true, true, false, false) == HealthState.Ready);
Check("health stopped wins", () => HealthModel.Evaluate(false, false, true, true) == HealthState.Stopped);
Check("health hook before pause", () => HealthModel.Evaluate(true, false, false, true) == HealthState.HookUnavailable);
Check("health emergency before pause", () => HealthModel.Evaluate(true, true, true, true) == HealthState.EmergencyPaused);

// --- 2.4 updates ---
Check("semver order", () => SemanticVersion.Parse("2.4.0")!.CompareTo(SemanticVersion.Parse("2.4.1")) < 0);
Check("prerelease before release", () => SemanticVersion.Parse("2.4.0-beta.1")!.CompareTo(SemanticVersion.Parse("2.4.0")) < 0);
Check("beta.2 after beta.1", () => SemanticVersion.Parse("2.4.0-beta.2")!.CompareTo(SemanticVersion.Parse("2.4.0-beta.1")) > 0);
Check("reject leading zeros", () => SemanticVersion.Parse("2.04.0") is null);
Check("newer windows tag detected", () => UpdatePolicy.IsNewer("windows-v2.5.0", "2.5.0-beta.1"));
Check("macOS tag ignored", () => !UpdatePolicy.IsNewer("v9.0.0", "2.4.0"));
Check("same version not newer", () => !UpdatePolicy.IsNewer("windows-v2.4.0", "2.4.0"));
Check("manifest rejects unsafe name", () => Throws<UpdateException>(() => new ReleaseManifest { Version = "2.4.0", Artifact = "../x.zip", Sha256 = new string('a', 64) }.Validate()));
Check("manifest verifies checksum", () =>
{
    Directory.CreateDirectory(home);
    var f = Path.Combine(home, "pkg.zip"); File.WriteAllText(f, "clicksmith");
    var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(f))).ToLowerInvariant();
    new ReleaseManifest { Version = "2.4.1", Artifact = "pkg.zip", Sha256 = hash }.VerifyFile(f);
    return Throws<UpdateException>(() => new ReleaseManifest { Version = "2.4.1", Artifact = "pkg.zip", Sha256 = new string('0', 64) }.VerifyFile(f));
});

// --- Router: simulated presses (the engine's real decision logic) ---
RouterSnapshot Snap(ClicksmithConfig c, string? app = null, bool active = true, Guid? identify = null, Guid? test = null) => new()
{
    Config = new CompiledConfig(c), Active = active, App = app,
    IdentifyId = identify, IdentifyUntilMs = identify is null ? 0 : long.MaxValue,
    TestId = test, TestUntilMs = test is null ? 0 : long.MaxValue
};
var backT = new Trigger(ClickButton.Back);
var fwdT = new Trigger(ClickButton.Forward);
var holdCfg = ClicksmithConfig.CreateDefault();
holdCfg.ActiveProfile.Mappings.Add(back);
holdCfg.ActiveProfile.Mappings.Add(new ButtonMapping { Label = "Forward", Button = ClickButton.Forward, DefaultAction = screenshot, LongPressAction = mute, LongPressMs = 500 });

Check("router: mapped press runs action and blocks", () =>
{
    var r = new ButtonRouter(); var d = r.OnInput(backT, true, 1000, Snap(holdCfg));
    var u = r.OnInput(backT, false, 1050, Snap(holdCfg));
    return d.Swallow && d.Action == screenshot && u.Swallow && u.Action is null;
});
Check("router: Teams override used in Teams", () => new ButtonRouter().OnInput(backT, true, 0, Snap(holdCfg, "ms-teams.exe")).Action == mute);
Check("router: unmapped button passes through", () =>
{
    var r = new ButtonRouter(); var d = r.OnInput(new Trigger(ClickButton.Middle), true, 0, Snap(holdCfg));
    return !d.Swallow && d.Action is null && !r.OnInput(new Trigger(ClickButton.Middle), false, 10, Snap(holdCfg)).Swallow;
});
Check("router: paused passes through", () => !new ButtonRouter().OnInput(backT, true, 0, Snap(holdCfg, active: false)).Swallow);
Check("router: tap fires tap action on release", () =>
{
    var r = new ButtonRouter(); var s0 = Snap(holdCfg);
    var d = r.OnInput(fwdT, true, 1000, s0);
    return d.Swallow && d.Action is null && r.Tick(1200) is null && r.OnInput(fwdT, false, 1200, s0).Action == screenshot;
});
Check("router: hold fires hold action once, never tap", () =>
{
    var r = new ButtonRouter(); var s0 = Snap(holdCfg);
    r.OnInput(fwdT, true, 1000, s0);
    var a1 = r.Tick(1510); var a2 = r.Tick(1600);
    var up = r.OnInput(fwdT, false, 1700, s0);
    return a1 == mute && a2 is null && up.Swallow && up.Action is null && !r.HasPending;
});
Check("router: cancel during hold fires nothing, release still swallowed", () =>
{
    var r = new ButtonRouter(); var s0 = Snap(holdCfg);
    r.OnInput(fwdT, true, 0, s0); r.Cancel();
    var up = r.OnInput(fwdT, false, 900, s0);
    return up.Swallow && up.Action is null && r.Tick(1000) is null;
});
Check("router: reset forgets held buttons", () =>
{
    var r = new ButtonRouter(); var s0 = Snap(holdCfg);
    r.OnInput(backT, true, 0, s0); r.Reset();
    return !r.OnInput(backT, false, 10, s0).Swallow;
});
Check("router: repeat guard drops a bounce", () =>
{
    var r = new ButtonRouter(150); var s0 = Snap(holdCfg);
    var a = r.OnInput(backT, true, 0, s0).Action; r.OnInput(backT, false, 20, s0);
    var b = r.OnInput(backT, true, 60, s0).Action;
    return a is not null && b is null;
});
Check("router: test mode reports and never runs", () =>
{
    var r = new ButtonRouter(); var id = Guid.NewGuid();
    var d = r.OnInput(backT, true, 0, Snap(holdCfg, "ms-teams.exe", test: id));
    return d.Action is null && d.Swallow && d.Result!.Kind == "test" && d.Result.Summary.Contains("Ctrl+Shift+M");
});
var txtCfg = ClicksmithConfig.CreateDefault();
txtCfg.ActiveProfile.Mappings.Add(new ButtonMapping { Label = "Signature", Button = ClickButton.Middle, DefaultAction = new ButtonAction { Kind = ActionKind.TypeText, Text = "Hej" } });
Check("router: test mode explains text safety", () =>
    new ButtonRouter().OnInput(new Trigger(ClickButton.Middle), true, 0, Snap(txtCfg, test: Guid.NewGuid())).Result!.Summary.Contains("password"));
Check("status keeps last skipped action", () =>
{
    IpcFiles.Write(ClicksmithPaths.Status, new EngineStatus { LastSkipped = "x", LastSkippedAt = DateTimeOffset.UtcNow });
    return IpcFiles.Read<EngineStatus>(ClicksmithPaths.Status)?.LastSkipped == "x";
});
Check("router: test mode works while paused", () =>
    new ButtonRouter().OnInput(backT, true, 0, Snap(holdCfg, active: false, test: Guid.NewGuid())).Result is not null);
Check("router: identify reports once, then normal", () =>
{
    var r = new ButtonRouter(); var id = Guid.NewGuid(); var s0 = Snap(holdCfg, identify: id);
    var first = r.OnInput(fwdT, true, 0, s0); r.OnInput(fwdT, false, 10, s0);
    var second = r.OnInput(backT, true, 400, s0);
    return first.Result?.Kind == "identified" && first.Result.Button == ClickButton.Forward && first.Action is null && second.Result is null && second.Action is not null;
});
Check("router: identify ignores left click", () =>
    new ButtonRouter().OnInput(new Trigger(ClickButton.Left), true, 0, Snap(holdCfg, identify: Guid.NewGuid())).Result is null);

// --- Extra buttons (F13–F24) ---
var dpi = new ButtonMapping { Label = "DPI Button", Button = ClickButton.Key, TriggerVk = 0x7C, DefaultAction = screenshot };
var keyCfg = ClicksmithConfig.CreateDefault(); keyCfg.ActiveProfile.Mappings.Add(dpi); keyCfg.ActiveProfile.Mappings.Add(back);
Check("extra key: valid config", () => { ConfigCodec.Validate(keyCfg); return true; });
Check("extra key: listed for hot keys", () => new CompiledConfig(keyCfg).ExtraKeys.SequenceEqual(new ushort[] { 0x7C }));
Check("extra key: resolves and runs", () => new ButtonRouter().OnInput(new Trigger(ClickButton.Key, 0x7C), true, 0, Snap(keyCfg)).Action == screenshot);
Check("extra key: F13 name", () => new Trigger(ClickButton.Key, 0x7C).ToString() == "F13");
Check("extra key: rejects non-F13–F24", () => { var c = keyCfg.Clone(); c.ActiveProfile.Mappings[0].TriggerVk = 0x41; return Throws<ConfigException>(() => ConfigCodec.Validate(c)); });
Check("extra key: mouse buttons can't have a key", () => { var c = keyCfg.Clone(); c.ActiveProfile.Mappings[1].TriggerVk = 0x7C; return Throws<ConfigException>(() => ConfigCodec.Validate(c)); });
Check("extra key: two different F-keys allowed", () =>
{
    var c = keyCfg.Clone(); c.ActiveProfile.Mappings.Add(new ButtonMapping { Label = "DPI 2", Button = ClickButton.Key, TriggerVk = 0x7D, DefaultAction = mute });
    ConfigCodec.Validate(c); return true;
});
Check("extra key: same F-key twice rejected", () =>
{
    var c = keyCfg.Clone(); c.ActiveProfile.Mappings.Add(new ButtonMapping { Label = "Again", Button = ClickButton.Key, TriggerVk = 0x7C, DefaultAction = mute });
    return Throws<ConfigException>(() => ConfigCodec.Validate(c));
});
Check("extra key: hold works via router", () =>
{
    var c = keyCfg.Clone(); c.ActiveProfile.Mappings[0].LongPressAction = mute;
    var r = new ButtonRouter(); var s0 = Snap(c); var t = new Trigger(ClickButton.Key, 0x7C);
    r.OnInput(t, true, 0, s0); var a = r.Tick(600); return a == mute && r.OnInput(t, false, 700, s0).Action is null;
});
Check("app path must be .exe", () => { var c = config.Clone(); c.ActiveProfile.Mappings[0].Overrides[0].Path = "C:\\x\\teams.lnk"; return Throws<ConfigException>(() => ConfigCodec.Validate(c)); });

// --- Open app: shells and script hosts refused everywhere ---
Check("open app refuses PowerShell in Program Files", () => !AppLocationPolicy.IsAllowed(Path.Combine(pf, "PowerShell", "7", "pwsh.exe"), new[] { pf }));
Check("open app refuses cmd.exe", () => !AppLocationPolicy.IsAllowed(Path.Combine(pf, "cmd.exe"), new[] { pf }));
Check("Windows folder not a default root", () => !AppLocationPolicy.DefaultRoots().Any(r =>
    !string.IsNullOrEmpty(Environment.GetFolderPath(Environment.SpecialFolder.Windows)) &&
    string.Equals(Path.GetFullPath(r).TrimEnd('\\', '/'), Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.Windows)).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase)));

// --- 2.4 release index ---
var indexJson = Encoding.UTF8.GetBytes("{\"version\":\"2.5.0\",\"artifacts\":[{\"arch\":\"win-x64\",\"file\":\"Clicksmith-2.5.0-win-x64.zip\",\"sha256\":\"" + new string('a', 64) + "\",\"size\":100}]}");
Check("release index picks the right package", () => ReleaseIndex.Parse(indexJson).Pick("win-x64").Artifact == "Clicksmith-2.5.0-win-x64.zip");
Check("release index: missing arch is an error", () => Throws<UpdateException>(() => ReleaseIndex.Parse(indexJson).Pick("win-arm64")));
Check("release index: rejects junk", () => Throws<UpdateException>(() => ReleaseIndex.Parse("[1,2]"u8.ToArray())));
Check("release index: rejects unsafe file", () =>
    Throws<UpdateException>(() => ReleaseIndex.Parse(Encoding.UTF8.GetBytes("{\"version\":\"2.5.0\",\"artifacts\":[{\"arch\":\"win-x64\",\"file\":\"..\\\\evil.zip\",\"sha256\":\"" + new string('a', 64) + "\"}]}")).Pick("win-x64")));

// --- 2.1 history (real files in a temp folder) ---
Check("save creates no backup the first time", () => { ConfigStore.Save(config); return ConfigurationHistory.List().Count == 0; });
Check("second save backs up the previous file", () => { ConfigStore.Save(config); return ConfigurationHistory.List().Count == 1; });
Check("history keeps at most 20", () => { for (var i = 0; i < 25; i++) ConfigStore.Save(config); return ConfigurationHistory.List().Count == ConfigurationHistory.Limit; });
Check("restore brings back an older config", () =>
{
    var c = config.Clone(); c.Settings.Paused = true; ConfigStore.Save(c);
    var newest = ConfigurationHistory.List()[0];
    ConfigurationHistory.Restore(newest);
    return !ConfigStore.Load().Settings.Paused;
});
Check("invalid save leaves file untouched", () =>
{
    var before = File.ReadAllBytes(ClicksmithPaths.Config);
    var bad = config.Clone(); bad.ActiveProfileId = Guid.NewGuid();
    return Throws<ConfigException>(() => ConfigStore.Save(bad)) && File.ReadAllBytes(ClicksmithPaths.Config).SequenceEqual(before);
});
Check("missing config gives defaults", () => { File.Delete(ClicksmithPaths.Config); return ConfigStore.Load().Profiles.Count == 1; });
Check("ipc round trip", () => { IpcFiles.Write(ClicksmithPaths.Status, new EngineStatus { Pid = 42 }); return IpcFiles.Read<EngineStatus>(ClicksmithPaths.Status)?.Pid == 42; });

try { Directory.Delete(home, recursive: true); } catch { }
foreach (var (name, ok) in results.Where(r => !r.Ok)) Console.WriteLine("  FAIL: " + name);
var passed = results.Count(r => r.Ok);
Console.WriteLine($"Self-test: {passed}/{results.Count} passed");
return passed == results.Count ? 0 : 1;
