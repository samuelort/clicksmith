import Foundation

/// Lifecycle and error logging only. Never logs button presses or keystrokes.
enum EngineLog {
    private static let formatter = ISO8601DateFormatter()
    private static let queue = DispatchQueue(label: "app.clicksmith.engine.log", qos: .utility)

    static func write(_ message: String) {
        let line = "\(formatter.string(from: Date())) \(message)\n"
        queue.async { fputs(line, stderr) }
    }
}
