import AppKit
import ClicksmithCore

final class AppDelegate: NSObject, NSApplicationDelegate {
    private var statusBar: StatusBarController?

    func applicationDidFinishLaunching(_ notification: Notification) {
        AppLog.rotateIfNeeded()
        AppLog.step("Launch Clicksmith \(ClicksmithInfo.version)")
        MainThreadWatchdog.shared.start()

        let state = AppState.shared
        AppLog.step("Configuration loaded (\(state.config.mappings.count) mappings, onboarding \(state.config.settings.onboardingCompleted ? "done" : "pending"))")

        NSApp.setActivationPolicy(state.config.settings.showDockIcon ? .regular : .accessory)
        statusBar = StatusBarController(state: state)
        AppLog.step("Menu bar item created")

        // Start (or repoint) the engine off the main thread. Before onboarding the user decides
        // first (and can migrate from rival3-dpi so the DPI button never fires twice).
        let settings = state.config.settings
        if settings.onboardingCompleted {
            state.engine.perform({ state.engine.ensureRunning(settings: settings) }) { message in
                state.engineMessage = message
                state.refreshStatus()
                AppLog.step("Engine check finished\(message.map { ": \($0)" } ?? "")")
            }
        }

        // Hold ⌥ while launching (or pass --safe) to start without opening the window.
        let safeMode = NSEvent.modifierFlags.contains(.option) || CommandLine.arguments.contains("--safe")
        if safeMode {
            AppLog.step("Safe mode: window not opened automatically")
        } else if !settings.onboardingCompleted || !settings.showMenuBarIcon {
            MainWindowController.shared.show()
        }
        AppLog.step("Startup finished")
        DispatchQueue.main.asyncAfter(deadline: .now() + 30) {
            if !MainWindowController.shared.isVisible { MainThreadWatchdog.shared.stop() }
        }
    }

    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool {
        MainWindowController.shared.show()
        return false
    }

    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { false }
}
