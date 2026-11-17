using System;
using System.IO;
using Clicksmith.Core;

namespace Clicksmith.Engine;

/// <summary>Lifecycle and errors only. Never button presses or keystrokes.</summary>
internal static class Log
{
    private static readonly object Gate = new();
    private static string File => Path.Combine(ClicksmithPaths.Logs, "engine.log");

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(ClicksmithPaths.Logs);
                var f = new FileInfo(File);
                if (f.Exists && f.Length > 1_000_000)
                {
                    var old = File + ".old";
                    if (System.IO.File.Exists(old)) System.IO.File.Delete(old);
                    System.IO.File.Move(File, old);
                }
                System.IO.File.AppendAllText(File, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
            }
        }
        catch { /* logging must never break the engine */ }
    }
}
