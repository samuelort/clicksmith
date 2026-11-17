using System.Text.Json;

namespace Clicksmith.Core;

/// <summary>Written by the engine (≈ every 10 s and on change). No input data, only state.</summary>
public sealed class EngineStatus
{
    public string Version { get; set; } = ClicksmithInfo.Version;
    public int Pid { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset Heartbeat { get; set; }
    public bool HookActive { get; set; }
    public DateTimeOffset? HookInstalledAt { get; set; }
    public DateTimeOffset? LastSleep { get; set; }
    public DateTimeOffset? LastResume { get; set; }
    public DateTimeOffset? LastRecovery { get; set; }
    public bool Paused { get; set; }
    public bool EmergencyPaused { get; set; }
    public bool EmergencyHotKeyRegistered { get; set; }
    public string? ForegroundApp { get; set; }
    public string? LastExternalApp { get; set; }
    public string ActiveProfile { get; set; } = "Default";
    public int ActiveMappings { get; set; }
    public bool TestMode { get; set; }
    public string? LastError { get; set; }
}

public enum RequestKind { Identify, StartTest, StopTest, Resume, Reload, Quit }

public sealed class EngineRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public RequestKind Kind { get; set; }
    public DateTimeOffset At { get; set; } = DateTimeOffset.UtcNow;
    public bool IsFresh => (DateTimeOffset.UtcNow - At).Duration() < TimeSpan.FromSeconds(15);
}

/// <summary>Answer to Identify or a press during test mode. Nothing is executed in test mode.</summary>
public sealed class EngineResult
{
    public Guid RequestId { get; set; }
    public string Kind { get; set; } = "";
    public ClickButton? Button { get; set; }
    public string? App { get; set; }
    public string? MappingLabel { get; set; }
    public string? ShortDisplay { get; set; }
    public string? LongDisplay { get; set; }
    public bool WouldBlock { get; set; }
    public string Summary { get; set; } = "";
    public DateTimeOffset At { get; set; } = DateTimeOffset.UtcNow;
}

public static class IpcFiles
{
    public static void Write<T>(string path, T value)
    {
        ClicksmithPaths.EnsureDirectories();
        SafeFile.WriteAtomic(path, JsonSerializer.SerializeToUtf8Bytes(value, Json.Options));
    }

    public static T? Read<T>(string path) where T : class
    {
        try
        {
            var data = SafeFile.ReadTrusted(path);
            return data is null ? null : JsonSerializer.Deserialize<T>(data, Json.Options);
        }
        catch { return null; }
    }
}
