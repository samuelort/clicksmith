using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Clicksmith.Core;

namespace Clicksmith.App;

/// <summary>Edit one button: which button, name, action everywhere, hold action (2.2), per-app
/// actions and blocking. Save stays disabled until the mapping is valid; what's missing is shown
/// above it. Text fields never rebuild the window, so typing keeps focus.</summary>
internal sealed class MappingEditor : Window
{
    private readonly AppState _s = AppState.Shared;
    private readonly ButtonMapping _m;
    private readonly bool _isNew;
    private readonly Guid _originalId;
    private bool _hasDefault;
    private readonly ButtonAction _default;
    private bool _hasLong;
    private readonly ButtonAction _long;
    private TextBlock? _hint;
    private Border? _saveHost;
    private ScrollViewer? _scroll;

    public MappingEditor(ButtonMapping mapping, bool isNew)
    {
        _m = mapping;
        _isNew = isNew;
        _originalId = mapping.Id;
        _hasDefault = mapping.DefaultAction is not null;
        _default = mapping.DefaultAction ?? new ButtonAction { Kind = ActionKind.Shortcut };
        _hasLong = mapping.LongPressAction is not null;
        _long = mapping.LongPressAction ?? new ButtonAction { Kind = ActionKind.Shortcut };

        Title = isNew ? "New mapping" : "Edit mapping";
        var work = SystemParameters.WorkArea;
        Width = Math.Min(700, work.Width * 0.9);
        Height = Math.Min(780, work.Height * 0.88);
        MinHeight = Math.Min(480, work.Height * 0.88);
        MinWidth = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = Theme.Canvas;
        Foreground = Theme.Text;
        FontFamily = Theme.Font;
        UseLayoutRounding = true;
        Chrome.Apply(this);
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape && !ShortcutRecorder.AnyRecording) { Close(); e.Handled = true; } };
        Rebuild();
    }

    private bool IsKey => _m.Button == ClickButton.Key;
    private bool Primary => ClickButtons.IsPrimary(_m.Button);
    private bool CanBlock => IsKey || !Primary || _s.Config.Settings.AllowPrimarySuppression;

    // ---------- Build ----------

    private void Rebuild()
    {
        var offset = _scroll?.VerticalOffset ?? 0;
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = Ui.Fill(Ui.M, 1, Ui.Glyph(IsKey ? "\uE765" : "\uE962", 22),
            Ui.Col(2, Ui.T(_isNew ? "New mapping" : "Edit mapping", 18, FontWeights.SemiBold), Ui.Caption("Profile: " + _s.ActiveProfile.Name, false)),
            Ui.Toggle(_m.Enabled, v => { _m.Enabled = v; Rebuild(); }, "On", key: "enabled"));
        header.Margin = new Thickness(Ui.XL, Ui.L, Ui.XL, Ui.S);
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        var name = Ui.Input(_m.Label, t => { _m.Label = t; Revalidate(); }, name: "Name");
        name.MaxLength = 60;
        var body = Ui.Col(Ui.L,
            Section("Button", null, TriggerPicker(), IsKey ? Ui.Caption("Set the mouse's own software (for example SteelSeries GG) to send this key from the button. Clicksmith then listens for it — no other keys are read.") : null),
            Section("Name", null, name),
            Section("Everywhere", "What the button does in every app, unless an app below says otherwise.",
                Ui.Toggle(_hasDefault, v => { _hasDefault = v; Rebuild(); }, "Do something in every app", key: "hasdefault"),
                _hasDefault ? new ActionEditor(_default, Revalidate, Rebuild, "default").Build() : Ui.Caption(IsKey ? "The button only acts in the apps below." : "The button only acts in the apps below — elsewhere it works as normal.")),
            HoldSection(),
            AppsSection(),
            BlockSection());
        _scroll = new ScrollViewer
        {
            Content = new Border { Padding = new Thickness(Ui.XL, Ui.S, Ui.XL, Ui.L), Child = body },
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        var sv = _scroll;
        sv.Loaded += (_, _) => sv.ScrollToVerticalOffset(offset);
        Grid.SetRow(_scroll, 1);
        root.Children.Add(_scroll);

        _hint = Ui.T("", 12.5, brush: Theme.Sub, wrap: true);
        System.Windows.Automation.AutomationProperties.SetLiveSetting(_hint, System.Windows.Automation.AutomationLiveSetting.Polite);
        _saveHost = new Border();
        var cancel = Ui.Btn("Cancel", Close);
        var footer = new Border
        {
            BorderBrush = Theme.Line, BorderThickness = new Thickness(0, 1, 0, 0), Background = Theme.Canvas,
            Padding = new Thickness(Ui.XL, Ui.M, Ui.XL, Ui.M),
            Child = Ui.Col(Ui.S, _hint, Ui.Row(Ui.S,
                _isNew ? null : Ui.Btn("Delete", () => { if (Dialogs.Confirm(this, $"Delete {_m.Label}?", "You can undo this from the Overview.", "Delete", true)) { _s.Delete(Current()); Close(); } }, BtnKind.Destructive),
                null, cancel, _saveHost))
        };
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);

        Content = root;
        Revalidate();
    }

    private UIElement TriggerPicker()
    {
        var options = ClickButtons.All.Where(b => !ClickButtons.IsPrimary(b) || _s.Config.Settings.AllowPrimarySuppression)
            .Select(b => (ClickButtons.Name(b), b)).ToList();
        options.Add(("Extra button (F13–F24)", ClickButton.Key));
        var button = Ui.Combo(options, _m.Button, b =>
        {
            _m.Button = b;
            if (b == ClickButton.Key && !ClickButtons.IsExtraKey(_m.TriggerVk)) _m.TriggerVk = FirstFreeKey();
            if (b != ClickButton.Key) _m.TriggerVk = 0;
            Rebuild();
        }, 220, "Button");
        if (!IsKey) return Ui.Row(Ui.M, button);
        var keys = Enumerable.Range(ClickButtons.FirstExtraKey, ClickButtons.LastExtraKey - ClickButtons.FirstExtraKey + 1)
            .Select(v => (KeyNames.Name((ushort)v), (ushort)v));
        return Ui.Row(Ui.M, button, Ui.Combo(keys, _m.TriggerVk, v => { _m.TriggerVk = v; Revalidate(); }, 100, "Key the mouse sends"));
    }

    private ushort FirstFreeKey()
    {
        var used = _s.ActiveProfile.Mappings.Where(x => x.Button == ClickButton.Key && x.Id != _m.Id).Select(x => x.TriggerVk).ToHashSet();
        for (var vk = ClickButtons.FirstExtraKey; vk <= ClickButtons.LastExtraKey; vk++) if (!used.Contains(vk)) return vk;
        return ClickButtons.FirstExtraKey;
    }

    private static Border Section(string title, string? caption, params UIElement?[] content)
    {
        var parts = new List<UIElement?> { Ui.T(title, 14, FontWeights.SemiBold) };
        if (caption is not null) parts.Add(Ui.Caption(caption));
        parts.AddRange(content);
        var b = Ui.Surface(Ui.Col(Ui.M - 2, parts.ToArray()), 16);
        System.Windows.Automation.AutomationProperties.SetName(b, title);
        return b;
    }

    /// <summary>2.2: one extra action when the button is held.</summary>
    private UIElement HoldSection()
    {
        if (!CanBlock)
            return Section("Hold for a second action", "Holding left or right click can only be changed after allowing it in Settings › General › Safety.");
        var content = new List<UIElement?> { Ui.Toggle(_hasLong, v => { _hasLong = v; Rebuild(); }, "Do something else when I hold the button", key: "haslong") };
        if (_hasLong)
        {
            var label = Ui.T($"Hold for {_m.LongPressMs} ms", 13, brush: Theme.Sub);
            var slider = new Slider
            {
                Minimum = ButtonMapping.MinLongPressMs, Maximum = ButtonMapping.MaxLongPressMs, Value = _m.LongPressMs,
                TickFrequency = 50, IsSnapToTickEnabled = true, SmallChange = 50, LargeChange = 250, Width = 260, VerticalAlignment = VerticalAlignment.Center
            };
            System.Windows.Automation.AutomationProperties.SetName(slider, "Hold time in milliseconds");
            slider.ValueChanged += (_, e) => { _m.LongPressMs = (int)e.NewValue; label.Text = $"Hold for {_m.LongPressMs} ms"; Revalidate(); };
            content.Add(Ui.Row(Ui.M, slider, label));
            content.Add(new ActionEditor(_long, Revalidate, Rebuild, "long").Build());
            content.Add(Ui.Caption("A quick press does the action above; holding does this instead — never both. The button's own Windows action is held back while you press it."));
        }
        return Section("Hold for a second action", null, content.ToArray());
    }

    private UIElement AppsSection()
    {
        var list = new StackPanel();
        foreach (var o in _m.Overrides.ToList())
        {
            var item = o;
            var head = Ui.Fill(Ui.M, 1, Ui.AppTile(o.AppName, 26, o.App, o.Path),
                Ui.Col(1, Ui.T("In " + o.AppName, 13.5, FontWeights.SemiBold), Ui.Caption(o.App, false)),
                Ui.Toggle(o.Enabled, v => { item.Enabled = v; Rebuild(); }, key: "ov:" + o.Id),
                Ui.Link("Remove", () => { _m.Overrides.Remove(item); Rebuild(); }, 12.5, "rm:" + o.Id));
            var block = Ui.Col(Ui.S, head, new ActionEditor(o.Action, Revalidate, Rebuild, "ov:" + o.Id).Build());
            block.Margin = new Thickness(0, list.Children.Count > 0 ? Ui.L : 0, 0, 0);
            list.Children.Add(block);
        }
        var add = Ui.Btn("Add an app", () =>
        {
            var pick = Dialogs.ChooseApp(this, AppsPage.RunningApps());
            if (pick is null) return;
            if (_m.Overrides.Any(x => x.App.Equals(pick.Exe, StringComparison.OrdinalIgnoreCase))) return;
            _m.Overrides.Add(new AppOverride { App = pick.Exe, AppName = pick.Name, Path = pick.Path, Action = new ButtonAction { Kind = ActionKind.Shortcut } });
            Rebuild();
        }, BtnKind.Secondary, true, "\uE710", "addapp");
        return Section("Different in specific apps", "When one of these apps is in front, its action is used. Switching away goes back to the default instantly.", list, add);
    }

    private UIElement BlockSection()
    {
        if (IsKey) return Section("The key itself", "Clicksmith takes the key while it listens, so it never reaches the app in front.");
        if (!CanBlock) return Section("Normal click", "Left and right click keep their normal action.");
        if (_hasLong) return Section("Normal button action", "Held back automatically while a hold action is set.");
        var name = ClickButtons.Name(_m.Button);
        return Section("Normal button action", null,
            Ui.Toggle(_m.BlockOriginal, v => { _m.BlockOriginal = v; Rebuild(); }, $"Block {name}'s normal Windows action", key: "block"),
            Ui.Caption(_m.BlockOriginal ? $"{name} won't do its usual thing (e.g. go back) while this mapping applies." : $"{name} does both: its usual thing and your action."));
    }

    // ---------- Validation + save ----------

    private ButtonMapping Current()
    {
        var m = AppState.Clone(_m);
        m.Id = _originalId;
        m.Label = (m.Label ?? "").Trim();
        if (m.Button != ClickButton.Key) m.TriggerVk = 0;
        m.DefaultAction = _hasDefault ? AppState.Clone(_default) : null;
        m.LongPressAction = _hasLong && CanBlock ? AppState.Clone(_long) : null;
        return m;
    }

    private string? Problem()
    {
        var m = Current();
        if (string.IsNullOrWhiteSpace(m.Label)) return "Give the button a name.";
        if (m.DefaultAction is null && m.Overrides.Count == 0 && m.LongPressAction is null && !m.BlockOriginal)
            return "Choose an action for every app, a hold action, or add an app.";
        var clash = _s.ActiveProfile.Mappings.FirstOrDefault(x => x.Trigger == m.Trigger && x.Id != m.Id);
        if (clash is not null) return $"{Ui.TriggerName(m)} is already mapped as “{clash.Label}”. Edit that mapping instead.";
        return _s.Validate(c =>
        {
            var list = c.ActiveProfile.Mappings;
            var i = list.FindIndex(x => x.Id == m.Id);
            if (i >= 0) list[i] = m; else list.Add(m);
        });
    }

    private void Revalidate()
    {
        if (_hint is null || _saveHost is null) return;
        var problem = Problem();
        _hint.Text = problem ?? "";
        _hint.Visibility = problem is null ? Visibility.Collapsed : Visibility.Visible;
        var save = Ui.Btn(_isNew ? "Add mapping" : "Save", Save, BtnKind.Primary, problem is null, key: "save");
        save.IsDefault = problem is null && !ShortcutRecorder.AnyRecording;
        _saveHost.Child = save;
    }

    private void Save()
    {
        if (Problem() is not null) return;
        _s.Upsert(Current());
        if (_s.SaveError is null) Close();
        else _hint!.Text = _s.SaveError;
    }
}

/// <summary>Editor for one action: what it does (shortcut, media key, app, text, nothing).</summary>
internal sealed class ActionEditor
{
    private readonly ButtonAction _a;
    private readonly Action _changed;
    private readonly Action _rebuild;
    private readonly string _key;

    public ActionEditor(ButtonAction action, Action changed, Action rebuild, string key)
    {
        _a = action;
        _changed = changed;
        _rebuild = rebuild;
        _key = key;
    }

    public UIElement Build()
    {
        var kinds = new (string, ActionKind)[]
        {
            ("Keyboard shortcut", ActionKind.Shortcut), ("Media key", ActionKind.MediaKey), ("Open app", ActionKind.OpenApp),
            ("Type text", ActionKind.TypeText), ("Nothing (disable)", ActionKind.Nothing)
        };
        var kind = Ui.Combo(kinds, _a.Kind, k =>
        {
            _a.Kind = k;
            if (k == ActionKind.MediaKey && _a.Media is null) _a.Media = MediaKey.PlayPause;
            _rebuild();
        }, 180, "Action");
        kind.Uid = "kind:" + _key;

        UIElement? detail = _a.Kind switch
        {
            ActionKind.Shortcut => new ShortcutRecorder(_a, _changed, _rebuild, _key).Build(),
            ActionKind.MediaKey => Ui.Combo(Enum.GetValues<MediaKey>().Select(m => (MediaKeys.Title(m), m)), _a.Media ?? MediaKey.PlayPause, m => { _a.Media = m; _changed(); }, 180, "Media key"),
            ActionKind.OpenApp => AppPicker(),
            _ => null
        };
        var first = detail is null ? Ui.Row(Ui.M, kind) : Ui.Row(Ui.M, kind, detail);
        if (_a.Kind != ActionKind.TypeText) return first;

        var counter = Ui.Caption($"{(_a.Text ?? "").Length}/{ButtonAction.MaxTextLength} characters · typed into the app in front", false);
        var box = Ui.Input(_a.Text ?? "", t => { _a.Text = t; counter.Text = $"{t.Length}/{ButtonAction.MaxTextLength} characters · typed into the app in front"; _changed(); }, double.NaN, true, "Text to type");
        box.MaxLength = ButtonAction.MaxTextLength;
        return Ui.Col(Ui.S, first, box, counter,
            Ui.Caption("Don't store passwords here: the text is saved unencrypted. Clicksmith never types into password fields or Windows sign-in prompts."));
    }

    private UIElement AppPicker()
    {
        var allowed = _a.AppPath is null || AppLocationPolicy.IsAllowed(_a.AppPath, AppLocationPolicy.DefaultRoots());
        UIElement label = _a.AppPath is null
            ? Ui.T("No app chosen", 13, brush: Theme.Sub)
            : Ui.Row(6, Ui.AppTile(_a.AppName ?? "", 18, System.IO.Path.GetFileName(_a.AppPath), _a.AppPath), Ui.T(_a.AppName ?? System.IO.Path.GetFileNameWithoutExtension(_a.AppPath), 13, FontWeights.Medium));
        var browse = Ui.Btn(_a.AppPath is null ? "Choose app…" : "Change…", () =>
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Programs (*.exe)|*.exe", InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) };
            if (dlg.ShowDialog() != true) return;
            _a.AppPath = dlg.FileName;
            string name;
            try { name = System.Diagnostics.FileVersionInfo.GetVersionInfo(dlg.FileName).FileDescription ?? ""; } catch { name = ""; }
            _a.AppName = string.IsNullOrWhiteSpace(name) ? System.IO.Path.GetFileNameWithoutExtension(dlg.FileName) : name.Trim();
            _rebuild();
        }, key: "pick:" + _key);
        var row = Ui.Row(Ui.S, label, browse);
        if (allowed) return row;
        return Ui.Col(6, row, Ui.T("Only programs installed in Program Files or AppData\\Programs can be opened, and never command shells. This one would be refused.", 12, brush: Theme.Amber, wrap: true));
    }
}

/// <summary>Press-to-record field plus modifier chips (Windows grabs some Win+ combinations before
/// apps see them, so Ctrl/Alt/Shift/Win can also be toggled by hand).</summary>
internal sealed class ShortcutRecorder
{
    public static bool AnyRecording { get; private set; }
    private readonly ButtonAction _a;
    private readonly Action _changed;
    private readonly Action _rebuild;
    private readonly string _key;

    public ShortcutRecorder(ButtonAction action, Action changed, Action rebuild, string key)
    {
        _a = action;
        _changed = changed;
        _rebuild = rebuild;
        _key = key;
    }

    public UIElement Build()
    {
        var s = _a.Shortcut;
        var text = Ui.T(s?.Display ?? "Record shortcut", 13, FontWeights.SemiBold);
        var field = new Button
        {
            Content = Ui.Row(6, Ui.Glyph("\uE765", 13, Theme.Sub), text),
            MinWidth = 180,
            Style = (Style)Application.Current.FindResource("Cs.Button"),
            Uid = "rec:" + _key
        };
        System.Windows.Automation.AutomationProperties.SetName(field, "Shortcut: " + (s?.Display ?? "none") + ". Press to record, then press the keys.");
        var recording = false;
        void Stop()
        {
            recording = false;
            AnyRecording = false;
            field.BorderBrush = Theme.Line;
            text.Text = _a.Shortcut?.Display ?? "Record shortcut";
        }
        field.Click += (_, _) =>
        {
            if (recording) { Stop(); return; }
            recording = true;
            AnyRecording = true;
            text.Text = "Press a shortcut… (Esc cancels)";
            field.BorderBrush = Theme.Copper;
        };
        field.PreviewKeyDown += (_, e) =>
        {
            if (!recording) return;
            e.Handled = true;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            var mods = Keyboard.Modifiers;
            if (key == Key.Escape && mods == ModifierKeys.None) { Stop(); return; }
            var vk = (ushort)KeyInterop.VirtualKeyFromKey(key);
            if (vk == 0 || KeyNames.IsModifier(vk)) return; // wait for the real key
            _a.Shortcut = new KeyShortcut
            {
                Vk = vk,
                KeyName = KeyNames.Name(vk),
                Ctrl = mods.HasFlag(ModifierKeys.Control),
                Alt = mods.HasFlag(ModifierKeys.Alt),
                Shift = mods.HasFlag(ModifierKeys.Shift),
                Win = mods.HasFlag(ModifierKeys.Windows) || Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin)
            };
            Stop();
            _rebuild();
        };
        field.LostKeyboardFocus += (_, _) => { if (recording) Stop(); };

        var parts = new List<UIElement?> { field };
        if (s is not null)
        {
            parts.Add(Chip("Ctrl", s.Ctrl, v => s.Ctrl = v));
            parts.Add(Chip("Alt", s.Alt, v => s.Alt = v));
            parts.Add(Chip("Shift", s.Shift, v => s.Shift = v));
            parts.Add(Chip("Win", s.Win, v => s.Win = v));
        }
        var row = Ui.Row(6, parts.ToArray());
        var warning = s is null ? null : KeyNames.Warning(s);
        return warning is null ? row : Ui.Col(6, row, Ui.T(warning, 12, brush: Theme.Amber, wrap: true));
    }

    private ToggleButton Chip(string title, bool on, Action<bool> set)
    {
        var chip = new ToggleButton
        {
            Content = title,
            IsChecked = on,
            Style = (Style)Application.Current.FindResource("Cs.Chip"),
            ToolTip = (on ? "Remove " : "Add ") + title,
            Uid = "chip:" + _key + ":" + title
        };
        System.Windows.Automation.AutomationProperties.SetName(chip, title + " modifier");
        chip.Click += (_, _) => { set(chip.IsChecked == true); _rebuild(); };
        return chip;
    }
}
