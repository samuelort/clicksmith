import Foundation
#if canImport(Darwin)
import Darwin
#endif

public enum ConfigError: Error, CustomStringConvertible, Equatable {
    case unsupportedSchema(Int)
    case invalid(String)
    case unreadable(String)
    case untrusted(String)

    public var description: String {
        switch self {
        case .unsupportedSchema(let v): return "Configuration schema \(v) is newer than this version of Clicksmith supports."
        case .invalid(let why): return "Invalid configuration: \(why)"
        case .unreadable(let why): return "Could not read configuration: \(why)"
        case .untrusted(let file): return "Ignored \(file): it isn't a private file owned by you (wrong owner, permissions or a link)."
        }
    }
}

/// File locations shared by app and engine. Everything lives in the user's own Application Support.
public enum ClicksmithPaths {
    public static var supportDirectory: URL {
        let base = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
        return base.appendingPathComponent("Clicksmith", isDirectory: true)
    }
    public static var config: URL { supportDirectory.appendingPathComponent("config.json") }
    public static var status: URL { supportDirectory.appendingPathComponent("engine-status.json") }
    public static var learnRequest: URL { supportDirectory.appendingPathComponent("learn-request.json") }
    public static var learnResult: URL { supportDirectory.appendingPathComponent("learn-result.json") }
    public static var lock: URL { supportDirectory.appendingPathComponent("engine.lock") }
    public static var resumeRequest: URL { supportDirectory.appendingPathComponent("resume-request.json") }
    public static var logFile: URL {
        FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent("Library/Logs/Clicksmith-engine.log")
    }

    /// Creates the folder if needed and always re-applies owner-only permissions.
    public static func ensureDirectory() {
        try? FileManager.default.createDirectory(at: supportDirectory, withIntermediateDirectories: true,
                                                 attributes: [.posixPermissions: 0o700])
        try? FileManager.default.setAttributes([.posixPermissions: 0o700], ofItemAtPath: supportDirectory.path)
    }
}

public enum ConfigCodec {
    public static func encoder() -> JSONEncoder {
        let e = JSONEncoder()
        e.outputFormatting = [.prettyPrinted, .sortedKeys]
        e.dateEncodingStrategy = .iso8601
        return e
    }

    public static func decoder() -> JSONDecoder {
        let d = JSONDecoder()
        d.dateDecodingStrategy = .iso8601
        return d
    }

    /// Decode, migrate and validate. Imported files are treated strictly as data.
    public static func decode(_ data: Data) throws -> ClicksmithConfig {
        guard data.count <= 2_000_000 else { throw ConfigError.invalid("file is larger than 2 MB") }
        struct Header: Decodable { let schemaVersion: Int? }
        let header: Header
        do { header = try decoder().decode(Header.self, from: data) }
        catch { throw ConfigError.invalid("not a Clicksmith configuration file") }

        let version = header.schemaVersion ?? 0
        guard version <= ClicksmithConfig.currentSchemaVersion else { throw ConfigError.unsupportedSchema(version) }
        guard version >= 1 else { throw ConfigError.invalid("missing schemaVersion") }

        var config: ClicksmithConfig
        do { config = try decoder().decode(ClicksmithConfig.self, from: data) }
        catch { throw ConfigError.invalid("malformed content (\(error.localizedDescription))") }
        config = migrate(config)
        try validate(config)
        return config
    }

    /// Schema 1 → 2: action modes became action kinds. ButtonAction's decoder reads both
    /// formats, so migration only stamps the new version; the next save writes schema 2.
    public static func migrate(_ config: ClicksmithConfig) -> ClicksmithConfig {
        var c = config
        c.schemaVersion = ClicksmithConfig.currentSchemaVersion
        return c
    }

    public static func validate(_ c: ClicksmithConfig) throws {
        guard c.mappings.count <= 512 else { throw ConfigError.invalid("more than 512 mappings") }
        guard (0...5000).contains(c.settings.duplicateGuardMs) else { throw ConfigError.invalid("duplicate guard out of range") }
        guard (0.5...30).contains(c.settings.wakeRestartDelay) else { throw ConfigError.invalid("wake delay out of range") }
        for m in c.mappings {
            guard (0...0xFFFF).contains(m.device.vendorID), (0...0xFFFF).contains(m.device.productID) else {
                throw ConfigError.invalid("device ID out of range")
            }
            guard (0..<4096).contains(m.button.byteIndex) else { throw ConfigError.invalid("byte index out of range") }
            guard m.button.mask.nonzeroBitCount == 1 else { throw ConfigError.invalid("button mask must be a single bit") }
            guard m.label.count <= 80, m.deviceName.count <= 200 else { throw ConfigError.invalid("label too long") }
            if let n = m.systemButtonNumber, !(0...31).contains(n) { throw ConfigError.invalid("system button out of range") }
            guard m.overrides.count <= 64 else { throw ConfigError.invalid("too many app overrides") }
            var actions: [ButtonAction] = m.overrides.map(\.action)
            if let d = m.defaultAction { actions.append(d) }
            for a in actions {
                if let problem = a.problem { throw ConfigError.invalid("\(m.label): \(problem)") }
            }
            for o in m.overrides {
                guard BundleID.isValid(o.bundleID) else { throw ConfigError.invalid("invalid bundle identifier '\(o.bundleID)'") }
                guard o.appName.count <= 200 else { throw ConfigError.invalid("app name too long") }
            }
        }
    }
}

/// Only trust files that are regular files (not links), owned by this user and not
/// writable by group or others. Guards config and IPC files against other local users
/// and symlink tricks.
public enum FileTrust {
    public static func isTrusted(_ url: URL) -> Bool {
        var st = stat()
        guard lstat(url.path, &st) == 0 else { return false }
        guard (st.st_mode & S_IFMT) == S_IFREG else { return false }
        guard st.st_uid == getuid() else { return false }
        return (st.st_mode & 0o022) == 0
    }
}

public enum ConfigStore {
    /// Missing file → default config. Corrupt or untrusted file → error
    /// (caller decides; the engine keeps its last good config).
    public static func load() throws -> ClicksmithConfig {
        let url = ClicksmithPaths.config
        guard FileManager.default.fileExists(atPath: url.path) else { return ClicksmithConfig() }
        guard FileTrust.isTrusted(url) else { throw ConfigError.untrusted("config.json") }
        let data: Data
        do { data = try Data(contentsOf: url) } catch { throw ConfigError.unreadable(error.localizedDescription) }
        return try ConfigCodec.decode(data)
    }

    public static func save(_ config: ClicksmithConfig) throws {
        try ConfigCodec.validate(config)
        ClicksmithPaths.ensureDirectory()
        let data = try ConfigCodec.encoder().encode(config)
        try AtomicFile.write(data, to: ClicksmithPaths.config)
    }
}

public enum AtomicFile {
    /// Atomic write with owner-only permissions.
    public static func write(_ data: Data, to url: URL) throws {
        try data.write(to: url, options: [.atomic])
        try? FileManager.default.setAttributes([.posixPermissions: 0o600], ofItemAtPath: url.path)
    }
}
