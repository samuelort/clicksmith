using System;
using System.Threading;
using System.Windows.Forms;

namespace Clicksmith.Engine;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var single = new Mutex(true, @"Local\Clicksmith.Engine", out var created);
        if (!created) return; // already running
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.Run(new EngineHost());
    }
}
