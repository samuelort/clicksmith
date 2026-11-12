import SwiftUI
import ClicksmithCore

/// Devices and Mappings in one "Buttons" page per mouse.
struct ButtonsView: View {
    @ObservedObject private var state = AppState.shared
    @Local private var draft: ButtonMapping? = nil
    @Local private var showNew = false

    private var devices: [DeviceInfo] { state.visibleDevices }
    private var device: DeviceInfo? { devices.first { $0.id == state.selectedDeviceID } ?? devices.first }

    var body: some View {
        Group {
            if let device {
                DeviceButtonsPage(device: device, devices: devices, draft: $draft, showNew: $showNew)
                    .id(device.id)
            } else {
                emptyState
            }
        }
        .sheet(item: $draft) { (m: ButtonMapping) in
            MappingEditor(mapping: m, isNew: !state.config.mappings.contains { $0.id == m.id }).tint(Color.ember)
        }
        .sheet(isPresented: $showNew) { NewMappingSheet(deviceID: device?.id).tint(Color.ember) }
    }

    private var emptyState: some View {
        VStack(spacing: 12) {
            Image(systemName: "computermouse").font(.system(size: 46, weight: .light)).foregroundStyle(Color.ember)
            Text("No mouse yet").font(.title2.weight(.semibold))
            Text(state.engineRunning ? "Connect a mouse and it appears here automatically."
                                     : "Start the engine to see your mice.")
                .foregroundStyle(.secondary)
            if state.hiddenBuiltInCount > 0 {
                Button("Show built-in trackpad") { state.update { $0.settings.showBuiltInDevices = true } }
                    .buttonStyle(.accentLink)
            }
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
    }
}

// MARK: - Hotspots on the mouse diagram

struct MouseHotspot: Identifiable, Hashable {
    let id: String
    let name: String
    /// CGEvent button number for standard buttons; nil for special buttons (DPI, extra).
    let systemNumber: Int?

    static let left = MouseHotspot(id: "left", name: "Left Click", systemNumber: 0)
    static let right = MouseHotspot(id: "right", name: "Right Click", systemNumber: 1)
    static let middle = MouseHotspot(id: "middle", name: "Middle Click", systemNumber: 2)
    static let back = MouseHotspot(id: "back", name: "Back", systemNumber: 3)
    static let forward = MouseHotspot(id: "forward", name: "Forward", systemNumber: 4)
    static func special(_ name: String) -> MouseHotspot { MouseHotspot(id: "special", name: name, systemNumber: nil) }
}

private struct DeviceButtonsPage: View {
    let device: DeviceInfo
    let devices: [DeviceInfo]
    @Binding var draft: ButtonMapping?
    @Binding var showNew: Bool
    @ObservedObject private var state = AppState.shared
    @Local private var pending: MouseHotspot? = nil
    @Local private var hovered: String? = nil
    @Local private var learnMessage: String? = nil
    @Local private var showDetails = false

    private var isRival: Bool { KnownDevices.isRival3WirelessGen2(device.key) }
    private var specialHotspot: MouseHotspot { .special(isRival ? "DPI Button" : "Top Button") }
    private var hotspots: [MouseHotspot] { [.left, .right, .middle, specialHotspot, .forward, .back] }

    private var scanning: Bool { state.activeLearn?.mode == .scan && state.activeLearn?.device == device.key }
    private var detected: [DetectedButton] {
        (state.learnResult?.buttons ?? []).filter { device.key.matches($0.device) && state.mapping(for: $0.device, button: $0.button) == nil }
    }
    private var mappings: [ButtonMapping] {
        state.config.mappings.filter { $0.device.matches(device.key) || device.key.matches($0.device) }
    }
    private var canLearn: Bool { device.connected && state.engineRunning }

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 18) {
                header
                diagramCard
                mappingsCard
                scanCard
                detailsCard
            }
            .padding(.horizontal, 32)
            .padding(.vertical, 24)
        }
        .onReceive(state.$learnResult) { result in handleLearn(result) }
    }

    // MARK: Header

    private var header: some View {
        HStack(alignment: .firstTextBaseline) {
            VStack(alignment: .leading, spacing: 4) {
                Text(device.name).font(.largeTitle.weight(.semibold)).lineLimit(1)
                Text(subtitle).foregroundStyle(.secondary)
            }
            Spacer()
            if devices.count > 1 {
                Menu {
                    ForEach(devices) { d in
                        Button(d.connected ? d.name : "\(d.name) (not connected)") { state.selectedDeviceID = d.id }
                    }
                } label: {
                    Label("Switch Mouse", systemImage: "arrow.left.arrow.right")
                }
                .fixedSize()
            }
        }
    }

    private var subtitle: String {
        var parts = [device.manufacturer ?? "Pointing device"]
        parts.append(device.connected ? transportName(device.transport) : "Not connected")
        parts.append("\(mappings.count) mapped")
        return parts.filter { !$0.isEmpty }.joined(separator: " · ")
    }

    // MARK: Diagram + legend

    private var diagramCard: some View {
        Card(title: "Buttons", systemImage: "hand.point.up.left") {
            Text("Click any button on the mouse or in the list to map it.")
                .foregroundStyle(.secondary)
            HStack(alignment: .center, spacing: 28) {
                MouseDiagram(hotspots: hotspots, pending: pending, hovered: $hovered,
                             mapped: { mapping(for: $0) != nil },
                             help: { h in mapping(for: h).map { scopeText($0) } ?? "Not mapped" },
                             onTap: tap)
                    .frame(width: 200, height: 290)
                legend
            }
            if let pending {
                HStack(spacing: 8) {
                    ProgressView().controlSize(.small)
                    Text("Press \(pending.name) on your mouse now…").foregroundStyle(Color.ember)
                    Spacer()
                    Button("Cancel") { cancelLearning() }
                }
            }
            if let learnMessage {
                Label(learnMessage, systemImage: "info.circle").font(.caption).foregroundStyle(.secondary)
            }
            if !canLearn {
                Label(device.connected ? "Start the engine to learn new buttons." : "Connect this mouse to learn new buttons.",
                      systemImage: "exclamationmark.triangle")
                    .font(.caption).foregroundStyle(Palette.amber)
            }
        }
    }

    private var legend: some View {
        VStack(alignment: .leading, spacing: 2) {
            ForEach(hotspots) { h in legendRow(h) }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }

    private func legendRow(_ h: MouseHotspot) -> some View {
        let m = mapping(for: h)
        return Button { tap(h) } label: {
            HStack(spacing: 10) {
                Circle().fill(m != nil ? Color.ember : Palette.stone.opacity(0.25)).frame(width: 8, height: 8)
                Text(h.name).frame(width: 110, alignment: .leading)
                if let m {
                    MappingActionsView(mapping: m)
                } else {
                    Text("—").foregroundStyle(.tertiary)
                }
                Spacer(minLength: 0)
            }
            .padding(.vertical, 6)
            .padding(.horizontal, 8)
            .background(RoundedRectangle(cornerRadius: 7).fill(hovered == h.id ? Color.ember.opacity(0.10) : Color.clear))
            .contentShape(Rectangle())
            .opacity(m == nil ? 0.7 : 1)
        }
        .buttonStyle(.plain)
        .onHover { inside in
            if inside { hovered = h.id } else if hovered == h.id { hovered = nil }
        }
        .accessibilityLabel(m == nil ? "\(h.name), not mapped. Map." : "\(h.name), \(scopeText(m!)). Edit.")
    }

    // MARK: Mappings on this mouse

    private var mappingsCard: some View {
        Card(title: "On this mouse", systemImage: "arrow.triangle.branch") {
            if mappings.isEmpty {
                Text("No buttons mapped yet. Click one above to start.").foregroundStyle(.secondary)
            } else {
                VStack(spacing: 0) {
                    ForEach(mappings) { m in
                        MappingRow(mapping: m) { draft = m }
                        if m.id != mappings.last?.id { Divider().padding(.leading, 30) }
                    }
                }
            }
            Button { showNew = true } label: { Label("Map another button", systemImage: "plus") }
                .buttonStyle(.accentLink)
                .disabled(!canLearn)
                .padding(.top, 2)
        }
    }

    // MARK: Hotspot ↔ mapping

    private func mapping(for h: MouseHotspot) -> ButtonMapping? {
        if let n = h.systemNumber {
            return mappings.first { $0.systemButtonNumber == n || ($0.systemButtonNumber == nil && SystemButton.guessNumber(for: $0.button) == n) }
        }
        if isRival { return mappings.first { $0.button == KnownDevices.rivalDPI } }
        // Other mice: only a button learned from this hotspot (label match) counts, never a guess.
        return mappings.first { $0.systemButtonNumber == nil && $0.label == h.name }
    }

    private func tap(_ h: MouseHotspot) {
        learnMessage = nil
        if let m = mapping(for: h) { draft = m; return }
        if h.systemNumber == nil && isRival {
            // The Rival's DPI button is known (report 01, byte 1, bit 0x20): no learning needed.
            draft = newMapping(button: KnownDevices.rivalDPI, hotspot: h)
            return
        }
        guard canLearn else { return }
        pending = h
        state.startLearning(device: device.key, mode: .single)
    }

    private func cancelLearning() {
        pending = nil
        state.stopLearning()
    }

    private func handleLearn(_ result: LearnResult?) {
        guard let h = pending, let result, result.finished else { return }
        pending = nil
        guard let b = result.buttons.first(where: { device.key.matches($0.device) }) else {
            learnMessage = "No button press was detected. Some buttons are handled inside the mouse and never reach macOS."
            return
        }
        draft = state.mapping(for: b.device, button: b.button) ?? newMapping(button: b.button, hotspot: h)
        DispatchQueue.main.async { state.clearLearnResult() }
    }

    private func newMapping(button: ButtonID, hotspot h: MouseHotspot) -> ButtonMapping {
        ButtonMapping(device: device.key, deviceName: device.name, button: button, label: h.name,
                      systemButtonNumber: h.systemNumber, defaultAction: ButtonAction(kind: .shortcut))
    }

    // MARK: Scan

    private var scanCard: some View {
        Card(title: "Find more buttons", systemImage: "dot.radiowaves.left.and.right") {
            Text("Has your mouse extra buttons? Start a scan, keep the mouse still and press each button once.")
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
            HStack {
                if scanning {
                    ProgressView().controlSize(.small)
                    Text("Listening… press each button").foregroundStyle(Color.ember)
                    Spacer()
                    Button("Stop") { state.stopLearning() }
                } else {
                    Button("Scan Buttons") { state.startLearning(device: device.key, mode: .scan) }
                        .disabled(!canLearn || pending != nil)
                }
            }
            ForEach(detected) { d in
                HStack {
                    Image(systemName: "sparkle").foregroundStyle(Color.ember)
                    Text(d.suggestedLabel)
                    Spacer()
                    Button("Map…") { draft = MappingEditor.draft(from: d) }
                }
            }
        }
    }

    // MARK: Details

    private var detailsCard: some View {
        Card(title: "Device", systemImage: "info.circle") {
            DisclosureGroup("Technical details", isExpanded: $showDetails) {
                Grid(alignment: .leading, horizontalSpacing: 18, verticalSpacing: 6) {
                    row("Status", device.connected ? "Connected" : "Not connected")
                    row("Connection", device.transport ?? "—")
                    row("Built-in", device.isBuiltIn ? "Yes" : "No")
                    row("Engine reads it", (device.listening ?? false) ? "Yes" : "No — idle until mapped")
                    row("Vendor ID", String(format: "0x%04X", device.key.vendorID))
                    row("Product ID", String(format: "0x%04X", device.key.productID))
                    row("Serial", device.key.serial ?? "Not reported")
                    row("Buttons in HID descriptor", device.declaredButtons > 0 ? "\(device.declaredButtons)" : "—")
                }
                .padding(.top, 6)
            }
        }
    }

    @ViewBuilder private func row(_ label: String, _ value: String) -> some View {
        GridRow {
            Text(label).foregroundStyle(.secondary)
            Text(value).textSelection(.enabled)
        }
    }
}

// MARK: - Mouse diagram

/// A top-down mouse with soft shading. Every part is a button; orange parts are mapped,
/// and hovering a part highlights the matching list row (and the other way round).
struct MouseDiagram: View {
    let hotspots: [MouseHotspot]
    let pending: MouseHotspot?
    @Binding var hovered: String?
    let mapped: (MouseHotspot) -> Bool
    let help: (MouseHotspot) -> String
    let onTap: (MouseHotspot) -> Void

    private let bodyShape = RoundedRectangle(cornerRadius: 72, style: .continuous)

    var body: some View {
        ZStack {
            bodyShape
                .fill(LinearGradient(colors: [Palette.card, Palette.sidebar],
                                     startPoint: .top, endPoint: .bottom))
                .shadow(color: .black.opacity(0.08), radius: 14, y: 8)
                .overlay(bodyShape.strokeBorder(Palette.stone.opacity(0.22), lineWidth: 1))
                .frame(width: 150, height: 250)

            // Left and right buttons, clipped to the body outline, with a seam between them.
            VStack(spacing: 0) {
                HStack(spacing: 1.5) {
                    zone(.left)
                    zone(.right)
                }
                .background(Palette.stone.opacity(0.22))
                .frame(height: 104)
                Rectangle().fill(Palette.stone.opacity(0.18)).frame(height: 1)
                Spacer(minLength: 0)
            }
            .frame(width: 150, height: 250)
            .clipShape(bodyShape)

            part(.middle, Capsule(), width: 18, height: 40).offset(y: -76)
            if let special = hotspots.first(where: { $0.systemNumber == nil }) {
                part(special, Circle(), width: 18, height: 18).offset(y: -28)
            }
            part(.forward, Capsule(), width: 11, height: 34).offset(x: -84, y: -12)
            part(.back, Capsule(), width: 11, height: 34).offset(x: -84, y: 28)
        }
        .frame(width: 200, height: 290)
        .accessibilityElement(children: .contain)
        .accessibilityLabel("Mouse diagram")
    }

    private func fill(_ h: MouseHotspot, base: Double) -> Color {
        if pending == h { return Color.ember.opacity(0.45) }
        if mapped(h) { return Color.ember.opacity(hovered == h.id ? 1 : 0.85) }
        return hovered == h.id ? Color.ember.opacity(0.20) : Palette.stone.opacity(base)
    }

    private func hover(_ h: MouseHotspot, _ inside: Bool) {
        if inside { hovered = h.id } else if hovered == h.id { hovered = nil }
    }

    private func zone(_ h: MouseHotspot) -> some View {
        Button { onTap(h) } label: {
            Rectangle().fill(fill(h, base: 0.0)).contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .background(Palette.card)
        .onHover { hover(h, $0) }
        .help("\(h.name): \(help(h))")
        .accessibilityLabel("\(h.name), \(help(h))")
    }

    private func part<S: Shape>(_ h: MouseHotspot, _ shape: S, width: CGFloat, height: CGFloat) -> some View {
        Button { onTap(h) } label: {
            shape.fill(fill(h, base: 0.12))
                .overlay(shape.stroke(Palette.stone.opacity(0.35), lineWidth: 1))
                .frame(width: width, height: height)
                .contentShape(shape)
        }
        .buttonStyle(.plain)
        .onHover { hover(h, $0) }
        .help("\(h.name): \(help(h))")
        .accessibilityLabel("\(h.name), \(help(h))")
    }
}
