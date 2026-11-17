using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Clicksmith.Core;
using Microsoft.Win32;

namespace Clicksmith.App;

/// <summary>Starts/stops the background engine and manages "start at login" (per user, no admin).</summary>
internal sealed class EngineControl
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public static string EnginePath => Path.Combine(AppContext.BaseDirectory, ClicksmithInfo.EngineProcessName + ".exe");
    public static string AppPath => Path.Combine(AppContext.BaseDirectory, ClicksmithInfo.AppProcessName + ".exe");

    public static bool IsAlive(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return !p.HasExited && p.ProcessName.Equals(ClicksmithInfo.EngineProcessName, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    public string? Start()
    {
        if (!File.Exists(EnginePath)) return "Clicksmith Engine wasn't found next to the app. Run install.cmd again.";
        try
        {
            Process.Start(new ProcessStartInfo(EnginePath) { UseShellExecute = false, WorkingDirectory = AppContext.BaseDirectory })?.Dispose();
            return null;
        }
        catch (Exception e) { return "Couldn't start the engine: " + e.Message; }
    }

    /// <summary>Asks the engine to quit, then makes sure it's gone.</summary>
    public void Stop()
    {
        try { IpcFiles.Write(ClicksmithPaths.Request, new EngineRequest { Kind = RequestKind.Quit }); } catch { }
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline && Process.GetProcessesByName(ClicksmithInfo.EngineProcessName).Length > 0)
            Thread.Sleep(100);
        foreach (var p in Process.GetProcessesByName(ClicksmithInfo.EngineProcessName))
        {
            try { p.Kill(); p.WaitForExit(2000); } catch { }
            p.Dispose();
        }
    }

    public string? Restart()
    {
        Stop();
        return Start();
    }

    public static bool RunsAtLogin(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(name) is string;
    }

    public static string? SetRunAtLogin(string name, string exe, bool on, string args = "")
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, true);
            if (on) key.SetValue(name, $"\"{exe}\"" + (args.Length > 0 ? " " + args : ""));
            else key.DeleteValue(name, false);
            return null;
        }
        catch (Exception e) { return "Windows refused the change: " + e.Message; }
    }
}
