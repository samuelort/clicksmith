import Foundation

/// Startup milestones and errors for the settings app: ~/Library/Logs/Clicksmith-app.log.
/// Never logs input.
enum AppLog {
    private static let queue = DispatchQueue(label: "app.clicksmith.applog", qos: .utility)
    private static let formatter = ISO8601DateFormatter()
    private static let lock = NSLock()
    private static var _lastStep = "not started"

    static var url: URL {
        FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent("Library/Logs/Clicksmith-app.log")
    }

    static var lastStep: String {
        lock.lock(); defer { lock.unlock() }
        return _lastStep
    }

    static func step(_ message: String) {
        lock.lock(); _lastStep = message; lock.unlock()
        write(message)
    }

    static func write(_ message: String) {
        let line = "\(formatter.string(from: Date())) \(message)\n"
        let target = url
        queue.async {
            let data = Data(line.utf8)
            if let handle = try? FileHandle(forWritingTo: target) {
                handle.seekToEndOfFile()
                handle.write(data)
                try? handle.close()
            } else {
                try? data.write(to: target)
                try? FileManager.default.setAttributes([.posixPermissions: 0o600], ofItemAtPath: target.path)
            }
        }
    }

    /// Keeps both Clicksmith logs below 1 MB.
    static func rotateIfNeeded() {
        let engineLog = FileManager.default.homeDirectoryForCurrentUser
            .appendingPathComponent("Library/Logs/Clicksmith-engine.log")
        for file in [url, engineLog] {
            let size = (try? FileManager.default.attributesOfItem(atPath: file.path)[.size] as? Int) ?? 0
            if size > 1_048_576 {
                let old = file.appendingPathExtension("old")
                try? FileManager.default.removeItem(at: old)
                try? FileManager.default.moveItem(at: file, to: old)
            }
        }
    }
}

/// Logs when the main thread stops responding for more than 4 seconds, together with the
/// last startup step. Runs during startup and while the window is open only.
final class MainThreadWatchdog {
    static let shared = MainThreadWatchdog()
    private let lock = NSLock()
    private var lastBeat = Date()
    private var reported = false
    private var timer: DispatchSourceTimer?

    func start() {
        guard timer == nil else { return }
        lock.lock(); lastBeat = Date(); reported = false; lock.unlock()
        let t = DispatchSource.makeTimerSource(queue: DispatchQueue.global(qos: .utility))
        t.schedule(deadline: .now() + 2, repeating: 1, leeway: .milliseconds(250))
        t.setEventHandler { [weak self] in self?.tick() }
        t.resume()
        timer = t
    }

    func stop() {
        timer?.cancel()
        timer = nil
    }

    private func tick() {
        DispatchQueue.main.async { [weak self] in self?.beat() }
        lock.lock()
        let age = Date().timeIntervalSince(lastBeat)
        let shouldReport = age > 4 && !reported
        if shouldReport { reported = true }
        lock.unlock()
        if shouldReport {
            AppLog.write("WARNING: main thread unresponsive for \(Int(age)) s — last step: \(AppLog.lastStep)")
        }
    }

    private func beat() {
        lock.lock()
        let wasReported = reported
        lastBeat = Date()
        reported = false
        lock.unlock()
        if wasReported { AppLog.write("Main thread responsive again") }
    }
}
