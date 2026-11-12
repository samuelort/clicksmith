import AppKit
import Carbon.HIToolbox
import IOKit.hid
import CoreGraphics
import ClicksmithCore

/// Per-device state. Owned by the engine; the report callback receives it as an opaque pointer.
final class DeviceState {
    let device: IOHIDDevice
    let key: DeviceKey
    let name: String
    let builtIn: Bool
    let infoID: String
    /// True while an input-report callback is registered.
    var listening = false
    let buffer: UnsafeMutablePointer<UInt8>
    let length: Int
    /// Previous report per report ID, preallocated so the hot path never allocates.
    private var previous: [UInt32: UnsafeMutablePointer<UInt8>] = [:]
    var watched: [WatchedButton] = []
    var tracker = VolatilityTracker()
    weak var engine: Engine?

    init(device: IOHIDDevice, key: DeviceKey, name: String, builtIn: Bool, infoID: String, engine: Engine) {
        self.device = device
        self.key = key
        self.name = name
        self.builtIn = builtIn
        self.infoID = infoID
        self.engine = engine
        let reported = IOHIDDeviceGetProperty(device, kIOHIDMaxInputReportSizeKey as CFString) as? Int ?? 64
        length = min(max(reported, 8), 4096)
        buffer = .allocate(capacity: length)
        buffer.initialize(repeating: 0, count: length)
    }

    /// Forget previous reports so a re-attached device starts from a clean state.
    func resetPrevious() {
        for (_, p) in previous { p.update(repeating: 0, count: length) }
    }

    func previousBuffer(for reportID: UInt32) -> UnsafeMutablePointer<UInt8> {
        if let p = previous[reportID] { return p }
        let p = UnsafeMutablePointer<UInt8>.allocate(capacity: length)
        p.initialize(repeating: 0, count: length)
        previous[reportID] = p
        return p
    }

    deinit {
        buffer.deinitialize(count: length)
        buffer.deallocate()
        for (_, p) in previous { p.deinitialize(count: length); p.deallocate() }
    }
}

final class Engine: NSObject {
    // HID
    private var manager: IOHIDManager?
    private var devices: [ObjectIdentifier: DeviceState] = [:]
    private var deviceInfos: [String: DeviceInfo] = [:]

    // Configuration (swapped on the main thread; hot path only reads)
    private var config = ClicksmithConfig()
    private var compiled = CompiledConfig(ClicksmithConfig())
    private var pressGuard = PressGuard(intervalMs: 250)
    private var mappingsActive = true

    // Context
    private var frontmostBundleID: String?
    private var frontmostName: String?
    private var emergencyPaused = false

    // Learning
    private var learnRequest: LearnRequest?
    private var learner = ButtonLearner()
    private var learned: [DetectedButton] = []
    private var learnTimeout: DispatchWorkItem?

    // Output
    private let eventSource = CGEventSource(stateID: .hidSystemState)
    private let suppressor = Suppressor()
    private let hotKey = EmergencyHotKey()

    // Status
    private var status = EngineStatus(engineVersion: ClicksmithInfo.version, pid: getpid())
    private let statusWriter = StatusWriter()
    private var wakeRestartScheduled = false
    private var permissionTimer: Timer?

    // Diagnostic-mode latency
    private var diagnostic = false
    private var latencyCount = 0
    private var latencySumNs: Double = 0
    private var latencyMaxNs: Double = 0
    private let timebase: Double = {
        var info = mach_timebase_info_data_t()
        mach_timebase_info(&info)
        return Double(info.numer) / Double(info.denom)
    }()

    // MARK: - Startup

    func start() {
        carryOverPreviousStatus()
        loadConfigSync()
        observeSystem()
        observeIPC()

        let front = NSWorkspace.shared.frontmostApplication
        frontmostBundleID = front?.bundleIdentifier
        frontmostName = front?.localizedName
        noteExternal(front)

        hotKey.onPress = { [weak self] in self?.emergencyPause() }
        applyConfig()

        status.accessibility = CGPreflightPostEventAccess()
        if !status.accessibility { _ = CGRequestPostEventAccess() }

        let access = IOHIDCheckAccess(kIOHIDRequestTypeListenEvent)
        if access == kIOHIDAccessTypeGranted {
            status.inputMonitoring = "granted"
            openHIDOrExit()
        } else {
            status.inputMonitoring = access == kIOHIDAccessTypeDenied ? "denied" : "unknown"
            _ = IOHIDRequestAccess(kIOHIDRequestTypeListenEvent)
            status.lastError = "Input Monitoring is not granted. Allow “Clicksmith Engine” in Privacy & Security."
            EngineLog.write("Waiting for Input Monitoring permission")
        }

        permissionTimer = Timer.scheduledTimer(withTimeInterval: 5, repeats: true) { [weak self] _ in
            self?.periodicCheck()
        }
        permissionTimer?.tolerance = 1
        EngineLog.write("Clicksmith Engine \(ClicksmithInfo.version) started\(status.startedAfterWake ? " (wake recovery)" : "")")
        publishStatus(now: true)
    }

    private func carryOverPreviousStatus() {
        guard let previous = IPCFiles.read(EngineStatus.self, from: ClicksmithPaths.status) else { return }
        status.lastSleep = previous.lastSleep
        status.lastWake = previous.lastWake
        status.lastWakeRecovery = previous.lastWakeRecovery
        status.lastExternalBundleID = previous.lastExternalBundleID
        status.lastExternalAppName = previous.lastExternalAppName
        if previous.pendingWakeRestart {
            status.startedAfterWake = true
            status.lastWakeRecovery = Date()
        }
    }

    private func openHIDOrExit() {
        let m = IOHIDManagerCreate(kCFAllocatorDefault, IOOptionBits(kIOHIDOptionsTypeNone))
        let matching: [[String: Any]] = [
            [kIOHIDDeviceUsagePageKey as String: kHIDPage_GenericDesktop, kIOHIDDeviceUsageKey as String: kHIDUsage_GD_Mouse],
            [kIOHIDDeviceUsagePageKey as String: kHIDPage_GenericDesktop, kIOHIDDeviceUsageKey as String: kHIDUsage_GD_Pointer]
        ]
        IOHIDManagerSetDeviceMatchingMultiple(m, matching as CFArray)
        let ctx = Unmanaged.passUnretained(self).toOpaque()
        IOHIDManagerRegisterDeviceMatchingCallback(m, { context, result, _, device in
            guard result == kIOReturnSuccess, let context else { return }
            Unmanaged<Engine>.fromOpaque(context).takeUnretainedValue().add(device)
        }, ctx)
        IOHIDManagerRegisterDeviceRemovalCallback(m, { context, _, _, device in
            guard let context else { return }
            Unmanaged<Engine>.fromOpaque(context).takeUnretainedValue().remove(device)
        }, ctx)
        IOHIDManagerScheduleWithRunLoop(m, CFRunLoopGetMain(), CFRunLoopMode.commonModes.rawValue)

        // Non-exclusive: the mouse keeps working normally for macOS.
        let result = IOHIDManagerOpen(m, IOOptionBits(kIOHIDOptionsTypeNone))
        guard result == kIOReturnSuccess else {
            IOHIDManagerUnscheduleFromRunLoop(m, CFRunLoopGetMain(), CFRunLoopMode.commonModes.rawValue)
            status.lastError = "Could not open HID manager (\(result)). Restarting with a fresh process."
            EngineLog.write("ERROR: could not open HID manager (\(result)); exiting so launchd starts a fresh process")
            publishStatus(now: true)
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.5) { exit(1) }
            return
        }
        manager = m
        status.lastHIDStart = Date()
        status.lastError = nil
        EngineLog.write("HID manager started")

        // Explicit enumeration (v3.4/v3.5 finding): don't rely on matching callbacks alone.
        let set = IOHIDManagerCopyDevices(m) as? Set<IOHIDDevice> ?? []
        for d in set { add(d) }
        status.lastEnumeration = Date()
        status.listeningDevices = devices.values.filter(\.listening).count
        EngineLog.write("HID enumeration: \(set.count) pointing devices, \(status.listeningDevices ?? 0) read")
    }

    // MARK: - Devices

    private func add(_ device: IOHIDDevice) {
        let id = ObjectIdentifier(device)
        guard devices[id] == nil else { return }
        func prop(_ key: String) -> Any? { IOHIDDeviceGetProperty(device, key as CFString) }
        let key = DeviceKey(vendorID: prop(kIOHIDVendorIDKey) as? Int ?? 0,
                            productID: prop(kIOHIDProductIDKey) as? Int ?? 0,
                            serial: prop(kIOHIDSerialNumberKey) as? String)
        let name = (prop(kIOHIDProductKey) as? String) ?? "Mouse \(key.shortID)"
        var declared = 0
        let buttonMatch = [kIOHIDElementUsagePageKey as String: kHIDPage_Button] as CFDictionary
        if let elements = IOHIDDeviceCopyMatchingElements(device, buttonMatch, IOOptionBits(kIOHIDOptionsTypeNone)) {
            declared = CFArrayGetCount(elements)
        }

        let transport = prop(kIOHIDTransportKey) as? String
        let builtInProp = (prop(kIOHIDBuiltInKey) as? NSNumber)?.boolValue ?? false
        let builtIn = AttachPolicy.looksBuiltIn(builtInProperty: builtInProp, transport: transport)
        let info = DeviceInfo(key: key, name: name, manufacturer: prop(kIOHIDManufacturerKey) as? String,
                              transport: transport, declaredButtons: declared,
                              connected: true, lastConnected: Date(), builtIn: builtIn)

        let state = DeviceState(device: device, key: key, name: name, builtIn: builtIn, infoID: info.id, engine: self)
        state.watched = compiled.watched(for: key, bundleID: frontmostBundleID)
        devices[id] = state
        deviceInfos[info.id] = info
        updateListening(state)
        EngineLog.write("Device connected: \(name) [\(key.shortID)]\(builtIn ? " (built-in)" : "")\(state.listening ? "" : " — idle, not read")")
        publishStatus()
    }

    private func remove(_ device: IOHIDDevice) {
        let id = ObjectIdentifier(device)
        guard let s = devices[id] else { return }
        if s.listening { IOHIDDeviceRegisterInputReportCallback(device, s.buffer, s.length, nil, nil) }
        s.listening = false
        devices[id] = nil
        if !devices.values.contains(where: { $0.key == s.key }) {
            deviceInfos[s.infoID]?.connected = false
            deviceInfos[s.infoID]?.listening = false
        }
        EngineLog.write("Device disconnected: \(s.name) (\(devices.count) active)")
        publishStatus()
    }

    // MARK: - Listening: built-in trackpads/keyboards are only read when needed

    private func learningTargets(_ s: DeviceState) -> Bool {
        guard let req = learnRequest else { return false }
        return req.device.map { $0.matches(s.key) } ?? true
    }

    private func updateListening(_ s: DeviceState) {
        let want = AttachPolicy.shouldListen(builtIn: s.builtIn, hasWatchedButtons: !s.watched.isEmpty,
                                             learningTargetsDevice: learningTargets(s))
        guard want != s.listening else { return }
        if want {
            s.resetPrevious()
            IOHIDDeviceRegisterInputReportCallback(s.device, s.buffer, s.length, { context, result, _, _, reportID, report, length in
                guard result == kIOReturnSuccess, let context else { return }
                let st = Unmanaged<DeviceState>.fromOpaque(context).takeUnretainedValue()
                st.engine?.handleReport(st, reportID: reportID, report: report, length: length)
            }, Unmanaged.passUnretained(s).toOpaque())
        } else {
            IOHIDDeviceRegisterInputReportCallback(s.device, s.buffer, s.length, nil, nil)
        }
        s.listening = want
        // A model can expose several HID interfaces; the device counts as read if any is.
        deviceInfos[s.infoID]?.listening = devices.values.contains { $0.infoID == s.infoID && $0.listening }
    }

    private func updateAllListening() {
        for (_, s) in devices { updateListening(s) }
        status.listeningDevices = devices.values.filter(\.listening).count
    }

    // MARK: - Hot path (main run loop, no allocation, no I/O)

    @inline(__always)
    fileprivate func handleReport(_ s: DeviceState, reportID: UInt32, report: UnsafeMutablePointer<UInt8>, length: Int) {
        let n = min(length, s.length)
        guard n > 0 else { return }
        let t0 = diagnostic ? mach_absolute_time() : 0
        let rid = UInt8(truncatingIfNeeded: reportID)
        let prev = s.previousBuffer(for: reportID)

        // Keep learning which bytes are movement noise (cheap: early exit once volatile).
        let start = rid == 0 ? 0 : 1
        if n > start {
            for i in start..<n where report[i] != prev[i] {
                s.tracker.observe(reportID: rid, byte: i, old: prev[i], new: report[i])
            }
        }

        if learnRequest != nil {
            learnObserve(s, rid: rid, report: report, prev: prev, n: n)
        } else if mappingsActive && !s.watched.isEmpty {
            var fired = false
            for w in s.watched {
                let b = w.button
                guard b.byteIndex < n else { continue }
                guard b.reportID == rid || (rid == 0 && report[0] == b.reportID) else { continue }
                if (report[b.byteIndex] & b.mask) != 0 && (prev[b.byteIndex] & b.mask) == 0 {
                    fire(w)
                    fired = true
                }
            }
            if fired && diagnostic { recordLatency(since: t0) }
        }
        memcpy(prev, report, n)
    }

    @inline(__always)
    private func fire(_ w: WatchedButton) {
        guard pressGuard.allow(w.button, now: DispatchTime.now().uptimeNanoseconds) else { return }
        let a = w.action
        switch a.kind {
        case .shortcut:
            if let sc = a.shortcut { postShortcut(sc) }
        case .mediaKey:
            if let m = a.mediaKey { postMediaKey(m.nxKeyType) }
        case .openApp:
            if let id = a.appBundleID { openApp(id) }
        case .typeText:
            if let t = a.text { typeText(t) }
        case .nothing:
            break
        }
    }

    @inline(__always)
    private func postShortcut(_ sc: KeyShortcut) {
        guard let down = CGEvent(keyboardEventSource: eventSource, virtualKey: CGKeyCode(sc.keyCode), keyDown: true),
              let up = CGEvent(keyboardEventSource: eventSource, virtualKey: CGKeyCode(sc.keyCode), keyDown: false) else { return }
        var flags = CGEventFlags()
        if sc.command { flags.insert(.maskCommand) }
        if sc.shift { flags.insert(.maskShift) }
        if sc.option { flags.insert(.maskAlternate) }
        if sc.control { flags.insert(.maskControl) }
        if sc.function { flags.insert(.maskSecondaryFn) }
        down.flags = flags
        up.flags = flags
        down.post(tap: .cghidEventTap)
        up.post(tap: .cghidEventTap)
    }

    /// Media keys are NX system-defined events (subtype 8), the same events the
    /// keyboard's play/volume/brightness keys send.
    private func postMediaKey(_ keyType: Int) {
        for down in [true, false] {
            let state = down ? 0xA : 0xB
            let data1 = (keyType << 16) | (state << 8)
            let flags = NSEvent.ModifierFlags(rawValue: UInt(state << 8))
            guard let event = NSEvent.otherEvent(with: .systemDefined, location: .zero, modifierFlags: flags,
                                                 timestamp: 0, windowNumber: 0, context: nil,
                                                 subtype: 8, data1: data1, data2: -1) else { return }
            event.cgEvent?.post(tap: .cghidEventTap)
        }
    }

    /// Opens an app by bundle ID only — never a path, URL or arguments.
    private func openApp(_ bundleID: String) {
        DispatchQueue.global(qos: .userInitiated).async {
            guard let url = NSWorkspace.shared.urlForApplication(withBundleIdentifier: bundleID) else {
                EngineLog.write("Open app: \(bundleID) is not installed")
                return
            }
            // 2.0: only apps installed in an Applications or system folder.
            guard AppLocationPolicy.isAllowed(path: url.path, home: NSHomeDirectory()) else {
                EngineLog.write("Open app refused: \(bundleID) is not in an Applications folder")
                return
            }
            let config = NSWorkspace.OpenConfiguration()
            config.activates = true
            NSWorkspace.shared.openApplication(at: url, configuration: config) { _, error in
                if let error { EngineLog.write("Open app failed: \(error.localizedDescription)") }
            }
        }
    }

    /// Types text as Unicode keyboard events, 20 UTF-16 units per event pair.
    /// Skipped while a password field (secure input) is active.
    private func typeText(_ text: String) {
        if IsSecureEventInputEnabled() == true {
            EngineLog.write("Type text skipped: a password field (secure input) is active")
            return
        }
        let units = Array(text.utf16)
        var i = 0
        while i < units.count {
            let chunk = Array(units[i..<min(i + 20, units.count)])
            if let down = CGEvent(keyboardEventSource: eventSource, virtualKey: 0, keyDown: true),
               let up = CGEvent(keyboardEventSource: eventSource, virtualKey: 0, keyDown: false) {
                chunk.withUnsafeBufferPointer { p in
                    down.keyboardSetUnicodeString(stringLength: chunk.count, unicodeString: p.baseAddress)
                    up.keyboardSetUnicodeString(stringLength: chunk.count, unicodeString: p.baseAddress)
                }
                down.flags = []
                up.flags = []
                down.post(tap: .cghidEventTap)
                up.post(tap: .cghidEventTap)
            }
            i += 20
        }
    }

    private func recordLatency(since t0: UInt64) {
        let ns = Double(mach_absolute_time() &- t0) * timebase
        latencyCount += 1
        latencySumNs += ns
        latencyMaxNs = max(latencyMaxNs, ns)
    }

    // MARK: - Learning

    private func learnObserve(_ s: DeviceState, rid: UInt8, report: UnsafeMutablePointer<UInt8>,
                              prev: UnsafeMutablePointer<UInt8>, n: Int) {
        guard let req = learnRequest else { return }
        if let d = req.device, !d.matches(s.key) { return }
        let previous = Array(UnsafeBufferPointer(start: prev, count: n))
        let current = Array(UnsafeBufferPointer(start: report, count: n))
        let found = learner.observe(reportID: rid, previous: previous, current: current, tracker: s.tracker)
        guard !found.isEmpty else { return }
        for b in found {
            let detected = DetectedButton(device: s.key, deviceName: s.name, button: b)
            if !learned.contains(where: { $0.id == detected.id }) { learned.append(detected) }
        }
        let finished = req.mode == .single
        IPCFiles.write(LearnResult(requestID: req.id, buttons: learned, finished: finished), to: ClicksmithPaths.learnResult)
        DarwinNotify.post(IPCName.learnResult)
        if finished { endLearning(writeResult: false) }
    }

    private func beginLearning() {
        guard let req = IPCFiles.read(LearnRequest.self, from: ClicksmithPaths.learnRequest),
              Date().timeIntervalSince(req.startedAt) < 30 else { return }
        learnTimeout?.cancel()
        learnRequest = req
        learned = []
        learner = ButtonLearner()
        learner.armed = false
        // Short arming delay so the click on "Learn" itself is never captured.
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.3) { [weak self] in self?.learner.armed = true }
        let timeout = DispatchWorkItem { [weak self] in self?.endLearning(writeResult: true) }
        learnTimeout = timeout
        DispatchQueue.main.asyncAfter(deadline: .now() + req.timeout, execute: timeout)
        updateAllListening()
        publishStatus()
        EngineLog.write("Button learning started (\(req.mode.rawValue))")
    }

    private func endLearning(writeResult: Bool) {
        guard let req = learnRequest else { return }
        learnTimeout?.cancel()
        learnTimeout = nil
        learnRequest = nil
        if writeResult {
            IPCFiles.write(LearnResult(requestID: req.id, buttons: learned, finished: true), to: ClicksmithPaths.learnResult)
            DarwinNotify.post(IPCName.learnResult)
        }
        updateAllListening()
        publishStatus()
        EngineLog.write("Button learning ended (\(learned.count) detected)")
    }

    // MARK: - Configuration

    private func loadConfigSync() {
        do { config = try ConfigStore.load() }
        catch {
            status.lastError = "\(error)"
            EngineLog.write("ERROR: \(error) — running with empty configuration")
        }
    }

    private func reloadConfig() {
        // Parse off the main thread, swap on the main thread.
        DispatchQueue.global(qos: .userInitiated).async { [weak self] in
            let result = Result { try ConfigStore.load() }
            DispatchQueue.main.async {
                guard let self else { return }
                switch result {
                case .success(let c):
                    self.config = c
                    self.status.lastError = nil
                    self.applyConfig()
                case .failure(let e):
                    // Keep the last good configuration running.
                    self.status.lastError = "Configuration not applied: \(e)"
                    EngineLog.write("ERROR: configuration not applied: \(e)")
                    self.publishStatus()
                }
            }
        }
    }

    private func applyConfig() {
        compiled = CompiledConfig(config)
        pressGuard = PressGuard(intervalMs: config.settings.duplicateGuardMs)
        diagnostic = config.settings.diagnosticModeActive
        // The escape hatch stays on whenever left/right-click blocking is allowed.
        if config.settings.emergencyHotKeyEffective { hotKey.register() } else { hotKey.unregister() }
        refreshContext()
    }

    /// Rebuilds the per-device watch lists and suppression set for the frontmost app.
    private func refreshContext() {
        mappingsActive = !(config.settings.paused || emergencyPaused)
        for (_, s) in devices { s.watched = compiled.watched(for: s.key, bundleID: frontmostBundleID) }
        updateAllListening()
        let suppressed = mappingsActive ? compiled.suppressedSystemButtons(bundleID: frontmostBundleID) : []
        suppressor.update(suppressed)
        status.paused = config.settings.paused
        status.emergencyPaused = emergencyPaused
        status.suppressionActive = suppressor.isActive
        status.activeMappings = compiled.mappings.count
        updateFrontmostStatus()
        publishStatus()
    }

    private func updateFrontmostStatus() {
        status.frontmostBundleID = frontmostBundleID
        status.frontmostAppName = frontmostName
        status.activeProfile = compiled.profileName(bundleID: frontmostBundleID, appName: frontmostName)
    }

    // MARK: - System events

    private func observeSystem() {
        let nc = NSWorkspace.shared.notificationCenter
        nc.addObserver(self, selector: #selector(willSleep), name: NSWorkspace.willSleepNotification, object: nil)
        nc.addObserver(self, selector: #selector(didWake), name: NSWorkspace.didWakeNotification, object: nil)
        nc.addObserver(self, selector: #selector(appActivated(_:)),
                       name: NSWorkspace.didActivateApplicationNotification, object: nil)
    }

    /// Remembers the last app in front that isn't Clicksmith, so the UI can show its profile.
    private func noteExternal(_ app: NSRunningApplication?) {
        guard let id = app?.bundleIdentifier, !ClicksmithInfo.isOwnBundle(id) else { return }
        status.lastExternalBundleID = id
        status.lastExternalAppName = app?.localizedName
    }

    /// Most app switches don't involve an app with overrides. Those only update the
    /// status (coalesced write) instead of rebuilding watch lists and the event tap.
    @objc private func appActivated(_ note: Notification) {
        let app = note.userInfo?[NSWorkspace.applicationUserInfoKey] as? NSRunningApplication
        let previous = frontmostBundleID
        frontmostBundleID = app?.bundleIdentifier
        frontmostName = app?.localizedName
        noteExternal(app)
        if compiled.profileDiffers(previous, frontmostBundleID) {
            refreshContext()
        } else {
            updateFrontmostStatus()
            publishStatus()
        }
    }

    @objc private func willSleep() {
        status.lastSleep = Date()
        EngineLog.write("System will sleep")
        publishStatus(now: true)
    }

    /// Proven v3.5 recovery: don't repair IOHID in-process; exit cleanly and let launchd
    /// start a fresh process, which recreates IOHIDManager and enumerates explicitly.
    @objc private func didWake() {
        guard !wakeRestartScheduled else { return }
        wakeRestartScheduled = true
        status.lastWake = Date()
        status.pendingWakeRestart = true
        let delay = config.settings.wakeRestartDelay
        EngineLog.write("System woke; fresh-process restart in \(delay)s")
        publishStatus(now: true)
        DispatchQueue.main.asyncAfter(deadline: .now() + delay) { [weak self] in
            self?.suppressor.remove()
            EngineLog.write("Wake recovery: exiting cleanly; launchd will start a fresh engine")
            exit(0)
        }
    }

    // MARK: - IPC

    private func observeIPC() {
        let center = CFNotificationCenterGetDarwinNotifyCenter()
        let observer = Unmanaged.passUnretained(self).toOpaque()
        let names = [IPCName.configChanged, IPCName.learnStart, IPCName.learnStop, IPCName.resumeFromEmergency]
        for name in names {
            CFNotificationCenterAddObserver(center, observer, { _, observer, name, _, _ in
                guard let observer, let name else { return }
                let engine = Unmanaged<Engine>.fromOpaque(observer).takeUnretainedValue()
                let value = name.rawValue as String
                DispatchQueue.main.async { engine.handleIPC(value) }
            }, name as CFString, nil, .deliverImmediately)
        }
    }

    private func handleIPC(_ name: String) {
        switch name {
        case IPCName.configChanged: reloadConfig()
        case IPCName.learnStart: beginLearning()
        case IPCName.learnStop: endLearning(writeResult: true)
        case IPCName.resumeFromEmergency:
            // Any local process can post a notification, so require the app's request file.
            guard let req = IPCFiles.read(ResumeRequest.self, from: ClicksmithPaths.resumeRequest), req.isFresh else {
                EngineLog.write("Ignored resume request without a valid request file")
                return
            }
            try? FileManager.default.removeItem(at: ClicksmithPaths.resumeRequest)
            emergencyPaused = false
            EngineLog.write("Resumed after emergency pause")
            refreshContext()
        default: break
        }
    }

    private func emergencyPause() {
        emergencyPaused = true
        EngineLog.write("Emergency pause (⌃⌥⌘⎋): all mappings and suppression disabled")
        refreshContext()
    }

    // MARK: - Periodic checks (off the hot path)

    private func periodicCheck() {
        var changed = false
        let ax = CGPreflightPostEventAccess()
        if ax != status.accessibility { status.accessibility = ax; changed = true }

        let access = IOHIDCheckAccess(kIOHIDRequestTypeListenEvent)
        let im = access == kIOHIDAccessTypeGranted ? "granted" : (access == kIOHIDAccessTypeDenied ? "denied" : "unknown")
        if im != status.inputMonitoring {
            status.inputMonitoring = im
            changed = true
            if im == "granted" && manager == nil {
                EngineLog.write("Input Monitoring granted; restarting with a fresh process")
                publishStatus(now: true)
                exit(0)
            }
        }

        let diag = config.settings.diagnosticModeActive
        if diag != diagnostic {
            diagnostic = diag
            if !diag { latencyCount = 0; latencySumNs = 0; latencyMaxNs = 0 }
            changed = true
        }
        if diagnostic && latencyCount > 0 {
            status.latencyAvgMicros = (latencySumNs / Double(latencyCount)) / 1000
            status.latencyMaxMicros = latencyMaxNs / 1000
            changed = true
        } else if !diagnostic && status.latencyAvgMicros != nil {
            status.latencyAvgMicros = nil
            status.latencyMaxMicros = nil
            changed = true
        }
        if changed { publishStatus() }
    }

    private func publishStatus(now: Bool = false) {
        status.devices = deviceInfos.values.sorted { $0.name < $1.name }
        status.listeningDevices = devices.values.filter(\.listening).count
        if now { statusWriter.writeNow(status) } else { statusWriter.write(status) }
    }
}

/// Coalesced, off-main-thread status file writer.
final class StatusWriter {
    private let queue = DispatchQueue(label: "app.clicksmith.engine.status", qos: .utility)
    private var pending: EngineStatus?
    private var scheduled = false

    func write(_ status: EngineStatus) {
        queue.async {
            self.pending = status
            guard !self.scheduled else { return }
            self.scheduled = true
            self.queue.asyncAfter(deadline: .now() + 0.2) { self.flush() }
        }
    }

    func writeNow(_ status: EngineStatus) {
        queue.sync {
            self.pending = status
            self.flush()
        }
    }

    private func flush() {
        scheduled = false
        guard let p = pending else { return }
        pending = nil
        IPCFiles.write(p, to: ClicksmithPaths.status)
        DarwinNotify.post(IPCName.statusChanged)
    }
}
