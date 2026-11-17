using System.Text.Json.Serialization;

namespace Clicksmith.Core;

/// <summary>The five buttons Windows exposes for every mouse.</summary>
public enum ClickButton { Left = 0, Right = 1, Middle = 2, Back = 3, Forward = 4 }

public static class ClickButtons
{
    public static readonly ClickButton[] All =
        { ClickButton.Left, ClickButton.Right, ClickButton.Middle, ClickButton.Back, ClickButton.Forward };

    public static string Name(ClickButton b) => b switch
    {
        ClickButton.Left => "Left click",
        ClickButton.Right => "Right click",
        ClickButton.Middle => "Middle click",
        ClickButton.Back => "Back",
        ClickButton.Forward => "Forward",
        _ => b.ToString()
    };

    public static bool IsPrimary(ClickButton b) => b is ClickButton.Left or ClickButton.Right;
}

public enum ActionKind { Shortcut, MediaKey, OpenApp, TypeText, Nothing }

public enum MediaKey { PlayPause, Next, Previous, VolumeUp, VolumeDown, Mute }

public static class MediaKeys
{
    public static string Title(MediaKey k) => k switch
    {
        MediaKey.PlayPause => "Play / Pause",
        MediaKey.Next => "Next track",
        MediaKey.Previous => "Previous track",
        MediaKey.VolumeUp => "Volume up",
        MediaKey.VolumeDown => "Volume down",
        MediaKey.Mute => "Mute",
        _ => k.ToString()
    };

    /// <summary>Windows virtual-key codes for the media keys.</summary>
    public static ushort VirtualKey(MediaKey k) => k switch
    {
        MediaKey.PlayPause => 0xB3,
        MediaKey.Next => 0xB0,
        MediaKey.Previous => 0xB1,
        MediaKey.VolumeUp => 0xAF,
        MediaKey.VolumeDown => 0xAE,
        MediaKey.Mute => 0xAD,
        _ => 0
    };
}

public sealed class KeyShortcut
{
    public ushort Vk { get; set; }
    public bool Ctrl { get; set; }
    public bool Alt { get; set; }
    public bool Shift { get; set; }
    public bool Win { get; set; }
    public string KeyName { get; set; } = "";

    [JsonIgnore]
    public string Display
    {
        get
        {
            var parts = new List<string>();
            if (Ctrl) parts.Add("Ctrl");
            if (Alt) parts.Add("Alt");
            if (Shift) parts.Add("Shift");
            if (Win) parts.Add("Win");
            parts.Add(string.IsNullOrWhiteSpace(KeyName) ? KeyNames.Name(Vk) : KeyName);
            return string.Join("+", parts);
        }
    }
}

public sealed class ButtonAction
{
    public const int MaxTextLength = 500;

    public ActionKind Kind { get; set; } = ActionKind.Shortcut;
    public KeyShortcut? Shortcut { get; set; }
    public MediaKey? Media { get; set; }
    public string? AppPath { get; set; }
    public string? AppName { get; set; }
    public string? Text { get; set; }

    /// <summary>What is missing or wrong, or null when the action is complete.</summary>
    public string? Problem()
    {
        switch (Kind)
        {
            case ActionKind.Shortcut:
                if (Shortcut is null) return "Record a shortcut.";
                if (Shortcut.Vk == 0 || Shortcut.Vk > 0xFE) return "Invalid key.";
                if (Shortcut.KeyName.Length > 24) return "Invalid key name.";
                return null;
            case ActionKind.MediaKey:
                return Media is null ? "Choose a media key." : null;
            case ActionKind.OpenApp:
                if (string.IsNullOrWhiteSpace(AppPath)) return "Choose an app to open.";
                if (AppPath.Length > 260 || !AppPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    return "Choose an .exe file.";
                return null;
            case ActionKind.TypeText:
                if (string.IsNullOrEmpty(Text)) return "Enter the text to type.";
                if (Text.Length > MaxTextLength) return $"Text is longer than {MaxTextLength} characters.";
                if (Text.Any(c => c < 0x20 && c != '\n' && c != '\t')) return "Text contains control characters.";
                return null;
            default:
                return null;
        }
    }

    [JsonIgnore]
    public string Display => Kind switch
    {
        ActionKind.Shortcut => Shortcut?.Display ?? "—",
        ActionKind.MediaKey => Media is { } m ? MediaKeys.Title(m) : "—",
        ActionKind.OpenApp => "Open " + (AppName ?? Path.GetFileNameWithoutExtension(AppPath ?? "app")),
        ActionKind.TypeText => "Type “" + Shorten((Text ?? "").Replace('\n', ' '), 18) + "”",
        _ => "Nothing"
    };

    /// <summary>Fires something when pressed (Nothing only blocks).</summary>
    [JsonIgnore]
    public bool Fires => Kind != ActionKind.Nothing && Problem() is null;

    private static string Shorten(string s, int n) => s.Length > n ? s[..n] + "…" : s;
}

public sealed class AppOverride
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Executable file name, e.g. "ms-teams.exe". Compared case-insensitively.</summary>
    public string App { get; set; } = "";
    public string AppName { get; set; } = "";
    public ButtonAction Action { get; set; } = new();
    public bool Enabled { get; set; } = true;
}

public sealed class ButtonMapping
{
    public const int DefaultLongPressMs = 500;
    public const int MinLongPressMs = 250;
    public const int MaxLongPressMs = 1500;

    public Guid Id { get; set; } = Guid.NewGuid();
    public string Label { get; set; } = "";
    public ClickButton Button { get; set; } = ClickButton.Back;
    public bool Enabled { get; set; } = true;
    /// <summary>Block the button's normal Windows action while this mapping applies.</summary>
    public bool BlockOriginal { get; set; } = true;
    /// <summary>Action in all apps; null = only the app overrides apply.</summary>
    public ButtonAction? DefaultAction { get; set; }
    public List<AppOverride> Overrides { get; set; } = new();
    /// <summary>2.2: second action when the button is held. Applies in all apps.</summary>
    public ButtonAction? LongPressAction { get; set; }
    public int LongPressMs { get; set; } = DefaultLongPressMs;

    /// <summary>Exact app override wins, then the default action.</summary>
    public ButtonAction? ActionFor(string? app)
    {
        if (!string.IsNullOrEmpty(app))
        {
            var o = Overrides.FirstOrDefault(x => x.Enabled && string.Equals(x.App, app, StringComparison.OrdinalIgnoreCase));
            if (o is not null) return o.Action;
        }
        return DefaultAction;
    }

    [JsonIgnore]
    public bool HasLongPress => LongPressAction is { } l && l.Fires;

    [JsonIgnore]
    public string Scope
    {
        get
        {
            if (!Enabled) return "Off";
            var apps = Overrides.Where(o => o.Enabled).ToList();
            var hold = HasLongPress ? " · hold" : "";
            return (DefaultAction is not null, apps.Count) switch
            {
                (true, 0) => "Everywhere" + hold,
                (true, 1) => $"Everywhere · different in {apps[0].AppName}" + hold,
                (true, _) => $"Everywhere · different in {apps.Count} apps" + hold,
                (false, 0) => HasLongPress ? "Hold only" : "No action",
                (false, 1) => $"Only in {apps[0].AppName}" + hold,
                _ => $"Only in {apps.Count} apps" + hold
            };
        }
    }
}

/// <summary>2.3: a named set of mappings. Exactly one profile is active.</summary>
public sealed class Profile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Default";
    public List<ButtonMapping> Mappings { get; set; } = new();
}

public sealed class EngineSettings
{
    public bool Paused { get; set; }
    public bool EngineEnabled { get; set; } = true;
    public bool LaunchEngineAtLogin { get; set; } = true;
    public bool LaunchAppAtLogin { get; set; }
    public int DuplicateGuardMs { get; set; } = 150;
    public int WakeRecoveryDelayMs { get; set; } = 2000;
    public bool EmergencyHotKeyEnabled { get; set; } = true;
    public bool AllowPrimarySuppression { get; set; }
    public bool ShowTrayIcon { get; set; } = true;
    public bool CheckForUpdatesOnStart { get; set; }
    public bool OnboardingCompleted { get; set; }

    /// <summary>The emergency pause can't be turned off while left/right blocking is allowed.</summary>
    [JsonIgnore]
    public bool EmergencyHotKeyEffective => EmergencyHotKeyEnabled || AllowPrimarySuppression;
}

public sealed class ClicksmithConfig
{
    public const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public string Platform { get; set; } = "windows";
    public EngineSettings Settings { get; set; } = new();
    public List<Profile> Profiles { get; set; } = new();
    public Guid ActiveProfileId { get; set; }

    [JsonIgnore]
    public Profile ActiveProfile => Profiles.FirstOrDefault(p => p.Id == ActiveProfileId) ?? Profiles[0];

    public static ClicksmithConfig CreateDefault()
    {
        var p = new Profile { Name = "Default" };
        return new ClicksmithConfig { Profiles = { p }, ActiveProfileId = p.Id };
    }

    public ClicksmithConfig Clone() => ConfigCodec.Decode(ConfigCodec.Encode(this), validate: false);
}
