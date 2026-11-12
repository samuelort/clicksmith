import SwiftUI
import AppKit
import ClicksmithCore

/// Press-to-record shortcut field. Uses a local event monitor, so it only sees keys typed
/// into Clicksmith's own window while recording — no extra permission needed.
/// Esc on its own cancels recording (⎋ with modifiers can still be recorded).
struct ShortcutRecorder: View {
    @Binding var shortcut: KeyShortcut?
    @Local private var recording = false
    @Local private var monitor: Any? = nil

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack(spacing: 8) {
                Button(action: toggle) {
                    HStack(spacing: 6) {
                        Image(systemName: recording ? "record.circle" : "keyboard")
                        Text(recording ? "Press a shortcut… (Esc cancels)" : (shortcut?.display ?? "Record Shortcut"))
                            .font(.system(.body, design: .rounded).weight(.medium))
                    }
                    .frame(minWidth: 170)
                }
                .buttonStyle(.bordered)
                .tint(recording ? Color.ember : nil)
                .accessibilityLabel(recording ? "Recording shortcut. Press the key combination, or Escape to cancel." : "Shortcut: \(shortcut?.display ?? "none"). Activate to record.")

                if !recording && shortcut != nil {
                    Button { shortcut = nil } label: { Image(systemName: "xmark.circle.fill") }
                        .buttonStyle(.plain)
                        .foregroundStyle(.secondary)
                        .help("Clear shortcut")
                }
            }
            if let s = shortcut, let warning = KeyNames.warning(for: s) {
                Label(warning, systemImage: "exclamationmark.triangle")
                    .font(.caption)
                    .foregroundStyle(Palette.amber)
            }
        }
        .onDisappear { stop() }
    }

    private func toggle() { recording ? stop() : start() }

    private func start() {
        recording = true
        let binding = $shortcut
        monitor = NSEvent.addLocalMonitorForEvents(matching: [.keyDown]) { event in
            let flags = event.modifierFlags.intersection(.deviceIndependentFlagsMask)
            let hasModifier = !flags.intersection([.command, .shift, .option, .control]).isEmpty
            if event.keyCode == 53 && !hasModifier {   // plain Esc: cancel, keep the old shortcut
                stop()
                return nil
            }
            var name = KeyNames.name(for: event.keyCode)
            if name.hasPrefix("Key "), let chars = event.charactersIgnoringModifiers, !chars.isEmpty {
                name = chars.uppercased()
            }
            binding.wrappedValue = KeyShortcut(keyCode: event.keyCode,
                                               command: flags.contains(.command),
                                               shift: flags.contains(.shift),
                                               option: flags.contains(.option),
                                               control: flags.contains(.control),
                                               function: false,
                                               keyName: name)
            stop()
            return nil
        }
    }

    private func stop() {
        if recording { recording = false }
        if let m = monitor {
            NSEvent.removeMonitor(m)
            monitor = nil
        }
    }
}

/// Editor for one action (default or per-app): what it does + whether it blocks the original.
/// The action picker and its value sit on one row.
struct ActionEditor: View {
    @Binding var action: ButtonAction
    let systemButtonNumber: Int?
    @ObservedObject private var state = AppState.shared

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            HStack(alignment: .top, spacing: 12) {
                Picker("Action", selection: $action.kind) {
                    ForEach(ActionKind.allCases, id: \.self) { k in
                        Label(k.title, systemImage: icon(k)).tag(k)
                    }
                }
                .pickerStyle(.menu)
                .labelsHidden()
                .fixedSize()

                switch action.kind {
                case .shortcut:
                    ShortcutRecorder(shortcut: $action.shortcut)
                case .mediaKey:
                    Picker("Key", selection: Binding(get: { action.mediaKey ?? .playPause },
                                                     set: { action.mediaKey = $0 })) {
                        ForEach(MediaKey.allCases, id: \.self) { k in Text("\(k.symbol)  \(k.title)").tag(k) }
                    }
                    .pickerStyle(.menu)
                    .labelsHidden()
                    .fixedSize()
                    .onAppear { if action.mediaKey == nil { action.mediaKey = .playPause } }
                case .openApp:
                    AppPickerRow(bundleID: $action.appBundleID, name: $action.appName)
                case .typeText, .nothing:
                    EmptyView()
                }
                Spacer(minLength: 0)
            }

            if action.kind == .typeText {
                VStack(alignment: .leading, spacing: 4) {
                    TextField("Text to type", text: Binding(get: { action.text ?? "" }, set: { action.text = $0 }),
                              axis: .vertical)
                        .lineLimit(2...6)
                        .textFieldStyle(.roundedBorder)
                    Text("\((action.text ?? "").count)/\(ButtonAction.maxTextLength) characters · typed into the app in front")
                        .font(.caption).foregroundStyle(.secondary)
                    Label("Don't store passwords here: the text is saved unencrypted, and it's never typed into password fields.",
                          systemImage: "lock.slash")
                        .font(.caption).foregroundStyle(.secondary)
                }
            }

            if systemButtonNumber != nil {
                Toggle("Block the button's normal action", isOn: $action.blockOriginal)
            }
            if let note = blockNote {
                Label(note, systemImage: "info.circle").font(.caption).foregroundStyle(.secondary)
            }
        }
    }

    private func icon(_ k: ActionKind) -> String {
        switch k {
        case .shortcut: return "keyboard"
        case .mediaKey: return "playpause"
        case .openApp: return "app.badge"
        case .typeText: return "text.cursor"
        case .nothing: return "nosign"
        }
    }

    private var blockNote: String? {
        guard action.blockOriginal, let n = systemButtonNumber else { return nil }
        if n <= 1 && !state.config.settings.allowPrimaryButtonSuppression {
            return "Blocking \(SystemButton.name(n)) is off for safety. Allow it in Settings if you really need it."
        }
        return "\(SystemButton.name(n)) is blocked on all mice while this is active."
    }
}

/// Picks an app to open: running apps or any app from /Applications.
struct AppPickerRow: View {
    @Binding var bundleID: String?
    @Binding var name: String?

    var body: some View {
        HStack(spacing: 8) {
            if let id = bundleID {
                Image(nsImage: AppIcons.appIcon(bundleID: id)).resizable().frame(width: 22, height: 22)
                Text(name ?? id)
            } else {
                Text("No app chosen").foregroundStyle(.secondary)
            }
            Menu(bundleID == nil ? "Choose App" : "Change") {
                ForEach(runningRegularApps(), id: \.processIdentifier) { app in
                    Button(app.localizedName ?? app.bundleIdentifier ?? "App") {
                        bundleID = app.bundleIdentifier
                        name = app.localizedName
                    }
                }
                Divider()
                Button("Choose from Applications…") {
                    if let picked = chooseApplication() {
                        bundleID = picked.0
                        name = picked.1
                    }
                }
            }
            .fixedSize()
        }
    }
}
