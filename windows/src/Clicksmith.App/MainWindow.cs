using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using Clicksmith.Core;

namespace Clicksmith.App;

/// <summary>One window: custom title bar, four-section sidebar and the page area.
/// Pages are rebuilt from state on every change (cheap, and they can never drift out of sync).</summary>
internal sealed class MainWindow : Window
{
    private readonly AppState _state = AppState.Shared;
    private readonly Dictionary<string, double> _scroll = new();
    private ScrollViewer? _scroller;
    private string _scrollKey = "";
    public bool AllowClose { get; set; }

    private static readonly (Section Section, string Title, string Glyph)[] Sections =
    {
        (Section.Overview, "Overview", "\uE80F"),
        (Section.Buttons, "Buttons", "\uE962"),
        (Section.Apps, "Apps", "\uE71D"),
        (Section.Settings, "Settings", "\uE713")
    };

    public MainWindow()
    {
        Title = "Clicksmith";
        Width = 1020;
        Height = 700;
        MinWidth = 880;
        MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = Theme.Font;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
        try { Icon = Theme.Logo; } catch { }
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 40,
            ResizeBorderThickness = new Thickness(6),
            GlassFrameThickness = new Thickness(0),
            CornerRadius = new CornerRadius(8),
            UseAeroCaptionButtons = false
        });
        _state.Changed += () => Dispatcher.InvokeAsync(Render);
        Theme.Changed += () => Dispatcher.InvokeAsync(Render);
        StateChanged += (_, _) => Render();
        PreviewKeyDown += OnKey;
        Closing += (_, e) =>
        {
            if (AllowClose || !_state.Config.Settings.ShowTrayIcon) return;
            e.Cancel = true; // keep running in the tray; the engine never depends on this window
            Hide();
        };
        Closed += (_, _) => { if (!_state.Config.Settings.ShowTrayIcon) Application.Current.Shutdown(); };
        Render();
    }

    public void ShowAndActivate()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        _state.RefreshStatus(notify: true);
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control) return;
        var index = e.Key switch { Key.D1 => 0, Key.D2 => 1, Key.D3 => 2, Key.D4 => 3, _ => -1 };
        if (index < 0) return;
        Select(Sections[index].Section);
        e.Handled = true;
    }

    public void Select(Section s)
    {
        _state.Selection = s;
        Render();
    }

    public void Render()
    {
        if (_scroller is not null) _scroll[_scrollKey] = _scroller.VerticalOffset;
        Background = Theme.Canvas;
        Foreground = Theme.Text;

        var root = new Grid { Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0) };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var sidebar = BuildSidebar();
        Grid.SetColumn(sidebar, 0);
        root.Children.Add(sidebar);

        var main = new Grid { Background = Theme.Canvas };
        main.RowDefinitions.Add(new RowDefinition { Height = new GridLength(40) });
        main.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var caption = CaptionButtons();
        Grid.SetRow(caption, 0);
        main.Children.Add(caption);

        UIElement page = _state.Selection switch
        {
            Section.Buttons => new ButtonsPage(this).Build(),
            Section.Apps => new AppsPage(this).Build(),
            Section.Settings => new SettingsPage(this).Build(),
            _ => new OverviewPage(this).Build()
        };
        var content = new Border
        {
            MaxWidth = Ui.MaxContent + 2 * Ui.Page,
            Padding = new Thickness(Ui.Page, 0, Ui.Page, Ui.XL),
            Child = page,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        _scroller = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false };
        _scrollKey = _state.Selection + ":" + _state.SettingsTab;
        if (_scroll.TryGetValue(_scrollKey, out var offset))
        {
            var sv = _scroller;
            sv.Loaded += (_, _) => sv.ScrollToVerticalOffset(offset);
        }
        Grid.SetRow(_scroller, 1);
        main.Children.Add(_scroller);
        Grid.SetColumn(main, 1);
        root.Children.Add(main);

        Content = new Border { BorderBrush = Theme.Line, BorderThickness = new Thickness(1), Child = root };
    }

    private UIElement BuildSidebar()
    {
        var items = new StackPanel { Margin = new Thickness(10, 48, 10, 0) };
        for (var i = 0; i < Sections.Length; i++)
        {
            var (section, title, glyph) = Sections[i];
            var selected = _state.Selection == section;
            var attention = section == Section.Settings && _state.Health is HealthState.HookUnavailable or HealthState.Stopped;
            var row = Ui.Fill(10, 1,
                Ui.Glyph(glyph, 15, selected ? Theme.Copper : Theme.Stone),
                Ui.T(title, 13.5, selected ? FontWeights.SemiBold : FontWeights.Normal),
                attention ? new Border { Width = 7, Height = 7, CornerRadius = new CornerRadius(3.5), Background = Theme.Amber } : new Border());
            var item = new Border
            {
                Height = 34,
                Padding = new Thickness(10, 0, 10, 0),
                Margin = new Thickness(0, 0, 0, 2),
                CornerRadius = new CornerRadius(8),
                Background = selected ? Theme.Soft(Theme.Copper, 0.14) : Brushes.Transparent,
                Child = row,
                ToolTip = $"{title} (Ctrl+{i + 1})"
            };
            var s = section;
            Ui.MakeClickable(item, () => Select(s), selected ? null : Theme.Soft(Theme.Stone, 0.09), Brushes.Transparent);
            Ui.AutomationName(item, title);
            items.Children.Add(item);
        }

        var footer = Ui.Row(8, (UIElement?)Ui.Logo(18) ?? new Border(), Ui.T("Clicksmith " + ClicksmithInfo.DisplayVersion, 11.5, brush: Theme.Sub));
        footer.Margin = new Thickness(18, 0, 10, 16);
        footer.VerticalAlignment = VerticalAlignment.Bottom;

        var grid = new Grid { Background = Theme.Sidebar };
        grid.Children.Add(items);
        grid.Children.Add(footer);
        var drag = new Border { Height = 40, VerticalAlignment = VerticalAlignment.Top, Background = Brushes.Transparent };
        grid.Children.Add(drag);
        return new Border { BorderBrush = Theme.Line, BorderThickness = new Thickness(0, 0, 1, 0), Child = grid };
    }

    private UIElement CaptionButtons()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
        panel.Children.Add(CaptionButton("\uE921", "Minimize", () => WindowState = WindowState.Minimized, false));
        panel.Children.Add(CaptionButton(WindowState == WindowState.Maximized ? "\uE923" : "\uE922", "Maximize",
            () => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized, false));
        panel.Children.Add(CaptionButton("\uE8BB", "Close", Close, true));
        return panel;
    }

    private static Border CaptionButton(string glyph, string name, Action click, bool isClose)
    {
        var b = new Border { Width = 46, Height = 32, Background = Brushes.Transparent, Child = Ui.Glyph(glyph, 10, Theme.Text) };
        WindowChrome.SetIsHitTestVisibleInChrome(b, true);
        b.MouseEnter += (_, _) => b.Background = isClose ? new SolidColorBrush(Color.FromRgb(196, 43, 28)) : Theme.Soft(Theme.Stone, 0.14);
        b.MouseLeave += (_, _) => b.Background = Brushes.Transparent;
        b.MouseLeftButtonUp += (_, e) => { click(); e.Handled = true; };
        Ui.AutomationName(b, name);
        return b;
    }

    // ---------- Shared actions used by several pages ----------

    public void EditMapping(ButtonMapping? existing, ClickButton button, Action<ButtonMapping>? prepare = null)
    {
        var mapping = existing is null
            ? new ButtonMapping { Button = button, Label = ClickButtons.Name(button), DefaultAction = new ButtonAction { Kind = ActionKind.Shortcut } }
            : AppState.Clone(existing);
        prepare?.Invoke(mapping);
        var editor = new MappingEditor(mapping, existing is null) { Owner = this };
        editor.ShowDialog();
    }

    public ButtonMapping? MappingFor(ClickButton b) => _state.ActiveProfile.Mappings.FirstOrDefault(m => m.Button == b);
}
