import SwiftUI
import AppKit
import UniformTypeIdentifiers
import ClicksmithCore

/// One Settings section with tabs: General, Permissions, Diagnostics, About.
struct SettingsContainer: View {
    @ObservedObject private var state = AppState.shared

    var body: some View {
        VStack(spacing: 0) {
            HStack {
                Text("Settings").font(.largeTitle.weight(.semibold))
                Spacer()
                Picker("Section", selection: $state.settingsTab) {
                    ForEach(SettingsTab.allCases) { Text($0.title).tag($0) }
                }
                .pickerStyle(.segmented)
                .labelsHidden()
                .frame(maxWidth: 420)
            }
            .padding(.horizontal, 32)
            .padding(.top, 24)
            .padding(.bottom, 8)

            Group {
                switch state.settingsTab {
                case .general: GeneralSettingsView()
                case .permissions: PermissionsView()
                case .diagnostics: DiagnosticsView()
                case .about: AboutView()
                }
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .top)
        }
    }
}

// MARK: - General

struct GeneralSettingsView: View {
    @ObservedObject private var state = AppState.shared
    @Local private var menuBarAtLogin = false
    @Local private var message: String? = nil
    @Local private var confirmPrimary = false
    @Local private var confirmReset = false
    @Local private var legacyInstalled = false

    private var s: EngineSettings { state.config.settings }

    var body: some View {
        Form {
            Section("Startup") {
                Toggle("Start Clicksmith Engine at login", isOn: Binding(
                    get: { s.launchEngineAtLogin },
                    set: { on in
                        state.update { $0.settings.launchEngineAtLogin = on }
                        guard s.engineEnabled else { return }
                        state.engine.perform({ state.engine.install(atLogin: on) }) { message = $0; state.refreshStatus() }
                    }))
                Toggle("Open Clicksmith in the menu bar at login", isOn: Binding(
                    get: { menuBarAtLogin },
                    set: { on in
                        message = MenuBarLoginItem.set(on)
                        menuBarAtLogin = MenuBarLoginItem.isEnabled
                    }))
                Toggle("Clicksmith Engine enabled", isOn: Binding(
                    get: { s.engineEnabled },
                    set: { on in
                        state.update { $0.settings.engineEnabled = on }
                        let atLogin = s.launchEngineAtLogin
                        state.engine.perform({ on ? state.engine.install(atLogin: atLogin) : state.engine.uninstall() }) {
                            message = $0
                            state.refreshStatus()
                        }
                    }))
                Text("The engine keeps your buttons working even when this window and the menu bar icon are closed.")
                    .font(.caption).foregroundStyle(.secondary)
            }

            Section("Behaviour") {
                Toggle("Pause all mappings", isOn: Binding(get: { s.paused }, set: { state.setPaused($0) }))
                Stepper(value: Binding(get: { s.duplicateGuardMs }, set: { v in state.update { $0.settings.duplicateGuardMs = v } }),
                        in: 0...1000, step: 50) {
                    LabeledContent("Ignore repeat presses within", value: "\(s.duplicateGuardMs) ms")
                }
                Stepper(value: Binding(get: { s.wakeRestartDelay }, set: { v in state.update { $0.settings.wakeRestartDelay = v } }),
                        in: 1...10, step: 0.5) {
                    LabeledContent("Wake recovery delay", value: String(format: "%.1f s", s.wakeRestartDelay))
                }
                Toggle("Emergency pause shortcut ⌃⌥⌘⎋", isOn: Binding(
                    get: { s.emergencyHotKeyEffective },
                    set: { v in state.update { $0.settings.emergencyHotKeyEnabled = v } }))
                    .disabled(s.allowPrimaryButtonSuppression)
                if s.allowPrimaryButtonSuppression {
                    Text("Always on while left/right-click blocking is allowed, so you can't lock yourself out.")
                        .font(.caption).foregroundStyle(.secondary)
                }
                Toggle("Allow blocking left and right click", isOn: Binding(
                    get: { s.allowPrimaryButtonSuppression },
                    set: { v in if v { confirmPrimary = true } else { state.update { $0.settings.allowPrimaryButtonSuppression = false } } }))
            }

            Section("Appearance") {
                Toggle("Show icon in menu bar", isOn: Binding(
                    get: { s.showMenuBarIcon },
                    set: { v in state.update { $0.settings.showMenuBarIcon = v; if !v { $0.settings.showDockIcon = true } } }))
                Toggle("Show icon in Dock", isOn: Binding(
                    get: { s.showDockIcon },
                    set: { v in state.update { $0.settings.showDockIcon = v; if !v { $0.settings.showMenuBarIcon = true } } }))
                Toggle("Show built-in trackpad and keyboard", isOn: Binding(
                    get: { s.showBuiltInDevices },
                    set: { v in state.update { $0.settings.showBuiltInDevices = v } }))
                Text("At least one icon stays visible so you can always reach Clicksmith.")
                    .font(.caption).foregroundStyle(.secondary)
            }

            Section("Configuration") {
                HStack {
                    Button("Export…") { exportConfig() }
                    Button("Import…") { importConfig() }
                    Button("Show in Finder") { NSWorkspace.shared.activateFileViewerSelecting([ClicksmithPaths.config]) }
                    Spacer()
                    Button("Reset All Mappings…", role: .destructive) { confirmReset = true }
                }
            }

            if legacyInstalled || LegacyMigration.canRestore {
                Section("rival3-dpi") {
                    if legacyInstalled {
                        Text("The stand-alone rival3-dpi engine is still installed. Running both makes the DPI button fire twice.")
                        Button("Import DPI → ⇧⌘3 and switch off rival3-dpi") { migrateLegacy() }
                    } else {
                        Text("rival3-dpi is switched off and backed up.")
                        Button("Restore rival3-dpi") {
                            message = LegacyMigration.restore(using: state.engine)
                            legacyInstalled = LegacyMigration.isLegacyInstalled
                        }
                    }
                }
            }

            if let message {
                Section { Label(message, systemImage: "exclamationmark.triangle").foregroundStyle(Palette.amber) }
            }
        }
        .formStyle(.grouped)
        .scrollContentBackground(.hidden)
        .background(Palette.canvas)
        .onAppear {
            // Read system state once when the tab opens, never during rendering.
            menuBarAtLogin = MenuBarLoginItem.isEnabled
            legacyInstalled = LegacyMigration.isLegacyInstalled
        }
        .alert("Allow blocking left and right click?", isPresented: $confirmPrimary) {
            Button("Cancel", role: .cancel) {}
            Button("Allow", role: .destructive) { state.update { $0.settings.allowPrimaryButtonSuppression = true } }
        } message: {
            Text("A wrong mapping could leave you without a working click. ⌃⌥⌘⎋ always pauses Clicksmith and stays on while this is allowed. Quitting the engine also restores normal clicks.")
        }
        .alert("Delete all mappings?", isPresented: $confirmReset) {
            Button("Cancel", role: .cancel) {}
            Button("Delete All", role: .destructive) { state.update { $0.mappings = [] } }
        } message: {
            Text("Settings are kept. Export first if you might want them back.")
        }
    }

    private func exportConfig() {
        let panel = NSSavePanel()
        panel.nameFieldStringValue = "Clicksmith-config.json"
        panel.allowedContentTypes = [.json]
        guard panel.runModal() == .OK, let url = panel.url else { return }
        do { try state.exportConfig(to: url) } catch { message = error.localizedDescription }
    }

    private func importConfig() {
        let panel = NSOpenPanel()
        panel.allowedContentTypes = [.json]
        guard panel.runModal() == .OK, let url = panel.url else { return }
        do { try state.importConfig(from: url); message = nil } catch { message = "\(error)" }
    }

    private func migrateLegacy() {
        let engine = state.engine
        engine.perform({
            let mapping = LegacyMigration.migrate(using: engine)
            DispatchQueue.main.async {
                if AppState.shared.mapping(for: mapping.device, button: mapping.button) == nil { AppState.shared.upsert(mapping) }
            }
            return nil
        }) { msg in
            message = msg
            legacyInstalled = LegacyMigration.isLegacyInstalled
        }
    }
}

// MARK: - Permissions

struct PermissionsView: View {
    @ObservedObject private var state = AppState.shared

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 16) {
                Text("Only Clicksmith Engine needs input permissions. The settings app needs none.")
                    .foregroundStyle(.secondary)
                PermissionRow(title: "Input Monitoring",
                              detail: "Lets Clicksmith Engine see which mouse button you press. Button events are processed locally and never stored or transmitted.",
                              granted: state.status?.inputMonitoring == "granted",
                              unknown: state.status == nil,
                              url: SystemSettingsLink.inputMonitoring)
                PermissionRow(title: "Accessibility",
                              detail: "Lets Clicksmith Engine send the action you assign, and block a button's original action if you ask it to.",
                              granted: state.status?.accessibility == true,
                              unknown: state.status == nil,
                              url: SystemSettingsLink.accessibility)
                Card(title: "If Clicksmith Engine isn't in the list", systemImage: "questionmark.circle") {
                    Text("1. Open the setting above and click +.\n2. Click “Show Engine in Finder” and drag “Clicksmith Engine” into the list (or press ⌘⇧G and paste the path).\n3. Switch it on, then click “Restart Engine”.\n\nAfter rebuilding Clicksmith, remove the old entry and add it again — local builds get a new signature on every build.")
                        .fixedSize(horizontal: false, vertical: true)
                    HStack {
                        Button("Show Engine in Finder") {
                            NSWorkspace.shared.activateFileViewerSelecting([state.engine.engineAppURL])
                        }
                        Button("Copy Path") {
                            NSPasteboard.general.clearContents()
                            NSPasteboard.general.setString(state.engine.engineAppURL.path, forType: .string)
                        }
                        Button("Restart Engine") {
                            state.engine.perform({ state.engine.restart() }) { state.engineMessage = $0 }
                        }
                        .buttonStyle(.borderedProminent)
                    }
                }
                Text("Clicksmith never tries to bypass macOS privacy controls. Status shown here comes from the engine itself.")
                    .font(.caption).foregroundStyle(.secondary)
            }
            .padding(.horizontal, 28)
            .padding(.vertical, 16)
        }
    }
}

private struct PermissionRow: View {
    let title: String
    let detail: String
    let granted: Bool
    let unknown: Bool
    let url: URL

    var body: some View {
        Card(title: title, systemImage: granted ? "checkmark.shield.fill" : "exclamationmark.shield.fill") {
            HStack(alignment: .top) {
                Text(detail).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
                Spacer(minLength: 20)
                VStack(alignment: .trailing, spacing: 8) {
                    Text(unknown ? "Engine not running" : (granted ? "Granted" : "Not granted"))
                        .font(.callout.weight(.semibold))
                        .foregroundStyle(granted ? Palette.sage : Palette.amber)
                    Button("Open System Settings") { NSWorkspace.shared.open(url) }
                }
            }
        }
    }
}

// MARK: - Diagnostics

struct DiagnosticsView: View {
    @ObservedObject private var state = AppState.shared

    private var architecture: String {
        #if arch(arm64)
        return "Apple Silicon"
        #else
        return "Intel"
        #endif
    }

    var body: some View {
        Form {
            Section("Versions") {
                LabeledContent("Clicksmith", value: ClicksmithInfo.version)
                LabeledContent("Engine", value: (state.status?.engineVersion ?? "—") + (state.engineVersionMismatch != nil ? " (restart to update)" : ""))
                LabeledContent("macOS", value: ProcessInfo.processInfo.operatingSystemVersionString)
                LabeledContent("Architecture", value: architecture)
            }
            Section("Engine") {
                LabeledContent("State", value: state.statusText)
                LabeledContent("Process ID", value: state.status.map { "\($0.pid)" } ?? "—")
                LabeledContent("Started", value: relative(state.status?.startedAt))
                LabeledContent("Started by wake recovery", value: state.status?.startedAfterWake == true ? "Yes" : "No")
                LabeledContent("Last sleep", value: relative(state.status?.lastSleep))
                LabeledContent("Last wake", value: relative(state.status?.lastWake))
                LabeledContent("Last fresh-process recovery", value: relative(state.status?.lastWakeRecovery))
                LabeledContent("Last HID start", value: relative(state.status?.lastHIDStart))
                LabeledContent("Last device enumeration", value: relative(state.status?.lastEnumeration))
                LabeledContent("Button blocking active", value: state.status?.suppressionActive == true ? "Yes" : "No")
                LabeledContent("Last error", value: state.status?.lastError ?? "None")
            }
            Section("Permissions") {
                LabeledContent("Input Monitoring", value: state.status?.inputMonitoring.capitalized ?? "—")
                LabeledContent("Accessibility", value: state.status.map { $0.accessibility ? "Granted" : "Not granted" } ?? "—")
            }
            Section("Devices & mappings") {
                ForEach(state.connectedDevices) { d in
                    LabeledContent(d.name, value: "\(d.key.shortID) · \(transportName(d.transport))\((d.listening ?? false) ? " · read" : " · idle")")
                }
                LabeledContent("HID interfaces read", value: state.status?.listeningDevices.map { "\($0)" } ?? "—")
                LabeledContent("Active mappings", value: "\(state.status?.activeMappings ?? 0)")
                LabeledContent("Profile in effect", value: state.status?.activeProfile ?? "—")
            }
            Section {
                Toggle("Diagnostic mode (15 minutes)", isOn: Binding(
                    get: { state.config.settings.diagnosticModeActive },
                    set: { on in state.update { $0.settings.diagnosticModeUntil = on ? Date().addingTimeInterval(900) : nil } }
                ))
                if state.config.settings.diagnosticModeActive {
                    LabeledContent("Ends", value: relative(state.config.settings.diagnosticModeUntil))
                    LabeledContent("Processing time (avg / max)", value: latencyText)
                }
            } header: {
                Text("Latency")
            } footer: {
                Text("Measures Clicksmith's own time from HID report to posted action. Bluetooth/USB transport is not included. Only timings are recorded — never which buttons or keys.")
            }
            Section {
                HStack {
                    Button("Restart Input Engine") {
                        state.engine.perform({ state.engine.restart() }) { state.engineMessage = $0 }
                    }
                    Button("Engine Log") { NSWorkspace.shared.open(ClicksmithPaths.logFile) }
                    Button("App Log") { NSWorkspace.shared.open(AppLog.url) }
                    Button("Export Report…") { exportReport() }
                }
            }
        }
        .formStyle(.grouped)
        .scrollContentBackground(.hidden)
        .background(Palette.canvas)
    }

    private var latencyText: String {
        guard let avg = state.status?.latencyAvgMicros, let max = state.status?.latencyMaxMicros else { return "Press a mapped button" }
        return String(format: "%.1f µs / %.1f µs", avg, max)
    }

    private struct Report: Encodable {
        let generated: Date
        let appVersion: String
        let macOS: String
        let architecture: String
        let engineInstalled: Bool
        let engineRunning: Bool
        let status: EngineStatus?
        let settings: EngineSettings
        let mappingCount: Int
        let overrideCount: Int
    }

    private func exportReport() {
        let panel = NSSavePanel()
        panel.nameFieldStringValue = "Clicksmith-diagnostics.json"
        panel.allowedContentTypes = [.json]
        guard panel.runModal() == .OK, let url = panel.url else { return }
        let report = Report(generated: Date(), appVersion: ClicksmithInfo.version,
                            macOS: ProcessInfo.processInfo.operatingSystemVersionString, architecture: architecture,
                            engineInstalled: state.engineInstalled, engineRunning: state.engineRunning,
                            status: state.status, settings: state.config.settings,
                            mappingCount: state.config.mappings.count,
                            overrideCount: state.config.mappings.reduce(0) { $0 + $1.overrides.count })
        if let data = try? ConfigCodec.encoder().encode(report) { try? data.write(to: url) }
    }
}

// MARK: - About

struct AboutView: View {
    var body: some View {
        VStack(spacing: 14) {
            Spacer(minLength: 10)
            Image(nsImage: AppIcons.logo).resizable().frame(width: 140, height: 140)
                .shadow(color: .black.opacity(0.10), radius: 14, y: 6)
            Text("Clicksmith").font(.system(size: 34, weight: .semibold))
            Text("Forge every mouse button into something useful.").font(.title3).foregroundStyle(.secondary)
            Text("Version \(ClicksmithInfo.version)").foregroundStyle(.secondary)
            Divider().frame(width: 320)
            Label("Local only · no telemetry · no network · no keystroke logging", systemImage: "hand.raised.fill")
                .font(.callout)
            Text("Built on the rival3-dpi engine: native IOHID input with fresh-process wake recovery.\nMIT License.")
                .multilineTextAlignment(.center)
                .font(.caption)
                .foregroundStyle(.secondary)
            Spacer(minLength: 10)
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .padding(30)
    }
}
