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
Check("newer windows tag detected", () => UpdatePolicy.IsNewer("windows-v2.4.1", "2.4.0-beta.1"));
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
