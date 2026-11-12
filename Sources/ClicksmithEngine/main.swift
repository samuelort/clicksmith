import AppKit
import ClicksmithCore

// Clicksmith Engine — background input engine, started by launchd (KeepAlive).
// Architecture inherited from the proven rival3-dpi v3.5:
//   IOHID callback → edge detection → in-memory lookup → event
//   wake → clean exit → launchd starts a fresh process → fresh IOHIDManager + explicit enumeration

if CommandLine.arguments.contains("--version") {
    print("Clicksmith Engine \(ClicksmithInfo.version)")
    exit(0)
}

umask(0o077)
signal(SIGPIPE, SIG_IGN)
ClicksmithPaths.ensureDirectory()
// launchd creates the log file with default permissions; keep it private.
try? FileManager.default.setAttributes([.posixPermissions: 0o600], ofItemAtPath: ClicksmithPaths.logFile.path)

// Single instance: a second copy (e.g. started by hand) exits immediately.
let lockFD = open(ClicksmithPaths.lock.path, O_CREAT | O_RDWR | O_CLOEXEC, 0o600)
if lockFD >= 0 && flock(lockFD, LOCK_EX | LOCK_NB) != 0 {
    EngineLog.write("Another engine instance is running; exiting")
    exit(0)
}

_ = pthread_set_qos_class_self_np(QOS_CLASS_USER_INTERACTIVE, 0)

let application = NSApplication.shared
application.setActivationPolicy(.accessory)

let engine = Engine()
engine.start()
application.run()
