import AppKit
import SwiftUI
import ClicksmithCore

/// Owns the single settings window: transparent title bar with the sidebar running to
/// the top. Onboarding is shown inside the window.
final class MainWindowController: NSObject, NSWindowDelegate {
    static let shared = MainWindowController()
    private var window: NSWindow?
    var isVisible: Bool { window?.isVisible ?? false }

    func show(_ tab: SidebarItem? = nil, settings: SettingsTab? = nil) {
        if let tab { AppState.shared.selection = tab }
        if let settings {
            AppState.shared.selection = .settings
            AppState.shared.settingsTab = settings
        }
        if window == nil {
            window = makeWindow()
            AppLog.step("Window created")
        }
        MainThreadWatchdog.shared.start()
        AppState.shared.refreshStatus()
        // Regular activation while the window is open, so text fields and the
        // shortcut recorder receive keyboard focus reliably.
        NSApp.setActivationPolicy(.regular)
        window?.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)
        AppLog.step("Window shown")
    }

    private func makeWindow() -> NSWindow {
        let host = NSHostingController(rootView: RootView())
        host.sizingOptions = [.minSize]
        let w = NSWindow(contentViewController: host)
        w.title = "Clicksmith"
        w.styleMask = [.titled, .closable, .miniaturizable, .resizable, .fullSizeContentView]
        w.titlebarAppearsTransparent = true
        w.titleVisibility = .hidden
        w.setContentSize(NSSize(width: 940, height: 620))
        w.isReleasedWhenClosed = false
        w.delegate = self
        w.center()
        w.setFrameAutosaveName("ClicksmithMainWindow.v12")
        return w
    }

    func windowWillClose(_ notification: Notification) {
        MainThreadWatchdog.shared.stop()
        if !AppState.shared.config.settings.showDockIcon {
            NSApp.setActivationPolicy(.accessory)
        }
    }
}

struct RootView: View {
    @ObservedObject private var state = AppState.shared

    var body: some View {
        Group {
            if state.config.settings.onboardingCompleted {
                MainView()
            } else {
                OnboardingView()
            }
        }
        .tint(Color.ember)
        .background(Palette.canvas)
        .frame(minWidth: 860, minHeight: 560)
    }
}
