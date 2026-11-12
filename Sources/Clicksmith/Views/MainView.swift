import SwiftUI
import ClicksmithCore

/// Custom sidebar (four sections, Clicksmith accent) on the native sidebar material,
/// running under the transparent title bar.
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
                SidebarRow(item: item, selected: state.selection == item) { state.selection = item }
                    .keyboardShortcut(KeyEquivalent(Character("\(index + 1)")), modifiers: .command)
            }
            Spacer()
            HStack(spacing: 8) {
                Image(nsImage: AppIcons.logo).resizable().frame(width: 18, height: 18)
                Text("Clicksmith \(ClicksmithInfo.version)").font(.caption).foregroundStyle(.secondary)
            }
            .padding(.horizontal, 8)
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
            }
            .padding(.horizontal, 8)
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
            VStack(alignment: .leading, spacing: 20) {
                header
                banners
                // 1.2.4: two aligned rows; cards in a row share their height.
                VStack(spacing: 16) {
                    HStack(alignment: .top, spacing: 16) {
                        buttonsCard
                        profilesCard
                    }
                    .fixedSize(horizontal: false, vertical: true)
                    HStack(alignment: .top, spacing: 16) {
                        engineCard
                        miceCard
                    }
                    .fixedSize(horizontal: false, vertical: true)
                }
            }
            .padding(.horizontal, 32)
            .padding(.vertical, 24)
        }
        .sheet(isPresented: $showNewMapping) { NewMappingSheet().tint(Color.ember) }
        .sheet(item: $editing) { (m: ButtonMapping) in MappingEditor(mapping: m, isNew: false).tint(Color.ember) }
    }

    // MARK: Header

    private var header: some View {
        HStack(spacing: 16) {
            Image(nsImage: AppIcons.logo).resizable().frame(width: 60, height: 60)
                .shadow(color: .black.opacity(0.08), radius: 8, y: 3)
                .accessibilityHidden(true)
            VStack(alignment: .leading, spacing: 6) {
                Text("Clicksmith").font(.largeTitle.weight(.semibold))
                HStack(spacing: 8) {
                    StatusPill(state: state.visualState, text: state.statusText)
                    Text("Forge every mouse button into something useful.").foregroundStyle(.secondary)
                }
            }
            Spacer()
            Toggle("Pause all", isOn: Binding(get: { state.config.settings.paused }, set: { state.setPaused($0) }))
                .toggleStyle(.switch)
                .help("Pause every mapping (⌃⌥⌘⎋ for an emergency pause)")
        }
    }

    // MARK: Cards

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
                        MappingRow(mapping: m, showDevice: state.hasSeveralMice) { editing = m }
                        if m.id != state.config.mappings.last?.id { Divider().padding(.leading, 30) }
                    }
                }
            }
            Spacer(minLength: 0)
            HStack {
                Button { showNewMapping = true } label: { Label("New Mapping", systemImage: "plus") }
                    .buttonStyle(.borderedProminent)
                    .disabled(!state.engineRunning)
                    .help(state.engineRunning ? "Map another mouse button" : "Start the engine first")
                Spacer()
                if !state.config.mappings.isEmpty {
                    Button("Open Buttons") { state.selection = .buttons }.buttonStyle(.accentLink)
                }
            }
            .padding(.top, 4)
        }
    }

    private var engineCard: some View {
        Card(title: "Engine", systemImage: "bolt.fill") {
            TimelineView(.periodic(from: .now, by: 30)) { _ in
                VStack(alignment: .leading, spacing: 4) {
                    if state.engineRunning {
                        Text(listeningText)
                        Text("Running since \(relative(state.status?.startedAt))").font(.caption).foregroundStyle(.secondary)
                        if let wake = state.status?.lastWake {
                            Label("Woke \(relative(wake)) · refreshed \(relative(state.status?.lastWakeRecovery))", systemImage: "moon.zzz")
                                .font(.caption).foregroundStyle(.secondary)
                        } else {
                            Label("Refreshes itself after every sleep", systemImage: "moon.zzz")
                                .font(.caption).foregroundStyle(.secondary)
                        }
                    } else {
                        Text(state.config.settings.engineEnabled ? "The engine isn't running." : "The engine is switched off.")
                            .foregroundStyle(Palette.amber)
                    }
                }
            }
            Spacer(minLength: 0)
            HStack(spacing: 12) {
                if state.engineRunning {
                    Button("Restart Engine") { restartEngine() }
                } else {
                    Button("Start Engine") { startEngine() }.buttonStyle(.borderedProminent)
                }
                Button("Diagnostics") { state.openSettings(.diagnostics) }.buttonStyle(.accentLink)
            }
        }
    }

    private var listeningText: String {
        let names = state.listeningNames
        if names.isEmpty { return "Waiting for a mouse" }
        return "Listening to " + names.joined(separator: ", ")
    }

    /// Lists profiles instead of describing Clicksmith itself (which is always in front
    /// while this window is open). The "Last used" badge only marks app profiles;
    /// the caption already says when the last app used Default.
    private var profilesCard: some View {
        Card(title: "Profiles", systemImage: "square.stack.3d.up") {
            VStack(alignment: .leading, spacing: 8) {
                profileRow(icon: Image(systemName: "globe"), name: "Default", detail: "All other apps", active: false)
                ForEach(state.appProfiles) { p in
                    profileRow(icon: Image(nsImage: AppIcons.appIcon(bundleID: p.id)), name: p.name,
                               detail: "\(p.overrideCount) button\(p.overrideCount == 1 ? "" : "s") changed",
                               active: state.lastUsedApp?.bundleID == p.id)
                }
            }
            if let last = state.lastUsedApp {
                Text(last.profile == nil ? "\(last.name) was last in front and uses Default."
                                         : "\(last.name) was last in front and uses its own profile.")
                    .font(.caption).foregroundStyle(.secondary)
            }
            Spacer(minLength: 0)
            Button(state.appProfiles.isEmpty ? "Set up an app" : "Manage apps") { state.selection = .apps }
                .buttonStyle(.accentLink)
        }
    }

    private func profileRow(icon: Image, name: String, detail: String, active: Bool) -> some View {
        HStack(spacing: 10) {
            icon.resizable().scaledToFit().frame(width: 22, height: 22).foregroundStyle(Color.ember)
            VStack(alignment: .leading, spacing: 1) {
                Text(name).font(.body.weight(.medium))
                Text(detail).font(.caption).foregroundStyle(.secondary)
            }
            Spacer()
            if active {
                Text("Last used").font(.caption2.weight(.semibold))
                    .padding(.horizontal, 6).padding(.vertical, 2)
                    .background(Capsule().fill(Color.ember.opacity(0.15)))
                    .foregroundStyle(Color.ember)
            }
        }
    }

    private var miceCard: some View {
        Card(title: "Mice", systemImage: "computermouse.fill") {
            if state.connectedMice.isEmpty {
                Text(state.engineRunning ? "No mouse connected." : "Start the engine to see your mice.")
                    .foregroundStyle(.secondary)
            }
            ForEach(state.connectedMice) { d in
                HStack {
                    Text(d.name).lineLimit(1)
                    Spacer()
                    Text(transportName(d.transport)).font(.caption).foregroundStyle(.secondary)
                }
            }
            if state.hiddenBuiltInCount > 0 {
                Button("\(state.hiddenBuiltInCount) built-in device\(state.hiddenBuiltInCount == 1 ? "" : "s") hidden · Show") {
                    state.update { $0.settings.showBuiltInDevices = true }
                }
                .buttonStyle(.accentLink)
                .font(.caption)
            }
        }
    }

    private var sleepCard: some View {
        Card(title: "Sleep & wake", systemImage: "moon.zzz.fill") {
            TimelineView(.periodic(from: .now, by: 30)) { _ in
                if let wake = state.status?.lastWake {
                    VStack(alignment: .leading, spacing: 4) {
                        Text("Last wake \(relative(wake))")
                        Text(state.status?.lastWakeRecovery.map { "Engine refreshed \(relative($0))" } ?? "Engine refresh pending")
                            .font(.caption).foregroundStyle(.secondary)
                    }
                } else {
                    Text("No sleep yet. After the Mac wakes, the engine restarts itself fresh so buttons keep working.")
                        .foregroundStyle(.secondary)
                        .fixedSize(horizontal: false, vertical: true)
                }
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
            Banner(kind: .info, text: "The engine is still version \(v). Restart it to use Clicksmith \(ClicksmithInfo.version).",
                   actionTitle: "Restart") { restartEngine() }
        }
        if let e = state.configLoadError { Banner(kind: .error, text: e) }
        if let e = state.saveError { Banner(kind: .error, text: e) }
        if let e = state.status?.lastError {
            Banner(kind: .warning, text: e, actionTitle: "Permissions") { state.openSettings(.permissions) }
        }
        if let m = state.engineMessage { Banner(kind: .error, text: m) }
        if state.engineRunning && state.status?.accessibility == false {
            Banner(kind: .warning, text: "Clicksmith Engine needs Accessibility permission to send actions.",
                   actionTitle: "Fix") { state.openSettings(.permissions) }
        }
    }

    private func restartEngine() {
        state.engine.perform({ state.engine.restart() }) { state.engineMessage = $0 }
    }

    private func startEngine() {
        let atLogin = state.config.settings.launchEngineAtLogin
        state.update { $0.settings.engineEnabled = true }
        state.engine.perform({ state.engine.install(atLogin: atLogin) }) { state.engineMessage = $0; state.refreshStatus() }
    }
}
