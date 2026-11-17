using System;
using System.IO;
using System.Windows;
using Clicksmith.Core;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace Clicksmith.App;

/// <summary>System-tray icon with a Nordic-styled menu: status, pause, profile switch, open, quit.
/// The menu is rebuilt each time it opens, so it always shows the current state.</summary>
internal sealed class Tray : IDisposable
{
    private readonly Forms.NotifyIcon _icon = new();
    private readonly MainWindow _window;
    private readonly AppState _s = AppState.Shared;

    public Tray(MainWindow window)
    {
        _window = window;
        var ico = Path.Combine(AppContext.BaseDirectory, "assets", "clicksmith.ico");
        _icon.Icon = File.Exists(ico) ? new Drawing.Icon(ico, Forms.SystemInformation.SmallIconSize) : Drawing.SystemIcons.Application;
        _icon.Text = "Clicksmith";
        _icon.ContextMenuStrip = new Forms.ContextMenuStrip { ShowImageMargin = false, ShowCheckMargin = true, Padding = new Forms.Padding(4) };
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
        menu.Renderer = new NordicRenderer();
        menu.Font = new Drawing.Font("Segoe UI", 9.5f);
        menu.Items.Clear();
        menu.Items.Add(new Forms.ToolStripMenuItem("Clicksmith — " + _s.HealthTitle) { Enabled = false, Font = new Drawing.Font("Segoe UI Semibold", 9.5f) });
        menu.Items.Add(new Forms.ToolStripMenuItem(_s.HealthDetail) { Enabled = false });
        menu.Items.Add(new Forms.ToolStripSeparator());

        var paused = _s.Config.Settings.Paused;
        menu.Items.Add(new Forms.ToolStripMenuItem(paused ? "Resume mappings" : "Pause mappings", null, (_, _) => _s.SetPaused(!paused)));
        if (_s.Status?.EmergencyPaused == true)
            menu.Items.Add(new Forms.ToolStripMenuItem("Resume after emergency pause", null, (_, _) => _s.Send(RequestKind.Resume)));

        if (_s.Config.Profiles.Count > 1)
        {
            var profiles = new Forms.ToolStripMenuItem("Profile");
            profiles.DropDown.Renderer = menu.Renderer;
            foreach (var p in _s.Config.Profiles)
            {
                var id = p.Id;
                profiles.DropDownItems.Add(new Forms.ToolStripMenuItem(p.Name, null, (_, _) => _s.ActivateProfile(id)) { Checked = id == _s.Config.ActiveProfileId });
            }
            menu.Items.Add(profiles);
        }
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(new Forms.ToolStripMenuItem("Open Clicksmith", null, (_, _) => Show(Section.Overview)) { Font = new Drawing.Font("Segoe UI Semibold", 9.5f) });
        menu.Items.Add(new Forms.ToolStripMenuItem("Buttons…", null, (_, _) => Show(Section.Buttons)));
        menu.Items.Add(new Forms.ToolStripMenuItem("Settings…", null, (_, _) => Show(Section.Settings)));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(new Forms.ToolStripMenuItem("Quit Clicksmith (the engine keeps running)", null, (_, _) => Quit()));
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

    /// <summary>Tray menu in the app's palette (follows light/dark mode).</summary>
    private sealed class NordicRenderer : Forms.ToolStripProfessionalRenderer
    {
        public NordicRenderer() : base(new Colors()) { RoundedEdges = true; }

        private static Drawing.Color C(System.Windows.Media.SolidColorBrush b) => Drawing.Color.FromArgb(b.Color.R, b.Color.G, b.Color.B);
        private static Drawing.Color Mix(Drawing.Color a, Drawing.Color b, double t) =>
            Drawing.Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));

        protected override void OnRenderItemText(Forms.ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? C(Theme.Text) : C(Theme.Sub);
            base.OnRenderItemText(e);
        }

        protected override void OnRenderItemCheck(Forms.ToolStripItemImageRenderEventArgs e)
        {
            var r = e.ImageRectangle;
            using var brush = new Drawing.SolidBrush(C(Theme.Copper));
            e.Graphics.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
            e.Graphics.FillEllipse(brush, r.X + r.Width / 2 - 3, r.Y + r.Height / 2 - 3, 7, 7);
        }

        private sealed class Colors : Forms.ProfessionalColorTable
        {
            private static Drawing.Color Card => C(Theme.Card);
            private static Drawing.Color Hover => Mix(C(Theme.Card), C(Theme.Stone), Theme.Dark ? 0.18 : 0.12);
            private static Drawing.Color Line => C(Theme.Line);
            public override Drawing.Color ToolStripDropDownBackground => Card;
            public override Drawing.Color ImageMarginGradientBegin => Card;
            public override Drawing.Color ImageMarginGradientMiddle => Card;
            public override Drawing.Color ImageMarginGradientEnd => Card;
            public override Drawing.Color MenuBorder => Line;
            public override Drawing.Color MenuItemBorder => Hover;
            public override Drawing.Color MenuItemSelected => Hover;
            public override Drawing.Color MenuItemSelectedGradientBegin => Hover;
            public override Drawing.Color MenuItemSelectedGradientEnd => Hover;
            public override Drawing.Color SeparatorDark => Line;
            public override Drawing.Color SeparatorLight => Card;
            public override Drawing.Color CheckBackground => Card;
            public override Drawing.Color CheckSelectedBackground => Hover;
            public override Drawing.Color CheckPressedBackground => Hover;
        }
    }
}
