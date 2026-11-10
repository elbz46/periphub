using System.IO;
namespace PeriphHub.Core.Deck;

/// <summary>"Control deck" page in the main window: size, layout, behaviour and pages of the deck.</summary>
public sealed class DeckSettingsPage : ManagedDevice
{
    private readonly TextSetting _pageName;
    private readonly TextSetting _pageApp;
    private readonly InfoSetting _pages;
    private readonly ToggleSetting _visible;
    public Action? ShowDeck, HideDeck, EditDeck, HotkeyChanged;

    public DeckSettingsPage() : base("app:deck", "Control deck", "", DeviceKind.Other)
    {
        Transport = "On-screen button panel";
        var c = DeckStore.Config;
        Task Save() { DeckStore.Save(); return Task.CompletedTask; }

        _visible = Add("Deck", new ToggleSetting("show", "Show the deck", c.Visible, v =>
        {
            if (v) ShowDeck?.Invoke(); else HideDeck?.Invoke();
            return Task.CompletedTask;
        }) { Persist = false, Description = "A small panel of buttons that stays on top of other windows. Its own shortcut shows and hides it." });
        Add("Deck", new CommandSetting("cmds", "Buttons",
            new CommandItem("Edit buttons on the deck", () => { EditDeck?.Invoke(); return Task.CompletedTask; }),
            new CommandItem("Reset to the starter buttons", () => { DeckStore.Reset(); Refresh(); return Task.CompletedTask; }))
        { Description = "Tip: right-click any deck button to edit, copy, paste or clear it. In edit mode, drag buttons to move them." });

        Add("Layout", new RangeSetting("cols", "Buttons per row (columns)", 1, 12, 1, c.Cols, v => { c.Cols = v; return Save(); }) { Persist = false });
        Add("Layout", new RangeSetting("rows", "Rows", 1, 10, 1, c.Rows, v => { c.Rows = v; return Save(); }) { Persist = false });
        Add("Layout", new RangeSetting("size", "Button size", 40, 160, 4, c.ButtonSize, v => { c.ButtonSize = v; return Save(); }) { Unit = "px", Persist = false });
        Add("Layout", new RangeSetting("opacity", "See-through", 30, 100, 1, c.Opacity, v => { c.Opacity = v; return Save(); })
        { Unit = "%", Persist = false, Description = "100% = solid." });
        Add("Layout", new ToggleSetting("labels", "Show labels under icons", c.Labels, v => { c.Labels = v; return Save(); }) { Persist = false });

        Add("Behaviour", new ToggleSetting("ontop", "Always on top", c.AlwaysOnTop, v => { c.AlwaysOnTop = v; return Save(); })
        { Persist = false, Description = "Stays above every window until you minimise or hide it." });
        Add("Behaviour", new TextSetting("hotkey", "Show / hide shortcut", c.Hotkey, v => { c.Hotkey = v.Trim(); DeckStore.Save(false); HotkeyChanged?.Invoke(); return Task.CompletedTask; })
        { Persist = false, Hint = "e.g. Ctrl+Alt+D, Ctrl+Shift+F12. Leave empty for none." });
        Add("Behaviour", new ToggleSetting("follow", "Switch pages automatically with the app you are using", c.FollowApp, v => { c.FollowApp = v; return Save(); })
        { Persist = false, Description = "Give a page an app below (e.g. spotify.exe). When you leave that app the deck goes back to the page you were on." });
        Add("Behaviour", new ToggleSetting("autohide", "Auto-hide at the screen edge", c.AutoHide, v => { c.AutoHide = v; return Save(); })
        {
            Persist = false,
            Description = "Drag the deck against the edge of a screen (it snaps there). When the mouse leaves it slides away, " +
                          "leaving a thin strip; touch the strip and it slides back.",
        });
        Add("Behaviour", new InfoSetting("screens", "Multiple screens",
            "The deck remembers a separate position for every screen setup (laptop only, laptop + monitor, …). " +
            "If the screen it was on is unplugged, it moves back onto one you can see."));

        _pages = Add("Pages", new InfoSetting("pages", "Pages"));
        _pageName = Add("Pages", new TextSetting("pagename", "Name of the current page", "", v =>
        {
            var p = DeckStore.CurrentPage;
            if (v.Trim().Length > 0 && p.Name != v.Trim()) { p.Name = v.Trim(); DeckStore.Save(); }
            return Task.CompletedTask;
        }) { Persist = false });
        _pageApp = Add("Pages", new TextSetting("pageapp", "Open this page automatically for app", "", v =>
        {
            DeckStore.CurrentPage.App = string.IsNullOrWhiteSpace(v) ? null : v.Trim().ToLowerInvariant();
            DeckStore.Save(false);
            return Task.CompletedTask;
        }) { Persist = false, Hint = "Program name, e.g. spotify.exe, obs64.exe, chrome.exe" });
        Add("Pages", new CommandSetting("pagecmds", "Manage pages",
            new CommandItem("Add page", () =>
            {
                var cfg = DeckStore.Config;
                cfg.Pages.Add(new DeckPageModel { Name = $"Page {cfg.Pages.Count + 1}" });
                cfg.Page = cfg.Pages.Count - 1;
                DeckStore.Save(); Refresh();
                return Task.CompletedTask;
            }),
            new CommandItem("Duplicate page", () =>
            {
                var cfg = DeckStore.Config;
                var cur = DeckStore.CurrentPage;
                cfg.Pages.Insert(cfg.Page + 1, new DeckPageModel { Name = cur.Name + " copy", Buttons = cur.Buttons.ToDictionary(k => k.Key, k => k.Value.Clone()) });
                cfg.Page++;
                DeckStore.Save(); Refresh();
                return Task.CompletedTask;
            }),
            new CommandItem("Previous page", () => { var cfg = DeckStore.Config; cfg.Page = (cfg.Page - 1 + cfg.Pages.Count) % cfg.Pages.Count; DeckStore.Save(); Refresh(); return Task.CompletedTask; }),
            new CommandItem("Next page", () => { var cfg = DeckStore.Config; cfg.Page = (cfg.Page + 1) % cfg.Pages.Count; DeckStore.Save(); Refresh(); return Task.CompletedTask; }),
            new CommandItem("Delete current page", () =>
            {
                var cfg = DeckStore.Config;
                if (cfg.Pages.Count <= 1) throw new InvalidOperationException("The deck needs at least one page.");
                cfg.Pages.RemoveAt(cfg.Page);
                cfg.Page = Math.Max(0, cfg.Page - 1);
                DeckStore.Save(); Refresh();
                return Task.CompletedTask;
            })));

        BuildPhone();
        BuildObs();
        BuildDiscord();

        DeckStore.Changed += Refresh;
        Refresh();
    }

    // ------------------------------------------------------------------ phone
    private ImageSetting? _qr;
    private InfoSetting? _phoneStatus;

    private void BuildPhone()
    {
        var c = DeckStore.Config;
        Add("Phone deck (Wi-Fi)", new ToggleSetting("phone", "Use my phone as a deck", c.PhoneEnabled, v =>
        {
            c.PhoneEnabled = v; DeckStore.Save(false);
            PhoneServer.Instance.Apply();
            UpdatePhone();
            return Task.CompletedTask;
        })
        {
            Persist = false,
            Description = "Your phone shows the same buttons. Phone and PC must be on the same Wi-Fi / home network. " +
                          "Tip: add the page to your home screen for a full-screen app.",
        });
        _phoneStatus = Add("Phone deck (Wi-Fi)", new InfoSetting("phonestatus", "Status"));
        _qr = Add("Phone deck (Wi-Fi)", new ImageSetting("phoneqr", "Scan with your phone's camera")
        { Description = "The link contains a secret code, so only phones that scanned it can press your buttons." });
        Add("Phone deck (Wi-Fi)", new CommandSetting("phonecmds", "Pairing",
            new CommandItem("New pairing code (disconnects paired phones)", () =>
            {
                c.PhoneToken = PhoneServer.NewToken(); DeckStore.Save(false);
                PhoneServer.Instance.Apply(); UpdatePhone();
                return Task.CompletedTask;
            }),
            new CommandItem("Copy link", () =>
            {
                if (PhoneServer.Instance.Url is { } u) System.Windows.Clipboard.SetText(u);
                return Task.CompletedTask;
            })));
        PhoneServer.Instance.StatusChanged += () => OnUi(UpdatePhone);
    }

    private void UpdatePhone()
    {
        if (_phoneStatus == null || _qr == null) return;
        _phoneStatus.Text = PhoneServer.Instance.Status;
        var url = DeckStore.Config.PhoneEnabled ? PhoneServer.Instance.Url : null;
        if (url == null) { _qr.Image = null; return; }
        try
        {
            using var gen = new QRCoder.QRCodeGenerator();
            using var data = gen.CreateQrCode(url, QRCoder.QRCodeGenerator.ECCLevel.M);
            var png = new QRCoder.PngByteQRCode(data).GetGraphic(8);
            var img = new System.Windows.Media.Imaging.BitmapImage();
            img.BeginInit(); img.StreamSource = new MemoryStream(png); img.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad; img.EndInit();
            img.Freeze();
            _qr.Image = img;
        }
        catch (Exception ex) { Log.Warn($"QR code: {ex.Message}"); }
    }

    // ------------------------------------------------------------------ OBS
    private void BuildObs()
    {
        var c = DeckStore.Config;
        var status = Add("OBS", new InfoSetting("obsstatus", "Connection", Obs.Instance.Status)
        {
            Description = "In OBS: Tools › WebSocket Server Settings › tick “Enable WebSocket server”. " +
                          "Then use the OBS actions on deck buttons (scenes, record, stream, replay, mute).",
        });
        Add("OBS", new TextSetting("obshost", "OBS computer", c.ObsHost, v => { c.ObsHost = v.Trim().Length == 0 ? "localhost" : v.Trim(); DeckStore.Save(false); Obs.Instance.Disconnect(); return Task.CompletedTask; })
        { Persist = false, Hint = "localhost = this PC" });
        Add("OBS", new RangeSetting("obsport", "Port", 1024, 65535, 1, c.ObsPort, v => { c.ObsPort = v; DeckStore.Save(false); Obs.Instance.Disconnect(); return Task.CompletedTask; }) { Persist = false });
        Add("OBS", new CommandSetting("obscmds", "Password & test",
            new CommandItem("Set OBS password…", () =>
            {
                var pw = AskPassword();
                if (pw != null) { Obs.Password = pw; Obs.Instance.Disconnect(); status.Text = "Password saved (encrypted)"; }
                return Task.CompletedTask;
            }),
            new CommandItem("Test connection", async () => status.Text = await Obs.Instance.TestAsync()))
        { Description = "Copy the password from OBS › WebSocket Server Settings › Show Connect Info." });
    }

    private static string? AskPassword()
    {
        var box = new System.Windows.Controls.PasswordBox { Margin = new System.Windows.Thickness(0, 8, 0, 14), MinWidth = 280 };
        var ok = new System.Windows.Controls.Button { Content = "Save", IsDefault = true, Padding = new System.Windows.Thickness(16, 5, 16, 5) };
        var panel = new System.Windows.Controls.StackPanel { Margin = new System.Windows.Thickness(20) };
        panel.Children.Add(new System.Windows.Controls.TextBlock { Text = "OBS WebSocket password:" });
        panel.Children.Add(box);
        panel.Children.Add(ok);
        var w = new System.Windows.Window
        {
            Title = "OBS password", Content = panel, SizeToContent = System.Windows.SizeToContent.WidthAndHeight,
            WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen, ResizeMode = System.Windows.ResizeMode.NoResize,
        };
        ok.Click += (_, _) => w.DialogResult = true;
        box.Loaded += (_, _) => box.Focus();
        return w.ShowDialog() == true ? box.Password : null;
    }

    // ------------------------------------------------------------------ Discord
    private void BuildDiscord()
    {
        var c = DeckStore.Config;
        Add("Discord", new InfoSetting("discordhow", "How it works",
            "Discord only lets its partner apps control voice directly, so PeriphHub presses Discord's own shortcuts. " +
            "In Discord › Settings › Keybinds, add “Toggle Mute” and “Toggle Deafen” with the same keys as below."));
        Add("Discord", new TextSetting("dmute", "Toggle mute shortcut", c.DiscordMuteKey, v => { c.DiscordMuteKey = v.Trim(); DeckStore.Save(false); return Task.CompletedTask; })
        { Persist = false, Hint = "Default Ctrl+Shift+M" });
        Add("Discord", new TextSetting("ddeafen", "Toggle deafen shortcut", c.DiscordDeafenKey, v => { c.DiscordDeafenKey = v.Trim(); DeckStore.Save(false); return Task.CompletedTask; })
        { Persist = false, Hint = "Default Ctrl+Shift+D" });
    }

    public override string Glyph => "";

    public void Refresh()
    {
        var cfg = DeckStore.Config;
        var cur = DeckStore.CurrentPage;
        _pages.Text = string.Join("\n", cfg.Pages.Select((p, i) => (p, i)).Where(x => !x.p.IsFolder || x.i == cfg.Page).Select(x => (x.p, x.i)).Select(t =>
            $"{(t.i == cfg.Page ? "▶ " : "   ")}{(t.p.IsFolder ? "📁 " : "")}{t.p.Name}{(string.IsNullOrEmpty(t.p.App) ? "" : $"   (auto: {t.p.App})")}"));
        _pageName.SetFromDevice(cur.Name);
        _pageApp.SetFromDevice(cur.App ?? "");
        _visible.SetFromDevice(cfg.Visible);
        UpdatePhone();
    }
}
