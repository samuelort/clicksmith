using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using Clicksmith.Core;

namespace Clicksmith.Engine;

/// <summary>All engine file writes happen here, on one low-priority thread, so a slow disk or a
/// virus scanner can never delay the input thread or the control thread. Writes to the same file
/// are coalesced: only the newest content is written.</summary>
internal static class BackgroundIo
{
    private static readonly ConcurrentDictionary<string, byte[]> Latest = new(StringComparer.OrdinalIgnoreCase);
    private static readonly BlockingCollection<string> Queue = new(256);
    private static readonly Thread Worker = new(Run) { IsBackground = true, Name = "Clicksmith file writer", Priority = ThreadPriority.BelowNormal };
    private static int _started;

    public static void Write(string path, byte[] data)
    {
        Start();
        Latest[path] = data;
        Queue.TryAdd(path);
    }

    /// <summary>Synchronous write for shutdown, when the process is about to exit.</summary>
    public static void WriteNow(string path, byte[] data)
    {
        try { SafeFile.WriteAtomic(path, data); } catch { /* best effort at exit */ }
    }

    private static void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) == 0) Worker.Start();
    }

    private static void Run()
    {
        foreach (var path in Queue.GetConsumingEnumerable())
        {
            if (!Latest.TryRemove(path, out var data)) continue; // already written by an earlier item
            try { SafeFile.WriteAtomic(path, data); }
            catch (Exception e) { Log.Write($"Write failed for {Path.GetFileName(path)}: {e.GetType().Name}"); }
        }
    }
}
