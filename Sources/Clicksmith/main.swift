import AppKit

// Plain AppKit lifecycle (since 1.0.1): AppKit owns the status item and the window;
// SwiftUI only draws the window content.
let application = NSApplication.shared
let appDelegate = AppDelegate()
application.delegate = appDelegate
application.run()
