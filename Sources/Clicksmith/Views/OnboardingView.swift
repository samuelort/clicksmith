import SwiftUI
import AppKit
import ClicksmithCore

/// First-run flow, shown inside the main window.
struct OnboardingView: View {
    @ObservedObject private var state = AppState.shared
    @Local private var step = 0
    @Local private var launchAtLogin = true
    @Local private var legacyInstalled = LegacyMigration.isLegacyInstalled
    @Local private var importLegacy = true
    @Local private var working = false
    @Local private var message: String? = nil

    var body: some View {
        VStack(spacing: 0) {
            Group {
                switch step {
                case 0: welcome
                case 1: engineStep
                default: permissionsStep
                }
            }
            .frame(maxWidth: 560, maxHeight: .infinity)
            .padding(30)

            Divider()
            HStack {
                ForEach(0..<3, id: \.self) { i in
                    Circle().fill(i == step ? Color.ember : Color.secondary.opacity(0.3)).frame(width: 7, height: 7)
                }
                Spacer()
                if step > 0 { Button("Back") { step -= 1 }.disabled(working) }
                if step == 1 && message != nil {
                    Button("Skip for now") { message = nil; step = 2 }
                }
                Button(step == 2 ? "Start Forging" : "Continue") { next() }
                    .buttonStyle(.borderedProminent)
                    .keyboardShortcut(.defaultAction)
                    .disabled(working)
            }
            .padding(16)
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .background(Palette.canvas)
    }

    private var welcome: some View {
        VStack(spacing: 14) {
            Image(nsImage: AppIcons.logo).resizable().frame(width: 120, height: 120)
                .shadow(color: .black.opacity(0.10), radius: 14, y: 6)
            Text("Welcome to Clicksmith").font(.largeTitle.weight(.semibold))
            Text("Forge every mouse button into something useful — globally, or differently in each app.")
                .multilineTextAlignment(.center).foregroundStyle(.secondary)
            Text("Everything stays on this Mac. No telemetry, no network, no keystroke logging.")
                .font(.callout)
        }
    }

    private var engineStep: some View {
        VStack(alignment: .leading, spacing: 16) {
            Text("The engine").font(.title.weight(.semibold))
            Text("Clicksmith Engine is a tiny background process that listens to your mouse and sends your actions. It restarts itself cleanly after sleep, so buttons keep working without you doing anything.")
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
            Toggle("Start Clicksmith Engine at login", isOn: $launchAtLogin)
            if legacyInstalled {
                Toggle("Import my rival3-dpi setup (DPI → ⇧⌘3) and switch rival3-dpi off", isOn: $importLegacy)
                Text("Recommended — otherwise the DPI button fires twice. rival3-dpi is backed up and can be restored in Settings.")
                    .font(.caption).foregroundStyle(.secondary)
            }
            if working { ProgressView("Starting engine…").controlSize(.small) }
            if let message { Banner(kind: .error, text: message) }
        }
    }

    private var permissionsStep: some View {
        VStack(alignment: .leading, spacing: 14) {
            Text("Two permissions").font(.title.weight(.semibold))
            Text("macOS may already have asked. If not, allow “Clicksmith Engine” in both lists:")
                .foregroundStyle(.secondary)
            permission("Input Monitoring", "To see which mouse button you press.",
                       state.status?.inputMonitoring == "granted", SystemSettingsLink.inputMonitoring)
            permission("Accessibility", "To send the action you assign.",
                       state.status?.accessibility == true, SystemSettingsLink.accessibility)
            HStack {
                Button("Show Engine in Finder") { NSWorkspace.shared.activateFileViewerSelecting([state.engine.engineAppURL]) }
                Button("Restart Engine") { state.engine.perform({ state.engine.restart() }) { message = $0 } }
            }
            Text("You can finish now and fix permissions later under Settings › Permissions.")
                .font(.caption).foregroundStyle(.secondary)
        }
    }

    private func permission(_ title: String, _ detail: String, _ granted: Bool, _ url: URL) -> some View {
        HStack {
            Image(systemName: granted ? "checkmark.circle.fill" : "circle.dashed")
                .foregroundStyle(granted ? Palette.sage : Palette.amber).font(.title2)
            VStack(alignment: .leading) {
                Text(title).font(.headline)
                Text(detail).font(.caption).foregroundStyle(.secondary)
            }
            Spacer()
            Button("Open") { NSWorkspace.shared.open(url) }
        }
        .padding(10)
        .background(RoundedRectangle(cornerRadius: 12, style: .continuous).fill(Palette.card))
    }

    private func next() {
        switch step {
        case 1:
            working = true
            message = nil
            let atLogin = launchAtLogin
            let migrate = importLegacy && legacyInstalled
            let engine = state.engine
            state.update {
                $0.settings.launchEngineAtLogin = atLogin
                $0.settings.engineEnabled = true
            }
            AppLog.step("Onboarding: installing engine (migrate rival3-dpi: \(migrate))")
            engine.perform({
                if migrate {
                    let mapping = LegacyMigration.migrate(using: engine)
                    DispatchQueue.main.async {
                        if AppState.shared.mapping(for: mapping.device, button: mapping.button) == nil {
                            AppState.shared.upsert(mapping)
                        }
                    }
                }
                return engine.install(atLogin: atLogin)
            }) { error in
                working = false
                message = error
                AppState.shared.refreshStatus()
                AppLog.step("Onboarding: engine install \(error == nil ? "OK" : "failed: \(error!)")")
                if error == nil { step = 2 }
            }
        case 2:
            state.update { $0.settings.onboardingCompleted = true }
            AppLog.step("Onboarding completed")
        default:
            step += 1
        }
    }
}
