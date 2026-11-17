using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Clicksmith.Core;

public sealed class ConfigException : Exception
{
    public ConfigException(string message) : base(message) { }
}

/// <summary>All files live in the user's own profile. CLICKSMITH_HOME overrides the root (tests).</summary>
public static class ClicksmithPaths
{
    public static string Root
    {
        get
        {
            var o = Environment.GetEnvironmentVariable("CLICKSMITH_HOME");
            if (!string.IsNullOrWhiteSpace(o)) return o;
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Clicksmith");
        }
    }

    public static string Config => Path.Combine(Root, "config.json");
    public static string Status => Path.Combine(Root, "engine-status.json");
    public static string Request => Path.Combine(Root, "engine-request.json");
    public static string Result => Path.Combine(Root, "engine-result.json");
    public static string Backups => Path.Combine(Root, "Configuration Backups");
    public static string Logs => Path.Combine(Root, "Logs");

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Backups);
        Directory.CreateDirectory(Logs);
    }
}

public static class Json
{
    public static readonly JsonSerializerOptions Options = Create();

    private static JsonSerializerOptions Create()
    {
        var o = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        o.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return o;
    }
}

/// <summary>Safe file helpers: atomic writes, size limits, no links/reparse points.</summary>
public static class SafeFile
{
    public const long MaxBytes = 2_000_000;

    public static bool IsTrusted(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return false;
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0) return false;
            return info.Length <= MaxBytes;
        }
        catch { return false; }
    }

    public static void WriteAtomic(string path, byte[] data)
    {
        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);
        var tmp = Path.Combine(dir, "." + Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllBytes(tmp, data);
            File.Move(tmp, path, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* best effort */ }
        }
    }

    public static byte[]? ReadTrusted(string path) => IsTrusted(path) ? File.ReadAllBytes(path) : null;
}

public static class ConfigCodec
{
    private static readonly Regex ExeName = new(@"^[A-Za-z0-9 ._()\-]{1,100}\.exe$", RegexOptions.IgnoreCase);

    public static byte[] Encode(ClicksmithConfig c) => JsonSerializer.SerializeToUtf8Bytes(c, Json.Options);

    /// <summary>Decode, migrate (schema 1 → 2) and validate. Imported files are data only.</summary>
    public static ClicksmithConfig Decode(byte[] data, bool validate = true)
    {
        if (data.Length > SafeFile.MaxBytes) throw new ConfigException("File is larger than 2 MB.");
        JsonNode? root;
        try { root = JsonNode.Parse(data); }
        catch (JsonException) { throw new ConfigException("Not a Clicksmith configuration file."); }
        if (root is not JsonObject obj) throw new ConfigException("Not a Clicksmith configuration file.");

        int version;
        try { version = obj["schemaVersion"]?.GetValue<int>() ?? 0; }
        catch { throw new ConfigException("Invalid schemaVersion."); }
        if (version > ClicksmithConfig.CurrentSchemaVersion)
            throw new ConfigException($"Configuration schema {version} is newer than this version of Clicksmith supports.");
        if (version < 1) throw new ConfigException("Missing schemaVersion.");
        string? platform;
        try { platform = obj["platform"]?.GetValue<string>(); }
        catch { throw new ConfigException("Invalid platform field."); }
        if (platform is not null && platform != "windows")
            throw new ConfigException("This configuration was made for Clicksmith on another platform.");

        ClicksmithConfig config;
        try
        {
            config = version == 1 ? Migrations.FromV1(obj) : obj.Deserialize<ClicksmithConfig>(Json.Options)!;
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or NotSupportedException)
        {
            throw new ConfigException("Malformed configuration: " + e.Message);
        }
        if (config is null) throw new ConfigException("Empty configuration.");
        config.SchemaVersion = ClicksmithConfig.CurrentSchemaVersion;
        config.Platform = "windows";
        if (validate) Validate(config);
        return config;
    }

    public static void Validate(ClicksmithConfig c)
    {
        var s = c.Settings ?? throw new ConfigException("Missing settings.");
        if (s.DuplicateGuardMs is < 0 or > 2000) throw new ConfigException("Repeat guard out of range.");
        if (s.WakeRecoveryDelayMs is < 500 or > 10000) throw new ConfigException("Wake delay out of range.");
        if (c.Profiles is null || c.Profiles.Count is < 1 or > 20) throw new ConfigException("Between 1 and 20 profiles are allowed.");
        if (c.Profiles.Select(p => p.Id).Distinct().Count() != c.Profiles.Count) throw new ConfigException("Duplicate profile id.");
        if (c.Profiles.All(p => p.Id != c.ActiveProfileId)) throw new ConfigException("The active profile doesn't exist.");

        foreach (var p in c.Profiles)
        {
            if (string.IsNullOrWhiteSpace(p.Name) || p.Name.Length > 40) throw new ConfigException("Profile names need 1–40 characters.");
            if (p.Mappings is null || p.Mappings.Count > 64) throw new ConfigException($"{p.Name}: too many mappings.");
            if (p.Mappings.Select(m => m.Trigger).Distinct().Count() != p.Mappings.Count)
                throw new ConfigException($"{p.Name}: each button can only be mapped once per profile.");
            foreach (var m in p.Mappings)
            {
                var where = $"{p.Name} › {m.Label}";
                if (string.IsNullOrWhiteSpace(m.Label) || m.Label.Length > 60) throw new ConfigException($"{p.Name}: a button needs a name (max 60 characters).");
                if (!Enum.IsDefined(m.Button)) throw new ConfigException($"{where}: unknown button.");
                if (m.Button == ClickButton.Key && !ClickButtons.IsExtraKey(m.TriggerVk))
                    throw new ConfigException($"{where}: an extra button must use F13–F24.");
                if (m.Button != ClickButton.Key && m.TriggerVk != 0)
                    throw new ConfigException($"{where}: only extra buttons have a key.");
                if (m.LongPressMs is < ButtonMapping.MinLongPressMs or > ButtonMapping.MaxLongPressMs)
                    throw new ConfigException($"{where}: hold time must be {ButtonMapping.MinLongPressMs}–{ButtonMapping.MaxLongPressMs} ms.");
                if (m.Overrides is null || m.Overrides.Count > 64) throw new ConfigException($"{where}: too many app overrides.");
                CheckAction(m.DefaultAction, where);
                CheckAction(m.LongPressAction, where + " (hold)");
                if (ClickButtons.IsPrimary(m.Button) && !s.AllowPrimarySuppression && (m.LongPressAction is not null || m.BlockOriginal && m.Enabled && m.DefaultAction is not null))
                    throw new ConfigException($"{where}: left/right click can only be changed after allowing it in Settings.");
                foreach (var o in m.Overrides)
                {
                    if (o.App is null || !ExeName.IsMatch(o.App)) throw new ConfigException($"{where}: invalid app '{o.App}'.");
                    if (o.AppName is null || o.AppName.Length > 100) throw new ConfigException($"{where}: app name too long.");
                    if (o.Path is not null && (o.Path.Length > 260 || !o.Path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)))
                        throw new ConfigException($"{where}: invalid app path.");
                    CheckAction(o.Action ?? throw new ConfigException($"{where}: missing app action."), where + " in " + o.AppName);
                }
                if (m.Overrides.Select(o => o.App.ToLowerInvariant()).Distinct().Count() != m.Overrides.Count)
                    throw new ConfigException($"{where}: each app can only appear once.");
            }
        }
    }

    private static void CheckAction(ButtonAction? a, string where)
    {
        if (a is null) return;
        if (!Enum.IsDefined(a.Kind)) throw new ConfigException($"{where}: unknown action.");
        var problem = a.Problem();
        if (problem is not null) throw new ConfigException($"{where}: {problem}");
    }
}

/// <summary>2.4: versioned, tested migrations. Schema 1 = one implicit profile.</summary>
public static class Migrations
{
    private sealed class V1
    {
        public EngineSettings Settings { get; set; } = new();
        public List<ButtonMapping> Mappings { get; set; } = new();
    }

    public static ClicksmithConfig FromV1(JsonObject obj)
    {
        var v1 = obj.Deserialize<V1>(Json.Options) ?? new V1();
        var profile = new Profile { Name = "Default", Mappings = v1.Mappings ?? new() };
        return new ClicksmithConfig
        {
            Settings = v1.Settings ?? new(),
            Profiles = { profile },
            ActiveProfileId = profile.Id
        };
    }
}

public static class ConfigStore
{
    /// <summary>Missing file → defaults. Corrupt/untrusted file → exception (caller keeps the file).</summary>
    public static ClicksmithConfig Load()
    {
        var path = ClicksmithPaths.Config;
        if (!File.Exists(path)) return ClicksmithConfig.CreateDefault();
        var data = SafeFile.ReadTrusted(path) ?? throw new ConfigException("config.json is a link or larger than 2 MB and was ignored.");
        return ConfigCodec.Decode(data);
    }

    /// <summary>2.1: the previous file is snapshotted before every save. A failed backup aborts the save.</summary>
    public static void Save(ClicksmithConfig c)
    {
        ConfigCodec.Validate(c);
        ClicksmithPaths.EnsureDirectories();
        ConfigurationHistory.SnapshotCurrent();
        SafeFile.WriteAtomic(ClicksmithPaths.Config, ConfigCodec.Encode(c));
    }

    public static void Export(ClicksmithConfig c, string path) => File.WriteAllBytes(path, ConfigCodec.Encode(c));

    /// <summary>Reads and validates a file for preview. Nothing is saved.</summary>
    public static ClicksmithConfig ReadForImport(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists) throw new ConfigException("File not found.");
        if (info.Length > SafeFile.MaxBytes) throw new ConfigException("File is larger than 2 MB.");
        return ConfigCodec.Decode(File.ReadAllBytes(path));
    }
}

public sealed record BackupEntry(string Path, DateTime CreatedUtc, long Bytes);

/// <summary>2.1: local, bounded configuration history. Never leaves the machine.</summary>
public static class ConfigurationHistory
{
    public const int Limit = 20;

    public static void SnapshotCurrent()
    {
        var source = ClicksmithPaths.Config;
        if (!File.Exists(source)) return;
        var data = SafeFile.ReadTrusted(source) ?? throw new ConfigException("Can't back up config.json (link or too large). Nothing was saved.");
        Directory.CreateDirectory(ClicksmithPaths.Backups);
        var name = $"config-{DateTime.UtcNow:yyyyMMdd'T'HHmmssfff'Z'}-{Guid.NewGuid().ToString("N")[..8]}.json";
        SafeFile.WriteAtomic(Path.Combine(ClicksmithPaths.Backups, name), data);
        foreach (var old in List().Skip(Limit))
        {
            try { File.Delete(old.Path); } catch { /* retention is best effort */ }
        }
    }

    /// <summary>Newest first.</summary>
    public static List<BackupEntry> List()
    {
        if (!Directory.Exists(ClicksmithPaths.Backups)) return new();
        return new DirectoryInfo(ClicksmithPaths.Backups).GetFiles("config-*.json")
            .Where(f => (f.Attributes & FileAttributes.ReparsePoint) == 0)
            .OrderByDescending(f => f.Name, StringComparer.Ordinal)
            .Select(f => new BackupEntry(f.FullName, f.CreationTimeUtc, f.Length))
            .ToList();
    }

    public static ClicksmithConfig Read(BackupEntry e)
    {
        var data = SafeFile.ReadTrusted(e.Path) ?? throw new ConfigException("Backup can't be read.");
        return ConfigCodec.Decode(data);
    }

    /// <summary>Restoring is a normal save, so the current file is backed up first.</summary>
    public static void Restore(BackupEntry e) => ConfigStore.Save(Read(e));
}
