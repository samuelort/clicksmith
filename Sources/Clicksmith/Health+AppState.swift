import SwiftUI
import ClicksmithCore

/// UI wording for the Core health model (2.0).
extension AppState {
    var inputMonitoringGranted: Bool { status?.inputMonitoring == "granted" }
    var accessibilityGranted: Bool { status?.accessibility == true }

    var missingPermissions: Int {
        guard engineRunning else { return 0 }
        return (inputMonitoringGranted ? 0 : 1) + (accessibilityGranted ? 0 : 1)
    }

    var health: HealthState {
        HealthModel.evaluate(engineRunning: engineRunning,
                             inputMonitoringGranted: inputMonitoringGranted,
                             accessibilityGranted: accessibilityGranted,
                             emergencyPaused: status?.emergencyPaused == true,
                             paused: config.settings.paused,
                             connectedMice: connectedMice.count)
    }

    var healthVisual: EngineVisualState {
        switch health {
        case .ready: return .active
        case .paused, .needsSetup, .noMouse: return .paused
        case .stopped: return .stopped
        }
    }

    var healthTitle: String {
        switch health {
        case .ready: return "Forging clicks"
        case .paused(let emergency): return emergency ? "Emergency pause" : "Paused"
        case .needsSetup: return "Setup needed"
        case .noMouse: return "No mouse"
        case .stopped: return config.settings.engineEnabled ? "Engine stopped" : "Engine off"
        }
    }

    var healthDetail: String {
        switch health {
        case .ready:
            let n = config.mappings.filter(\.enabled).count
            return n == 1 ? "1 button active" : "\(n) buttons active"
        case .paused(let emergency): return emergency ? "Press Resume to continue" : "Mappings are paused"
        case .needsSetup(let missing): return missing == 1 ? "1 permission missing" : "\(missing) permissions missing"
        case .noMouse: return "Connect a mouse to start"
        case .stopped: return "Start the engine to use your buttons"
        }
    }
}
