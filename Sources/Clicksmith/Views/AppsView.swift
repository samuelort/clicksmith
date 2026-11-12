import SwiftUI
import AppKit
import ClicksmithCore

struct PickedApp: Identifiable, Hashable {
    let id: String   // bundle ID
    let name: String
}

/// Per-app profiles, with "Add App" on the page itself.
struct AppsView: View {
    @ObservedObject private var state = AppState.shared
    @Local private var adding: PickedApp? = nil
    @Local private var editing: ButtonMapping? = nil
    @Local private var confirmRemove: PickedApp? = nil

    private struct Item: Identifiable {
        let mapping: ButtonMapping
        let override: AppOverride
        var id: UUID { override.id }
    }

    private struct AppGroup: Identifiable {
        let id: String
        let name: String
        var items: [Item]
    }

    private var apps: [AppGroup] {
        var dict: [String: AppGroup] = [:]
        for m in state.config.mappings {
            for o in m.overrides {
                dict[o.bundleID, default: AppGroup(id: o.bundleID, name: o.appName, items: [])]
                    .items.append(Item(mapping: m, override: o))
            }
        }
        return dict.values.sorted { $0.name.localizedCaseInsensitiveCompare($1.name) == .orderedAscending }
    }

    private var defaults: [ButtonMapping] { state.config.mappings.filter { $0.defaultAction != nil } }

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 18) {
                HStack(alignment: .firstTextBaseline) {
                    PageHeader(title: "Apps", subtitle: "Give buttons a different job in specific apps. Everywhere else, the defaults apply.")
                    addMenu
                }
                if let last = state.lastUsedApp {
                    Banner(kind: .info, text: last.profile == nil
                           ? "\(last.name) was last in front and uses the default buttons."
                           : "\(last.name) was last in front and uses its own profile.")
                }
                defaultCard
                ForEach(apps) { app in appCard(app) }
                if apps.isEmpty { emptyCard }
            }
            .padding(.horizontal, 32)
            .padding(.vertical, 24)
        }
        .sheet(item: $adding) { app in
            AddAppSheet(app: app) { mapping in
                adding = nil
                DispatchQueue.main.asyncAfter(deadline: .now() + 0.35) { editing = mapping }
            }
            .tint(Color.ember)
        }
        .sheet(item: $editing) { (m: ButtonMapping) in MappingEditor(mapping: m, isNew: false).tint(Color.ember) }
        .alert("Remove \(confirmRemove?.name ?? "app")?", isPresented: Binding(
            get: { confirmRemove != nil }, set: { if !$0 { confirmRemove = nil } })) {
            Button("Cancel", role: .cancel) { confirmRemove = nil }
            Button("Remove", role: .destructive) {
                if let app = confirmRemove { state.removeApp(app.id) }
                confirmRemove = nil
            }
        } message: {
            Text("Its buttons go back to the default behaviour.")
        }
    }

    // MARK: Add

    private var addMenu: some View {
        Menu {
            let existing = Set(apps.map(\.id))
            let running = runningRegularApps().filter { !existing.contains($0.bundleIdentifier ?? "") }
            if !running.isEmpty {
                Section("Running apps") {
                    ForEach(running, id: \.processIdentifier) { app in
                        Button(app.localizedName ?? app.bundleIdentifier ?? "App") {
                            adding = PickedApp(id: app.bundleIdentifier ?? "", name: app.localizedName ?? "App")
                        }
                    }
                }
            }
            Divider()
            Button("Choose from Applications…") {
                if let picked = chooseApplication() { adding = PickedApp(id: picked.0, name: picked.1) }
            }
        } label: {
            Label("Add App", systemImage: "plus")
        }
        .fixedSize()
        .disabled(state.config.mappings.isEmpty)
        .help(state.config.mappings.isEmpty ? "Map a mouse button first" : "Choose an app to give a button a different job")
    }

    // MARK: Cards

    private var defaultCard: some View {
        Card(title: "Default · all other apps", systemImage: "globe") {
            if defaults.isEmpty {
                Text("No button has a default action yet.").foregroundStyle(.secondary)
            }
            ForEach(defaults) { m in
                HStack {
                    Text(m.label)
                    if state.hasSeveralMice { Text(m.deviceName).font(.caption).foregroundStyle(.secondary) }
                    Spacer()
                    if let d = m.defaultAction { ShortcutBadge(text: d.display).opacity(m.enabled ? 1 : 0.45) }
                    Button("Edit") { editing = m }.buttonStyle(.accentLink)
                }
            }
        }
    }

    private func appCard(_ app: AppGroup) -> some View {
        VStack(alignment: .leading, spacing: 10) {
            HStack(spacing: 12) {
                Image(nsImage: AppIcons.appIcon(bundleID: app.id)).resizable().frame(width: 32, height: 32)
                VStack(alignment: .leading, spacing: 1) {
                    Text(app.name).font(.title3.weight(.semibold))
                    Text("\(app.items.count) button\(app.items.count == 1 ? "" : "s") changed")
                        .font(.caption).foregroundStyle(.secondary)
                }
                Spacer()
                Menu {
                    Button("Add another button…") { adding = PickedApp(id: app.id, name: app.name) }
                    Divider()
                    Button("Remove App…", role: .destructive) { confirmRemove = PickedApp(id: app.id, name: app.name) }
                } label: {
                    Image(systemName: "ellipsis.circle").foregroundStyle(.secondary)
                }
                .menuStyle(.borderlessButton)
                .menuIndicator(.hidden)
                .fixedSize()
            }
            .help(app.id)
            Divider()
            ForEach(app.items) { item in
                HStack(spacing: 10) {
                    VStack(alignment: .leading, spacing: 1) {
                        Text(item.mapping.label).font(.body.weight(.medium))
                        if state.hasSeveralMice {
                            Text(item.mapping.deviceName).font(.caption).foregroundStyle(.secondary)
                        }
                    }
                    Spacer()
                    if let d = item.mapping.defaultAction {
                        Text("instead of \(d.display)").font(.caption).foregroundStyle(.secondary)
                    }
                    ShortcutBadge(text: item.override.action.display)
                        .opacity(item.override.enabled ? 1 : 0.45)
                    Toggle("", isOn: Binding(get: { item.override.enabled },
                                             set: { state.toggleOverride(mappingID: item.mapping.id, overrideID: item.override.id, enabled: $0) }))
                        .labelsHidden()
                        .toggleStyle(.switch)
                        .controlSize(.mini)
                        .accessibilityLabel("Enable \(item.mapping.label) in \(app.name)")
                    Button("Edit") { editing = item.mapping }.buttonStyle(.accentLink)
                }
            }
        }
        .padding(18)
        .frame(maxWidth: .infinity, alignment: .leading)
        .cardBackground()
    }

    private var emptyCard: some View {
        VStack(spacing: 10) {
            Image(systemName: "square.grid.2x2").font(.system(size: 34, weight: .light)).foregroundStyle(Color.ember)
            Text("No app-specific buttons yet").font(.title3.weight(.semibold))
            Text("Example: make Back mute your microphone (⇧⌘M) only in Microsoft Teams.")
                .foregroundStyle(.secondary)
                .multilineTextAlignment(.center)
        }
        .frame(maxWidth: .infinity)
        .padding(28)
        .cardBackground()
    }
}

/// Step 1 of adding an app: choose which mouse button changes there.
struct AddAppSheet: View {
    let app: PickedApp
    let onPick: (ButtonMapping) -> Void
    @ObservedObject private var state = AppState.shared
    @Environment(\.dismiss) private var dismiss

    var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            HStack(spacing: 12) {
                Image(nsImage: AppIcons.appIcon(bundleID: app.id)).resizable().frame(width: 40, height: 40)
                VStack(alignment: .leading, spacing: 2) {
                    Text("Change a button in \(app.name)").font(.title2.weight(.semibold))
                    Text("Choose the button. Next you pick what it does in \(app.name).").foregroundStyle(.secondary)
                }
            }
            VStack(spacing: 4) {
                ForEach(state.config.mappings) { m in
                    Button { onPick(withOverride(m)) } label: {
                        HStack {
                            Image(systemName: "computermouse.fill").foregroundStyle(Color.ember)
                            VStack(alignment: .leading, spacing: 1) {
                                Text(m.label).font(.body.weight(.medium))
                                Text(m.deviceName).font(.caption).foregroundStyle(.secondary)
                            }
                            Spacer()
                            if m.overrides.contains(where: { $0.bundleID == app.id }) {
                                Text("Already changed").font(.caption).foregroundStyle(.secondary)
                            }
                            Image(systemName: "chevron.right").foregroundStyle(.tertiary)
                        }
                        .padding(10)
                        .background(RoundedRectangle(cornerRadius: 10, style: .continuous).fill(Palette.stone.opacity(0.08)))
                        .contentShape(Rectangle())
                    }
                    .buttonStyle(.plain)
                }
            }
            Text("Want a button that isn't listed? Map it first on the Buttons page.")
                .font(.caption).foregroundStyle(.secondary)
            HStack {
                Spacer()
                Button("Cancel") { dismiss() }.keyboardShortcut(.cancelAction)
            }
        }
        .padding(24)
        .frame(width: 480)
    }

    private func withOverride(_ m: ButtonMapping) -> ButtonMapping {
        guard !m.overrides.contains(where: { $0.bundleID == app.id }) else { return m }
        var copy = m
        copy.overrides.append(AppOverride(bundleID: app.id, appName: app.name,
                                          action: ButtonAction(kind: .shortcut, blockOriginal: m.systemButtonNumber != nil)))
        return copy
    }
}
