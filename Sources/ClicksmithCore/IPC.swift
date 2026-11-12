import Foundation

/// App ↔ engine communication: JSON files in Application Support + Darwin notifications as
/// "something changed" pings. Notifications carry no data and no command that can inject
/// keystrokes — every action still requires a physical mouse press.
public enum IPCName {
    public static let configChanged = "app.clicksmith.config-changed"
    public static let statusChanged = "app.clicksmith.status-changed"
    public static let learnStart = "app.clicksmith.learn-start"
    public static let learnStop = "app.clicksmith.learn-stop"
    public static let learnResult = "app.clicksmith.learn-result"
    public static let resumeFromEmergency = "app.clicksmith.resume"
}

public enum DarwinNotify {
    public static func post(_ name: String) {
        let center = CFNotificationCenterGetDarwinNotifyCenter()
        CFNotificationCenterPostNotification(center, CFNotificationName(name as CFString), nil, nil, true)
    }
}

public struct DeviceInfo: Codable, Hashable, Identifiable, Sendable {
    public var key: DeviceKey
    public var name: String
    public var manufacturer: String?
    public var transport: String?
    public var declaredButtons: Int
    public var connected: Bool
    public var lastConnected: Date?
    /// Built-in trackpad/keyboard. Optional so status files from 1.0 still decode.
    public var builtIn: Bool?
    /// Whether the engine is currently reading this device's reports.
    public var listening: Bool?

    public var id: String { key.shortID + (key.serial ?? "") }
    public var isBuiltIn: Bool { builtIn ?? false }

    public init(key: DeviceKey, name: String, manufacturer: String?, transport: String?,
                declaredButtons: Int, connected: Bool, lastConnected: Date?, builtIn: Bool = false, listening: Bool = false) {
        self.key = key
        self.name = name
        self.manufacturer = manufacturer
        self.transport = transport
        self.declaredButtons = declaredButtons
        self.connected = connected
        self.lastConnected = lastConnected
        self.builtIn = builtIn
        self.listening = listening
    }
}

public struct EngineStatus: Codable, Hashable, Sendable {
    public var engineVersion: String
    public var pid: Int32
    public var startedAt: Date
    public var startedAfterWake: Bool
    public var lastSleep: Date?
    public var lastWake: Date?
    public var lastWakeRecovery: Date?
    public var lastHIDStart: Date?
    public var lastEnumeration: Date?
    public var lastError: String?
    public var inputMonitoring: String   // granted / denied / unknown
    public var accessibility: Bool
    public var devices: [DeviceInfo]
    public var frontmostBundleID: String?
    public var frontmostAppName: String?
    /// The last app in front that isn't Clicksmith itself, for the UI.
    public var lastExternalBundleID: String?
    public var lastExternalAppName: String?
    public var activeProfile: String
    public var paused: Bool
    public var emergencyPaused: Bool
    public var suppressionActive: Bool
    public var activeMappings: Int
    public var pendingWakeRestart: Bool
    /// Application-side processing time (HID callback → event posted). Only measured in diagnostic mode.
    public var latencyAvgMicros: Double?
    public var latencyMaxMicros: Double?
    /// HID interfaces whose reports the engine currently reads (1.1+).
    public var listeningDevices: Int?

    public init(engineVersion: String, pid: Int32) {
        self.engineVersion = engineVersion
        self.pid = pid
        startedAt = Date()
        startedAfterWake = false
        inputMonitoring = "unknown"
        accessibility = false
        devices = []
        activeProfile = "Default"
        paused = false
        emergencyPaused = false
        suppressionActive = false
        activeMappings = 0
        pendingWakeRestart = false
    }
}

/// Written by the app right before asking the engine to resume after an emergency pause,
/// so a notification alone (which any local process can post) can't undo the pause.
public struct ResumeRequest: Codable, Hashable, Sendable {
    public var at: Date
    public init() { at = Date() }
    public var isFresh: Bool { abs(Date().timeIntervalSince(at)) < 10 }
}

/// Which devices the engine reads reports from. External mice are always read (cheap, and
/// keeps button learning accurate); built-in trackpads and keyboards only when needed.
public enum AttachPolicy {
    public static func shouldListen(builtIn: Bool, hasWatchedButtons: Bool, learningTargetsDevice: Bool) -> Bool {
        !builtIn || hasWatchedButtons || learningTargetsDevice
    }

    public static func looksBuiltIn(builtInProperty: Bool, transport: String?) -> Bool {
        builtInProperty || transport == "SPI" || transport == "FIFO"
    }
}

public struct LearnRequest: Codable, Hashable, Sendable {
    public enum Mode: String, Codable, Sendable { case single, scan }
    public var id: UUID
    public var device: DeviceKey?
    public var mode: Mode
    public var startedAt: Date

    public init(device: DeviceKey?, mode: Mode) {
        id = UUID()
        self.device = device
        self.mode = mode
        startedAt = Date()
    }

    public var timeout: TimeInterval { mode == .scan ? 60 : 20 }
}

public struct DetectedButton: Codable, Hashable, Identifiable, Sendable {
    public var device: DeviceKey
    public var deviceName: String
    public var button: ButtonID
    public var suggestedLabel: String
    public var systemButtonNumber: Int?
    public var id: String { device.shortID + "-\(button.reportID)-\(button.byteIndex)-\(button.mask)" }

    public init(device: DeviceKey, deviceName: String, button: ButtonID) {
        self.device = device
        self.deviceName = deviceName
        self.button = button
        suggestedLabel = SystemButton.suggestedLabel(for: button, device: device)
        systemButtonNumber = SystemButton.guessNumber(for: button)
    }
}

public struct LearnResult: Codable, Hashable, Sendable {
    public var requestID: UUID
    public var buttons: [DetectedButton]
    public var finished: Bool

    public init(requestID: UUID, buttons: [DetectedButton], finished: Bool) {
        self.requestID = requestID
        self.buttons = buttons
        self.finished = finished
    }
}

public enum IPCFiles {
    public static func write<T: Encodable>(_ value: T, to url: URL) {
        ClicksmithPaths.ensureDirectory()
        if let data = try? ConfigCodec.encoder().encode(value) { try? AtomicFile.write(data, to: url) }
    }

    public static func read<T: Decodable>(_ type: T.Type, from url: URL) -> T? {
        guard FileTrust.isTrusted(url), let data = try? Data(contentsOf: url), data.count < 1_000_000 else { return nil }
        return try? ConfigCodec.decoder().decode(type, from: data)
    }
}

public enum ClicksmithInfo {
    public static let version = "1.2.6"
    public static let engineLabel = "app.clicksmith.engine"
    public static let legacyRivalLabel = "se.samuel.rival3-dpi"

    /// True for Clicksmith's own app and engine bundle IDs.
    public static func isOwnBundle(_ id: String?) -> Bool {
        guard let id else { return false }
        return id.hasPrefix("app.clicksmith.")
    }
}
