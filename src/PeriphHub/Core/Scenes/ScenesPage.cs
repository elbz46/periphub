using System.Collections.ObjectModel;

namespace PeriphHub.Core.Scenes;

public sealed class SceneRow : Observable
{
    public SceneRow(SceneModel m) { Model = m; }
    public SceneModel Model { get; }
    public string Name => Model.Name;
    public string Icon => Model.Icon;
    public string TriggerText => Model.TriggerText;
    public string StepsText => Model.Steps.Count == 1 ? "1 step" : $"{Model.Steps.Count} steps";
    public bool IsAuto => Model.Trigger != "manual";
    public bool Enabled
    {
        get => Model.Enabled;
        set { if (Model.Enabled == value) return; Model.Enabled = value; Raise(); SceneStore.Save(false); }
    }
}

/// <summary>List of scenes with run / edit / delete; rendered by a template in the main window.</summary>
public sealed class SceneListSetting : DeviceSetting
{
    public SceneListSetting() : base("scenes", "Your scenes") { Persist = false; }
    public ObservableCollection<SceneRow> Rows { get; } = new();
    public void Reload()
    {
        void Do() { Rows.Clear(); foreach (var s in SceneStore.Scenes) Rows.Add(new SceneRow(s)); }
        var d = System.Windows.Application.Current?.Dispatcher;
        if (d == null || d.CheckAccess()) Do(); else d.Invoke(Do);
    }
}

public sealed class ScenesPage : ManagedDevice
{
    private readonly SceneListSetting _list;
    private readonly InfoSetting _last;

    public ScenesPage() : base("app:scenes", "Scenes & automation", "", DeviceKind.Other)
    {
        Transport = "One switch for your whole setup";
        _list = Add("Scenes", new SceneListSetting
        {
            Description = "A scene changes several things at once: fan mode, keyboard lighting, sound output, mouse DPI, deck page, " +
                          "Home Assistant, Spotify… Run one from here, a deck/phone button (“Run scene…”), a mouse button, or let it start by itself.",
        });
        Add("Scenes", new CommandSetting("scenecmds", "Manage",
            new CommandItem("New scene…", () => { EditRequested?.Invoke(null); return Task.CompletedTask; }),
            new CommandItem("Add the starter scenes again", () =>
            {
                foreach (var s in SceneStore.Starters().Where(s => SceneStore.Scenes.All(x => x.Name != s.Name))) SceneStore.Scenes.Add(s);
                SceneStore.Save();
                return Task.CompletedTask;
            })));
        _last = Add("Scenes", new InfoSetting("last", "Last scene", "—"));
        Add("Tips", new InfoSetting("tips", "Automatic scenes",
            "Starter scenes for docked / laptop only / battery / plugged in are switched off: tick “On” to use them, " +
            "and check their sound device names match yours (e.g. G321, Speakers).\n" +
            "For games: edit Gaming, choose “When a program starts” and type the game's .exe; set “Work” as the scene to run when it closes."));

        Add("Headset", new ToggleSetting("hs.auto", "When a headset connects, switch Windows sound and mic to it",
            Integrations.Secrets.Config.HeadsetAutoSwitch, v => { Integrations.Secrets.Config.HeadsetAutoSwitch = v; Integrations.Secrets.Save(); return Task.CompletedTask; }) { Persist = false });
        Add("Headset", new ToggleSetting("hs.back", "When it disconnects, switch back to what was used before",
            Integrations.Secrets.Config.HeadsetSwitchBack, v => { Integrations.Secrets.Config.HeadsetSwitchBack = v; Integrations.Secrets.Save(); return Task.CompletedTask; }) { Persist = false });
        Add("Headset", new ToggleSetting("hs.pause", "Pause music when the headset disconnects",
            Integrations.Secrets.Config.HeadsetPauseOnDisconnect, v => { Integrations.Secrets.Config.HeadsetPauseOnDisconnect = v; Integrations.Secrets.Save(); return Task.CompletedTask; }) { Persist = false });

        SceneStore.Changed += _list.Reload;
        SceneEngine.Instance.SceneRan += n => _last.Text = $"{n} at {DateTime.Now:HH:mm}";
        _list.Reload();
    }

    public override string Glyph => "";

    /// <summary>Raised to open the editor (null = new scene).</summary>
    public static event Action<SceneModel?>? EditRequested;
    public static void RequestEdit(SceneModel? m) => EditRequested?.Invoke(m);
}
