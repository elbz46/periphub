namespace PeriphHub.Core.Deck;

/// <summary>One step of a multi-action button.</summary>
public sealed class DeckStep
{
    public string Id { get; set; } = "default";
    public string? Arg { get; set; }
    public int DelayMs { get; set; } = 100;
    public ActionSpec ToSpec() => new() { Id = Id, Arg = Arg };
}

public sealed class DeckButton
{
    public string Label { get; set; } = "";
    /// <summary>Emoji or short text shown large on the button.</summary>
    public string Icon { get; set; } = "";
    public string? ImagePath { get; set; }
    public string Color { get; set; } = "#2B2F3A";
    /// <summary>"" = normal button; otherwise: nowplaying, clock, battery, micmute, volume, timer, stopwatch, folder.</summary>
    public string Tile { get; set; } = "";
    /// <summary>battery: device key · timer: seconds · folder: name of its sub-page.</summary>
    public string? TileArg { get; set; }
    public ActionSpec Action { get; set; } = new() { Id = "default" };
    public List<DeckStep> Steps { get; set; } = new();

    public bool IsEmpty => Label.Length == 0 && Icon.Length == 0 && ImagePath == null && Tile.Length == 0 && Action.IsDefault && Steps.Count == 0;

    public DeckButton Clone() => new()
    {
        Label = Label, Icon = Icon, ImagePath = ImagePath, Color = Color, Tile = Tile, TileArg = TileArg,
        Action = Action.Clone(), Steps = Steps.Select(s => new DeckStep { Id = s.Id, Arg = s.Arg, DelayMs = s.DelayMs }).ToList(),
    };
}

public sealed class DeckPageModel
{
    public string Name { get; set; } = "Main";
    /// <summary>Program (e.g. spotify.exe) that brings this page up automatically.</summary>
    public string? App { get; set; }
    /// <summary>Sub-page opened by a folder button (not part of the normal page cycle; cell 0 is "Back").</summary>
    public bool IsFolder { get; set; }
    public Dictionary<int, DeckButton> Buttons { get; set; } = new();
}

public sealed class DeckConfig
{
    public int Rows { get; set; } = 3;
    public int Cols { get; set; } = 5;
    public int ButtonSize { get; set; } = 76;
    public int Opacity { get; set; } = 96;
    public bool Labels { get; set; } = true;
    public bool AlwaysOnTop { get; set; } = true;
    public bool FollowApp { get; set; }
    public string Hotkey { get; set; } = "Ctrl+Alt+D";
    public bool Visible { get; set; } = true;
    public int Page { get; set; }
    public List<DeckPageModel> Pages { get; set; } = new();
    /// <summary>Window position (screen pixels) per monitor layout, so laptop-only and dual-screen each keep their own spot.</summary>
    public Dictionary<string, int[]> Positions { get; set; } = new();

    /// <summary>Slide away at the screen edge, leaving a thin strip; comes back when the mouse touches it.</summary>
    public bool AutoHide { get; set; }

    /// <summary>Layout version, used to add new starter buttons to existing decks once.</summary>
    public int Version { get; set; }

    // Phone deck (Wi-Fi)
    public bool PhoneEnabled { get; set; }
    public int PhonePort { get; set; } = 8770;
    public string PhoneToken { get; set; } = "";

    // OBS (obs-websocket 5, built into OBS 28+)
    public string ObsHost { get; set; } = "localhost";
    public int ObsPort { get; set; } = 4455;
    /// <summary>Encrypted with Windows DPAPI (current user).</summary>
    public string? ObsPasswordProtected { get; set; }

    // Discord keybinds PeriphHub presses (must match Discord › Settings › Keybinds)
    public string DiscordMuteKey { get; set; } = "Ctrl+Shift+M";
    public string DiscordDeafenKey { get; set; } = "Ctrl+Shift+D";
}

public static class DeckStore
{
    private static DeckConfig? _config;
    public static event Action? Changed;

    public static DeckConfig Config
    {
        get
        {
            if (_config != null) return _config;
            _config = SettingsStore.Instance.GetApp<DeckConfig>("deck");
            _config ??= Defaults();
            if (_config.Pages.Count == 0) _config.Pages = Defaults().Pages;
            Upgrade(_config);
            return _config;
        }
    }

    /// <summary>Normal pages (no folders) in order, for the ‹ › arrows.</summary>
    public static List<int> MainPages => Config.Pages.Select((p, i) => (p, i)).Where(x => !x.p.IsFolder).Select(x => x.i).ToList();

    /// <summary>Gets (or creates) the sub-page for a folder button.</summary>
    public static int FolderPage(string name)
    {
        var i = Config.Pages.FindIndex(p => p.IsFolder && p.Name == name);
        if (i >= 0) return i;
        Config.Pages.Add(new DeckPageModel { Name = name, IsFolder = true });
        return Config.Pages.Count - 1;
    }

    public static DeckPageModel CurrentPage
    {
        get
        {
            var c = Config;
            c.Page = Math.Clamp(c.Page, 0, c.Pages.Count - 1);
            return c.Pages[c.Page];
        }
    }

    public static void Save(bool notify = true)
    {
        SettingsStore.Instance.SetApp("deck", Config);
        if (notify) Changed?.Invoke();
    }

    public static void Reset()
    {
        var keepPos = Config.Positions;
        _config = Defaults();
        _config.Positions = keepPos;
        Save();
    }

    private static DeckButton B(string icon, string label, string action, string? arg = null, string color = "#2B2F3A") =>
        new() { Icon = icon, Label = label, Action = new ActionSpec { Id = action, Arg = arg }, Color = color };

    private static DeckButton T(string tile, string label, string color = "#23262E") => new() { Tile = tile, Label = label, Color = color };

    public static DeckConfig Defaults()
    {
        var main = new DeckPageModel { Name = "Main" };
        var b = new[]
        {
            T("nowplaying", "Now playing", "#1E3A2B"), B("⏮", "Previous", "deck:prev"), B("⏯", "Play / Pause", "deck:playpause", color: "#1F4D36"),
            B("⏭", "Next", "deck:next"), T("volume", "Volume"),
            T("micmute", "Mic"), B("🎧", "Switch output", "deck:outputcycle"), B("📸", "Screenshot", "win:snip"),
            B("🖥", "Desktop", "win:desktop"), T("clock", "Clock"),
            B("📁", "Explorer", "win:explorer"), B("🧮", "Calculator", "launch", "calc.exe"), B("📊", "Task Manager", "deck:taskmgr"),
            B("🔒", "Lock", "win:lock", color: "#4A2430"), B("➡", "Next page", "deck:nextpage"),
        };
        for (int i = 0; i < b.Length; i++) main.Buttons[i] = b[i];

        var media = new DeckPageModel { Name = "Media" };
        var m = new[]
        {
            T("nowplaying", "Now playing", "#1E3A2B"), B("⏮", "Previous", "deck:prev"), B("⏯", "Play / Pause", "deck:playpause", color: "#1F4D36"),
            B("⏭", "Next", "deck:next"), T("volume", "Volume"),
            B("🔉", "Vol −", "deck:voldown"), B("🔊", "Vol +", "deck:volup"), B("🔇", "Mute", "deck:outmute"),
            B("🔀", "Shuffle", "key", "Ctrl+S"), B("⬅", "Main page", "deck:page", "Main"),
        };
        for (int i = 0; i < m.Length; i++) media.Buttons[i] = m[i];

        var tools = new DeckPageModel { Name = "Tools" };
        var t = new[]
        {
            new DeckButton { Tile = "timer", TileArg = "300", Label = "5 min", Color = "#3F2A5C" },
            new DeckButton { Tile = "timer", TileArg = "1500", Label = "Focus 25", Color = "#3F2A5C" },
            new DeckButton { Tile = "stopwatch", Label = "Stopwatch", Color = "#1E3A5F" },
            B("🎙", "Discord mute", "discord:mute", color: "#3B3F8F"), B("🎧", "Discord deafen", "discord:deafen", color: "#3B3F8F"),
        };
        for (int i = 0; i < t.Length; i++) tools.Buttons[i] = t[i];
        var pages = new List<DeckPageModel> { main, media, tools };
        AddV2(pages);
        AddV3(pages);
        return new DeckConfig { Pages = pages, Version = 3 };
    }

    private static DeckButton Folder(string name, string icon, string color) =>
        new() { Tile = "folder", TileArg = name, Icon = icon, Label = name, Color = color };

    /// <summary>ShareX, Scenes and Stats folders + Spotify like on the Tools page (also used to upgrade older decks).</summary>
    private static void AddV2(List<DeckPageModel> pages)
    {
        var tools = pages.FirstOrDefault(p => p.Name == "Tools" && !p.IsFolder);
        if (tools == null) { tools = new DeckPageModel { Name = "Tools" }; pages.Add(tools); }

        if (!pages.Any(p => p.IsFolder && p.Name == "ShareX"))
        {
            var sx = new DeckPageModel { Name = "ShareX", IsFolder = true };
            var x = new[]
            {
                B("✂", "Region", "sharex:RectangleRegion", color: "#1E3A5F"), B("🖥", "Full screen", "sharex:PrintScreen"), B("🪟", "Window", "sharex:ActiveWindow"),
                B("📜", "Scrolling", "sharex:ScrollingCapture"), B("🎥", "Record video", "sharex:ScreenRecorder", color: "#7A1F1F"),
                B("🎞", "Record GIF", "sharex:ScreenRecorderGIF", color: "#7A1F1F"), B("⏹", "Stop rec", "sharex:StopScreenRecording", color: "#5C1A16"),
                B("🔤", "Grab text", "sharex:OCR"), B("🎨", "Colour pick", "sharex:ScreenColorPicker"), B("📂", "Screenshots", "sharex:OpenScreenshotsFolder"),
            };
            for (int i = 0; i < x.Length; i++) sx.Buttons[i + 1] = x[i];
            pages.Add(sx);
        }
        if (!pages.Any(p => p.IsFolder && p.Name == "Scenes"))
        {
            var sc = new DeckPageModel { Name = "Scenes", IsFolder = true };
            var x = new[]
            {
                B("🎮", "Gaming", "scene:run", "Gaming", "#7A1F1F"), B("💼", "Work", "scene:run", "Work", "#1E3A5F"),
                B("🌙", "Night", "scene:run", "Night", "#3F2A5C"), B("🚪", "Away", "scene:run", "Away", "#3A3F4C"),
            };
            for (int i = 0; i < x.Length; i++) sc.Buttons[i + 1] = x[i];
            pages.Add(sc);
        }
        if (!pages.Any(p => p.IsFolder && p.Name == "Stats"))
        {
            var st = new DeckPageModel { Name = "Stats", IsFolder = true };
            var kinds = new[] { "cputemp", "gputemp", "cpufan", "gpufan", "cpu", "ram", "battery" };
            for (int i = 0; i < kinds.Length; i++) st.Buttons[i + 1] = new DeckButton { Tile = "stat", TileArg = kinds[i], Color = "#23262E" };
            pages.Add(st);
        }

        // OBS isn't used: swap an OBS folder button on Tools for ShareX.
        foreach (var kv in tools.Buttons.Where(b => b.Value.Tile == "folder" && b.Value.TileArg == "OBS").ToList())
            tools.Buttons[kv.Key] = Folder("ShareX", "📸", "#1E3A5F");

        void Place(DeckButton b, Func<DeckButton, bool> exists)
        {
            if (tools.Buttons.Values.Any(exists)) return;
            for (int i = 0; i < 15; i++) if (!tools.Buttons.ContainsKey(i)) { tools.Buttons[i] = b; return; }
        }
        Place(Folder("ShareX", "📸", "#1E3A5F"), b => b.Tile == "folder" && b.TileArg == "ShareX");
        Place(Folder("Scenes", "✨", "#5B3A12"), b => b.Tile == "folder" && b.TileArg == "Scenes");
        Place(Folder("Stats", "🌡", "#0F5F5F"), b => b.Tile == "folder" && b.TileArg == "Stats");
        Place(B("♥", "Like song", "spotify:like", color: "#1F4D36"), b => b.Action.Id == "spotify:like");
        Place(B("🔊", "Speakers ↔ headset", "deck:outputcycle"), b => b.Action.Id == "deck:outputcycle");
        Place(T("micmute", "Mic"), b => b.Tile == "micmute");
        Place(T("clock", "Clock"), b => b.Tile == "clock");
        Place(B("⬅", "Main page", "deck:page", "Main"), b => b.Action.Id == "deck:page" && b.Action.Arg == "Main");
    }

    /// <summary>YouTube Music instead of Spotify: like/dislike/shuffle/repeat/open buttons.</summary>
    private static void AddV3(List<DeckPageModel> pages)
    {
        foreach (var p in pages)
            foreach (var kv in p.Buttons.Where(b => b.Value.Action.Id == "spotify:like").ToList())
                p.Buttons[kv.Key] = B("👍", "Like", "ytm:like", color: "#5C1A16");

        var media = pages.FirstOrDefault(p => p.Name == "Media" && !p.IsFolder);
        if (media == null) return;
        foreach (var kv in media.Buttons.Where(b => b.Value.Action.Id == "key" && b.Value.Action.Arg == "Ctrl+S").ToList())
            media.Buttons[kv.Key] = B("🔀", "Shuffle", "ytm:shuffle");
        void Place(DeckButton b)
        {
            if (media.Buttons.Values.Any(x => x.Action.Id == b.Action.Id)) return;
            for (int i = 0; i < 15; i++) if (!media.Buttons.ContainsKey(i)) { media.Buttons[i] = b; return; }
        }
        Place(B("🎵", "YT Music", "ytm:open", color: "#7A1F1F"));
        Place(B("👍", "Like", "ytm:like", color: "#5C1A16"));
        Place(B("👎", "Dislike", "ytm:dislike"));
        Place(B("🔁", "Repeat", "ytm:repeat"));
        if (media.App == "spotify.exe") media.App = null;
    }

    /// <summary>Brings decks made by older versions up to date without touching your own buttons.</summary>
    private static void Upgrade(DeckConfig c)
    {
        if (c.Version >= 3) return;
        if (c.Version < 2) AddV2(c.Pages);
        AddV3(c.Pages);
        c.Version = 3;
        SettingsStore.Instance.SetApp("deck", c);
    }
}
