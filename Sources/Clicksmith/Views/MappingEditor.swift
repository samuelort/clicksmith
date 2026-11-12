import SwiftUI
import AppKit
import ClicksmithCore

// MARK: - New mapping: pick mouse → press button → editor

struct NewMappingSheet: View {
    @ObservedObject private var state = AppState.shared
    @Environment(\.dismiss) private var dismiss
    @Local private var deviceID: String?
    @Local private var draft: ButtonMapping? = nil

    init(deviceID: String? = nil) {
        _deviceID = Local(wrappedValue: deviceID)
    }

    private var learning: Bool { state.activeLearn?.mode == .single }
    private var device: DeviceInfo? { state.connectedMice.first { $0.id == deviceID } }

    var body: some View {
        Group {
            if let draft {
                MappingEditor(mapping: draft, isNew: !state.config.mappings.contains { $0.id == draft.id }) { dismiss() }
            } else {
                chooser
            }
        }
        .onAppear {
            if device == nil { deviceID = state.connectedMice.first?.id }
        }
        .onReceive(state.$learnResult) { result in
            guard let result, result.finished, let b = result.buttons.first, draft == nil else { return }
            draft = state.mapping(for: b.device, button: b.button) ?? MappingEditor.draft(from: b)
            DispatchQueue.main.async { state.clearLearnResult() }
        }
        .onDisappear { if learning { state.stopLearning() } }
    }

    private var chooser: some View {
        VStack(alignment: .leading, spacing: 18) {
            Text("New Mapping").font(.title2.weight(.semibold))
            if state.connectedMice.count > 1 {
                Picker("Mouse", selection: $deviceID) {
                    ForEach(state.connectedMice) { d in Text(d.name).tag(Optional(d.id)) }
                }
                .frame(maxWidth: 360)
            } else if let device {
                Label(device.name, systemImage: "computermouse.fill").foregroundStyle(.secondary)
            }
            if state.connectedMice.isEmpty {
                Banner(kind: .warning, text: "No mouse detected. Check that Clicksmith Engine is running and has Input Monitoring permission.")
            }
            VStack(spacing: 14) {
                Image(systemName: learning ? "hand.point.up.left.fill" : "computermouse")
                    .font(.system(size: 46, weight: .light))
                    .foregroundStyle(learning ? Color.ember : Color.secondary)
                Text(learning ? "Press the mouse button you want to configure." : "Click “Learn Button”, keep the mouse still, then press the button.")
                    .multilineTextAlignment(.center)
                if learning { ProgressView().controlSize(.small) }
            }
            .frame(maxWidth: .infinity, minHeight: 170)
            .background(RoundedRectangle(cornerRadius: 14, style: .continuous).fill(Palette.stone.opacity(0.07)))
            HStack {
                Button("Cancel") { if learning { state.stopLearning() }; dismiss() }
                    .keyboardShortcut(.cancelAction)
                Spacer()
                if learning {
                    Button("Stop") { state.stopLearning() }
                } else {
                    Button("Learn Button") { state.startLearning(device: device?.key, mode: .single) }
                        .buttonStyle(.borderedProminent)
                        .disabled(device == nil)
                }
            }
        }
        .padding(24)
        .frame(width: 520)
    }
}

// MARK: - Editor

/// 1.2.2: back to the proven 1.2.0 behaviour — Save is disabled until the mapping is
/// complete, and the sheet has a fixed size. What's missing is shown as a quiet grey line
/// above the buttons (full width, so it never gets cut off).
struct MappingEditor: View {
    @ObservedObject private var state = AppState.shared
    @Environment(\.dismiss) private var dismiss
    @Local private var m: ButtonMapping
    @Local private var hasDefault: Bool
    @Local private var defaultAction: ButtonAction
    @Local private var showAdvanced = false
    let isNew: Bool
    var onDone: (() -> Void)?

    init(mapping: ButtonMapping, isNew: Bool, onDone: (() -> Void)? = nil) {
        _m = Local(wrappedValue: mapping)
        _hasDefault = Local(wrappedValue: mapping.defaultAction != nil)
        _defaultAction = Local(wrappedValue: mapping.defaultAction ?? ButtonAction(kind: .shortcut))
        self.isNew = isNew
        self.onDone = onDone
    }

    static func draft(from d: DetectedButton) -> ButtonMapping {
        ButtonMapping(device: d.device, deviceName: d.deviceName, button: d.button, label: d.suggestedLabel,
                      systemButtonNumber: d.systemButtonNumber,
                      defaultAction: ButtonAction(kind: .shortcut))
    }

    var body: some View {
        VStack(spacing: 0) {
            HStack(spacing: 12) {
                Image(systemName: "computermouse.fill").font(.title2).foregroundStyle(Color.ember)
                VStack(alignment: .leading, spacing: 1) {
                    Text(isNew ? "New Mapping" : "Edit Mapping").font(.title3.weight(.semibold))
                    Text(m.deviceName).font(.caption).foregroundStyle(.secondary)
                }
                Spacer()
                Toggle("Enabled", isOn: $m.enabled).toggleStyle(.switch)
            }
            .padding(.horizontal, 20)
            .padding(.top, 18)
            .padding(.bottom, 6)

            Form {
                Section {
                    TextField("Name", text: $m.label)
                }

                Section("Everywhere (default)") {
                    Toggle("Do something in all apps", isOn: $hasDefault)
                    if hasDefault {
                        ActionEditor(action: $defaultAction, systemButtonNumber: m.systemButtonNumber)
                    } else {
                        Text("The button only does something in the apps below.").foregroundStyle(.secondary)
                    }
                }

                Section {
                    ForEach($m.overrides) { $o in
                        HStack(alignment: .top, spacing: 10) {
                            Image(nsImage: AppIcons.appIcon(bundleID: o.bundleID)).resizable().frame(width: 28, height: 28)
                            VStack(alignment: .leading, spacing: 6) {
                                HStack {
                                    Text("In \(o.appName)").font(.body.weight(.medium)).help(o.bundleID)
                                    Spacer()
                                    Toggle("", isOn: $o.enabled).labelsHidden().toggleStyle(.switch).controlSize(.mini)
                                    Button {
                                        let id = o.id
                                        DispatchQueue.main.async { m.overrides.removeAll { $0.id == id } }
                                    } label: { Image(systemName: "trash") }
                                    .buttonStyle(.borderless)
                                    .help("Remove \(o.appName)")
                                }
                                ActionEditor(action: $o.action, systemButtonNumber: m.systemButtonNumber)
                            }
                        }
                        .padding(.vertical, 4)
                    }
                    Menu {
                        ForEach(runningRegularApps(), id: \.processIdentifier) { app in
                            Button(app.localizedName ?? app.bundleIdentifier ?? "App") {
                                addOverride(bundleID: app.bundleIdentifier ?? "", name: app.localizedName ?? "App")
                            }
                        }
                        Divider()
                        Button("Choose from Applications…") {
                            if let picked = chooseApplication() { addOverride(bundleID: picked.0, name: picked.1) }
                        }
                    } label: { Label("Add an App", systemImage: "plus.app") }
                    .fixedSize()
                } header: {
                    Text("Different in specific apps")
                } footer: {
                    Text("When one of these apps is in front, its action is used. Switching away returns to the default instantly.")
                        .font(.caption).foregroundStyle(.secondary)
                }

                Section {
                    DisclosureGroup("Advanced", isExpanded: $showAdvanced) {
                        Picker("Normal macOS button", selection: $m.systemButtonNumber) {
                            Text("None / special button").tag(Int?.none)
                            ForEach(0..<8, id: \.self) { n in Text(SystemButton.name(n)).tag(Int?.some(n)) }
                        }
                        LabeledContent("Report ID", value: String(format: "0x%02X", m.button.reportID))
                        LabeledContent("Byte", value: "\(m.button.byteIndex)")
                        LabeledContent("Bit mask", value: String(format: "0x%02X", m.button.mask))
                        LabeledContent("Device", value: m.device.shortID + (m.device.serial.map { " · \($0)" } ?? ""))
                    }
                }
            }
            .formStyle(.grouped)
            .scrollContentBackground(.hidden)
            .background(Palette.canvas)

            if let e = validationError {
                Label(e, systemImage: "info.circle")
                    .font(.callout)
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .padding(.horizontal, 20)
                    .padding(.top, 10)
            }

            HStack {
                if !isNew {
                    Button("Delete", role: .destructive) { state.delete(m.id); close() }
                }
                Spacer()
                Button("Cancel") { close() }.keyboardShortcut(.cancelAction)
                Button(isNew ? "Add Mapping" : "Save") { save() }
                    .keyboardShortcut(.defaultAction)
                    .buttonStyle(.borderedProminent)
                    .disabled(validationError != nil)
            }
            .padding(16)
        }
        .frame(width: 620, height: 660)
        .background(Palette.canvas)
    }

    private var validationError: String? {
        if m.label.trimmingCharacters(in: .whitespaces).isEmpty { return "Give the button a name." }
        var actions = m.overrides.map(\.action)
        if hasDefault { actions.append(defaultAction) }
        if let problem = actions.compactMap(\.problem).first { return problem }
        if !hasDefault && m.overrides.isEmpty { return "Choose an action for all apps or add at least one app." }
        let ids = m.overrides.map(\.bundleID)
        if Set(ids).count != ids.count { return "Each app can only appear once per button." }
        return nil
    }

    private func addOverride(bundleID: String, name: String) {
        guard !bundleID.isEmpty, !m.overrides.contains(where: { $0.bundleID == bundleID }) else { return }
        m.overrides.append(AppOverride(bundleID: bundleID, appName: name,
                                       action: ButtonAction(kind: .shortcut, blockOriginal: m.systemButtonNumber != nil)))
    }

    private func save() {
        var result = m
        result.label = result.label.trimmingCharacters(in: .whitespaces)
        result.defaultAction = hasDefault ? defaultAction : nil
        state.upsert(result)
        if state.saveError == nil { close() }
    }

    private func close() {
        if let onDone { onDone() } else { dismiss() }
    }
}
