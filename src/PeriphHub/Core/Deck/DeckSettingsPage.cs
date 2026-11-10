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

        DeckStore.Changed += Refresh;
        Refresh();
    }

    public override string Glyph => "";

    public void Refresh()
    {
        var cfg = DeckStore.Config;
        var cur = DeckStore.CurrentPage;
        _pages.Text = string.Join("\n", cfg.Pages.Select((p, i) =>
            $"{(i == cfg.Page ? "▶ " : "   ")}{p.Name}{(string.IsNullOrEmpty(p.App) ? "" : $"   (auto: {p.App})")}"));
        _pageName.SetFromDevice(cur.Name);
        _pageApp.SetFromDevice(cur.App ?? "");
        _visible.SetFromDevice(cfg.Visible);
    }
}
