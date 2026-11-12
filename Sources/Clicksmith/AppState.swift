import SwiftUI
import AppKit
import ClicksmithCore

/// Four sections (since 1.2).
enum SidebarItem: String, CaseIterable, Identifiable, Hashable {
    case overview, buttons, apps, settings
    var id: String { rawValue }
    var title: String {
        switch self {
        case .overview: return "Overview"
        case .buttons: return "Buttons"
        case .apps: return "Apps"
        case .settings: return "Settings"
        }
    }
    var icon: String {
        switch self {
        case .overview: return "gauge.with.dots.needle.33percent"
        case .buttons: return "computermouse"
        case .apps: return "square.grid.2x2"
        case .settings: return "gearshape"
        }
    }
}

enum SettingsTab: String, CaseIterable, Identifiable, Hashable {
    case general, permissions, diagnostics, about
    var id: String { rawValue }
    var title: String {
        switch self {
        case .general: return "General"
        case .permissions: return "Permissions"
        case .diagnostics: return "Diagnostics"
        case .about: return "About"
        }
    }
}

enum EngineVisualState { case active, paused, stopped }

/// An app with its own button behaviour.
struct AppProfile: Identifiable, Hashable {
    let id: String          // bundle ID
    let name: String
    let overrideCount: Int
}

/// Single source of truth for the UI, main thread only.
/// The UI never sits in the input path: it only writes config.json and pings the engine.
/// Every @Published property is assigned only when its value really changes.
final class AppState: ObservableObject {
    static let shared = AppState()

    @Published private(set) var config: ClicksmithConfig
    @Published var configLoadError: String?
    @Published var saveError: String?
    @Published private(set) var status: EngineStatus?
    @Published private(set) var engineRunning = false
    @Published private(set) var engineInstalled = false
    @Published var engineMessage: String?
    @Published var selection: SidebarItem = .overview
    @Published var settingsTab: SettingsTab = .general
    @Published var selectedDeviceID: String?

    @Published private(set) var activeLearn: LearnRequest?
    @Published private(set) var learnResult: LearnResult?

    let engine = EngineController()
    private var timer: Timer?

    private init() {
        do { config = try ConfigStore.load() }
        catch {
            config = ClicksmithConfig()
            configLoadError = "\(error) Your existing file was left untouched; saving will replace it."
        }
        refreshStatus()
        observeEngine()
        // The engine announces every status change; this slow timer only catches a crashed engine.
        timer = Timer.scheduledTimer(withTimeInterval: 30, repeats: true) { [weak self] _ in self?.refreshStatus() }
        timer?.tolerance = 5
    }

    // MARK: - Navigation

    func openSettings(_ tab: SettingsTab) {
        selection = .settings
        settingsTab = tab
    }

    // MARK: - Config

    func update(_ body: (inout ClicksmithConfig) -> Void) {
        var c = config
        body(&c)
        guard c != config else { return }   // no-op changes never save or re-render
        do {
            try ConfigStore.save(c)
            config = c
            if saveError != nil { saveError = nil }
            if configLoadError != nil { configLoadError = nil }
            DarwinNotify.post(IPCName.configChanged)
        } catch {
            saveError = "\(error)"
        }
    }

    func upsert(_ mapping: ButtonMapping) {
        update { c in
            if let i = c.mappings.firstIndex(where: { $0.id == mapping.id }) { c.mappings[i] = mapping }
            else { c.mappings.append(mapping) }
        }
    }

    func delete(_ id: UUID) { update { $0.mappings.removeAll { $0.id == id } } }

    func duplicate(_ m: ButtonMapping) {
        var copy = m
        copy.id = UUID()
        copy.label += " copy"
        copy.enabled = false
        upsert(copy)
    }

    func setPaused(_ paused: Bool) { update { $0.settings.paused = paused } }

    func toggleMapping(_ m: ButtonMapping, enabled: Bool) {
        update { c in
            if let i = c.mappings.firstIndex(where: { $0.id == m.id }) { c.mappings[i].enabled = enabled }
        }
    }

    func toggleOverride(mappingID: UUID, overrideID: UUID, enabled: Bool) {
        update { c in
            guard let i = c.mappings.firstIndex(where: { $0.id == mappingID }),
                  let j = c.mappings[i].overrides.firstIndex(where: { $0.id == overrideID }) else { return }
            c.mappings[i].overrides[j].enabled = enabled
        }
    }

    /// Removes every override for one app (the app's own profile).
    func removeApp(_ bundleID: String) {
        update { c in
            for i in c.mappings.indices { c.mappings[i].overrides.removeAll { $0.bundleID == bundleID } }
            // A mapping with neither a default nor overrides left does nothing; drop it.
            c.mappings.removeAll { $0.defaultAction == nil && $0.overrides.isEmpty }
        }
    }

    func mapping(for device: DeviceKey, button: ButtonID) -> ButtonMapping? {
        config.mappings.first { $0.device.matches(device) && $0.button == button }
    }

    func importConfig(from url: URL) throws {
        let data = try Data(contentsOf: url)
        var imported = try ConfigCodec.decode(data)
        imported.settings.onboardingCompleted = true
        try ConfigStore.save(imported)
        config = imported
        DarwinNotify.post(IPCName.configChanged)
    }

    func exportConfig(to url: URL) throws {
        let data = try ConfigCodec.encoder().encode(config)
        try data.write(to: url, options: .atomic)
    }

    // MARK: - Profiles

    /// Apps with at least one enabled override on an enabled mapping.
    var appProfiles: [AppProfile] {
        var counts: [String: (String, Int)] = [:]
        for m in config.mappings where m.enabled {
            for o in m.overrides where o.enabled {
                let current = counts[o.bundleID] ?? (o.appName, 0)
                counts[o.bundleID] = (current.0, current.1 + 1)
            }
        }
        return counts.map { AppProfile(id: $0.key, name: $0.value.0, overrideCount: $0.value.1) }
            .sorted { $0.name.localizedCaseInsensitiveCompare($1.name) == .orderedAscending }
    }

    /// The last app (other than Clicksmith) in front, and whether it has its own profile.
    var lastUsedApp: (bundleID: String, name: String, profile: AppProfile?)? {
        guard let id = status?.lastExternalBundleID else { return nil }
        let name = status?.lastExternalAppName ?? id
        return (id, name, appProfiles.first { $0.id == id })
    }

    // MARK: - Engine status

    func refreshStatus() {
        let newStatus = IPCFiles.read(EngineStatus.self, from: ClicksmithPaths.status)
        var running = false
        if let pid = newStatus?.pid, pid > 0 { running = kill(pid, 0) == 0 }
        if newStatus != status { status = newStatus }
        if running != engineRunning { engineRunning = running }
        let installed = engine.isInstalled
        if installed != engineInstalled { engineInstalled = installed }
    }

    var visualState: EngineVisualState {
        guard engineRunning else { return .stopped }
        if config.settings.paused || (status?.emergencyPaused ?? false) { return .paused }
        return .active
    }

    var statusText: String {
        switch visualState {
        case .active: return "Forging clicks"
        case .paused: return (status?.emergencyPaused ?? false) ? "Emergency pause" : "Paused"
        case .stopped: return config.settings.engineEnabled ? "Engine not running" : "Engine off"
        }
    }

    /// The running engine is from another version (e.g. after an update without restart).
    var engineVersionMismatch: String? {
        guard engineRunning, let v = status?.engineVersion, v != ClicksmithInfo.version else { return nil }
        return v
    }

    /// Connected devices from the engine plus offline devices that have mappings.
    var allDevices: [DeviceInfo] {
        var result = status?.devices ?? []
        for m in config.mappings where !result.contains(where: { $0.key.matches(m.device) || m.device.matches($0.key) }) {
            result.append(DeviceInfo(key: m.device, name: m.deviceName, manufacturer: nil, transport: nil,
                                     declaredButtons: 0, connected: false, lastConnected: nil))
        }
        return result.sorted { ($0.connected ? 0 : 1, $0.name) < ($1.connected ? 0 : 1, $1.name) }
    }

    var connectedDevices: [DeviceInfo] { (status?.devices ?? []).filter(\.connected) }

    /// Connected devices, without built-in trackpads/keyboards unless the user asked for them.
    var connectedMice: [DeviceInfo] {
        connectedDevices.filter { !$0.isBuiltIn || config.settings.showBuiltInDevices }
    }

    var hiddenBuiltInCount: Int {
        config.settings.showBuiltInDevices ? 0 : connectedDevices.filter(\.isBuiltIn).count
    }

    /// Devices for the Buttons page, honouring the built-in filter.
    var visibleDevices: [DeviceInfo] {
        allDevices.filter { !$0.isBuiltIn || config.settings.showBuiltInDevices }
    }

    /// Show the mouse name in mapping rows only when the user has more than one mouse.
    var hasSeveralMice: Bool {
        Set(config.mappings.map(\.deviceName)).count > 1 || connectedDevices.filter { !$0.isBuiltIn }.count > 1
    }

    /// Names of the devices the engine is reading (deduplicated: one mouse can expose
    /// several HID interfaces).
    var listeningNames: [String] {
        var seen = Set<String>()
        return connectedDevices.filter { $0.listening ?? false }.map(\.name).filter { seen.insert($0).inserted }
    }

    // MARK: - Learning

    func startLearning(device: DeviceKey?, mode: LearnRequest.Mode) {
        let request = LearnRequest(device: device, mode: mode)
        IPCFiles.write(request, to: ClicksmithPaths.learnRequest)
        activeLearn = request
        learnResult = nil
        DarwinNotify.post(IPCName.learnStart)
    }

    func stopLearning() {
        DarwinNotify.post(IPCName.learnStop)
        activeLearn = nil
    }

    func clearLearnResult() { learnResult = nil }

    func resumeFromEmergency() {
        IPCFiles.write(ResumeRequest(), to: ClicksmithPaths.resumeRequest)
        DarwinNotify.post(IPCName.resumeFromEmergency)
    }

    // MARK: - Darwin notifications from the engine

    private func observeEngine() {
        let center = CFNotificationCenterGetDarwinNotifyCenter()
        let observer = Unmanaged.passUnretained(self).toOpaque()
        for name in [IPCName.statusChanged, IPCName.learnResult] {
            CFNotificationCenterAddObserver(center, observer, { _, observer, name, _, _ in
                guard let observer, let name else { return }
                let state = Unmanaged<AppState>.fromOpaque(observer).takeUnretainedValue()
                let value = name.rawValue as String
                DispatchQueue.main.async { state.handle(value) }
            }, name as CFString, nil, .deliverImmediately)
        }
    }

    private func handle(_ name: String) {
        if name == IPCName.statusChanged { refreshStatus(); return }
        guard name == IPCName.learnResult,
              let result = IPCFiles.read(LearnResult.self, from: ClicksmithPaths.learnResult),
              result.requestID == activeLearn?.id || result.requestID == learnResult?.requestID else { return }
        if result != learnResult { learnResult = result }
        if result.finished { activeLearn = nil }
    }
}
