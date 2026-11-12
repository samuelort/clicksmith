import Foundation

/// Human-readable names for macOS virtual key codes (ANSI layout).
public enum KeyNames {
    private static let table: [UInt16: String] = [
        0: "A", 1: "S", 2: "D", 3: "F", 4: "H", 5: "G", 6: "Z", 7: "X", 8: "C", 9: "V",
        11: "B", 12: "Q", 13: "W", 14: "E", 15: "R", 16: "Y", 17: "T", 18: "1", 19: "2",
        20: "3", 21: "4", 22: "6", 23: "5", 24: "=", 25: "9", 26: "7", 27: "-", 28: "8",
        29: "0", 30: "]", 31: "O", 32: "U", 33: "[", 34: "I", 35: "P", 36: "↩", 37: "L",
        38: "J", 39: "'", 40: "K", 41: ";", 42: "\\", 43: ",", 44: "/", 45: "N", 46: "M",
        47: ".", 48: "⇥", 49: "Space", 50: "`", 51: "⌫", 53: "⎋", 117: "⌦",
        122: "F1", 120: "F2", 99: "F3", 118: "F4", 96: "F5", 97: "F6", 98: "F7", 100: "F8",
        101: "F9", 109: "F10", 103: "F11", 111: "F12", 105: "F13", 107: "F14", 113: "F15",
        106: "F16", 64: "F17", 79: "F18", 80: "F19", 90: "F20",
        123: "←", 124: "→", 125: "↓", 126: "↑", 115: "Home", 119: "End", 116: "Page Up", 121: "Page Down"
    ]

    public static func name(for keyCode: UInt16) -> String {
        table[keyCode] ?? "Key \(keyCode)"
    }

    /// Shortcuts that macOS reserves or that are easy to regret.
    public static func warning(for s: KeyShortcut) -> String? {
        let mods = (s.command, s.shift, s.option, s.control)
        switch (s.keyCode, mods) {
        case (12, (true, false, false, false)): return "⌘Q quits the active app."
        case (12, (true, false, false, true)): return "⌃⌘Q locks the screen."
        case (13, (true, false, false, false)): return "⌘W closes the active window."
        case (48, (true, _, _, _)): return "⌘⇥ is the app switcher."
        case (49, (true, false, false, false)): return "⌘Space opens Spotlight."
        case (53, (true, false, true, false)): return "⌥⌘⎋ opens Force Quit."
        case (51, (true, false, false, false)): return "⌘⌫ moves files to the Bin in Finder."
        default: break
        }
        if !s.command && !s.control && !s.option && !s.function && s.keyName.count == 1 {
            return "A single key without modifiers will type into text fields."
        }
        return nil
    }
}
