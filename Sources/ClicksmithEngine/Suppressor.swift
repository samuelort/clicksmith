import CoreGraphics
import Foundation

/// Blocks the original action of standard buttons (Back, Forward, Middle…) when a mapping
/// asks for it. The tap only exists while such a mapping is active. If the engine exits or
/// crashes, the tap disappears and the mouse behaves normally again.
/// Limitation: CGEvent mouse events carry no device identity, so blocking applies to that
/// button number on every connected mouse.
final class Suppressor {
    fileprivate var suppressed = Set<Int64>()
    private var tap: CFMachPort?
    private var source: CFRunLoopSource?
    private var includesPrimary = false

    var isActive: Bool { tap != nil }

    func update(_ buttons: Set<Int>) {
        suppressed = Set(buttons.map { Int64($0) })
        guard !buttons.isEmpty else { remove(); return }
        let needPrimary = buttons.contains(0) || buttons.contains(1)
        if tap == nil || needPrimary != includesPrimary {
            remove()
            install(primary: needPrimary)
        }
    }

    private func install(primary: Bool) {
        var mask: CGEventMask = 0
        func add(_ t: CGEventType) { mask |= CGEventMask(1) << CGEventMask(t.rawValue) }
        add(.otherMouseDown); add(.otherMouseUp); add(.otherMouseDragged)
        if primary {
            add(.leftMouseDown); add(.leftMouseUp); add(.leftMouseDragged)
            add(.rightMouseDown); add(.rightMouseUp); add(.rightMouseDragged)
        }
        guard let t = CGEvent.tapCreate(tap: .cgSessionEventTap, place: .headInsertEventTap, options: .defaultTap,
                                        eventsOfInterest: mask, callback: suppressorCallback,
                                        userInfo: Unmanaged.passUnretained(self).toOpaque()) else {
            EngineLog.write("WARNING: could not create event tap; original actions not blocked")
            return
        }
        let src = CFMachPortCreateRunLoopSource(kCFAllocatorDefault, t, 0)
        CFRunLoopAddSource(CFRunLoopGetMain(), src, .commonModes)
        CGEvent.tapEnable(tap: t, enable: true)
        tap = t
        source = src
        includesPrimary = primary
        EngineLog.write("Suppression tap installed\(primary ? " (including primary buttons)" : "")")
    }

    func remove() {
        if let t = tap {
            CGEvent.tapEnable(tap: t, enable: false)
            CFMachPortInvalidate(t)
        }
        if let s = source { CFRunLoopRemoveSource(CFRunLoopGetMain(), s, .commonModes) }
        if tap != nil { EngineLog.write("Suppression tap removed") }
        tap = nil
        source = nil
    }

    fileprivate func reenable() {
        if let t = tap { CGEvent.tapEnable(tap: t, enable: true) }
    }
}

private let suppressorCallback: CGEventTapCallBack = { _, type, event, userInfo in
    guard let userInfo else { return Unmanaged.passUnretained(event) }
    let s = Unmanaged<Suppressor>.fromOpaque(userInfo).takeUnretainedValue()
    if type == .tapDisabledByTimeout || type == .tapDisabledByUserInput {
        s.reenable()
        return Unmanaged.passUnretained(event)
    }
    let button = event.getIntegerValueField(.mouseEventButtonNumber)
    return s.suppressed.contains(button) ? nil : Unmanaged.passUnretained(event)
}
