using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Clicksmith.Core;

namespace Clicksmith.Engine;

/// <summary>Sends actions on its own thread so the mouse hook always returns immediately.</summary>
internal sealed class ActionSender : IDisposable
{
    private static readonly HashSet<string> CredentialProcesses = new(StringComparer.OrdinalIgnoreCase)
        { "consent.exe", "logonui.exe", "credentialuibroker.exe", "lockapp.exe" };

    private readonly BlockingCollection<(ButtonAction Action, string? App)> _queue = new(64);
    private readonly Thread _thread;
    private readonly string[] _allowedRoots = AppLocationPolicy.DefaultRoots();

    public ActionSender()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "Clicksmith actions" };
        _thread.Start();
    }

    /// <summary>Non-blocking. Drops the action if the queue is full rather than stalling input.</summary>
    public void Enqueue(ButtonAction action, string? foregroundApp) => _queue.TryAdd((action, foregroundApp));

    private void Run()
    {
        foreach (var (action, app) in _queue.GetConsumingEnumerable())
        {
            try { Perform(action, app); }
            catch (Exception e) { Log.Write("Action failed: " + e.GetType().Name); }
        }
    }

    private void Perform(ButtonAction a, string? app)
    {
        switch (a.Kind)
        {
            case ActionKind.Shortcut when a.Shortcut is { } s:
                SendShortcut(s);
                break;
            case ActionKind.MediaKey when a.Media is { } m:
                SendKeys(new List<Native.INPUT> { Key(MediaKeys.VirtualKey(m), false), Key(MediaKeys.VirtualKey(m), true) });
                break;
            case ActionKind.OpenApp when a.AppPath is { } path:
                if (!AppLocationPolicy.IsAllowed(path, _allowedRoots) || !File.Exists(path))
                {
                    Log.Write("Open app refused: not an installed program in a trusted folder");
                    return;
                }
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
                break;
            case ActionKind.TypeText when a.Text is { } text:
                if (app is not null && CredentialProcesses.Contains(app))
                {
                    Log.Write("Type text skipped: a sign-in prompt is in front");
                    return;
                }
                TypeText(text);
                break;
        }
    }

    private static void SendShortcut(KeyShortcut s)
    {
        var mods = new List<ushort>();
        if (s.Ctrl) mods.Add(0x11);
        if (s.Alt) mods.Add(0x12);
        if (s.Shift) mods.Add(0x10);
        if (s.Win) mods.Add(0x5B);
        var inputs = new List<Native.INPUT>();
        foreach (var m in mods) inputs.Add(Key(m, false));
        inputs.Add(Key(s.Vk, false));
        inputs.Add(Key(s.Vk, true));
        for (var i = mods.Count - 1; i >= 0; i--) inputs.Add(Key(mods[i], true));
        SendKeys(inputs);
    }

    private static void TypeText(string text)
    {
        var inputs = new List<Native.INPUT>();
        foreach (var c in text)
        {
            if (c == '\n') { inputs.Add(Key(0x0D, false)); inputs.Add(Key(0x0D, true)); continue; }
            if (c == '\t') { inputs.Add(Key(0x09, false)); inputs.Add(Key(0x09, true)); continue; }
            inputs.Add(Unicode(c, false));
            inputs.Add(Unicode(c, true));
        }
        SendKeys(inputs);
    }

    private static Native.INPUT Key(ushort vk, bool up)
    {
        var flags = up ? Native.KEYEVENTF_KEYUP : 0u;
        if (KeyNames.IsExtended(vk)) flags |= Native.KEYEVENTF_EXTENDEDKEY;
        return new Native.INPUT
        {
            type = Native.INPUT_KEYBOARD,
            u = new Native.InputUnion { ki = new Native.KEYBDINPUT { wVk = vk, dwFlags = flags, dwExtraInfo = (IntPtr)ClicksmithInfo.InjectedMarker } }
        };
    }

    private static Native.INPUT Unicode(char c, bool up) => new()
    {
        type = Native.INPUT_KEYBOARD,
        u = new Native.InputUnion
        {
            ki = new Native.KEYBDINPUT
            {
                wScan = c,
                dwFlags = Native.KEYEVENTF_UNICODE | (up ? Native.KEYEVENTF_KEYUP : 0u),
                dwExtraInfo = (IntPtr)ClicksmithInfo.InjectedMarker
            }
        }
    };

    private static void SendKeys(List<Native.INPUT> inputs)
    {
        if (inputs.Count == 0) return;
        var arr = inputs.ToArray();
        var sent = Native.SendInput((uint)arr.Length, arr, Marshal.SizeOf<Native.INPUT>());
        if (sent != arr.Length) Log.Write("SendInput was blocked (the app in front may run as administrator)");
    }

    public void Dispose()
    {
        _queue.CompleteAdding();
        _thread.Join(500);
    }
}
