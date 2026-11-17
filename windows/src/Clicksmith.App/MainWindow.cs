using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using Clicksmith.Core;

namespace Clicksmith.App;

/// <summary>One window: custom title bar, four-section sidebar and the page area. Pages are rebuilt
/// from state when something changes; scroll position and keyboard focus are restored by Uid, so
/// rebuilding is invisible to keyboard and Narrator users.</summary>
internal sealed class MainWindow : Window
{
    private readonly AppState _state = AppState.Shared;
    private readonly Dictionary<string, double> _scroll = new();
    private ScrollViewer? _scroller;
    private string _scrollKey = "";
    private bool _renderQueued;
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
        var work = SystemParameters.WorkArea;
        Width = Math.Min(1040, work.Width * 0.9);
        Height = Math.Min(720, work.Height * 0.9);
        MinWidth = 860;
        MinHeight = 560;
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
        Chrome.Apply(this);
        _state.Changed += QueueRender;
        Theme.Changed += () => { Chrome.Paint(this); QueueRender(); };
        StateChanged += (_, _) => QueueRender();
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

    /// <summary>Several state changes in a row produce one rebuild.</summary>
    private void QueueRender()
    {
        if (_renderQueued) return;
        _renderQueued = true;
        Dispatcher.InvokeAsync(() => { _renderQueued = false; Render(); }, DispatcherPriority.Background);
    }

    public void ShowAndActivate()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        _state.RefreshDevices();
        _state.RefreshStatus(notify: true);
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control || !_state.Config.Settings.OnboardingCompleted) return;
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
        var focusUid = (Keyboard.FocusedElement as FrameworkElement)?.Uid;
        if (!IsKeyboardFocusWithin) focusUid = null;

        Background = Theme.Canvas;
        Foreground = Theme.Text;
        var onboarding = !_state.Config.Settings.OnboardingCompleted;

        var root = new Grid { Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0) };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(onboarding ? 0 : 204) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        if (!onboarding)
        {
            var sidebar = BuildSidebar();
            Grid.SetColumn(sidebar, 0);
            root.Children.Add(sidebar);
        }

        var main = new Grid { Background = Theme.Canvas };
        main.RowDefinitions.Add(new RowDefinition { Height = new GridLength(40) });
        main.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var caption = CaptionButtons();
        Grid.SetRow(caption, 0);
        main.Children.Add(caption);

        UIElement page = onboarding ? new OnboardingPage(this).Build() : _state.Selection switch
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
        _scrollKey = onboarding ? "onboarding" : _state.Selection + ":" + _state.SettingsTab;
        var sv = _scroller;
        var offset = _scroll.TryGetValue(_scrollKey, out var o) ? o : 0;
        sv.Loaded += (_, _) =>
        {
            if (offset > 0) sv.ScrollToVerticalOffset(offset);
            if (focusUid is { Length: > 0 } && Ui.FindByUid(this, focusUid) is { } target) target.Focus();
        };
        Grid.SetRow(_scroller, 1);
        main.Children.Add(_scroller);
        Grid.SetColumn(main, 1);
        root.Children.Add(main);

        Content = new Border { BorderBrush = Theme.Line, BorderThickness = new Thickness(1), Child = root };
    }

    private UIElement BuildSidebar()
    {
        var items = new StackPanel { Margin = new Thickness(10, 48, 10, 0) };
        AutomationProperties.SetName(items, "Sections");
        for (var i = 0; i < Sections.Length; i++)
        {
            var (section, title, glyph) = Sections[i];
            var selected = _state.Selection == section;
            var attention = section == Section.Settings && _state.Health is HealthState.HookUnavailable or HealthState.Stopped;
            var row = Ui.Fill(10, 1,
                Ui.Glyph(glyph, 15, selected ? Theme.Copper : Theme.Stone),
                Ui.T(title, 13.5, selected ? FontWeights.SemiBold : FontWeights.Normal),
                attention ? new Border { Width = 7, Height = 7, CornerRadius = new CornerRadius(3.5), Background = Theme.Amber, ToolTip = "Needs attention" } : new Border());
            var s = section;
            var item = Ui.RowButton(row, () => Select(s), title + (selected ? ", selected" : ""), "nav:" + title, new Thickness(10, 0, 10, 0));
            item.Height = 34;
            item.Margin = new Thickness(0, 0, 0, 2);
            item.ToolTip = $"{title} (Ctrl+{i + 1})";
            if (selected) item.Background = Theme.Soft(Theme.Copper, 0.14);
            items.Children.Add(item);
        }

        var footer = Ui.Row(8, (UIElement?)Ui.Logo(18) ?? new Border(), Ui.T("Clicksmith " + ClicksmithInfo.DisplayVersion, 11.5, brush: Theme.Sub));
        footer.Margin = new Thickness(18, 0, 10, 16);
        footer.VerticalAlignment = VerticalAlignment.Bottom;

        var grid = new Grid { Background = Theme.Sidebar };
        grid.Children.Add(items);
        grid.Children.Add(footer);
        return new Border { BorderBrush = Theme.Line, BorderThickness = new Thickness(0, 0, 1, 0), Child = grid };
    }

    private UIElement CaptionButtons()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
        panel.Children.Add(CaptionButton("\uE921", "Minimize", () => WindowState = WindowState.Minimized, false));
        panel.Children.Add(CaptionButton(WindowState == WindowState.Maximized ? "\uE923" : "\uE922", WindowState == WindowState.Maximized ? "Restore" : "Maximize",
            () => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized, false));
        panel.Children.Add(CaptionButton("\uE8BB", "Close", Close, true));
        return panel;
    }

    private static UIElement CaptionButton(string glyph, string name, Action click, bool isClose)
    {
        var face = new Border { Width = 46, Height = 32, Background = Brushes.Transparent, Child = Ui.Glyph(glyph, 10, Theme.Text) };
        var b = Ui.BareButton(face, click, name, "cap:" + name);
        b.Focusable = false; // Alt+F4 / Win+arrows cover the keyboard
        WindowChrome.SetIsHitTestVisibleInChrome(b, true);
        b.MouseEnter += (_, _) => face.Background = isClose ? new SolidColorBrush(Color.FromRgb(196, 43, 28)) : Theme.Soft(Theme.Stone, 0.14);
        b.MouseLeave += (_, _) => face.Background = Brushes.Transparent;
        return b;
    }

    // ---------- Shared actions used by several pages ----------

    public void EditMapping(ButtonMapping? existing, Trigger trigger, Action<ButtonMapping>? prepare = null)
    {
        var mapping = existing is null
            ? new ButtonMapping
            {
                Button = trigger.Button,
                TriggerVk = trigger.IsKey ? trigger.Vk : (ushort)0,
                Label = trigger.IsKey ? "Extra button" : ClickButtons.Name(trigger.Button),
                DefaultAction = new ButtonAction { Kind = ActionKind.Shortcut }
            }
            : AppState.Clone(existing);
        prepare?.Invoke(mapping);
        var editor = new MappingEditor(mapping, existing is null) { Owner = this };
        editor.ShowDialog();
    }

    public void EditMapping(ButtonMapping? existing, ClickButton button, Action<ButtonMapping>? prepare = null) =>
        EditMapping(existing, new Trigger(button), prepare);

    public ButtonMapping? MappingFor(ClickButton b) => _state.MappingFor(new Trigger(b));

    /// <summary>"New mapping": press the button first (2.1 flow), then edit it.</summary>
    public void NewMapping()
    {
        var trigger = Dialogs.PressButton(this);
        if (trigger is not { } t) return;
        EditMapping(_state.MappingFor(t), t);
    }

    public void OpenSettings(int tab)
    {
        _state.SettingsTab = tab;
        Select(Section.Settings);
    }
}
