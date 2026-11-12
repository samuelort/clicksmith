import Foundation

/// One health state drives the header, menu bar, setup card, Engine and Mice cards.
/// 2.0: lives in Core so every rule is covered by the self-tests.
public enum HealthState: Equatable, Sendable {
    case ready
    case paused(emergency: Bool)
    case needsSetup(missing: Int)
    case noMouse
    case stopped
}

public enum HealthModel {
    /// Priority: stopped → missing permissions → emergency pause → pause → no mouse → ready.
    public static func evaluate(engineRunning: Bool, inputMonitoringGranted: Bool, accessibilityGranted: Bool,
                                emergencyPaused: Bool, paused: Bool, connectedMice: Int) -> HealthState {
        guard engineRunning else { return .stopped }
        let missing = (inputMonitoringGranted ? 0 : 1) + (accessibilityGranted ? 0 : 1)
        if missing > 0 { return .needsSetup(missing: missing) }
        if emergencyPaused { return .paused(emergency: true) }
        if paused { return .paused(emergency: false) }
        if connectedMice <= 0 { return .noMouse }
        return .ready
    }
}

/// Where "Open app" actions may launch from. An app with a look-alike bundle ID sitting in
/// Downloads or a temporary folder is never opened — only installed apps.
public enum AppLocationPolicy {
    public static func isAllowed(path: String, home: String) -> Bool {
        let p = (path as NSString).standardizingPath
        guard p.hasSuffix(".app") else { return false }
        let roots = [
            "/Applications/",
            "/System/Applications/",
            "/System/Library/CoreServices/",                              // Finder
            "/System/Volumes/Preboot/Cryptexes/App/System/Applications/", // Safari on macOS 13+
            (home as NSString).standardizingPath + "/Applications/"
        ]
        return roots.contains { p.hasPrefix($0) }
    }
}
