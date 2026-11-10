using PeriphHub.Core.Deck;

namespace PeriphHub.Core.Scenes;

public sealed class SceneModel
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "New scene";
    public string Icon { get; set; } = "✨";
    /// <summary>manual, app, docked, undocked, plugged, battery, headset-on, headset-off</summary>
    public string Trigger { get; set; } = "manual";
    /// <summary>For "app": program name, e.g. valorant.exe</summary>
    public string? TriggerArg { get; set; }
    /// <summary>For "app": scene to run when the program closes.</summary>
    public string? OnExitScene { get; set; }
    public bool Enabled { get; set; } = true;
    public List<DeckStep> Steps { get; set; } = new();

    public static readonly (string id, string label)[] Triggers =
    {
        ("manual", "Only when I run it (button, deck, phone)"),
        ("app", "When a program starts"),
        ("docked", "When a second screen is connected"),
        ("undocked", "When it's back to the laptop screen only"),
        ("plugged", "When the charger is plugged in"),
        ("battery", "When running on battery"),
        ("headset-on", "When a headset connects"),
        ("headset-off", "When a headset disconnects"),
        ("startup", "When PeriphHub starts"),
    };

    public string TriggerText => Trigger == "app"
        ? $"When {TriggerArg} starts{(string.IsNullOrEmpty(OnExitScene) ? "" : $" · on close: {OnExitScene}")}"
        : Triggers.FirstOrDefault(t => t.id == Trigger).label ?? Trigger;
}

public static class SceneStore
{
    private static List<SceneModel>? _scenes;
    public static event Action? Changed;

    public static List<SceneModel> Scenes
    {
        get
        {
            if (_scenes != null) return _scenes;
            _scenes = SettingsStore.Instance.GetApp<List<SceneModel>>("scenes");
            if (_scenes == null) { _scenes = Starters(); Save(false); }
            return _scenes;
        }
    }

    public static void Save(bool notify = true)
    {
        SettingsStore.Instance.SetApp("scenes", Scenes);
        if (notify) Changed?.Invoke();
    }

    private static DeckStep S(string id, string? arg = null, int delay = 50) => new() { Id = id, Arg = arg, DelayMs = delay };

    public static List<SceneModel> Starters() => new()
    {
        new SceneModel
        {
            Name = "Gaming", Icon = "🎮",
            Steps =
            {
                S("laptop:mode", "Turbo"), S("laptop:kbeffect", "Static"), S("laptop:kbcolor", "#FF0000"), S("laptop:kbbright", "High"),
                S("sound:output", "G321"), S("sound:input", "G321"), S("mouse:dpi", "1600"), S("deck:page", "Tools"),
            },
        },
        new SceneModel
        {
            Name = "Work", Icon = "💼",
            Steps = { S("laptop:mode", "Performance"), S("laptop:kbeffect", "Static"), S("laptop:kbcolor", "#FFFFFF"), S("laptop:kbbright", "Low"), S("mouse:dpi", "1000"), S("deck:page", "Main") },
        },
        new SceneModel
        {
            Name = "Night", Icon = "🌙",
            Steps = { S("laptop:mode", "Silent"), S("laptop:kbbright", "Off"), S("sound:volume", "25") },
        },
        new SceneModel
        {
            Name = "Away", Icon = "🚪",
            Steps = { S("media:pausecurrent"), S("sound:micmute", "on"), S("laptop:kbbright", "Off"), S("win:lock") },
        },
        new SceneModel
        {
            Name = "Docked", Icon = "🖥", Trigger = "docked", Enabled = false,
            Steps = { S("laptop:mode", "Performance"), S("laptop:chargelimit", "80"), S("sound:output", "G321") },
        },
        new SceneModel
        {
            Name = "Laptop only", Icon = "💻", Trigger = "undocked", Enabled = false,
            Steps = { S("laptop:mode", "Silent"), S("sound:output", "Speakers") },
        },
        new SceneModel
        {
            Name = "On battery", Icon = "🔋", Trigger = "battery", Enabled = false,
            Steps = { S("laptop:mode", "Silent"), S("laptop:kbbright", "Low") },
        },
        new SceneModel
        {
            Name = "Plugged in", Icon = "🔌", Trigger = "plugged", Enabled = false,
            Steps = { S("laptop:mode", "Performance"), S("laptop:kbbright", "High") },
        },
    };
}
