using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using PeriphHub.Core;
using PeriphHub.Core.Deck;

namespace PeriphHub.UI;

/// <summary>
/// The on-screen control deck: a small always-on-top grid of customisable buttons. It never takes
/// keyboard focus (so shortcuts go to the app you are using) and remembers its position separately
/// for every monitor layout (laptop only, laptop + second screen, …).
/// </summary>
public partial class DeckWindow : Window, IDeckHost
{
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr h, int i);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr h, int i, int v);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr h, int id);
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }

    private const int GWL_EXSTYLE = -20, WS_EX_NOACTIVATE = 0x08000000, WS_EX_APPWINDOW = 0x40000;
    private const uint SWP_NOSIZE = 1, SWP_NOMOVE = 2, SWP_NOZORDER = 4, SWP_NOACTIVATE = 0x10;
    private static readonly IntPtr HWND_TOPMOST = new(-1), HWND_NOTOPMOST = new(-2);
    private const int HotkeyId = 0xD3C;

    private IntPtr _hwnd;
    private HwndSource? _hotkeySource;
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _savePos = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private readonly List<(Border cell, DeckButton? button, int index)> _cells = new();
    private bool _edit, _placing, _placedOnce;
    private DeckButton? _clipboard;
    private Point _dragStart;
    private int _pressedIndex = -1;
    private string? _autoFrom;
    private int _tickCount;

    // live tile state (refreshed off the UI thread)
    private int? _volume;
    private bool _outMuted, _micMuted;

    public DeckWindow()
    {
        InitializeComponent();
        DeckStore.Changed += () => Dispatcher.BeginInvoke(Rebuild);
        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            var ex = GetWindowLong(_hwnd, GWL_EXSTYLE);
            SetWindowLong(_hwnd, GWL_EXSTYLE, ex | WS_EX_NOACTIVATE | WS_EX_APPWINDOW);
        };
        LocationChanged += (_, _) => { if (!_placing) { _savePos.Stop(); _savePos.Start(); } };
        _savePos.Tick += (_, _) => { _savePos.Stop(); SavePosition(); };
        SizeChanged += (_, _) => Dispatcher.BeginInvoke(() => PlaceOnScreen(onlyIfOffscreen: true), DispatcherPriority.Background);
        SystemEvents.DisplaySettingsChanged += (_, _) => Dispatcher.BeginInvoke(() => PlaceOnScreen(onlyIfOffscreen: false), DispatcherPriority.Background);
        _tick.Tick += (_, _) => OnTick();
        _tick.Start();
        Rebuild();
        RegisterHotkey();
    }

    // ------------------------------------------------------------------ show / hide
    public void ShowDeck()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        UpdateLayout();
        PlaceOnScreen(onlyIfOffscreen: false);
        ApplyTopmost();
        if (!DeckStore.Config.Visible) { DeckStore.Config.Visible = true; DeckStore.Save(false); }
    }

    public void HideDeck()
    {
        Hide();
        DeckStore.Config.Visible = false;
        DeckStore.Save(false);
    }

    public void Toggle()
    {
        if (IsVisible && WindowState != WindowState.Minimized) HideDeck(); else ShowDeck();
    }

    private void OnMinimize(object s, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void OnClose(object s, RoutedEventArgs e) => HideDeck();
    private void OnSettings(object s, RoutedEventArgs e) => ((App)Application.Current).ShowPage("app:deck");
    private void OnHeaderDrag(object s, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }

    private void ApplyTopmost()
    {
        Topmost = DeckStore.Config.AlwaysOnTop;
        if (_hwnd != IntPtr.Zero)
            SetWindowPos(_hwnd, DeckStore.Config.AlwaysOnTop ? HWND_TOPMOST : HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    // ------------------------------------------------------------------ multi-monitor placement
    private static string LayoutKey() => string.Join(";", System.Windows.Forms.Screen.AllScreens
        .OrderBy(s => s.Bounds.X).ThenBy(s => s.Bounds.Y)
        .Select(s => $"{s.Bounds.X},{s.Bounds.Y},{s.Bounds.Width}x{s.Bounds.Height}"));

    private static bool OnScreen(int x, int y, int w, int h) =>
        System.Windows.Forms.Screen.AllScreens.Any(s =>
        {
            var a = s.WorkingArea;
            int ix = Math.Min(x + w, a.Right) - Math.Max(x, a.Left);
            int iy = Math.Min(y + h, a.Bottom) - Math.Max(y, a.Top);
            return ix >= Math.Min(80, w) && iy >= Math.Min(40, h);
        });

    private void SavePosition()
    {
        if (_hwnd == IntPtr.Zero || !IsVisible || WindowState != WindowState.Normal) return;
        GetWindowRect(_hwnd, out var r);
        if (!OnScreen(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top)) return;
        DeckStore.Config.Positions[LayoutKey()] = new[] { r.Left, r.Top };
        DeckStore.Save(false);
    }

    /// <summary>
    /// Moves the deck to the spot remembered for the current monitor layout. If there is none, or the
    /// screen it was on is gone, it is brought back onto a visible screen (bottom-right of the main one).
    /// </summary>
    private void PlaceOnScreen(bool onlyIfOffscreen)
    {
        if (_hwnd == IntPtr.Zero || !IsVisible) return;
        GetWindowRect(_hwnd, out var r);
        int w = r.Right - r.Left, h = r.Bottom - r.Top;
        int x = r.Left, y = r.Top;
        bool saved = DeckStore.Config.Positions.TryGetValue(LayoutKey(), out var p) && p.Length == 2 && OnScreen(p[0], p[1], w, h);
        if (!onlyIfOffscreen && saved)
        {
            x = p![0]; y = p[1];
        }
        else if (!onlyIfOffscreen && !_placedOnce)
        {
            // First time on this screen setup: bottom-right corner of the main screen.
            var a = System.Windows.Forms.Screen.PrimaryScreen!.WorkingArea;
            x = a.Right - w - 24; y = a.Bottom - h - 24;
        }
        else if (OnScreen(x, y, w, h))
        {
            // keep: also clamp so the whole deck stays visible on its screen
            var a = System.Windows.Forms.Screen.FromRectangle(new System.Drawing.Rectangle(x, y, w, h)).WorkingArea;
            x = Math.Clamp(x, a.Left, Math.Max(a.Left, a.Right - w));
            y = Math.Clamp(y, a.Top, Math.Max(a.Top, a.Bottom - h));
        }
        else
        {
            var a = System.Windows.Forms.Screen.PrimaryScreen!.WorkingArea;
            x = a.Right - w - 24; y = a.Bottom - h - 24;
        }
        _placedOnce = true;
        if (x == r.Left && y == r.Top) return;
        _placing = true;
        SetWindowPos(_hwnd, IntPtr.Zero, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
        _placing = false;
    }

    // ------------------------------------------------------------------ hotkey
    public void RegisterHotkey()
    {
        try
        {
            if (_hotkeySource == null)
            {
                _hotkeySource = new HwndSource(new HwndSourceParameters("PeriphHubDeckHotkey") { Width = 0, Height = 0, WindowStyle = 0 });
                _hotkeySource.AddHook((IntPtr hwnd, int msg, IntPtr wp, IntPtr lp, ref bool handled) =>
                {
                    if (msg == 0x0312 && wp.ToInt32() == HotkeyId) { Toggle(); handled = true; }
                    return IntPtr.Zero;
                });
            }
            var h = _hotkeySource.Handle;
            UnregisterHotKey(h, HotkeyId);
            var combo = DeckStore.Config.Hotkey;
            if (string.IsNullOrWhiteSpace(combo)) return;
            uint mods = 0x4000; // MOD_NOREPEAT
            uint vk = 0;
            foreach (var k in Input.ParseCombo(combo))
            {
                switch (k)
                {
                    case 0xA4 or 0xA5: mods |= 1; break;
                    case 0xA2 or 0xA3: mods |= 2; break;
                    case 0xA0 or 0xA1: mods |= 4; break;
                    case 0x5B or 0x5C: mods |= 8; break;
                    default: vk = k; break;
                }
            }
            if (vk == 0 || !RegisterHotKey(h, HotkeyId, mods, vk))
                Log.Warn($"Deck shortcut {combo} could not be registered (in use by another app?)");
        }
        catch (Exception ex) { Log.Warn($"Deck shortcut: {ex.Message}"); }
    }

    // ------------------------------------------------------------------ pages
    public void GoToPage(string name)
    {
        var i = DeckStore.Config.Pages.FindIndex(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (i < 0) { Toast.Show($"No deck page called “{name}”"); return; }
        SetPage(i);
    }

    public void NextPage() => SetPage((DeckStore.Config.Page + 1) % DeckStore.Config.Pages.Count);
    public void PreviousPage() => SetPage((DeckStore.Config.Page - 1 + DeckStore.Config.Pages.Count) % DeckStore.Config.Pages.Count);
    private void OnPrevPage(object s, RoutedEventArgs e) { _autoFrom = null; PreviousPage(); }
    private void OnNextPage(object s, RoutedEventArgs e) { _autoFrom = null; NextPage(); }

    private void SetPage(int i)
    {
        if (i == DeckStore.Config.Page) return;
        DeckStore.Config.Page = i;
        DeckStore.Save();
    }

    private void FollowApp()
    {
        var cfg = DeckStore.Config;
        if (!cfg.FollowApp || _edit) return;
        var app = ForegroundApp.ProcessName;
        if (app.Length == 0 || app == "periphhub.exe") return;
        var i = cfg.Pages.FindIndex(p => !string.IsNullOrWhiteSpace(p.App) &&
                                          string.Equals(p.App.Trim(), app, StringComparison.OrdinalIgnoreCase));
        if (i >= 0 && i != cfg.Page)
        {
            _autoFrom ??= cfg.Pages[cfg.Page].Name;
            SetPage(i);
        }
        else if (i < 0 && _autoFrom != null)
        {
            var back = _autoFrom;
            _autoFrom = null;
            GoToPage(back);
        }
    }

    // ------------------------------------------------------------------ edit mode
    private void OnEdit(object s, RoutedEventArgs e) => SetEdit(!_edit);

    public void SetEdit(bool on)
    {
        _edit = on;
        EditHint.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        EditBtn.Background = on ? new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xB9, 0x4A)) : Brushes.Transparent;
        Rebuild();
    }

    private void EditCell(int index)
    {
        var page = DeckStore.CurrentPage;
        page.Buttons.TryGetValue(index, out var current);
        var dlg = new DeckButtonEditor(current?.Clone() ?? new DeckButton());
        if (dlg.ShowDialog() != true) return;
        if (dlg.Result == null || dlg.Result.IsEmpty) page.Buttons.Remove(index);
        else page.Buttons[index] = dlg.Result;
        DeckStore.Save();
    }

    private ContextMenu MenuFor(int index)
    {
        var page = DeckStore.CurrentPage;
        page.Buttons.TryGetValue(index, out var b);
        var m = new ContextMenu();
        void Item(string text, Action a, bool enabled = true)
        {
            var mi = new MenuItem { Header = text, IsEnabled = enabled };
            mi.Click += (_, _) => a();
            m.Items.Add(mi);
        }
        Item("Edit button…", () => EditCell(index));
        Item("Copy", () => _clipboard = b?.Clone(), b != null);
        Item("Paste", () => { page.Buttons[index] = _clipboard!.Clone(); DeckStore.Save(); }, _clipboard != null);
        Item("Clear", () => { page.Buttons.Remove(index); DeckStore.Save(); }, b != null);
        m.Items.Add(new Separator());
        Item(_edit ? "Finish editing" : "Edit all buttons", () => SetEdit(!_edit));
        Item("Deck settings…", () => ((App)Application.Current).ShowPage("app:deck"));
        return m;
    }

    // ------------------------------------------------------------------ building the grid
    public void Rebuild()
    {
        var cfg = DeckStore.Config;
        var page = DeckStore.CurrentPage;
        PageName.Text = page.Name;
        PageCount.Text = cfg.Pages.Count > 1 ? $"{cfg.Page + 1}/{cfg.Pages.Count}" : "";
        Frame.Opacity = 1;
        Opacity = Math.Clamp(cfg.Opacity, 30, 100) / 100.0;
        ApplyTopmost();

        Cells.Rows = Math.Clamp(cfg.Rows, 1, 10);
        Cells.Columns = Math.Clamp(cfg.Cols, 1, 12);
        Cells.Children.Clear();
        _cells.Clear();
        int count = Cells.Rows * Cells.Columns;
        for (int i = 0; i < count; i++)
        {
            page.Buttons.TryGetValue(i, out var b);
            var cell = MakeCell(i, b, cfg);
            _cells.Add((cell, b, i));
            Cells.Children.Add(cell);
        }
        UpdateTiles();
    }

    private Border MakeCell(int index, DeckButton? b, DeckConfig cfg)
    {
        int size = Math.Clamp(cfg.ButtonSize, 40, 160);
        var bg = Brush(b?.Color ?? "#22252D");
        var cell = new Border
        {
            Width = size, Height = size, Margin = new Thickness(3), CornerRadius = new CornerRadius(Math.Max(6, size / 7)),
            Background = b == null ? new SolidColorBrush(Color.FromArgb(0x30, 0x80, 0x88, 0x99)) : bg,
            BorderThickness = new Thickness(_edit ? 1.5 : 0),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0xAA, 0xFF, 0xB9, 0x4A)),
            Cursor = Cursors.Hand, AllowDrop = true, ClipToBounds = true, Tag = index,
            RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(1, 1),
            ToolTip = b == null ? (_edit ? "Click to add a button" : null) : (string.IsNullOrEmpty(b.Label) ? null : b.Label),
        };
        var grid = new Grid();
        if (b?.ImagePath is { Length: > 0 } img)
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit(); bmp.UriSource = new Uri(img); bmp.DecodePixelWidth = size * 2; bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.EndInit();
                grid.Children.Add(new Image { Source = bmp, Stretch = Stretch.UniformToFill });
            }
            catch { }
        }
        var art = new Image { Stretch = Stretch.UniformToFill, Opacity = 0.55, Name = "Art" };
        grid.Children.Add(art);
        var icon = new TextBlock
        {
            Text = b == null ? (_edit ? "+" : "") : b.Icon,
            FontSize = size * (b?.Icon.Length > 3 ? 0.22 : 0.38),
            FontFamily = new FontFamily("Segoe UI Emoji, Segoe UI Variable Display, Segoe UI"),
            Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, cfg.Labels ? size * 0.16 : 0),
            TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap,
        };
        if (b == null && _edit) icon.Opacity = 0.5;
        grid.Children.Add(icon);
        var label = new TextBlock
        {
            Text = b?.Label ?? "", FontSize = Math.Max(9, size * 0.135), Foreground = new SolidColorBrush(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF)),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(4, 0, 4, size * 0.07), TextTrimming = TextTrimming.CharacterEllipsis,
            Visibility = cfg.Labels ? Visibility.Visible : Visibility.Collapsed,
        };
        grid.Children.Add(label);
        cell.Child = grid;

        cell.MouseEnter += (_, _) => { if (b != null) cell.Background = Lighten(cell.Background); };
        cell.MouseLeave += (_, _) =>
        {
            cell.Background = b == null ? new SolidColorBrush(Color.FromArgb(0x30, 0x80, 0x88, 0x99)) : TileBackground(b);
            SetScale(cell, 1);
            if (_pressedIndex == index && !_edit) Release(index);
        };
        cell.MouseLeftButtonDown += (_, e) =>
        {
            _dragStart = e.GetPosition(this);
            if (_edit) { cell.CaptureMouse(); return; }
            if (b == null) return;
            cell.CaptureMouse();
            SetScale(cell, 0.93);
            _pressedIndex = index;
            Press(b);
        };
        cell.MouseLeftButtonUp += (_, _) =>
        {
            cell.ReleaseMouseCapture();
            SetScale(cell, 1);
            if (_edit) { EditCell(index); return; }
            if (_pressedIndex == index) Release(index);
        };
        cell.MouseMove += (_, e) =>
        {
            if (!_edit || e.LeftButton != MouseButtonState.Pressed || b == null) return;
            var d = e.GetPosition(this) - _dragStart;
            if (Math.Abs(d.X) < 6 && Math.Abs(d.Y) < 6) return;
            cell.ReleaseMouseCapture();
            DragDrop.DoDragDrop(cell, new DataObject("deck-index", index), DragDropEffects.Move);
        };
        cell.Drop += (_, e) =>
        {
            if (e.Data.GetData("deck-index") is not int from || from == index) return;
            var buttons = DeckStore.CurrentPage.Buttons;
            buttons.TryGetValue(from, out var a);
            buttons.TryGetValue(index, out var c);
            if (a != null) buttons[index] = a; else buttons.Remove(index);
            if (c != null) buttons[from] = c; else buttons.Remove(from);
            DeckStore.Save();
        };
        cell.MouseRightButtonUp += (_, e) => { cell.ContextMenu = MenuFor(index); cell.ContextMenu.IsOpen = true; e.Handled = true; };
        cell.MouseWheel += (_, e) =>
        {
            if (b?.Tile != "volume") return;
            var step = e.Delta > 0 ? 2 : -2;
            Task.Run(() =>
            {
                if (AudioEndpoints.DefaultOutputId() is { } id) AudioEndpoints.SetVolume(id, (AudioEndpoints.GetVolume(id) ?? 50) + step);
            });
            _volume = Math.Clamp((_volume ?? 50) + step, 0, 100);
            UpdateTiles();
        };
        return cell;
    }

    private static void SetScale(Border b, double s)
    {
        if (b.RenderTransform is ScaleTransform t) { t.ScaleX = s; t.ScaleY = s; }
    }

    private static Brush Brush(string hex)
    {
        try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
        catch { return new SolidColorBrush(Color.FromRgb(0x2B, 0x2F, 0x3A)); }
    }

    private static Brush Lighten(Brush b)
    {
        if (b is not SolidColorBrush s) return b;
        var c = s.Color;
        byte L(byte v) => (byte)Math.Min(255, v + 22);
        return new SolidColorBrush(Color.FromArgb(c.A, L(c.R), L(c.G), L(c.B)));
    }

    private Brush TileBackground(DeckButton b) =>
        b.Tile == "micmute" && _micMuted ? new SolidColorBrush(Color.FromRgb(0x8A, 0x1F, 0x2B)) : Brush(b.Color);

    // ------------------------------------------------------------------ pressing
    private ActionSpec? _held;

    private void Press(DeckButton b)
    {
        if (b.Steps.Count > 0)
        {
            var steps = b.Steps.ToList();
            _ = Task.Run(async () =>
            {
                foreach (var s in steps)
                {
                    ActionRunner.Run(s.ToSpec(), true);
                    ActionRunner.Run(s.ToSpec(), false);
                    if (s.DelayMs > 0) await Task.Delay(s.DelayMs);
                }
            });
            return;
        }
        var spec = b.Action;
        if (spec.IsDefault)
        {
            spec = b.Tile switch
            {
                "nowplaying" => new ActionSpec { Id = "deck:playpause" },
                "micmute" => new ActionSpec { Id = "deck:micmute" },
                "volume" => new ActionSpec { Id = "deck:outmute" },
                "battery" when b.TileArg != null => new ActionSpec { Id = "app:show", Arg = b.TileArg },
                _ => spec,
            };
        }
        if (spec.Id == "app:show") { ((App)Application.Current).ShowPage(spec.Arg!); return; }
        _held = spec;
        ActionRunner.Run(spec, true);
        if (b.Tile is "micmute") _micMuted = !_micMuted;
        if (b.Tile is "volume") _outMuted = !_outMuted;
        UpdateTiles();
    }

    private void Release(int index)
    {
        _pressedIndex = -1;
        if (_held is { } h) { _held = null; ActionRunner.Run(h, false); }
    }

    // ------------------------------------------------------------------ live tiles
    private void OnTick()
    {
        _tickCount++;
        FollowApp();
        if (_tickCount % 5 == 0 && IsVisible && DeckStore.Config.AlwaysOnTop) ApplyTopmost();
        if (!IsVisible) return;
        var page = DeckStore.CurrentPage;
        bool needAudio = page.Buttons.Values.Any(b => b.Tile is "micmute" or "volume");
        if (needAudio && _tickCount % 2 == 0)
        {
            Task.Run(() =>
            {
                if (AudioEndpoints.DefaultOutputId() is { } o) { _volume = AudioEndpoints.GetVolume(o); _outMuted = AudioEndpoints.GetMute(o) ?? false; }
                if (AudioEndpoints.DefaultInputId() is { } i) _micMuted = AudioEndpoints.GetMute(i) ?? false;
            }).ContinueWith(_ => Dispatcher.BeginInvoke(UpdateTiles));
        }
        UpdateTiles();
    }

    private void UpdateTiles()
    {
        var media = MediaController.Instance;
        foreach (var (cell, b, _) in _cells)
        {
            if (b == null || b.Tile.Length == 0 || cell.Child is not Grid g) continue;
            var art = g.Children.OfType<Image>().FirstOrDefault(i => i.Name == "Art");
            var texts = g.Children.OfType<TextBlock>().ToList();
            if (texts.Count < 2) continue;
            TextBlock icon = texts[0], label = texts[1];
            switch (b.Tile)
            {
                case "clock":
                    icon.Text = DateTime.Now.ToString("HH:mm");
                    icon.FontSize = cell.Width * 0.26;
                    label.Text = DateTime.Now.ToString("ddd d MMM");
                    break;
                case "nowplaying":
                    if (art != null) art.Source = media.Art;
                    icon.Text = media.HasSession ? (media.IsPlaying ? "⏸" : "▶") : "♪";
                    label.Text = media.HasSession ? media.Title : "Nothing playing";
                    break;
                case "volume":
                    icon.Text = _outMuted ? "🔇" : _volume is int v ? $"{v}%" : "🔊";
                    icon.FontSize = cell.Width * (_outMuted ? 0.38 : 0.26);
                    label.Text = string.IsNullOrEmpty(b.Label) ? "Volume" : b.Label;
                    break;
                case "micmute":
                    icon.Text = _micMuted ? "🔇" : "🎙";
                    label.Text = _micMuted ? "Mic muted" : "Mic on";
                    cell.Background = TileBackground(b);
                    break;
                case "battery":
                    var dev = App.Hub.Devices.FirstOrDefault(d => d.Key == b.TileArg);
                    icon.Text = dev?.BatteryPercent is int p ? $"{p}%" : "—";
                    icon.FontSize = cell.Width * 0.26;
                    label.Text = string.IsNullOrEmpty(b.Label) ? dev?.Name ?? "Battery" : b.Label;
                    break;
            }
        }
    }
}
