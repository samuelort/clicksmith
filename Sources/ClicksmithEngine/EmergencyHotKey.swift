import Carbon.HIToolbox
import Foundation

/// ⌃⌥⌘⎋ pauses every mapping and removes the suppression tap.
/// Uses a registered hot key, so the engine never sees any other keystrokes.
final class EmergencyHotKey {
    private var ref: EventHotKeyRef?
    private var handler: EventHandlerRef?
    var onPress: (() -> Void)?

    func register() {
        guard ref == nil else { return }
        if handler == nil {
            var spec = EventTypeSpec(eventClass: OSType(kEventClassKeyboard), eventKind: UInt32(kEventHotKeyPressed))
            InstallEventHandler(GetApplicationEventTarget(), { _, _, userData in
                guard let userData else { return OSStatus(noErr) }
                let me = Unmanaged<EmergencyHotKey>.fromOpaque(userData).takeUnretainedValue()
                DispatchQueue.main.async { me.onPress?() }
                return OSStatus(noErr)
            }, 1, &spec, Unmanaged.passUnretained(self).toOpaque(), &handler)
        }
        let id = EventHotKeyID(signature: OSType(0x434C_4B53), id: 1) // "CLKS"
        let status = RegisterEventHotKey(UInt32(kVK_Escape), UInt32(cmdKey | optionKey | controlKey), id,
                                         GetApplicationEventTarget(), 0, &ref)
        if status != OSStatus(noErr) { EngineLog.write("WARNING: emergency shortcut unavailable (\(status))") }
    }

    func unregister() {
        if let r = ref { UnregisterEventHotKey(r) }
        ref = nil
    }
}
