import SwiftUI
import AppKit
import UniformTypeIdentifiers
import ClicksmithCore

/// 1.2.3: Nordic palette, taken from the app icon — warm off-whites, soft stone greys and
/// one muted copper accent. Every colour adapts to light and dark mode.
enum Palette {
    private static func dynamic(_ light: (CGFloat, CGFloat, CGFloat), _ dark: (CGFloat, CGFloat, CGFloat)) -> Color {
        Color(nsColor: NSColor(name: nil) { appearance in
            let isDark = appearance.bestMatch(from: [.darkAqua, .aqua]) == .darkAqua
            let c = isDark ? dark : light
            return NSColor(srgbRed: c.0 / 255, green: c.1 / 255, blue: c.2 / 255, alpha: 1)
        })
    }

    /// Muted copper — the scroll wheel and spark in the icon.
    static let copper = dynamic((168, 101, 63), (212, 152, 112))
    /// Window background: warm paper / warm charcoal.
    static let canvas = dynamic((244, 241, 236), (30, 29, 27))
    /// Cards and sheets.
    static let card = dynamic((251, 250, 247), (41, 39, 37))
    /// Sidebar: a step darker than the canvas, like the icon tile.
    static let sidebar = dynamic((235, 231, 225), (36, 34, 32))
    /// Hairlines and quiet fills.
    static let stone = dynamic((120, 112, 102), (170, 162, 152))
    /// Calm status colours instead of traffic-light green/orange/red.
    static let sage = dynamic((104, 136, 100), (142, 176, 137))
    static let amber = dynamic((176, 128, 52), (214, 170, 96))
    static let clay = dynamic((170, 86, 70), (214, 128, 112))
}

extension Color {
    /// The one Clicksmith accent (name kept from 1.2 so all views pick up the new copper).
    static let ember = Palette.copper
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

// MARK: - Materials and styles

/// Native sidebar material behind the custom sidebar.
struct VisualEffect: NSViewRepresentable {
    var material: NSVisualEffectView.Material = .sidebar

    func makeNSView(context: Context) -> NSVisualEffectView {
        let v = NSVisualEffectView()
        v.material = material
        v.blendingMode = .behindWindow
        v.state = .followsWindowActiveState
        return v
    }

    func updateNSView(_ v: NSVisualEffectView, context: Context) { v.material = material }
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
            .frame(width: 8, height: 8)
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
        HStack(spacing: 5) {
            StatusDot(state: state)
            Text(text).font(.caption.weight(.semibold))
        }
        .padding(.horizontal, 8)
        .padding(.vertical, 3)
        .background(Capsule().fill(Palette.stone.opacity(0.12)))
    }
}

// MARK: - Containers

struct Card<Content: View>: View {
    let title: String
    let systemImage: String
    @ViewBuilder var content: Content

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            Label {
                Text(title).foregroundStyle(.secondary)
            } icon: {
                Image(systemName: systemImage).foregroundStyle(Color.ember)
            }
            .font(.subheadline.weight(.medium))
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
                .shadow(color: .black.opacity(0.035), radius: 6, y: 2)
        )
        .overlay(RoundedRectangle(cornerRadius: 14, style: .continuous).strokeBorder(Palette.stone.opacity(0.14)))
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
            Spacer()
            if let actionTitle, let action { Button(actionTitle, action: action) }
        }
        .padding(12)
        .background(RoundedRectangle(cornerRadius: 12, style: .continuous).fill(tint.opacity(0.10)))
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
            Image(nsImage: AppIcons.appIcon(bundleID: bundleID)).resizable().frame(width: 15, height: 15)
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

/// The real result of a mapping: default action plus per-app actions.
/// Never truncated — the row's text gives way instead.
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
        .opacity(mapping.enabled ? 1 : 0.45)
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

/// One mapping row: icon, name + scope, what it does, on/off, ⋯ menu. Double-click edits.
struct MappingRow: View {
    let mapping: ButtonMapping
    var showDevice = false
    let onEdit: () -> Void
    @ObservedObject private var state = AppState.shared
    @Local private var hovering = false

    var body: some View {
        HStack(spacing: 12) {
            Image(systemName: "computermouse.fill")
                .foregroundStyle(mapping.enabled ? Color.ember : Color.secondary)
                .frame(width: 18)
            VStack(alignment: .leading, spacing: 2) {
                Text(mapping.label).font(.body.weight(.medium)).lineLimit(1)
                Text(showDevice ? "\(mapping.deviceName) · \(scopeText(mapping))" : scopeText(mapping))
                    .font(.caption).foregroundStyle(.secondary).lineLimit(1).truncationMode(.middle)
            }
            .help(scopeText(mapping))
            Spacer(minLength: 8)
            MappingActionsView(mapping: mapping)
                .layoutPriority(1)
            Toggle("", isOn: Binding(get: { mapping.enabled }, set: { state.toggleMapping(mapping, enabled: $0) }))
                .labelsHidden()
                .toggleStyle(.switch)
                .controlSize(.mini)
                .accessibilityLabel("Enable \(mapping.label)")
            Menu {
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
        .padding(.vertical, 7)
        .background(RoundedRectangle(cornerRadius: 8).fill(hovering ? Palette.stone.opacity(0.07) : Color.clear).padding(.horizontal, -6))
        .contentShape(Rectangle())
        .onHover { hovering = $0 }
        .onTapGesture(count: 2, perform: onEdit)
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
    return relativeFormatter.localizedString(for: date, relativeTo: Date())
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
