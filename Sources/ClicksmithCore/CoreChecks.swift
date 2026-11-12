import Foundation

/// Core behaviour checks shared by the XCTest suite and the XCTest-free self-test runner.
public enum CoreChecks {
    public static func all() -> [(String, Bool)] {
        var r: [(String, Bool)] = []
        func check(_ name: String, _ ok: @autoclosure () -> Bool) { r.append((name, ok())) }

        // --- Edge detection (same behaviour as rival3-dpi) ---
        var d = EdgeDetector(button: KnownDevices.rivalDPI)
        check("DPI press fires", d.process(reportID: 1, bytes: [0x01, 0x20, 0, 0, 0, 0, 0]))
        check("held + movement does not refire", !d.process(reportID: 1, bytes: [0x01, 0x20, 0x01, 0, 0xFF, 0xFF, 0]))
        check("release does not fire", !d.process(reportID: 1, bytes: [0x01, 0x00, 0, 0, 0, 0, 0]))
        check("second press fires", d.process(reportID: 1, bytes: [0x01, 0x20, 0, 0, 0, 0, 0]))
        _ = d.process(reportID: 1, bytes: [0x01, 0, 0, 0, 0, 0, 0])
        check("DPI together with left click fires", d.process(reportID: 1, bytes: [0x01, 0x21, 0, 0, 0, 0, 0]))
        _ = d.process(reportID: 1, bytes: [0x01, 0, 0, 0, 0, 0, 0])
        check("left click alone ignored", !d.process(reportID: 1, bytes: [0x01, 0x01, 0, 0, 0, 0, 0]))
        check("wrong report ID ignored", !d.process(reportID: 2, bytes: [0x02, 0x20, 0, 0, 0, 0, 0]))
        check("short report ignored", !d.process(reportID: 1, bytes: [0x01]))
        check("report ID 0 with prefixed ID byte matches", ReportMatcher.matches(button: KnownDevices.rivalDPI, reportID: 0, bytes: [0x01, 0x20]))

        // --- Duplicate guard ---
        let g = PressGuard(intervalMs: 250)
        let b = KnownDevices.rivalDPI
        check("guard allows first press", g.allow(b, now: 5_000_000))
        check("guard blocks within 250 ms", !g.allow(b, now: 105_000_000))
        check("guard allows after 250 ms", g.allow(b, now: 300_000_000))
        check("guard is per button", g.allow(ButtonID(reportID: 1, byteIndex: 1, mask: 0x08), now: 301_000_000))

        // --- Mapping priority ---
        let teams = "com.microsoft.teams2"
        let mute = KeyShortcut(keyCode: 46, command: true, shift: true, keyName: "M")
        let back = ButtonID(reportID: 1, byteIndex: 1, mask: 0x08)
        let mapping = ButtonMapping(device: KnownDevices.rival, deviceName: "Rival", button: back, label: "Back",
                                    systemButtonNumber: 3,
                                    defaultAction: ButtonAction.sending(.screenshot),
                                    overrides: [AppOverride(bundleID: teams, appName: "Teams",
                                                            action: ButtonAction.sending(mute, blockOriginal: true))])
        check("exact bundle override wins", mapping.action(forBundleID: teams)?.shortcut == mute)
        check("other app falls back to default", mapping.action(forBundleID: "com.apple.Safari")?.shortcut == .screenshot)
        check("no frontmost app uses default", mapping.action(forBundleID: nil)?.shortcut == .screenshot)
        var noDefault = mapping
        noDefault.defaultAction = nil
        check("no default → no action outside app", noDefault.action(forBundleID: "com.apple.Safari") == nil)
        var disabledOverride = mapping
        disabledOverride.overrides[0].enabled = false
        check("disabled override ignored", disabledOverride.action(forBundleID: teams)?.shortcut == .screenshot)

        let compiled = CompiledConfig(ClicksmithConfig(mappings: [mapping]))
        let rivalWithSerial = DeviceKey(vendorID: 0x0111, productID: 0x1872, serial: "ABC")
        check("watched list for matching device", compiled.watched(for: rivalWithSerial, bundleID: nil).count == 1)
        check("no watch for other device", compiled.watched(for: DeviceKey(vendorID: 1, productID: 2), bundleID: nil).isEmpty)
        check("suppress Back only in Teams", compiled.suppressedSystemButtons(bundleID: teams) == [3])
        check("no suppression outside Teams", compiled.suppressedSystemButtons(bundleID: "com.apple.Safari").isEmpty)
        check("profile name for Teams", compiled.profileName(bundleID: teams, appName: "Microsoft Teams") == "Microsoft Teams")
        check("profile name default", compiled.profileName(bundleID: "x.y", appName: "Other") == "Default")

        var leftMapping = mapping
        leftMapping.systemButtonNumber = 0
        leftMapping.defaultAction = ButtonAction(kind: .nothing, blockOriginal: true)
        check("left click never suppressed by default", CompiledConfig(ClicksmithConfig(mappings: [leftMapping])).suppressedSystemButtons(bundleID: nil).isEmpty)
        var allowSettings = EngineSettings()
        allowSettings.allowPrimaryButtonSuppression = true
        check("left click suppressed only when allowed",
              CompiledConfig(ClicksmithConfig(settings: allowSettings, mappings: [leftMapping])).suppressedSystemButtons(bundleID: nil) == [0])

        // --- Device identity ---
        check("model mapping matches any serial", KnownDevices.rival.matches(rivalWithSerial))
        check("serial mapping rejects other serial", !rivalWithSerial.matches(DeviceKey(vendorID: 0x0111, productID: 0x1872, serial: "XYZ")))

        // --- Button learning ---
        var tracker = VolatilityTracker()
        var learner = ButtonLearner()
        func feed(_ prev: [UInt8], _ cur: [UInt8]) -> [ButtonID] {
            for i in 1..<cur.count where prev[i] != cur[i] { tracker.observe(reportID: 1, byte: i, old: prev[i], new: cur[i]) }
            return learner.observe(reportID: 1, previous: prev, current: cur, tracker: tracker)
        }
        var last: [UInt8] = [1, 0, 0, 0, 0, 0, 0]
        for v: UInt8 in [3, 250, 7, 1, 254, 12, 9, 2, 255, 5, 4, 8, 6, 11] {
            let next: [UInt8] = [1, 0, v, v > 127 ? 0xFF : 0, 0, 0, 0]
            _ = feed(last, next)
            last = next
        }
        check("movement bytes become volatile", tracker.isVolatile(reportID: 1, byte: 2) && tracker.isVolatile(reportID: 1, byte: 3))
        check("button byte stays stable", !tracker.isVolatile(reportID: 1, byte: 1))
        let base: [UInt8] = [1, 0, 0, 0, 0, 0, 0]
        _ = feed(last, base)
        check("press alone does not confirm", feed(base, [1, 0x20, 0, 0, 0, 0, 0]).isEmpty)
        check("press + release learns DPI", feed([1, 0x20, 0, 0, 0, 0, 0], base) == [KnownDevices.rivalDPI])
        check("movement jitter not learned", feed(base, [1, 0, 1, 0, 0, 0, 0]).isEmpty && feed([1, 0, 1, 0, 0, 0, 0], base).isEmpty)
        check("DPI suggested label", SystemButton.suggestedLabel(for: KnownDevices.rivalDPI, device: KnownDevices.rival) == "DPI Button")
        check("Back suggested label", SystemButton.suggestedLabel(for: back, device: DeviceKey(vendorID: 1, productID: 1)) == "Back")

        // --- Configuration ---
        let config = ClicksmithConfig(mappings: [mapping])
        if let data = try? ConfigCodec.encoder().encode(config), let decoded = try? ConfigCodec.decode(data) {
            check("config round trip", decoded == config)
        } else {
            check("config round trip", false)
        }
        check("reject non-JSON", (try? ConfigCodec.decode(Data("hello".utf8))) == nil)
        check("reject future schema", (try? ConfigCodec.decode(Data(#"{"schemaVersion":99,"settings":{},"mappings":[]}"#.utf8))) == nil)
        check("reject missing schema", (try? ConfigCodec.decode(Data(#"{"settings":{},"mappings":[]}"#.utf8))) == nil)
        check("old settings get defaults", (try? ConfigCodec.decode(Data(#"{"schemaVersion":1,"settings":{"paused":true},"mappings":[]}"#.utf8)))?.settings.duplicateGuardMs == 250)
        var bad = mapping
        bad.button.mask = 0x30
        check("reject multi-bit mask", (try? ConfigCodec.validate(ClicksmithConfig(mappings: [bad]))) == nil)
        var badBundle = mapping
        badBundle.overrides[0].bundleID = "evil; rm -rf ~"
        check("reject bad bundle ID", (try? ConfigCodec.validate(ClicksmithConfig(mappings: [badBundle]))) == nil)
        var missingShortcut = mapping
        missingShortcut.defaultAction = ButtonAction.sending(nil)
        check("reject missing shortcut", (try? ConfigCodec.validate(ClicksmithConfig(mappings: [missingShortcut]))) == nil)

        // --- Shortcuts ---
        check("screenshot display", KeyShortcut.screenshot.display == "⇧⌘3")
        check("⌘Q warns", KeyNames.warning(for: KeyShortcut(keyCode: 12, command: true)) != nil)
        check("⇧⌘3 has no warning", KeyNames.warning(for: .screenshot) == nil)

        // --- Schema 1 → 2 migration ---
        let v1 = #"""
        {"schemaVersion":1,"settings":{"onboardingCompleted":true},"mappings":[
         {"id":"6F1C2C61-5D7A-4F5B-9F64-2B3E1C0A0001","device":{"vendorID":273,"productID":6258},"deviceName":"Rival",
          "button":{"reportID":1,"byteIndex":1,"mask":32},"label":"DPI","enabled":true,
          "defaultAction":{"mode":"shortcutKeepOriginal","shortcut":{"keyCode":20,"command":true,"shift":true,"option":false,"control":false,"function":false,"keyName":"3"}},
          "overrides":[{"id":"6F1C2C61-5D7A-4F5B-9F64-2B3E1C0A0002","bundleID":"com.microsoft.teams2","appName":"Teams","enabled":true,
            "action":{"mode":"disableButton"}}]}]}
        """#
        let migrated = try? ConfigCodec.decode(Data(v1.utf8))
        check("v1 config migrates to schema 2", migrated?.schemaVersion == 2)
        check("v1 keep-original → shortcut, not blocking",
              migrated?.mappings.first?.defaultAction == ButtonAction.sending(.screenshot))
        check("v1 disableButton → none + block",
              migrated?.mappings.first?.overrides.first?.action == ButtonAction(kind: .nothing, blockOriginal: true))
        if let m = migrated, let data = try? ConfigCodec.encoder().encode(m), let text = String(data: data, encoding: .utf8) {
            check("saved config uses new format", text.contains("\"kind\"") && !text.contains("\"mode\""))
        } else {
            check("saved config uses new format", false)
        }

        // --- Action kinds ---
        check("media key valid", ButtonAction(kind: .mediaKey, mediaKey: .playPause).problem == nil)
        check("media key required", ButtonAction(kind: .mediaKey).problem != nil)
        check("open app valid", ButtonAction(kind: .openApp, appBundleID: "com.apple.Safari", appName: "Safari").problem == nil)
        check("open app rejects bad bundle", ButtonAction(kind: .openApp, appBundleID: "../../evil").problem != nil)
        check("type text valid", ButtonAction(kind: .typeText, text: "Hej!\nMed vänlig hälsning").problem == nil)
        check("type text rejects empty", ButtonAction(kind: .typeText, text: "").problem != nil)
        check("type text rejects too long", ButtonAction(kind: .typeText, text: String(repeating: "a", count: 501)).problem != nil)
        check("type text rejects control chars", ButtonAction(kind: .typeText, text: "a\u{1B}[2J").problem != nil)
        check("next track is NX_KEYTYPE_FAST", MediaKey.next.nxKeyType == 19)
        let mediaMapping = ButtonMapping(device: KnownDevices.rival, deviceName: "Rival", button: back, label: "Play",
                                         defaultAction: ButtonAction(kind: .mediaKey, mediaKey: .playPause))
        check("media action is watched", CompiledConfig(ClicksmithConfig(mappings: [mediaMapping])).watched(for: KnownDevices.rival, bundleID: nil).count == 1)
        var disableOnly = mapping
        disableOnly.defaultAction = ButtonAction(kind: .nothing, blockOriginal: true)
        disableOnly.overrides = []
        check("disable-only is not watched", CompiledConfig(ClicksmithConfig(mappings: [disableOnly])).watched(for: KnownDevices.rival, bundleID: nil).isEmpty)
        check("action round trip", (try? ConfigCodec.decoder().decode(ButtonAction.self,
              from: ConfigCodec.encoder().encode(ButtonAction(kind: .typeText, blockOriginal: true, text: "x")))) == ButtonAction(kind: .typeText, blockOriginal: true, text: "x"))

        // --- Device listening policy ---
        check("external mouse always listened to", AttachPolicy.shouldListen(builtIn: false, hasWatchedButtons: false, learningTargetsDevice: false))
        check("idle trackpad not listened to", !AttachPolicy.shouldListen(builtIn: true, hasWatchedButtons: false, learningTargetsDevice: false))
        check("mapped trackpad listened to", AttachPolicy.shouldListen(builtIn: true, hasWatchedButtons: true, learningTargetsDevice: false))
        check("trackpad listened to while learning", AttachPolicy.shouldListen(builtIn: true, hasWatchedButtons: false, learningTargetsDevice: true))
        check("SPI transport counts as built-in", AttachPolicy.looksBuiltIn(builtInProperty: false, transport: "SPI"))
        check("Bluetooth mouse not built-in", !AttachPolicy.looksBuiltIn(builtInProperty: false, transport: "Bluetooth Low Energy"))

        // --- File trust ---
        let dir = FileManager.default.temporaryDirectory.appendingPathComponent("clicksmith-trust-\(UUID().uuidString)")
        try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        let good = dir.appendingPathComponent("good.json")
        try? Data("{}".utf8).write(to: good)
        try? FileManager.default.setAttributes([.posixPermissions: 0o600], ofItemAtPath: good.path)
        check("private file trusted", FileTrust.isTrusted(good))
        let loose = dir.appendingPathComponent("open.json")
        try? Data("{}".utf8).write(to: loose)
        try? FileManager.default.setAttributes([.posixPermissions: 0o666], ofItemAtPath: loose.path)
        check("world-writable file rejected", !FileTrust.isTrusted(loose))
        let link = dir.appendingPathComponent("link.json")
        try? FileManager.default.createSymbolicLink(at: link, withDestinationURL: good)
        check("symlink rejected", !FileTrust.isTrusted(link))
        check("missing file rejected", !FileTrust.isTrusted(dir.appendingPathComponent("nope.json")))
        try? FileManager.default.removeItem(at: dir)

        check("fresh resume request accepted", ResumeRequest().isFresh)

        // --- 1.2 ---
        check("own bundle IDs recognised", ClicksmithInfo.isOwnBundle("app.clicksmith.Clicksmith") && ClicksmithInfo.isOwnBundle("app.clicksmith.engine"))
        check("other apps are not own bundles", !ClicksmithInfo.isOwnBundle("com.microsoft.teams2") && !ClicksmithInfo.isOwnBundle(nil))
        var escape = EngineSettings()
        escape.emergencyHotKeyEnabled = false
        check("emergency shortcut can be off normally", !escape.emergencyHotKeyEffective)
        escape.allowPrimaryButtonSuppression = true
        check("emergency shortcut forced on with click blocking", escape.emergencyHotKeyEffective)

        // --- 1.2.1: skip work on irrelevant app switches ---
        check("switch between two default apps changes nothing", !compiled.profileDiffers("com.apple.Safari", "com.apple.Finder"))
        check("switch into Teams changes the profile", compiled.profileDiffers("com.apple.Safari", teams))
        check("switch out of Teams changes the profile", compiled.profileDiffers(teams, nil))

        return r
    }
}
