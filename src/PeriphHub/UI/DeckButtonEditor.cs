using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PeriphHub.Core;
using PeriphHub.Core.Deck;

namespace PeriphHub.UI;

/// <summary>Dialog to set up one deck button: look (text, emoji, picture, colour) and what it does.</summary>
public sealed class DeckButtonEditor : Window
{
    public DeckButton? Result { get; private set; }

    private sealed record Kind(string Id, string Label);
    private static readonly Kind[] Kinds =
    {
        new("", "Button: run an action"),
        new("multi", "Button: run several actions in a row"),
        new("nowplaying", "Live: now playing (press = play/pause)"),
        new("volume", "Live: volume (scroll = change, press = mute)"),
        new("micmute", "Live: microphone (press = mute/unmute)"),
        new("clock", "Live: clock"),
        new("battery", "Live: device battery"),
        new("folder", "Folder: opens its own page of buttons"),
        new("timer", "Timer: countdown (press = start/pause, double-press = reset)"),
        new("stopwatch", "Stopwatch (press = start/pause, double-press = reset)"),
    };

    private static readonly ActionDef None = new("default", "Nothing", "General");
    private static IReadOnlyList<ActionDef> Actions =>
        new[] { None }.Concat(ActionCatalog.Common.Where(a => a.Id is not ("default" or "disabled") && !a.Id.StartsWith("device:"))).ToList();

    private readonly TextBox _label = new(), _icon = new(), _color = new() { Width = 90 }, _arg = new(), _delay = new() { Width = 70, Text = "100" };
    private readonly TextBlock _image = new() { Opacity = 0.75, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 300, VerticalAlignment = VerticalAlignment.Center };
    private readonly ComboBox _kind = new() { DisplayMemberPath = "Label" };
    private readonly ComboBox _action = new();
    private readonly ComboBox _device = new() { DisplayMemberPath = "Name" };
    private readonly TextBlock _argHint = new() { FontSize = 12, Opacity = 0.7, TextWrapping = TextWrapping.Wrap };
    private readonly ObservableCollection<DeckStep> _steps = new();
    private readonly ListBox _stepList = new() { Height = 120 };
    private readonly StackPanel _actionPanel = new(), _stepsPanel = new(), _devicePanel = new(), _folderPanel = new(), _timerPanel = new();
    private readonly TextBox _folderName = new(), _minutes = new() { Width = 70 }, _seconds = new() { Width = 70 };
    private readonly Border _preview = new() { Width = 76, Height = 76, CornerRadius = new CornerRadius(11) };
    private readonly TextBlock _previewIcon = new() { FontSize = 28, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 12), FontFamily = new FontFamily("Segoe UI Emoji, Segoe UI") };
    private readonly TextBlock _previewLabel = new() { FontSize = 10, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(3, 0, 3, 6), TextTrimming = TextTrimming.CharacterEllipsis };
    private string? _imagePath;

    public DeckButtonEditor(DeckButton b)
    {
        Title = "Deck button";
        Width = 620; SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;

        var root = new StackPanel { Margin = new Thickness(20) };

        // ---- look
        var look = new Grid();
        look.ColumnDefinitions.Add(new ColumnDefinition());
        look.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var fields = new StackPanel();
        fields.Children.Add(Field("Label", _label));
        fields.Children.Add(Field("Icon (emoji or short text — press Win + . for emoji)", _icon));
        var imgRow = new StackPanel { Orientation = Orientation.Horizontal };
        imgRow.Children.Add(Btn("Picture…", PickImage));
        imgRow.Children.Add(Btn("Remove picture", () => { _imagePath = null; UpdatePreview(); }));
        imgRow.Children.Add(_image);
        fields.Children.Add(Field("Picture (optional, fills the button)", imgRow));
        var colorRow = new WrapPanel();
        foreach (var hex in new[] { "#2B2F3A", "#1F4D36", "#1E3A5F", "#4A2430", "#5B3A12", "#3F2A5C", "#0F5F5F", "#6B1F1F", "#2E5E1F", "#7A4F00", "#111111", "#E0E0E0" })
        {
            var sw = new Button { Width = 26, Height = 26, Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(0), Background = Brush(hex), ToolTip = hex };
            sw.Click += (_, _) => { _color.Text = hex; UpdatePreview(); };
            colorRow.Children.Add(sw);
        }
        colorRow.Children.Add(_color);
        fields.Children.Add(Field("Colour", colorRow));
        look.Children.Add(fields);
        var prevGrid = new Grid();
        prevGrid.Children.Add(_previewIcon);
        prevGrid.Children.Add(_previewLabel);
        _preview.Child = prevGrid;
        _preview.Margin = new Thickness(20, 22, 0, 0);
        _preview.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(_preview, 1);
        look.Children.Add(_preview);
        root.Children.Add(look);

        // ---- behaviour
        _kind.ItemsSource = Kinds;
        root.Children.Add(Field("Type", _kind));

        _action.ItemsSource = Actions;
        _action.ItemTemplate = ActionTemplate();
        _actionPanel.Children.Add(Field("Action", _action));
        _actionPanel.Children.Add(Field("Details", _arg));
        _actionPanel.Children.Add(_argHint);
        _arg.PreviewKeyDown += CaptureKeystroke;
        root.Children.Add(_actionPanel);

        _stepList.ItemsSource = _steps;
        _stepList.DisplayMemberPath = null;
        _stepList.ItemTemplate = StepTemplate();
        var stepBtns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        stepBtns.Children.Add(new TextBlock { Text = "then wait", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        stepBtns.Children.Add(_delay);
        stepBtns.Children.Add(new TextBlock { Text = "ms", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 12, 0) });
        stepBtns.Children.Add(Btn("Add step", AddStep));
        stepBtns.Children.Add(Btn("Remove selected", () => { if (_stepList.SelectedItem is DeckStep s) _steps.Remove(s); }));
        stepBtns.Children.Add(Btn("Move up", MoveUp));
        _stepsPanel.Children.Add(new TextBlock { Text = "Steps (pick an action above, then “Add step”)", Margin = new Thickness(0, 12, 0, 4), FontWeight = FontWeights.SemiBold });
        _stepsPanel.Children.Add(_stepList);
        _stepsPanel.Children.Add(stepBtns);
        root.Children.Add(_stepsPanel);

        _device.ItemsSource = App.Hub.Devices.Where(d => d.BatteryPercent != null || d.Key.StartsWith("logitech") || d.Key.StartsWith("bt:") || d.Key.StartsWith("msi")).ToList();
        _devicePanel.Children.Add(Field("Device", _device));
        root.Children.Add(_devicePanel);

        _folderPanel.Children.Add(Field("Folder page name (leave empty to use the label)", _folderName));
        _folderPanel.Children.Add(new TextBlock
        {
            Text = "Press the folder on the deck to open it, then add buttons there. Its first button is always “Back”.",
            Opacity = 0.7, TextWrapping = TextWrapping.Wrap, FontSize = 12,
        });
        root.Children.Add(_folderPanel);

        var time = new StackPanel { Orientation = Orientation.Horizontal };
        time.Children.Add(_minutes);
        time.Children.Add(new TextBlock { Text = "min", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 14, 0) });
        time.Children.Add(_seconds);
        time.Children.Add(new TextBlock { Text = "sec", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) });
        _timerPanel.Children.Add(Field("Countdown length", time));
        root.Children.Add(_timerPanel);

        // ---- footer
        var footer = new DockPanel { Margin = new Thickness(0, 18, 0, 0), LastChildFill = false };
        var clear = Btn("Clear button", () => { Result = null; DialogResult = true; });
        DockPanel.SetDock(clear, Dock.Left);
        footer.Children.Add(clear);
        var cancel = Btn("Cancel", () => DialogResult = false);
        var save = Btn("Save", Save);
        save.IsDefault = true;
        if (TryFindResource("AccentButtonStyle") is Style accent) save.Style = accent;
        DockPanel.SetDock(cancel, Dock.Right); DockPanel.SetDock(save, Dock.Right);
        footer.Children.Add(cancel); footer.Children.Add(save);
        root.Children.Add(footer);

        Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 760 };

        // ---- load
        _label.Text = b.Label; _icon.Text = b.Icon; _color.Text = b.Color; _imagePath = b.ImagePath;
        foreach (var s in b.Steps) _steps.Add(s);
        _kind.SelectedItem = Kinds.FirstOrDefault(k => k.Id == (b.Steps.Count > 0 ? "multi" : b.Tile)) ?? Kinds[0];
        _action.SelectedItem = Actions.FirstOrDefault(a => a.Id == b.Action.Id) ?? None;
        _arg.Text = b.Action.Arg ?? "";
        _device.SelectedItem = ((IEnumerable<ManagedDevice>)_device.ItemsSource).FirstOrDefault(d => d.Key == b.TileArg);
        if (b.Tile == "folder") _folderName.Text = b.TileArg ?? "";
        var secs = b.Tile == "timer" && int.TryParse(b.TileArg, out var ts) ? ts : 300;
        _minutes.Text = (secs / 60).ToString(); _seconds.Text = (secs % 60).ToString();

        _kind.SelectionChanged += (_, _) => UpdateVisibility();
        _action.SelectionChanged += (_, _) => UpdateVisibility();
        _label.TextChanged += (_, _) => UpdatePreview();
        _icon.TextChanged += (_, _) => UpdatePreview();
        _color.TextChanged += (_, _) => UpdatePreview();
        UpdateVisibility();
        UpdatePreview();
    }

    private static Brush Brush(string hex)
    {
        try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
        catch { return Brushes.DimGray; }
    }

    private static FrameworkElement Field(string label, UIElement control)
    {
        var p = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        p.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 4), Opacity = 0.85 });
        p.Children.Add(control);
        return p;
    }

    private static Button Btn(string text, Action click)
    {
        var b = new Button { Content = text, Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(0, 0, 8, 0) };
        b.Click += (_, _) => click();
        return b;
    }

    private static DataTemplate ActionTemplate()
    {
        var f = new FrameworkElementFactory(typeof(TextBlock));
        f.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Label"));
        return new DataTemplate { VisualTree = f };
    }

    private static DataTemplate StepTemplate()
    {
        var f = new FrameworkElementFactory(typeof(TextBlock));
        f.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding { Converter = new StepText() });
        return new DataTemplate { VisualTree = f };
    }

    private sealed class StepText : System.Windows.Data.IValueConverter
    {
        public object Convert(object v, Type t, object p, System.Globalization.CultureInfo c)
        {
            if (v is not DeckStep s) return "";
            var name = ActionCatalog.Find(s.Id)?.Label ?? s.Id;
            return $"{name}{(string.IsNullOrEmpty(s.Arg) ? "" : $"  ({s.Arg})")}  → wait {s.DelayMs} ms";
        }
        public object ConvertBack(object v, Type t, object p, System.Globalization.CultureInfo c) => throw new NotSupportedException();
    }

    private void UpdateVisibility()
    {
        var kind = (_kind.SelectedItem as Kind)?.Id ?? "";
        var act = _action.SelectedItem as ActionDef ?? None;
        _actionPanel.Visibility = kind is "" or "multi" ? Visibility.Visible : Visibility.Collapsed;
        _stepsPanel.Visibility = kind == "multi" ? Visibility.Visible : Visibility.Collapsed;
        _devicePanel.Visibility = kind == "battery" ? Visibility.Visible : Visibility.Collapsed;
        _folderPanel.Visibility = kind == "folder" ? Visibility.Visible : Visibility.Collapsed;
        _timerPanel.Visibility = kind == "timer" ? Visibility.Visible : Visibility.Collapsed;
        var showArg = act.NeedsArg;
        ((FrameworkElement)_arg.Parent).Visibility = showArg ? Visibility.Visible : Visibility.Collapsed;
        _argHint.Text = act.Id == "key" ? "Click the box and press the shortcut (e.g. Ctrl+Shift+M). Backspace clears it." : act.ArgHint ?? "";
        _argHint.Visibility = showArg ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdatePreview()
    {
        _preview.Background = Brush(_color.Text);
        _previewIcon.Text = _icon.Text;
        _previewLabel.Text = _label.Text;
        _image.Text = _imagePath ?? "";
        if (_imagePath != null)
        {
            try { _preview.Background = new ImageBrush(new System.Windows.Media.Imaging.BitmapImage(new Uri(_imagePath))) { Stretch = Stretch.UniformToFill }; }
            catch { }
        }
    }

    private void PickImage()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Pictures|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.ico" };
        if (dlg.ShowDialog(this) == true) { _imagePath = dlg.FileName; UpdatePreview(); }
    }

    private void CaptureKeystroke(object sender, KeyEventArgs e)
    {
        if ((_action.SelectedItem as ActionDef)?.Id != "key") return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Tab && Keyboard.Modifiers == ModifierKeys.None) return;
        e.Handled = true;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin) return;
        if (key == Key.Back && Keyboard.Modifiers == ModifierKeys.None) { _arg.Text = ""; return; }
        var parts = new List<string>();
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) parts.Add("Ctrl");
        if (Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin)) parts.Add("Win");
        if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0) parts.Add("Alt");
        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) parts.Add("Shift");
        parts.Add(Input.VkName(KeyInterop.VirtualKeyFromKey(key)));
        _arg.Text = string.Join("+", parts);
    }

    private void AddStep()
    {
        var act = _action.SelectedItem as ActionDef ?? None;
        if (act.Id == "default") { MessageBox.Show(this, "Pick an action first.", "Deck button"); return; }
        int.TryParse(_delay.Text, out var ms);
        _steps.Add(new DeckStep { Id = act.Id, Arg = act.NeedsArg ? _arg.Text : null, DelayMs = Math.Clamp(ms, 0, 60000) });
    }

    private void MoveUp()
    {
        if (_stepList.SelectedItem is not DeckStep s) return;
        var i = _steps.IndexOf(s);
        if (i > 0) { _steps.Move(i, i - 1); _stepList.SelectedItem = s; }
    }

    private void Save()
    {
        var kind = (_kind.SelectedItem as Kind)?.Id ?? "";
        var act = _action.SelectedItem as ActionDef ?? None;
        Result = new DeckButton
        {
            Label = _label.Text.Trim(),
            Icon = _icon.Text.Trim(),
            ImagePath = _imagePath,
            Color = _color.Text.Trim().Length == 7 ? _color.Text.Trim() : "#2B2F3A",
            Tile = kind is "" or "multi" ? "" : kind,
            TileArg = kind switch
            {
                "battery" => (_device.SelectedItem as ManagedDevice)?.Key,
                "folder" => _folderName.Text.Trim().Length > 0 ? _folderName.Text.Trim() : (_label.Text.Trim().Length > 0 ? _label.Text.Trim() : "Folder"),
                "timer" => Math.Max(1, (int.TryParse(_minutes.Text, out var m) ? m : 0) * 60 + (int.TryParse(_seconds.Text, out var sx) ? sx : 0)).ToString(),
                _ => null,
            },
            Action = kind == "" ? new ActionSpec { Id = act.Id, Arg = act.NeedsArg ? _arg.Text.Trim() : null } : new ActionSpec { Id = "default" },
            Steps = kind == "multi" ? _steps.ToList() : new List<DeckStep>(),
        };
        if (kind == "battery" && Result.TileArg == null) { MessageBox.Show(this, "Pick a device for the battery tile.", "Deck button"); return; }
        if (Result.IsEmpty && kind == "") Result = null;
        DialogResult = true;
    }
}
