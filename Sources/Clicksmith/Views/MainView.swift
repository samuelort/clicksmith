import SwiftUI
import AppKit
import ClicksmithCore

// MARK: - Shell

/// Custom sidebar (four sections, Clicksmith accent) running under the transparent title bar.
struct MainView: View {
    @ObservedObject private var state = AppState.shared

    var body: some View {
        HStack(spacing: 0) {
            Sidebar()
                .frame(width: 196)
            Divider()
            detail
                .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
                .background(Palette.canvas)
        }
    }

    @ViewBuilder private var detail: some View {
        switch state.selection {
        case .overview: OverviewView()
        case .buttons: ButtonsView()
        case .apps: AppsView()
        case .settings: SettingsContainer()
        }
    }
}

private struct Sidebar: View {
    @ObservedObject private var state = AppState.shared

    var body: some View {
        VStack(alignment: .leading, spacing: 2) {
            ForEach(Array(SidebarItem.allCases.enumerated()), id: \.element) { index, item in
                SidebarRow(item: item, selected: state.selection == item,
                           badge: item == .settings && state.missingPermissions > 0) { state.selection = item }
                    .keyboardShortcut(KeyEquivalent(Character("\(index + 1)")), modifiers: .command)
            }
            Spacer()
            HStack(spacing: Space.s) {
                LogoImage(size: 18)
                Text("Clicksmith \(ClicksmithInfo.displayVersion)").font(.caption).foregroundStyle(.secondary)
            }
            .padding(.horizontal, Space.s)
        }
        .padding(.horizontal, 10)
        .padding(.top, 14)
        .padding(.bottom, 14)
        .frame(maxHeight: .infinity, alignment: .top)
        .background(Palette.sidebar.ignoresSafeArea())
    }
}

private struct SidebarRow: View {
    let item: SidebarItem
    let selected: Bool
    var badge = false
    let action: () -> Void
    @Local private var hovering = false

    var body: some View {
        Button(action: action) {
            HStack(spacing: 9) {
                Image(systemName: item.icon)
                    .font(.system(size: 14, weight: .medium))
                    .foregroundStyle(selected ? Palette.copper : Palette.stone)
                    .frame(width: 20)
                Text(item.title)
                    .font(.system(size: 13, weight: selected ? .semibold : .regular))
                    .foregroundStyle(Color.primary)
                Spacer()
                if badge {
                    Circle().fill(Palette.amber).frame(width: 7, height: 7)
                        .accessibilityLabel("Needs attention")
                }
            }
            .padding(.horizontal, Space.s)
            .frame(height: 32)
            .background(
                RoundedRectangle(cornerRadius: 7, style: .continuous)
                    .fill(selected ? Palette.copper.opacity(0.14) : (hovering ? Palette.stone.opacity(0.08) : Color.clear))
            )
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .onHover { hovering = $0 }
        .accessibilityLabel(item.title)
        .accessibilityAddTraits(selected ? .isSelected : [])
    }
}

// MARK: - Overview

struct OverviewView: View {
    @ObservedObject private var state = AppState.shared
    @Local private var showNewMapping = false
    @Local private var editing: ButtonMapping? = nil

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: Space.xl) {
                header
                if state.missingPermissions > 0 { setupCard }
                banners
                // Two aligned rows; cards in a row share their height.
                VStack(spacing: Space.l) {
                    HStack(alignment: .top, spacing: Space.l) {
                        buttonsCard
                        profilesCard
                    }
                    .fixedSize(horizontal: false, vertical: true)
                    HStack(alignment: .top, spacing: Space.l) {
                        engineCard
                        miceCard
                    }
                    .fixedSize(horizontal: false, vertical: true)
                }
            }
            .pageFrame()
        }
        .sheet(isPresented: $showNewMapping) { NewMappingSheet().tint(Color.ember) }
        .sheet(item: $editing) { (m: ButtonMapping) in MappingEditor(mapping: m, isNew: false).tint(Color.ember) }
    }

    // MARK: Header

    private var header: some View {
        HStack(alignment: .center, spacing: Space.l) {
            LogoImage(size: 56)
                .shadow(color: .black.opacity(0.06), radius: 6, y: 2)
            VStack(alignment: .leading, spacing: 6) {
                Text("Clicksmith").font(.largeTitle.weight(.semibold))
                HStack(spacing: Space.s) {
                    StatusPill(state: state.healthVisual, text: state.healthTitle)
                    Text(state.healthDetail).font(.callout).foregroundStyle(.secondary)
                }
            }
            Spacer()
            Toggle("Pause all", isOn: Binding(get: { state.config.settings.paused }, set: { state.setPaused($0) }))
                .toggleStyle(.switch)
                .help("Pause every mapping. ⌃⌥⌘⎋ is the emergency pause.")
        }
    }

    // MARK: Setup

    private var setupCard: some View {
        VStack(alignment: .leading, spacing: Space.m) {
            HStack(spacing: Space.s) {
                Image(systemName: "checklist").foregroundStyle(Palette.amber)
                Text("Finish setup").font(.headline)
                Spacer()
                Button("Restart Engine") { restartEngine() }
                    .help("Restart after granting permissions")
            }
            permissionRow("Input Monitoring", detail: "Lets Clicksmith see which mouse button you press.",
                          granted: state.inputMonitoringGranted, url: SystemSettingsLink.inputMonitoring)
            Divider()
            permissionRow("Accessibility", detail: "Lets Clicksmith send the action you assign.",
                          granted: state.accessibilityGranted, url: SystemSettingsLink.accessibility)
            Text("After an update macOS can ask again: remove the old “Clicksmith Engine” entry, add it again, then restart the engine.")
                .font(.caption)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
        }
        .padding(18)
        .background(RoundedRectangle(cornerRadius: 14, style: .continuous).fill(Palette.amber.opacity(0.07)))
        .overlay(RoundedRectangle(cornerRadius: 14, style: .continuous).strokeBorder(Palette.amber.opacity(0.22)))
    }

    private func permissionRow(_ title: String, detail: String, granted: Bool, url: URL) -> some View {
        HStack(spacing: Space.m) {
            Image(systemName: granted ? "checkmark.circle.fill" : "circle.dashed")
                .font(.system(size: 16))
                .foregroundStyle(granted ? Palette.sage : Palette.amber)
                .frame(width: 18)
            VStack(alignment: .leading, spacing: 1) {
                Text(title).font(.body.weight(.medium))
                Text(detail).font(.caption).foregroundStyle(.secondary)
            }
            Spacer()
            if granted {
                Text("Granted").font(.callout).foregroundStyle(Palette.sage)
            } else {
                Button("Open Settings") { NSWorkspace.shared.open(url) }
            }
        }
    }

    // MARK: Cards

    private var canMap: Bool { state.engineRunning && state.inputMonitoringGranted }

    private var buttonsCard: some View {
        Card(title: "Your buttons", systemImage: "computermouse") {
            if state.config.mappings.isEmpty {
                VStack(alignment: .leading, spacing: 6) {
                    Text("Nothing forged yet.").font(.title3.weight(.semibold))
                    Text("Map a mouse button to a shortcut, media key, app or text snippet.")
                        .foregroundStyle(.secondary)
                        .fixedSize(horizontal: false, vertical: true)
                }
            } else {
                VStack(spacing: 0) {
                    ForEach(state.config.mappings) { m in
                        MappingRow(mapping: m, showDevice: false) { editing = m }
                        if m.id != state.config.mappings.last?.id { Divider().padding(.leading, 28) }
                    }
                }
            }
            Spacer(minLength: 0)
            HStack {
                Button { showNewMapping = true } label: { Label("New Mapping", systemImage: "plus") }
                    .buttonStyle(.borderedProminent)
                    .disabled(!canMap)
                    .help(canMap ? "Map another mouse button" : "Finish setup first")
                Spacer()
                if !state.config.mappings.isEmpty {
                    Button("Open Buttons") { state.selection = .buttons }.buttonStyle(.accentLink)
                }
            }
            .padding(.top, Space.xs)
        }
    }

    private var profilesCard: some View {
        Card(title: "Profiles", systemImage: "square.stack.3d.up") {
            VStack(alignment: .leading, spacing: Space.m) {
                profileRow(icon: Image(systemName: "globe"), isSymbol: true, name: "Default", detail: "All other apps",
                           active: state.lastUsedApp != nil && state.lastUsedApp?.profile == nil)
                ForEach(state.appProfiles) { p in
                    profileRow(icon: Image(nsImage: AppIcons.appIcon(bundleID: p.id)), isSymbol: false, name: p.name,
                               detail: p.overrideCount == 1 ? "1 button changed" : "\(p.overrideCount) buttons changed",
                               active: state.lastUsedApp?.bundleID == p.id)
                }
            }
            Spacer(minLength: 0)
            HStack(alignment: .firstTextBaseline) {
                Button(state.appProfiles.isEmpty ? "Set up an app" : "Manage apps") { state.selection = .apps }
                    .buttonStyle(.accentLink)
                Spacer()
                if let last = state.lastUsedApp {
                    Text("Last active: \(last.name)")
                        .font(.caption)
                        .foregroundStyle(.secondary)
                        .lineLimit(1)
                }
            }
        }
    }

    private func profileRow(icon: Image, isSymbol: Bool, name: String, detail: String, active: Bool) -> some View {
        HStack(spacing: Space.m) {
            icon.resizable().interpolation(.high).scaledToFit()
                .padding(isSymbol ? 2 : 0)
                .frame(width: 22, height: 22)
                .foregroundStyle(Palette.stone)
            VStack(alignment: .leading, spacing: 1) {
                Text(name).font(.body.weight(.medium))
                Text(detail).font(.caption).foregroundStyle(.secondary)
            }
            Spacer()
            if active {
                Text("In use").font(.caption2.weight(.semibold))
                    .padding(.horizontal, 7).padding(.vertical, 2)
                    .background(Capsule().fill(Palette.copper.opacity(0.13)))
                    .foregroundStyle(Palette.copper)
            }
        }
    }

    private var engineCard: some View {
        Card(title: "Engine", systemImage: "bolt.fill") {
            TimelineView(.periodic(from: .now, by: 30)) { _ in
                VStack(alignment: .leading, spacing: Space.m) {
                    if state.engineRunning {
                        HStack(alignment: .top, spacing: Space.xl) {
                            metric(miceCount(state.listeningNames.count), label: "Listening")
                            metric(duration(since: state.status?.startedAt), label: "Uptime")
                            metric(state.status?.lastWakeRecovery == nil ? "Ready" : "Recovered", label: "After sleep")
                        }
                        Text(engineCaption).font(.caption).foregroundStyle(.secondary).lineLimit(2)
                    } else {
                        Text(state.config.settings.engineEnabled ? "The engine isn't running." : "The engine is switched off.")
                            .foregroundStyle(Palette.clay)
                    }
                }
            }
            Spacer(minLength: 0)
            HStack(spacing: Space.m) {
                if state.engineRunning {
                    Button("Restart Engine") { restartEngine() }
                } else {
                    Button("Start Engine") { startEngine() }.buttonStyle(.borderedProminent)
                }
                Button("Diagnostics") { state.openSettings(.diagnostics) }.buttonStyle(.accentLink)
            }
        }
    }

    private var engineCaption: String {
        if !state.inputMonitoringGranted { return "Waiting for Input Monitoring permission" }
        let names = state.listeningNames
        if names.isEmpty { return "Waiting for a mouse" }
        return names.joined(separator: " · ")
    }

    private func metric(_ value: String, label: String) -> some View {
        VStack(alignment: .leading, spacing: 2) {
            Text(value).font(.callout.weight(.semibold)).monospacedDigit()
            Text(label).font(.caption2).foregroundStyle(.secondary)
        }
    }

    private var miceCard: some View {
        Card(title: "Mice", systemImage: "computermouse.fill") {
            if !state.engineRunning {
                Text("Start the engine to see your mice.").foregroundStyle(.secondary)
            } else if !state.inputMonitoringGranted {
                VStack(alignment: .leading, spacing: 6) {
                    Text("Mice appear once Input Monitoring is allowed.").foregroundStyle(.secondary)
                        .fixedSize(horizontal: false, vertical: true)
                    Button("Open Settings") { NSWorkspace.shared.open(SystemSettingsLink.inputMonitoring) }
                        .buttonStyle(.accentLink)
                }
            } else if state.connectedMice.isEmpty {
                Text("No mouse connected.").foregroundStyle(.secondary)
            } else {
                VStack(alignment: .leading, spacing: Space.m) {
                    ForEach(state.connectedMice) { d in
                        HStack(spacing: Space.m) {
                            Image(systemName: d.isBuiltIn ? "laptopcomputer" : "computermouse.fill")
                                .font(.system(size: 13))
                                .foregroundStyle(Palette.stone)
                                .frame(width: 18)
                            VStack(alignment: .leading, spacing: 1) {
                                Text(d.name).lineLimit(1)
                                Text(transportName(d.transport)).font(.caption).foregroundStyle(.secondary)
                            }
                            Spacer()
                            if d.listening == true {
                                Circle().fill(Palette.sage).frame(width: 6, height: 6).help("Listening")
                            }
                        }
                    }
                }
            }
            Spacer(minLength: 0)
            if state.hiddenBuiltInCount > 0 {
                Button("\(state.hiddenBuiltInCount) built-in device\(state.hiddenBuiltInCount == 1 ? "" : "s") hidden · Show") {
                    state.update { $0.settings.showBuiltInDevices = true }
                }
                .buttonStyle(.accentLink)
                .font(.caption)
            }
        }
    }

    // MARK: Banners & actions

    @ViewBuilder private var banners: some View {
        if state.status?.emergencyPaused == true {
            Banner(kind: .warning, text: "Emergency pause is on (⌃⌥⌘⎋). Mappings and button blocking are off.",
                   actionTitle: "Resume") { state.resumeFromEmergency() }
        }
        if let v = state.engineVersionMismatch {
            Banner(kind: .info, text: "The engine is still version \(v). Restart it to use Clicksmith \(ClicksmithInfo.displayVersion).",
                   actionTitle: "Restart") { restartEngine() }
        }
        if let e = state.configLoadError { Banner(kind: .error, text: e) }
        if let e = state.saveError { Banner(kind: .error, text: e) }
        // Permission problems are shown once, in the setup card above.
        if state.missingPermissions == 0, let e = state.status?.lastError {
            Banner(kind: .warning, text: e, actionTitle: "Diagnostics") { state.openSettings(.diagnostics) }
        }
        if let m = state.engineMessage { Banner(kind: .error, text: m) }
    }

    private func restartEngine() {
        state.engine.perform({ state.engine.restart() }) { state.engineMessage = $0; state.refreshStatus() }
    }

    private func startEngine() {
        let atLogin = state.config.settings.launchEngineAtLogin
        state.update { $0.settings.engineEnabled = true }
        state.engine.perform({ state.engine.install(atLogin: atLogin) }) { state.engineMessage = $0; state.refreshStatus() }
    }
}
