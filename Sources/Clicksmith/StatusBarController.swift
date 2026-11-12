import AppKit
import Combine
import ClicksmithCore

/// Native NSStatusItem + NSMenu. The menu is rebuilt only when it opens; the icon changes
/// only when the engine state changes.
final class StatusBarController: NSObject, NSMenuDelegate {
    private let item: NSStatusItem
    private let state: AppState
    private var cancellables = Set<AnyCancellable>()
    private var lastVisual: EngineVisualState?
    private var lastVisible: Bool?

    init(state: AppState) {
        self.state = state
        item = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
        super.init()
        let menu = NSMenu()
        menu.delegate = self
        menu.autoenablesItems = false
        item.menu = menu
        item.button?.toolTip = "Clicksmith"
        updateIcon()
        state.objectWillChange
            .debounce(for: .milliseconds(150), scheduler: RunLoop.main)
            .sink { [weak self] _ in self?.updateIcon() }
            .store(in: &cancellables)
    }

    private func updateIcon() {
        let visible = state.config.settings.showMenuBarIcon
        if visible != lastVisible {
            item.isVisible = visible
            lastVisible = visible
        }
        let visual = state.healthVisual
        guard visual != lastVisual else { return }
        lastVisual = visual
        item.button?.image = AppIcons.menuBar(visual)
        item.button?.setAccessibilityLabel("Clicksmith: \(state.healthTitle)")
    }

    func menuNeedsUpdate(_ menu: NSMenu) {
        state.refreshStatus()
        menu.removeAllItems()

        menu.addItem(disabled("Clicksmith — \(state.healthTitle)"))
        let mice = state.connectedMice.map(\.name).joined(separator: ", ")
        menu.addItem(disabled("Mouse: \(mice.isEmpty ? "none connected" : mice)"))
        menu.addItem(disabled("Buttons: \(state.config.mappings.filter(\.enabled).count) active"))
        menu.addItem(.separator())

        menu.addItem(action(state.config.settings.paused ? "Resume Mappings" : "Pause Mappings", #selector(togglePause)))
        if state.status?.emergencyPaused == true {
            menu.addItem(action("Resume After Emergency Pause", #selector(resumeEmergency)))
        }
        menu.addItem(.separator())
        menu.addItem(action("Open Clicksmith…", #selector(openOverview), key: "o"))
        menu.addItem(action("Buttons…", #selector(openButtons)))
        menu.addItem(action("Apps…", #selector(openApps)))
        menu.addItem(action("Settings…", #selector(openSettings), key: ","))
        menu.addItem(.separator())
        menu.addItem(disabled("The engine keeps running when you quit."))
        menu.addItem(action("Quit Clicksmith", #selector(quit), key: "q"))
    }

    private func disabled(_ title: String) -> NSMenuItem {
        let i = NSMenuItem(title: title, action: nil, keyEquivalent: "")
        i.isEnabled = false
        return i
    }

    private func action(_ title: String, _ selector: Selector, key: String = "") -> NSMenuItem {
        let i = NSMenuItem(title: title, action: selector, keyEquivalent: key)
        i.target = self
        return i
    }

    @objc private func togglePause() { state.setPaused(!state.config.settings.paused) }
    @objc private func resumeEmergency() { state.resumeFromEmergency() }
    @objc private func openOverview() { MainWindowController.shared.show(.overview) }
    @objc private func openButtons() { MainWindowController.shared.show(.buttons) }
    @objc private func openApps() { MainWindowController.shared.show(.apps) }
    @objc private func openSettings() { MainWindowController.shared.show(.settings) }
    @objc private func quit() { NSApp.terminate(nil) }
}
