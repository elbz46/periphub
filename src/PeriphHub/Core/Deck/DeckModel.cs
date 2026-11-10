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
    /// <summary>"" = normal button; otherwise a live tile: nowplaying, clock, battery, micmute, volume.</summary>
    public string Tile { get; set; } = "";
    /// <summary>For the battery tile: the device key.</summary>
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
            if (_config == null || _config.Pages.Count == 0) _config = Defaults();
            return _config;
        }
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

        var media = new DeckPageModel { Name = "Media", App = "spotify.exe" };
        var m = new[]
        {
            T("nowplaying", "Now playing", "#1E3A2B"), B("⏮", "Previous", "deck:prev"), B("⏯", "Play / Pause", "deck:playpause", color: "#1F4D36"),
            B("⏭", "Next", "deck:next"), T("volume", "Volume"),
            B("🔉", "Vol −", "deck:voldown"), B("🔊", "Vol +", "deck:volup"), B("🔇", "Mute", "deck:outmute"),
            B("🔀", "Shuffle", "key", "Ctrl+S"), B("⬅", "Main page", "deck:page", "Main"),
        };
        for (int i = 0; i < m.Length; i++) media.Buttons[i] = m[i];

        return new DeckConfig { Pages = new() { main, media } };
    }
}
