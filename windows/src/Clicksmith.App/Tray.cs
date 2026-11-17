using System;
using System.IO;
using System.Linq;
using System.Windows;
using Clicksmith.Core;
using Forms = System.Windows.Forms;

namespace Clicksmith.App;

/// <summary>System-tray icon: status, pause, profile switch, open, quit. The menu is rebuilt
/// each time it opens, so it always shows the current state.</summary>
internal sealed class Tray : IDisposable
{
    private readonly Forms.NotifyIcon _icon = new();
    private readonly MainWindow _window;
    private readonly AppState _s = AppState.Shared;

    public Tray(MainWindow window)
    {
        _window = window;
        var ico = Path.Combine(AppContext.BaseDirectory, "assets", "clicksmith.ico");
        _icon.Icon = File.Exists(ico) ? new System.Drawing.Icon(ico) : System.Drawing.SystemIcons.Application;
        _icon.Text = "Clicksmith";
        _icon.ContextMenuStrip = new Forms.ContextMenuStrip();
        _icon.ContextMenuStrip.Opening += (_, _) => BuildMenu();
        _icon.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) _window.ShowAndActivate(); };
        _s.Changed += () => Application.Current?.Dispatcher.InvokeAsync(Refresh);
        Refresh();
    }

    private void Refresh()
    {
        _icon.Visible = _s.Config.Settings.ShowTrayIcon;
        var text = "Clicksmith — " + _s.HealthTitle;
        _icon.Text = text.Length > 63 ? text[..63] : text;
    }

    private void BuildMenu()
    {
        var menu = _icon.ContextMenuStrip!;
        menu.Items.Clear();
        menu.Items.Add(new Forms.ToolStripMenuItem("Clicksmith — " + _s.HealthTitle) { Enabled = false });
        menu.Items.Add(new Forms.ToolStripMenuItem(_s.HealthDetail) { Enabled = false });
        menu.Items.Add(new Forms.ToolStripSeparator());

        var paused = _s.Config.Settings.Paused;
        menu.Items.Add(new Forms.ToolStripMenuItem(paused ? "Resume mappings" : "Pause mappings", null, (_, _) => _s.SetPaused(!paused)));
        if (_s.Status?.EmergencyPaused == true)
            menu.Items.Add(new Forms.ToolStripMenuItem("Resume after emergency pause", null, (_, _) => _s.Send(RequestKind.Resume)));

        if (_s.Config.Profiles.Count > 1)
        {
            var profiles = new Forms.ToolStripMenuItem("Profile");
            foreach (var p in _s.Config.Profiles)
            {
                var id = p.Id;
                profiles.DropDownItems.Add(new Forms.ToolStripMenuItem(p.Name, null, (_, _) => _s.ActivateProfile(id)) { Checked = id == _s.Config.ActiveProfileId });
            }
            menu.Items.Add(profiles);
        }
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(new Forms.ToolStripMenuItem("Open Clicksmith", null, (_, _) => Show(Section.Overview)));
        menu.Items.Add(new Forms.ToolStripMenuItem("Buttons…", null, (_, _) => Show(Section.Buttons)));
        menu.Items.Add(new Forms.ToolStripMenuItem("Settings…", null, (_, _) => Show(Section.Settings)));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(new Forms.ToolStripMenuItem("The engine keeps running when you quit") { Enabled = false });
        menu.Items.Add(new Forms.ToolStripMenuItem("Quit Clicksmith", null, (_, _) => Quit()));
    }

    private void Show(Section s)
    {
        _window.Select(s);
        _window.ShowAndActivate();
    }

    private void Quit()
    {
        _window.AllowClose = true;
        _window.Close();
        Dispose();
        Application.Current.Shutdown();
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
