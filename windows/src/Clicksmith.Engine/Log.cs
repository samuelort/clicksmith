using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using Clicksmith.Core;

namespace Clicksmith.Engine;

/// <summary>Lifecycle and errors only — never button presses or keystrokes. Queued and written on
/// its own thread, so logging never blocks input.</summary>
internal static class Log
{
    private static readonly BlockingCollection<string> Queue = new(512);
    private static readonly Thread Worker = new(Run) { IsBackground = true, Name = "Clicksmith log", Priority = ThreadPriority.Lowest };
    private static int _started;
    private static string FilePath => Path.Combine(ClicksmithPaths.Logs, "engine.log");

    public static void Write(string message)
    {
        if (Interlocked.Exchange(ref _started, 1) == 0) Worker.Start();
        Queue.TryAdd($"{DateTimeOffset.Now:O} {message}");
    }

    private static void Run()
    {
        foreach (var line in Queue.GetConsumingEnumerable())
        {
            try
            {
                Directory.CreateDirectory(ClicksmithPaths.Logs);
                var f = new FileInfo(FilePath);
                if (f.Exists && f.Length > 1_000_000)
                {
                    var old = FilePath + ".old";
                    if (File.Exists(old)) File.Delete(old);
                    File.Move(FilePath, old);
                }
                File.AppendAllText(FilePath, line + Environment.NewLine);
            }
            catch { /* logging must never break the engine */ }
        }
    }
}
