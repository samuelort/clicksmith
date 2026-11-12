import SwiftUI
import AppKit
import UniformTypeIdentifiers
import ClicksmithCore

// MARK: - Design system

/// Nordic palette taken from the app icon — warm off-whites, soft stone greys and one muted
/// copper accent. Every colour is a light/dark pair.
enum Palette {
    private static func dynamic(_ light: (CGFloat, CGFloat, CGFloat), _ dark: (CGFloat, CGFloat, CGFloat)) -> Color {
        Color(nsColor: NSColor(name: nil) { appearance in
            let isDark = appearance.bestMatch(from: [.darkAqua, .aqua]) == .darkAqua
            let c = isDark ? dark : light
            return NSColor(srgbRed: c.0 / 255, green: c.1 / 255, blue: c.2 / 255, alpha: 1)
        })
    }

    /// Muted copper — the scroll wheel and spark in the icon.
    static let copper = dynamic((160, 96, 58), (212, 152, 112))
    /// Window background: warm paper / warm charcoal.
    static let canvas = dynamic((244, 241, 236), (30, 29, 27))
    /// Cards and sheets.
    static let card = dynamic((251, 250, 247), (41, 39, 37))
    /// Sidebar: a step darker than the canvas, like the icon tile.
    static let sidebar = dynamic((235, 231, 225), (36, 34, 32))
    /// Hairlines and quiet fills.
    static let stone = dynamic((120, 112, 102), (170, 162, 152))
    /// Calm status colours.
    static let sage = dynamic((96, 132, 92), (142, 176, 137))
    static let amber = dynamic((168, 120, 44), (214, 170, 96))
    static let clay = dynamic((166, 82, 66), (214, 128, 112))
}

/// 8-pt spacing scale used on every page.
enum Space {
    static let xs: CGFloat = 4
    static let s: CGFloat = 8
    static let m: CGFloat = 12
    static let l: CGFloat = 16
    static let xl: CGFloat = 24
    static let page: CGFloat = 32
    /// Content never stretches wider than this in large windows.
    static let maxContent: CGFloat = 1040
}

extension Color {
    /// The one Clicksmith accent (kept as `ember` so every view picks up the copper).
    static let ember = Palette.copper
}

extension View {
    /// Standard page frame: 32 pt margins, capped width, centred in wide windows.
    func pageFrame() -> some View {
        frame(maxWidth: Space.maxContent, alignment: .leading)
            .padding(.horizontal, Space.page)
            .padding(.vertical, Space.xl)
            .frame(maxWidth: .infinity)
    }
}

enum AppIcons {
    static func menuBar(_ state: EngineVisualState) -> NSImage {
        let name: String
        switch state {
        case .active: name = "MenuBarActive"
        case .paused: name = "MenuBarPaused"
        case .stopped: name = "MenuBarStopped"
        }
        let image = NSImage(named: name)
            ?? NSImage(systemSymbolName: "computermouse", accessibilityDescription: "Clicksmith")
            ?? NSImage()
        image.isTemplate = true
        return image
    }

    /// Transparent rounded tile — no white matte.
    static var logo: NSImage { NSImage(named: "LogoTile") ?? NSApp.applicationIconImage }

    private static var iconCache: [String: NSImage] = [:]

    /// Cached so re-renders never repeat Launch Services lookups.
    static func appIcon(bundleID: String) -> NSImage {
        if let cached = iconCache[bundleID] { return cached }
        let icon: NSImage
        if let url = NSWorkspace.shared.urlForApplication(withBundleIdentifier: bundleID) {
            icon = NSWorkspace.shared.icon(forFile: url.path)
        } else {
            icon = NSImage(systemSymbolName: "app.dashed", accessibilityDescription: nil) ?? NSImage()
        }
        iconCache[bundleID] = icon
        return icon
    }
}

/// The app logo drawn crisply at any size.
struct LogoImage: View {
    var size: CGFloat
    var body: some View {
        Image(nsImage: AppIcons.logo)
            .resizable()
            .interpolation(.high)
            .antialiased(true)
            .frame(width: size, height: size)
            .accessibilityHidden(true)
    }
}

/// Text link in the Clicksmith accent (replaces system-blue `.link` buttons).
struct AccentLinkStyle: ButtonStyle {
    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .foregroundStyle(Color.ember)
            .opacity(configuration.isPressed ? 0.6 : 1)
            .contentShape(Rectangle())
    }
}

extension ButtonStyle where Self == AccentLinkStyle {
    static var accentLink: AccentLinkStyle { AccentLinkStyle() }
}

// MARK: - Status

struct StatusDot: View {
    let state: EngineVisualState
    var body: some View {
        Circle()
            .fill(color)
            .frame(width: 7, height: 7)
            .accessibilityHidden(true)
    }
    private var color: Color {
        switch state {
        case .active: return Palette.sage
        case .paused: return Palette.amber
        case .stopped: return Palette.clay
        }
    }
}

struct StatusPill: View {
    let state: EngineVisualState
    let text: String
    var body: some View {
        HStack(spacing: 6) {
            StatusDot(state: state)
            Text(text).font(.caption.weight(.semibold))
        }
        .padding(.horizontal, 9)
        .padding(.vertical, 4)
        .background(Capsule().fill(Palette.stone.opacity(0.12)))
        .accessibilityElement(children: .combine)
    }
}

// MARK: - Containers

/// Uniform card header: copper icon + medium secondary title, same baseline everywhere.
struct CardHeader: View {
    let title: String
    let systemImage: String
    var body: some View {
        HStack(spacing: 7) {
            Image(systemName: systemImage)
                .font(.system(size: 12, weight: .medium))
                .foregroundStyle(Color.ember)
                .frame(width: 14)
            Text(title)
                .font(.subheadline.weight(.medium))
                .foregroundStyle(.secondary)
        }
    }
}

struct Card<Content: View>: View {
    let title: String
    let systemImage: String
    @ViewBuilder var content: Content

    var body: some View {
        VStack(alignment: .leading, spacing: Space.m) {
            CardHeader(title: title, systemImage: systemImage)
            content
        }
        .padding(18)
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
        .cardBackground()
    }
}

extension View {
    func cardBackground() -> some View {
        background(
            RoundedRectangle(cornerRadius: 14, style: .continuous)
                .fill(Palette.card)
                .shadow(color: .black.opacity(0.025), radius: 5, y: 1)
        )
        .overlay(RoundedRectangle(cornerRadius: 14, style: .continuous).strokeBorder(Palette.stone.opacity(0.12)))
    }
}

struct Banner: View {
    enum Kind { case info, warning, error }
    let kind: Kind
    let text: String
    var actionTitle: String?
    var action: (() -> Void)?

    var body: some View {
        HStack(spacing: 10) {
            Image(systemName: icon).foregroundStyle(tint)
            Text(text).fixedSize(horizontal: false, vertical: true)
            Spacer(minLength: Space.s)
            if let actionTitle, let action { Button(actionTitle, action: action) }
        }
        .padding(.horizontal, 14)
        .padding(.vertical, 11)
        .background(RoundedRectangle(cornerRadius: 12, style: .continuous).fill(tint.opacity(0.09)))
        .overlay(RoundedRectangle(cornerRadius: 12, style: .continuous).strokeBorder(tint.opacity(0.18)))
    }
    private var icon: String {
        switch kind {
        case .info: return "info.circle.fill"
        case .warning: return "exclamationmark.triangle.fill"
        case .error: return "xmark.octagon.fill"
        }
    }
    private var tint: Color {
        switch kind {
        case .info: return Palette.copper
        case .warning: return Palette.amber
        case .error: return Palette.clay
        }
    }
}

struct PageHeader: View {
    let title: String
    let subtitle: String
    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            Text(title).font(.largeTitle.weight(.semibold))
            Text(subtitle).foregroundStyle(.secondary)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }
}

// MARK: - Showing what a button does

struct ShortcutBadge: View {
    let text: String
    var body: some View {
        Text(text)
            .font(.system(.callout, design: .rounded).weight(.medium))
            .lineLimit(1)
            .fixedSize()
            .padding(.horizontal, 8)
            .padding(.vertical, 3)
            .background(RoundedRectangle(cornerRadius: 6, style: .continuous).fill(Palette.stone.opacity(0.11)))
    }
}

/// App icon + what the button does in that app.
struct AppChip: View {
    let bundleID: String
    let appName: String
    let action: ButtonAction

    var body: some View {
        HStack(spacing: 5) {
            Image(nsImage: AppIcons.appIcon(bundleID: bundleID)).resizable().interpolation(.high).frame(width: 15, height: 15)
            Text(action.display)
                .font(.system(.callout, design: .rounded).weight(.medium))
                .lineLimit(1)
                .fixedSize()
        }
        .padding(.leading, 5)
        .padding(.trailing, 8)
        .padding(.vertical, 3)
        .background(RoundedRectangle(cornerRadius: 6, style: .continuous).fill(Palette.stone.opacity(0.11)))
        .help("In \(appName): \(action.display)")
        .accessibilityElement(children: .combine)
        .accessibilityLabel("In \(appName): \(action.display)")
    }
}

/// The real result of a mapping: default action plus per-app actions. Never truncated.
struct MappingActionsView: View {
    let mapping: ButtonMapping

    var body: some View {
        let overrides = mapping.overrides.filter(\.enabled)
        HStack(spacing: 6) {
            if let d = mapping.defaultAction {
                ShortcutBadge(text: d.display).help("Everywhere: \(d.display)")
            }
            ForEach(Array(overrides.prefix(2))) { o in
                AppChip(bundleID: o.bundleID, appName: o.appName, action: o.action)
            }
            if overrides.count > 2 {
                Text("+\(overrides.count - 2)").font(.caption.weight(.semibold)).foregroundStyle(.secondary)
            }
            if mapping.defaultAction == nil && overrides.isEmpty {
                Text("No action").font(.callout).foregroundStyle(.secondary)
            }
        }
        .fixedSize()
        .opacity(mapping.enabled ? 1 : 0.4)
    }
}

/// Plain-language scope: where the button does something.
func scopeText(_ m: ButtonMapping) -> String {
    if !m.enabled { return "Off" }
    let apps = m.overrides.filter(\.enabled)
    switch (m.defaultAction != nil, apps.count) {
    case (true, 0): return "Everywhere"
    case (true, 1): return "Everywhere · different in \(apps[0].appName)"
    case (true, _): return "Everywhere · different in \(apps.count) apps"
    case (false, 0): return "No action"
    case (false, 1): return "Only in \(apps[0].appName)"
    case (false, _): return "Only in \(apps.count) apps"
    }
}

/// ⋯ menu for one mapping: Enabled, Edit, Duplicate, Delete.
struct MappingMenu: View {
    let mapping: ButtonMapping
    let onEdit: () -> Void
    @ObservedObject private var state = AppState.shared

    var body: some View {
        Menu {
            Toggle("Enabled", isOn: Binding(get: { mapping.enabled },
                                            set: { state.toggleMapping(mapping, enabled: $0) }))
            Divider()
            Button("Edit…", action: onEdit)
            Button("Duplicate") { state.duplicate(mapping) }
            Divider()
            Button("Delete", role: .destructive) { state.delete(mapping.id) }
        } label: {
            Image(systemName: "ellipsis.circle").foregroundStyle(.secondary)
        }
        .menuStyle(.borderlessButton)
        .menuIndicator(.hidden)
        .fixedSize()
        .accessibilityLabel("More for \(mapping.label)")
    }
}

/// One mapping row: name + scope on the left, what it does on the right, one ⋯ menu.
struct MappingRow: View {
    let mapping: ButtonMapping
    var showDevice = false
    let onEdit: () -> Void
    @Local private var hovering = false

    var body: some View {
        HStack(spacing: Space.m) {
            Image(systemName: "computermouse.fill")
                .font(.system(size: 13))
                .foregroundStyle(mapping.enabled ? Color.ember : Palette.stone.opacity(0.6))
                .frame(width: 16)
            VStack(alignment: .leading, spacing: 2) {
                Text(mapping.label).font(.body.weight(.medium)).lineLimit(1)
                Text(showDevice ? "\(mapping.deviceName) · \(scopeText(mapping))" : scopeText(mapping))
                    .font(.caption).foregroundStyle(.secondary).lineLimit(1).truncationMode(.middle)
            }
            .opacity(mapping.enabled ? 1 : 0.6)
            Spacer(minLength: Space.s)
            MappingActionsView(mapping: mapping)
                .layoutPriority(1)
            MappingMenu(mapping: mapping, onEdit: onEdit)
        }
        .padding(.vertical, 8)
        .background(RoundedRectangle(cornerRadius: 8).fill(hovering ? Palette.stone.opacity(0.07) : Color.clear).padding(.horizontal, -6))
        .contentShape(Rectangle())
        .onHover { hovering = $0 }
        .onTapGesture(count: 2, perform: onEdit)
        .help("Double-click to edit")
    }
}

// MARK: - Helpers

private let relativeFormatter: RelativeDateTimeFormatter = {
    let f = RelativeDateTimeFormatter()
    f.unitsStyle = .short
    return f
}()

func relative(_ date: Date?) -> String {
    guard let date else { return "—" }
    if abs(date.timeIntervalSinceNow) < 45 { return "just now" }
    return relativeFormatter.localizedString(for: date, relativeTo: Date())
}

/// Elapsed time as a duration ("10 min", "2 h 5 min"), not "10 min ago".
func duration(since date: Date?) -> String {
    guard let date else { return "—" }
    let seconds = max(0, Date().timeIntervalSince(date))
    if seconds < 60 { return "< 1 min" }
    let f = DateComponentsFormatter()
    f.unitsStyle = .abbreviated
    f.allowedUnits = seconds < 3600 ? [.minute] : (seconds < 86_400 ? [.hour, .minute] : [.day, .hour])
    f.maximumUnitCount = 2
    return f.string(from: seconds) ?? "—"
}

func miceCount(_ n: Int) -> String {
    switch n {
    case 0: return "None"
    case 1: return "1 mouse"
    default: return "\(n) mice"
    }
}

func transportName(_ t: String?) -> String {
    let value = t ?? ""
    switch value {
    case "Bluetooth Low Energy": return "Bluetooth"
    case "SPI", "FIFO": return "Built-in"
    default: return value
    }
}

func runningRegularApps() -> [NSRunningApplication] {
    NSWorkspace.shared.runningApplications
        .filter { $0.activationPolicy == .regular && $0.bundleIdentifier != nil && !ClicksmithInfo.isOwnBundle($0.bundleIdentifier) }
        .sorted { ($0.localizedName ?? "") < ($1.localizedName ?? "") }
}

/// Lets the user pick an app from /Applications. Returns (bundleID, name).
func chooseApplication() -> (String, String)? {
    let panel = NSOpenPanel()
    panel.allowedContentTypes = [.applicationBundle]
    panel.directoryURL = URL(fileURLWithPath: "/Applications")
    panel.allowsMultipleSelection = false
    panel.prompt = "Choose"
    guard panel.runModal() == .OK, let url = panel.url, let bundle = Bundle(url: url),
          let id = bundle.bundleIdentifier else { return nil }
    let name = (bundle.object(forInfoDictionaryKey: "CFBundleDisplayName") as? String)
        ?? (bundle.object(forInfoDictionaryKey: "CFBundleName") as? String)
        ?? url.deletingPathExtension().lastPathComponent
    return (id, name)
}

enum SystemSettingsLink {
    static let inputMonitoring = URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_ListenEvent")!
    static let accessibility = URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility")!
}
