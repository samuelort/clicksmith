import Foundation

// MARK: - Device identity

/// Stable identity of a mouse. Serial number is used when the device reports one,
/// so two identical models can be told apart.
public struct DeviceKey: Codable, Hashable, Sendable {
    public var vendorID: Int
    public var productID: Int
    public var serial: String?

    public init(vendorID: Int, productID: Int, serial: String? = nil) {
        self.vendorID = vendorID
        self.productID = productID
        self.serial = (serial?.isEmpty ?? true) ? nil : serial
    }

    /// A mapping saved without a serial applies to every device of that model.
    public func matches(_ device: DeviceKey) -> Bool {
        guard vendorID == device.vendorID, productID == device.productID else { return false }
        guard let serial else { return true }
        return serial == device.serial
    }

    public var shortID: String { String(format: "%04X:%04X", vendorID, productID) }
}

// MARK: - Buttons

/// A button is identified by where its bit lives in the raw HID input report.
/// Same model as the proven rival3-dpi engine (report 0x01, byte 1, mask 0x20).
public struct ButtonID: Codable, Hashable, Sendable {
    public var reportID: UInt8
    public var byteIndex: Int
    public var mask: UInt8

    public init(reportID: UInt8, byteIndex: Int, mask: UInt8) {
        self.reportID = reportID
        self.byteIndex = byteIndex
        self.mask = mask
    }

    public var bitIndex: Int { mask == 0 ? 0 : mask.trailingZeroBitCount }
}

public enum SystemButton {
    /// macOS button numbers used by CGEvent (0 left, 1 right, 2 middle, 3 back, 4 forward).
    public static func name(_ number: Int) -> String {
        switch number {
        case 0: return "Left Click"
        case 1: return "Right Click"
        case 2: return "Middle Click"
        case 3: return "Back"
        case 4: return "Forward"
        default: return "Button \(number + 1)"
        }
    }

    /// Best-effort guess for standard mice: the first data byte carries buttons 1-8 in order.
    public static func guessNumber(for button: ButtonID) -> Int? {
        let firstDataByte = button.reportID == 0 ? 0 : 1
        guard button.byteIndex == firstDataByte, button.mask.nonzeroBitCount == 1 else { return nil }
        return button.bitIndex
    }

    public static func suggestedLabel(for button: ButtonID, device: DeviceKey) -> String {
        if KnownDevices.isRival3WirelessGen2(device), button == KnownDevices.rivalDPI {
            return "DPI Button"
        }
        if let n = guessNumber(for: button) {
            return n <= 4 ? name(n) : "Button \(n + 1)"
        }
        return "Extra Button (byte \(button.byteIndex), bit \(button.bitIndex))"
    }
}

public enum KnownDevices {
    public static let rival = DeviceKey(vendorID: 0x0111, productID: 0x1872)
    public static let rivalDPI = ButtonID(reportID: 0x01, byteIndex: 1, mask: 0x20)
    public static func isRival3WirelessGen2(_ d: DeviceKey) -> Bool {
        d.vendorID == rival.vendorID && d.productID == rival.productID
    }
}

// MARK: - Shortcuts

public struct KeyShortcut: Codable, Hashable, Sendable {
    public var keyCode: UInt16
    public var command: Bool
    public var shift: Bool
    public var option: Bool
    public var control: Bool
    public var function: Bool
    /// Key name captured when recording (e.g. "M", "F5", "←").
    public var keyName: String

    public init(keyCode: UInt16, command: Bool = false, shift: Bool = false, option: Bool = false,
                control: Bool = false, function: Bool = false, keyName: String? = nil) {
        self.keyCode = keyCode
        self.command = command
        self.shift = shift
        self.option = option
        self.control = control
        self.function = function
        self.keyName = keyName ?? KeyNames.name(for: keyCode)
    }

    public var modifierSymbols: String {
        (control ? "⌃" : "") + (option ? "⌥" : "") + (shift ? "⇧" : "") + (command ? "⌘" : "")
    }

    public var display: String { (function ? "fn " : "") + modifierSymbols + keyName }

    public static let screenshot = KeyShortcut(keyCode: 20, command: true, shift: true, keyName: "3")
}

// MARK: - Actions

/// What a button does when pressed.
public enum ActionKind: String, Codable, CaseIterable, Sendable {
    case shortcut, mediaKey, openApp, typeText
    case nothing = "none"

    public var title: String {
        switch self {
        case .shortcut: return "Keyboard shortcut"
        case .mediaKey: return "Media key"
        case .openApp: return "Open app"
        case .typeText: return "Type text"
        case .nothing: return "Nothing"
        }
    }
}

/// System media/brightness keys, sent as NX system-defined events.
public enum MediaKey: String, Codable, CaseIterable, Sendable {
    case playPause, next, previous, volumeUp, volumeDown, mute, brightnessUp, brightnessDown

    public var title: String {
        switch self {
        case .playPause: return "Play / Pause"
        case .next: return "Next Track"
        case .previous: return "Previous Track"
        case .volumeUp: return "Volume Up"
        case .volumeDown: return "Volume Down"
        case .mute: return "Mute"
        case .brightnessUp: return "Brightness Up"
        case .brightnessDown: return "Brightness Down"
        }
    }

    public var symbol: String {
        switch self {
        case .playPause: return "⏯"
        case .next: return "⏭"
        case .previous: return "⏮"
        case .volumeUp: return "🔊"
        case .volumeDown: return "🔉"
        case .mute: return "🔇"
        case .brightnessUp: return "☀︎+"
        case .brightnessDown: return "☀︎−"
        }
    }

    /// NX_KEYTYPE_* values from IOKit/hidsystem/ev_keymap.h.
    public var nxKeyType: Int {
        switch self {
        case .volumeUp: return 0
        case .volumeDown: return 1
        case .brightnessUp: return 2
        case .brightnessDown: return 3
        case .mute: return 7
        case .playPause: return 16
        case .next: return 19       // NX_KEYTYPE_FAST, what the F9 key sends
        case .previous: return 20   // NX_KEYTYPE_REWIND, what the F7 key sends
        }
    }
}

/// Legacy 1.0 action modes, kept only to read schema-1 files.
public enum LegacyActionMode: String, Codable, Sendable {
    case shortcutKeepOriginal, shortcutReplaceOriginal, disableButton, passThrough
}

public struct ButtonAction: Codable, Hashable, Sendable {
    public static let maxTextLength = 500

    public var kind: ActionKind
    /// Block the button's normal macOS action (standard buttons only).
    public var blockOriginal: Bool
    public var shortcut: KeyShortcut?
    public var mediaKey: MediaKey?
    public var appBundleID: String?
    public var appName: String?
    public var text: String?

    public init(kind: ActionKind, blockOriginal: Bool = false, shortcut: KeyShortcut? = nil,
                mediaKey: MediaKey? = nil, appBundleID: String? = nil, appName: String? = nil, text: String? = nil) {
        self.kind = kind
        self.blockOriginal = blockOriginal
        self.shortcut = shortcut
        self.mediaKey = mediaKey
        self.appBundleID = appBundleID
        self.appName = appName
        self.text = text
    }

    public static func sending(_ s: KeyShortcut?, blockOriginal: Bool = false) -> ButtonAction {
        ButtonAction(kind: .shortcut, blockOriginal: blockOriginal, shortcut: s)
    }

    /// Does pressing the button need to trigger something in the engine?
    public var firesOnPress: Bool { kind != .nothing }

    /// Validation problem with this action, or nil when it's complete.
    public var problem: String? {
        switch kind {
        case .shortcut:
            guard let s = shortcut else { return "Record a shortcut." }
            if s.keyCode >= 256 || s.keyName.count > 20 { return "Invalid key." }
        case .mediaKey:
            if mediaKey == nil { return "Choose a media key." }
        case .openApp:
            guard let id = appBundleID, BundleID.isValid(id) else { return "Choose an app to open." }
        case .typeText:
            guard let t = text, !t.isEmpty else { return "Enter the text to type." }
            if t.count > Self.maxTextLength { return "Text is longer than \(Self.maxTextLength) characters." }
            if t.unicodeScalars.contains(where: { $0.value < 0x20 && $0 != "\n" && $0 != "\t" }) {
                return "Text contains control characters."
            }
        case .nothing:
            break
        }
        return nil
    }

    public var display: String {
        switch kind {
        case .shortcut: return shortcut?.display ?? "—"
        case .mediaKey: return mediaKey.map { "\($0.symbol) \($0.title)" } ?? "—"
        case .openApp: return "Open \(appName ?? appBundleID ?? "app")"
        case .typeText:
            let t = (text ?? "").replacingOccurrences(of: "\n", with: " ")
            return "Type “\(t.count > 18 ? String(t.prefix(18)) + "…" : t)”"
        case .nothing: return blockOriginal ? "Disabled" : "Default behaviour"
        }
    }

    public var summary: String {
        kind == .nothing ? (blockOriginal ? "Button disabled" : "No action")
                         : kind.title + (blockOriginal ? " · replaces original" : "")
    }

    // Reads both the 1.1+ format and the 1.0 {mode, shortcut} format.
    private enum CodingKeys: String, CodingKey { case kind, blockOriginal, shortcut, mediaKey, appBundleID, appName, text, mode }

    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        shortcut = try c.decodeIfPresent(KeyShortcut.self, forKey: .shortcut)
        mediaKey = try c.decodeIfPresent(MediaKey.self, forKey: .mediaKey)
        appBundleID = try c.decodeIfPresent(String.self, forKey: .appBundleID)
        appName = try c.decodeIfPresent(String.self, forKey: .appName)
        text = try c.decodeIfPresent(String.self, forKey: .text)
        if let k = try c.decodeIfPresent(ActionKind.self, forKey: .kind) {
            kind = k
            blockOriginal = try c.decodeIfPresent(Bool.self, forKey: .blockOriginal) ?? false
        } else {
            switch try c.decode(LegacyActionMode.self, forKey: .mode) {
            case .shortcutKeepOriginal: kind = .shortcut; blockOriginal = false
            case .shortcutReplaceOriginal: kind = .shortcut; blockOriginal = true
            case .disableButton: kind = .nothing; blockOriginal = true
            case .passThrough: kind = .nothing; blockOriginal = false
            }
        }
    }

    public func encode(to encoder: Encoder) throws {
        var c = encoder.container(keyedBy: CodingKeys.self)
        try c.encode(kind, forKey: .kind)
        try c.encode(blockOriginal, forKey: .blockOriginal)
        try c.encodeIfPresent(shortcut, forKey: .shortcut)
        try c.encodeIfPresent(mediaKey, forKey: .mediaKey)
        try c.encodeIfPresent(appBundleID, forKey: .appBundleID)
        try c.encodeIfPresent(appName, forKey: .appName)
        try c.encodeIfPresent(text, forKey: .text)
    }
}

public enum BundleID {
    public static func isValid(_ id: String) -> Bool {
        guard (1...200).contains(id.count) else { return false }
        return id.unicodeScalars.allSatisfy { s in
            (s >= "a" && s <= "z") || (s >= "A" && s <= "Z") || (s >= "0" && s <= "9") || s == "." || s == "-" || s == "_"
        }
    }
}

// MARK: - Mappings

public struct AppOverride: Codable, Hashable, Identifiable, Sendable {
    public var id: UUID
    public var bundleID: String
    public var appName: String
    public var action: ButtonAction
    public var enabled: Bool

    public init(id: UUID = UUID(), bundleID: String, appName: String, action: ButtonAction, enabled: Bool = true) {
        self.id = id
        self.bundleID = bundleID
        self.appName = appName
        self.action = action
        self.enabled = enabled
    }
}

public struct ButtonMapping: Codable, Hashable, Identifiable, Sendable {
    public var id: UUID
    public var device: DeviceKey
    public var deviceName: String
    public var button: ButtonID
    public var label: String
    /// CGEvent button number, needed only to block a standard button's original action.
    public var systemButtonNumber: Int?
    /// Global action. nil = no global action (only app overrides apply).
    public var defaultAction: ButtonAction?
    public var overrides: [AppOverride]
    public var enabled: Bool

    public init(id: UUID = UUID(), device: DeviceKey, deviceName: String, button: ButtonID, label: String,
                systemButtonNumber: Int? = nil, defaultAction: ButtonAction?, overrides: [AppOverride] = [],
                enabled: Bool = true) {
        self.id = id
        self.device = device
        self.deviceName = deviceName
        self.button = button
        self.label = label
        self.systemButtonNumber = systemButtonNumber
        self.defaultAction = defaultAction
        self.overrides = overrides
        self.enabled = enabled
    }

    /// Effective action for the given frontmost app. Exact bundle ID wins, then global.
    public func action(forBundleID bundleID: String?) -> ButtonAction? {
        if let bundleID, let o = overrides.first(where: { $0.enabled && $0.bundleID == bundleID }) {
            return o.action
        }
        return defaultAction
    }
}

// MARK: - Settings and config

public struct EngineSettings: Codable, Hashable, Sendable {
    public var paused: Bool
    public var engineEnabled: Bool
    public var launchEngineAtLogin: Bool
    public var wakeRestartDelay: Double
    public var duplicateGuardMs: Int
    public var emergencyHotKeyEnabled: Bool
    public var allowPrimaryButtonSuppression: Bool
    public var diagnosticModeUntil: Date?
    public var onboardingCompleted: Bool
    public var showMenuBarIcon: Bool
    public var showDockIcon: Bool
    public var showBuiltInDevices: Bool

    public init() {
        paused = false
        engineEnabled = true
        launchEngineAtLogin = true
        wakeRestartDelay = 2.0
        duplicateGuardMs = 250
        emergencyHotKeyEnabled = true
        allowPrimaryButtonSuppression = false
        diagnosticModeUntil = nil
        onboardingCompleted = false
        showMenuBarIcon = true
        showDockIcon = false
        showBuiltInDevices = false
    }

    public var diagnosticModeActive: Bool {
        guard let until = diagnosticModeUntil else { return false }
        return until > Date()
    }

    /// The emergency shortcut can't be switched off while left/right-click blocking is
    /// allowed, so there is always a way out.
    public var emergencyHotKeyEffective: Bool { emergencyHotKeyEnabled || allowPrimaryButtonSuppression }

    // Tolerant decoding: new settings get defaults when reading older files.
    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        let d = EngineSettings()
        paused = try c.decodeIfPresent(Bool.self, forKey: .paused) ?? d.paused
        engineEnabled = try c.decodeIfPresent(Bool.self, forKey: .engineEnabled) ?? d.engineEnabled
        launchEngineAtLogin = try c.decodeIfPresent(Bool.self, forKey: .launchEngineAtLogin) ?? d.launchEngineAtLogin
        wakeRestartDelay = try c.decodeIfPresent(Double.self, forKey: .wakeRestartDelay) ?? d.wakeRestartDelay
        duplicateGuardMs = try c.decodeIfPresent(Int.self, forKey: .duplicateGuardMs) ?? d.duplicateGuardMs
        emergencyHotKeyEnabled = try c.decodeIfPresent(Bool.self, forKey: .emergencyHotKeyEnabled) ?? d.emergencyHotKeyEnabled
        allowPrimaryButtonSuppression = try c.decodeIfPresent(Bool.self, forKey: .allowPrimaryButtonSuppression) ?? d.allowPrimaryButtonSuppression
        diagnosticModeUntil = try c.decodeIfPresent(Date.self, forKey: .diagnosticModeUntil)
        onboardingCompleted = try c.decodeIfPresent(Bool.self, forKey: .onboardingCompleted) ?? d.onboardingCompleted
        showMenuBarIcon = try c.decodeIfPresent(Bool.self, forKey: .showMenuBarIcon) ?? d.showMenuBarIcon
        showDockIcon = try c.decodeIfPresent(Bool.self, forKey: .showDockIcon) ?? d.showDockIcon
        showBuiltInDevices = try c.decodeIfPresent(Bool.self, forKey: .showBuiltInDevices) ?? d.showBuiltInDevices
    }
}

public struct ClicksmithConfig: Codable, Hashable, Sendable {
    public static let currentSchemaVersion = 2

    public var schemaVersion: Int
    public var settings: EngineSettings
    public var mappings: [ButtonMapping]

    public init(settings: EngineSettings = EngineSettings(), mappings: [ButtonMapping] = []) {
        self.schemaVersion = Self.currentSchemaVersion
        self.settings = settings
        self.mappings = mappings
    }
}
