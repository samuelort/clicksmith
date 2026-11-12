import Foundation
import AppKit
import ServiceManagement
import ClicksmithCore

/// Manages the engine's per-user launchd job — the same LaunchAgent model proven by
/// rival3-dpi v3.5 (KeepAlive restarts the engine after its clean wake exit).
///
/// - "Start at login" ON  → plist in ~/Library/LaunchAgents (loaded at every login)
/// - "Start at login" OFF → plist in Application Support, loaded for this session only
/// All methods that run launchctl block, so call them through `perform` (background queue).
final class EngineController {
    let label = ClicksmithInfo.engineLabel

    var engineAppURL: URL {
        Bundle.main.bundleURL.appendingPathComponent("Contents/Library/LoginItems/Clicksmith Engine.app")
    }
    var engineExecutable: URL { engineAppURL.appendingPathComponent("Contents/MacOS/ClicksmithEngine") }
    var isBundled: Bool { FileManager.default.isExecutableFile(atPath: engineExecutable.path) }

    private var loginPlist: URL {
        FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent("Library/LaunchAgents/\(label).plist")
    }
    private var sessionPlist: URL { ClicksmithPaths.supportDirectory.appendingPathComponent("\(label).plist") }

    var isInstalled: Bool {
        FileManager.default.fileExists(atPath: loginPlist.path) || FileManager.default.fileExists(atPath: sessionPlist.path)
    }

    private var domain: String { "gui/\(getuid())" }

    func perform(_ work: @escaping () -> String?, completion: @escaping (String?) -> Void) {
        DispatchQueue.global(qos: .userInitiated).async {
            let message = work()
            DispatchQueue.main.async { completion(message) }
        }
    }

    /// Installs or reinstalls the job and starts it. Returns an error message or nil.
    func install(atLogin: Bool) -> String? {
        guard isBundled else { return "Engine not found inside the app bundle. Run scripts/install.sh." }
        stopAndWait()
        ClicksmithPaths.ensureDirectory()
        try? FileManager.default.createDirectory(at: loginPlist.deletingLastPathComponent(), withIntermediateDirectories: true)
        let target = atLogin ? loginPlist : sessionPlist
        let other = atLogin ? sessionPlist : loginPlist
        try? FileManager.default.removeItem(at: other)
        do {
            try plistData().write(to: target, options: .atomic)
            try? FileManager.default.setAttributes([.posixPermissions: 0o600], ofItemAtPath: target.path)
        } catch {
            return "Could not write launch agent: \(error.localizedDescription)"
        }
        var lastError = ""
        for attempt in 1...5 {   // retry for the launchd bootout race
            let (code, out) = launchctl(["bootstrap", domain, target.path])
            if code == 0 { return nil }
            lastError = out
            Thread.sleep(forTimeInterval: Double(attempt) * 0.4)
        }
        return "launchctl bootstrap failed: \(lastError)"
    }

    func uninstall() -> String? {
        stopAndWait()
        try? FileManager.default.removeItem(at: loginPlist)
        try? FileManager.default.removeItem(at: sessionPlist)
        return nil
    }

    func restart() -> String? {
        let (code, out) = launchctl(["kickstart", "-k", "\(domain)/\(label)"])
        return code == 0 ? nil : "Restart failed: \(out)"
    }

    func isLoaded() -> Bool { launchctl(["print", "\(domain)/\(label)"]).0 == 0 }

    /// Makes sure the engine runs if it should, and that the launch agent points at this
    /// copy of the app (e.g. after moving it to /Applications).
    func ensureRunning(settings: EngineSettings) -> String? {
        guard settings.engineEnabled else { return nil }
        let expected = settings.launchEngineAtLogin ? loginPlist : sessionPlist
        let pointsHere = (NSDictionary(contentsOf: expected)?["Program"] as? String) == engineExecutable.path
        if pointsHere && isLoaded() { return nil }
        return install(atLogin: settings.launchEngineAtLogin)
    }

    private func stopAndWait() {
        _ = launchctl(["bootout", "\(domain)/\(label)"])
        for _ in 0..<20 where isLoaded() { Thread.sleep(forTimeInterval: 0.15) }
    }

    private func plistData() throws -> Data {
        let dict: [String: Any] = [
            "Label": label,
            "Program": engineExecutable.path,
            "ProgramArguments": [engineExecutable.path],
            "RunAtLoad": true,
            "KeepAlive": true,
            "ThrottleInterval": 3,
            "ProcessType": "Interactive",
            "LimitLoadToSessionType": "Aqua",
            "AssociatedBundleIdentifiers": [Bundle.main.bundleIdentifier ?? "app.clicksmith.Clicksmith"],
            "StandardOutPath": ClicksmithPaths.logFile.path,
            "StandardErrorPath": ClicksmithPaths.logFile.path
        ]
        return try PropertyListSerialization.data(fromPropertyList: dict, format: .xml, options: 0)
    }

    @discardableResult
    func launchctl(_ args: [String]) -> (Int32, String) {
        let p = Process()
        p.executableURL = URL(fileURLWithPath: "/bin/launchctl")
        p.arguments = args
        let pipe = Pipe()
        p.standardOutput = pipe
        p.standardError = pipe
        do { try p.run() } catch { return (-1, error.localizedDescription) }
        let data = pipe.fileHandleForReading.readDataToEndOfFile()   // read before wait: no pipe deadlock
        p.waitUntilExit()
        let out = String(data: data, encoding: .utf8) ?? ""
        return (p.terminationStatus, out.trimmingCharacters(in: .whitespacesAndNewlines))
    }
}

/// "Open Clicksmith at login" for the menu bar app itself.
enum MenuBarLoginItem {
    static var isEnabled: Bool { SMAppService.mainApp.status == .enabled }

    static func set(_ enabled: Bool) -> String? {
        do {
            if enabled { try SMAppService.mainApp.register() } else { try SMAppService.mainApp.unregister() }
            return nil
        } catch {
            return "macOS refused: \(error.localizedDescription). Make sure Clicksmith is in /Applications."
        }
    }
}

/// One-time migration from the stand-alone rival3-dpi engine, so the DPI button doesn't fire twice.
enum LegacyMigration {
    static var legacyPlist: URL {
        FileManager.default.homeDirectoryForCurrentUser
            .appendingPathComponent("Library/LaunchAgents/\(ClicksmithInfo.legacyRivalLabel).plist")
    }
    static var backupPlist: URL { ClicksmithPaths.supportDirectory.appendingPathComponent("rival3-dpi.plist.backup") }
    static var isLegacyInstalled: Bool { FileManager.default.fileExists(atPath: legacyPlist.path) }
    static var canRestore: Bool { FileManager.default.fileExists(atPath: backupPlist.path) && !isLegacyInstalled }

    /// Stops rival3-dpi (files kept as a backup) and returns the equivalent Clicksmith mapping.
    static func migrate(using engine: EngineController) -> ButtonMapping {
        engine.launchctl(["bootout", "gui/\(getuid())/\(ClicksmithInfo.legacyRivalLabel)"])
        ClicksmithPaths.ensureDirectory()
        try? FileManager.default.removeItem(at: backupPlist)
        try? FileManager.default.moveItem(at: legacyPlist, to: backupPlist)
        return ButtonMapping(device: KnownDevices.rival, deviceName: "Rival 3 Wireless Gen 2",
                             button: KnownDevices.rivalDPI, label: "DPI Button", systemButtonNumber: nil,
                             defaultAction: ButtonAction.sending(.screenshot))
    }

    static func restore(using engine: EngineController) -> String? {
        do { try FileManager.default.moveItem(at: backupPlist, to: legacyPlist) }
        catch { return error.localizedDescription }
        let (code, out) = engine.launchctl(["bootstrap", "gui/\(getuid())", legacyPlist.path])
        return code == 0 ? nil : out
    }
}
