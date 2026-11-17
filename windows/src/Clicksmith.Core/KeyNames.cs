namespace Clicksmith.Core;

public static class KeyNames
{
    private static readonly Dictionary<ushort, string> Table = Build();

    private static Dictionary<ushort, string> Build()
    {
        var t = new Dictionary<ushort, string>
        {
            [0x08] = "Backspace", [0x09] = "Tab", [0x0D] = "Enter", [0x1B] = "Esc", [0x20] = "Space",
            [0x21] = "Page Up", [0x22] = "Page Down", [0x23] = "End", [0x24] = "Home",
            [0x25] = "Left", [0x26] = "Up", [0x27] = "Right", [0x28] = "Down",
            [0x2C] = "Print Screen", [0x2D] = "Insert", [0x2E] = "Delete",
            [0xBA] = ";", [0xBB] = "=", [0xBC] = ",", [0xBD] = "-", [0xBE] = ".", [0xBF] = "/", [0xC0] = "`",
            [0xDB] = "[", [0xDC] = "\\", [0xDD] = "]", [0xDE] = "'"
        };
        for (ushort c = 0x30; c <= 0x39; c++) t[c] = ((char)c).ToString();
        for (ushort c = 0x41; c <= 0x5A; c++) t[c] = ((char)c).ToString();
        for (ushort i = 0; i < 24; i++) t[(ushort)(0x70 + i)] = "F" + (i + 1);
        for (ushort i = 0; i < 10; i++) t[(ushort)(0x60 + i)] = "Num " + i;
        return t;
    }

    public static string Name(ushort vk) => Table.TryGetValue(vk, out var n) ? n : $"Key {vk}";

    /// <summary>Keys Windows sends with the extended-key flag.</summary>
    public static bool IsExtended(ushort vk) => vk is >= 0x21 and <= 0x2E || vk is 0x5B or 0x5C or 0x6F || vk is >= 0xA6 and <= 0xB7;

    public static bool IsModifier(ushort vk) => vk is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C or >= 0xA0 and <= 0xA5;

    /// <summary>Shortcuts that are easy to regret.</summary>
    public static string? Warning(KeyShortcut s)
    {
        if (s.Alt && !s.Ctrl && !s.Win && s.Vk == 0x73) return "Alt+F4 closes the active window.";
        if (s.Ctrl && !s.Alt && !s.Win && s.Vk == 0x57) return "Ctrl+W closes the active tab or window.";
        if (s.Win && s.Vk == 0x4C) return "Win+L locks the computer.";
        if (s.Ctrl && s.Alt && s.Vk == 0x2E) return "Ctrl+Alt+Delete can't be sent by apps.";
        if (s.Alt && s.Vk == 0x09) return "Alt+Tab switches apps once and then releases.";
        if (!s.Ctrl && !s.Alt && !s.Win && !s.Shift && s.Vk is >= 0x30 and <= 0x5A)
            return "A single key without modifiers types into text fields.";
        return null;
    }
}

/// <summary>2.1: ready-made, editable suggestions. Nothing is applied until the user saves.</summary>
public sealed record Preset(string Title, string Detail, ClickButton Button, string Label, ButtonAction? Default, string? App = null, string? AppName = null, ButtonAction? AppAction = null);

public static class Presets
{
    private static ButtonAction Keys(ushort vk, string name, bool ctrl = false, bool alt = false, bool shift = false, bool win = false) =>
        new() { Kind = ActionKind.Shortcut, Shortcut = new KeyShortcut { Vk = vk, KeyName = name, Ctrl = ctrl, Alt = alt, Shift = shift, Win = win } };

    public static readonly Preset[] All =
    {
        new("Mute in Teams", "Back mutes your microphone in Microsoft Teams and goes back everywhere else.",
            ClickButton.Back, "Back", null, "ms-teams.exe", "Microsoft Teams", Keys(0x4D, "M", ctrl: true, shift: true)),
        new("Screenshot", "Middle click opens the Windows screenshot tool.",
            ClickButton.Middle, "Middle click", Keys(0x53, "S", shift: true, win: true)),
        new("Play / Pause", "Forward plays or pauses music and video.",
            ClickButton.Forward, "Forward", new ButtonAction { Kind = ActionKind.MediaKey, Media = MediaKey.PlayPause }),
        new("Copy and paste", "Back copies, Forward pastes.",
            ClickButton.Back, "Back", Keys(0x43, "C", ctrl: true)),
        new("Task view", "Forward shows all open windows.",
            ClickButton.Forward, "Forward", Keys(0x09, "Tab", win: true))
    };
}
