import Foundation

// Pure, testable logic used by the engine's input path.

/// One button the engine watches on a connected device for the current frontmost app.
public struct WatchedButton: Hashable, Sendable {
    public let button: ButtonID
    public let action: ButtonAction
    public let mappingID: UUID
}

/// Resolves the active mapping table. Called on config load, device connect and app switch —
/// never per mouse press.
public struct CompiledConfig: Sendable {
    public let mappings: [ButtonMapping]
    public let settings: EngineSettings

    public init(_ config: ClicksmithConfig) {
        mappings = config.mappings.filter(\.enabled)
        settings = config.settings
    }

    /// Buttons to watch on `device` while `bundleID` is frontmost.
    public func watched(for device: DeviceKey, bundleID: String?) -> [WatchedButton] {
        var result: [WatchedButton] = []
        for m in mappings where m.device.matches(device) {
            guard let action = m.action(forBundleID: bundleID), action.firesOnPress, action.problem == nil else { continue }
            result.append(WatchedButton(button: m.button, action: action, mappingID: m.id))
        }
        return result
    }

    /// CGEvent button numbers whose original action should be blocked while `bundleID` is frontmost.
    /// Left/right click are never included unless explicitly allowed.
    public func suppressedSystemButtons(bundleID: String?) -> Set<Int> {
        var set = Set<Int>()
        for m in mappings {
            guard let n = m.systemButtonNumber, let a = m.action(forBundleID: bundleID), a.blockOriginal else { continue }
            if n <= 1 && !settings.allowPrimaryButtonSuppression { continue }
            set.insert(n)
        }
        return set
    }

    /// Name of the profile in effect, for the UI.
    public func profileName(bundleID: String?, appName: String?) -> String {
        guard let bundleID else { return "Default" }
        let hasOverride = mappings.contains { $0.overrides.contains { $0.enabled && $0.bundleID == bundleID } }
        return hasOverride ? (appName ?? bundleID) : "Default"
    }

    /// Does switching between these two apps change anything the engine does?
    /// Used to skip rebuilding watch lists and the event tap on irrelevant app switches.
    public func profileDiffers(_ a: String?, _ b: String?) -> Bool {
        guard a != b else { return false }
        return mappings.contains { m in
            m.overrides.contains { $0.enabled && ($0.bundleID == a || $0.bundleID == b) }
        }
    }
}

/// Press-edge detection for one button (fires on released → pressed only).
public struct EdgeDetector: Sendable {
    public let button: ButtonID
    public private(set) var pressed = false

    public init(button: ButtonID) { self.button = button }

    public mutating func process(reportID: UInt8, bytes: [UInt8]) -> Bool {
        guard ReportMatcher.matches(button: button, reportID: reportID, bytes: bytes),
              button.byteIndex < bytes.count else { return false }
        let now = (bytes[button.byteIndex] & button.mask) != 0
        defer { pressed = now }
        return now && !pressed
    }
}

public enum ReportMatcher {
    /// Matches the IOKit report ID, with a fallback for devices that report ID 0 but
    /// still prefix the buffer with the ID byte (the rival3-dpi engine relied on bytes[0]).
    @inline(__always)
    public static func matches(button: ButtonID, reportID: UInt8, bytes: [UInt8]) -> Bool {
        if button.reportID == reportID { return true }
        return reportID == 0 && !bytes.isEmpty && bytes[0] == button.reportID
    }
}

/// Duplicate-press guard, per button.
public final class PressGuard {
    private let intervalNs: UInt64
    private var last: [ButtonID: UInt64] = [:]

    public init(intervalMs: Int) { intervalNs = UInt64(max(0, intervalMs)) * 1_000_000 }

    public func allow(_ button: ButtonID, now: UInt64) -> Bool {
        if let l = last[button], now &- l < intervalNs { return false }
        last[button] = now
        return true
    }
}

/// Tracks which report bytes behave like movement/wheel data so button learning ignores them.
/// Button bytes change one bit at a time and take few distinct values; movement bytes don't.
public struct VolatilityTracker: Sendable {
    private var seen: [Int: Set<UInt8>] = [:]
    private var volatile: Set<Int> = []

    public init() {}

    public static func key(reportID: UInt8, byte: Int) -> Int { Int(reportID) << 16 | byte }

    public mutating func observe(reportID: UInt8, byte: Int, old: UInt8, new: UInt8) {
        let k = Self.key(reportID: reportID, byte: byte)
        if volatile.contains(k) { return }
        if (old ^ new).nonzeroBitCount > 2 { volatile.insert(k); return }
        var s = seen[k, default: []]
        s.insert(new)
        if s.count > 12 { volatile.insert(k); seen[k] = nil } else { seen[k] = s }
    }

    public func isVolatile(reportID: UInt8, byte: Int) -> Bool {
        volatile.contains(Self.key(reportID: reportID, byte: byte))
    }
}

/// Learns buttons from raw reports: a single bit must rise in a stable byte and then fall again.
public struct ButtonLearner: Sendable {
    private var pending: [ButtonID: Date] = [:]
    public var armed = true

    public init() {}

    public mutating func observe(reportID: UInt8, previous: [UInt8], current: [UInt8],
                                 tracker: VolatilityTracker, now: Date = Date()) -> [ButtonID] {
        let start = reportID == 0 ? 0 : 1
        var confirmed: [ButtonID] = []
        guard current.count > start else { return [] }
        for i in start..<current.count {
            let old = i < previous.count ? previous[i] : 0
            let new = current[i]
            guard old != new else { continue }
            if tracker.isVolatile(reportID: reportID, byte: i) { continue }
            let rising = new & ~old
            let falling = old & ~new
            if armed, rising.nonzeroBitCount == 1 {
                pending[ButtonID(reportID: reportID, byteIndex: i, mask: rising)] = now
            }
            if falling != 0 {
                for bit in 0..<8 where falling & (1 << bit) != 0 {
                    let id = ButtonID(reportID: reportID, byteIndex: i, mask: UInt8(1 << bit))
                    if let t = pending.removeValue(forKey: id), now.timeIntervalSince(t) < 5 {
                        confirmed.append(id)
                    }
                }
            }
        }
        return confirmed
    }
}
