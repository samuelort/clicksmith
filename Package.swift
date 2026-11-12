// swift-tools-version: 5.9
import PackageDescription

let package = Package(
    name: "Clicksmith",
    platforms: [.macOS(.v13)],
    products: [
        .executable(name: "Clicksmith", targets: ["Clicksmith"]),
        .executable(name: "ClicksmithEngine", targets: ["ClicksmithEngine"]),
        .executable(name: "ClicksmithSelfTest", targets: ["ClicksmithSelfTest"])
    ],
    targets: [
        // Pure logic shared by the app and the engine. No UI, no IOKit.
        .target(name: "ClicksmithCore"),
        // Background input engine (LaunchAgent). Owns all input permissions.
        .executableTarget(name: "ClicksmithEngine", dependencies: ["ClicksmithCore"]),
        // AppKit-hosted settings app + menu bar item. Never touches the input path.
        .executableTarget(name: "Clicksmith", dependencies: ["ClicksmithCore"]),
        // XCTest-free test runner, so Command Line Tools-only Macs can verify the core.
        .executableTarget(name: "ClicksmithSelfTest", dependencies: ["ClicksmithCore"]),
        .testTarget(name: "ClicksmithCoreTests", dependencies: ["ClicksmithCore"])
    ]
)
