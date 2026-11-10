using System.Diagnostics;

namespace PeriphHub.Core;

/// <summary>What a button / key does. Serialised into settings.json.</summary>
public sealed class ActionSpec
{
    public string Id { get; set; } = "default";
    /// <summary>Keystroke ("Ctrl+C"), text to type, or program path depending on <see cref="Id"/>.</summary>
    public string? Arg { get; set; }

    public static ActionSpec Default => new() { Id = "default" };
    public bool IsDefault => Id == "default";
    public ActionSpec Clone() => new() { Id = Id, Arg = Arg };
    public override string ToString() => Arg == null ? Id : $"{Id}({Arg})";
}

public sealed record ActionDef(string Id, string Label, string Category, string? Combo = null, bool NeedsArg = false, string? ArgHint = null);

/// <summary>Implemented by devices that offer their own actions (DPI cycle, gestures, wheel mode...).</summary>
public interface IDeviceActionHandler
{
    void DeviceAction(string id, bool pressed);
}

public static class ActionCatalog
{
    public static readonly ActionDef Default = new("default", "Default (what the button normally does)", "General");
    public static readonly ActionDef Disabled = new("disabled", "Do nothing (disable)", "General");

    public static IReadOnlyList<ActionDef> Common { get; } = new ActionDef[]
    {
        Default,
        Disabled,
        new("key", "Keystroke…", "General", NeedsArg: true, ArgHint: "e.g. Ctrl+Shift+T, Win+Tab, F13"),
        new("text", "Type text…", "General", NeedsArg: true, ArgHint: "Text to type (\\n for Enter)"),
        new("launch", "Open program / file / website…", "General", NeedsArg: true, ArgHint: @"C:\path\app.exe or https://…"),

        new("mouse:left", "Left click", "Mouse"),
        new("mouse:right", "Right click", "Mouse"),
        new("mouse:middle", "Middle click", "Mouse"),
        new("mouse:back", "Back", "Mouse"),
        new("mouse:forward", "Forward", "Mouse"),
        new("scroll:left", "Scroll left", "Mouse"),
        new("scroll:right", "Scroll right", "Mouse"),

        new("media:playpause", "Play / Pause", "Media", "PlayPause"),
        new("media:next", "Next track", "Media", "NextTrack"),
        new("media:prev", "Previous track", "Media", "PrevTrack"),
        new("media:stop", "Stop", "Media", "Stop"),
        new("media:volup", "Volume up", "Media", "VolumeUp"),
        new("media:voldown", "Volume down", "Media", "VolumeDown"),
        new("media:mute", "Mute", "Media", "Mute"),

        new("win:taskview", "Task View", "Windows", "Win+Tab"),
        new("win:desktop", "Show desktop", "Windows", "Win+D"),
        new("win:deskleft", "Previous virtual desktop", "Windows", "Ctrl+Win+Left"),
        new("win:deskright", "Next virtual desktop", "Windows", "Ctrl+Win+Right"),
        new("win:snip", "Screenshot (Snipping Tool)", "Windows", "Win+Shift+S"),
        new("win:explorer", "File Explorer", "Windows", "Win+E"),
        new("win:search", "Search", "Windows", "Win+S"),
        new("win:action", "Quick settings", "Windows", "Win+A"),
        new("win:notifications", "Notifications", "Windows", "Win+N"),
        new("win:emoji", "Emoji panel", "Windows", "Win+."),
        new("win:lock", "Lock PC", "Windows", "Win+L"),
        new("win:dictate", "Voice typing", "Windows", "Win+H"),
        new("win:alttab", "Switch app (Alt+Tab)", "Windows", "Alt+Tab"),
        new("win:close", "Close window", "Windows", "Alt+F4"),
        new("win:minimize", "Minimize window", "Windows", "Win+Down"),
        new("win:maximize", "Maximize window", "Windows", "Win+Up"),

        new("edit:copy", "Copy", "Editing", "Ctrl+C"),
        new("edit:cut", "Cut", "Editing", "Ctrl+X"),
        new("edit:paste", "Paste", "Editing", "Ctrl+V"),
        new("edit:undo", "Undo", "Editing", "Ctrl+Z"),
        new("edit:redo", "Redo", "Editing", "Ctrl+Y"),
        new("edit:selectall", "Select all", "Editing", "Ctrl+A"),
        new("edit:save", "Save", "Editing", "Ctrl+S"),
        new("edit:find", "Find", "Editing", "Ctrl+F"),

        new("browser:back", "Browser back", "Browser", "Alt+Left"),
        new("browser:forward", "Browser forward", "Browser", "Alt+Right"),
        new("browser:refresh", "Refresh", "Browser", "F5"),
        new("browser:newtab", "New tab", "Browser", "Ctrl+T"),
        new("browser:closetab", "Close tab", "Browser", "Ctrl+W"),
        new("browser:reopentab", "Reopen closed tab", "Browser", "Ctrl+Shift+T"),
        new("browser:nexttab", "Next tab", "Browser", "Ctrl+Tab"),
        new("browser:prevtab", "Previous tab", "Browser", "Ctrl+Shift+Tab"),
        new("zoom:in", "Zoom in", "Browser", "Ctrl+Plus"),
        new("zoom:out", "Zoom out", "Browser", "Ctrl+Minus"),
        new("zoom:reset", "Reset zoom", "Browser", "Ctrl+0"),

        new("deck:toggle", "Show / hide the control deck", "PeriphHub"),
        new("deck:page", "Go to deck page…", "PeriphHub", NeedsArg: true, ArgHint: "Page name"),
        new("deck:nextpage", "Next deck page", "PeriphHub"),
        new("deck:prevpage", "Previous deck page", "PeriphHub"),
        new("deck:micmute", "Mute / unmute microphone", "PeriphHub"),
        new("deck:outmute", "Mute / unmute sound", "PeriphHub"),
        new("deck:volup", "Volume +5%", "PeriphHub"),
        new("deck:voldown", "Volume −5%", "PeriphHub"),
        new("deck:output", "Switch sound output to…", "PeriphHub", NeedsArg: true, ArgHint: "Part of the device name, e.g. G321 or Speakers"),
        new("deck:outputcycle", "Cycle sound output (speakers ↔ headset)", "PeriphHub"),
        new("deck:sound", "Play a sound file (soundboard)…", "PeriphHub", NeedsArg: true, ArgHint: @"C:\path\sound.mp3 or .wav"),
        new("deck:playpause", "Play / Pause (current app)", "PeriphHub"),
        new("deck:next", "Next track (current app)", "PeriphHub"),
        new("deck:prev", "Previous track (current app)", "PeriphHub"),
        new("deck:taskmgr", "Task Manager", "PeriphHub"),

        new("obs:scene", "OBS: switch to scene…", "OBS", NeedsArg: true, ArgHint: "Scene name exactly as in OBS"),
        new("obs:record", "OBS: start / stop recording", "OBS"),
        new("obs:pause", "OBS: pause / resume recording", "OBS"),
        new("obs:stream", "OBS: start / stop streaming", "OBS"),
        new("obs:replay", "OBS: save replay buffer", "OBS"),
        new("obs:vcam", "OBS: virtual camera on / off", "OBS"),
        new("obs:mute", "OBS: mute / unmute source…", "OBS", NeedsArg: true, ArgHint: "Audio source name, e.g. Mic/Aux or Desktop Audio"),

        new("scene:run", "Run scene…", "Scenes", NeedsArg: true, ArgHint: "Scene name, e.g. Gaming"),
        new("deck:paste", "Paste a snippet…", "PeriphHub", NeedsArg: true, ArgHint: "Text to paste instantly (can be several lines)"),
        new("sound:output", "Sound output: switch to…", "Sound", NeedsArg: true, ArgHint: "Part of the device name, e.g. G321 or Speakers"),
        new("sound:input", "Microphone: switch to…", "Sound", NeedsArg: true, ArgHint: "Part of the device name, e.g. G321 or Microphone Array"),
        new("sound:volume", "Set volume to…", "Sound", NeedsArg: true, ArgHint: "0–100"),
        new("sound:micmute", "Microphone mute on / off…", "Sound", NeedsArg: true, ArgHint: "on or off"),
        new("media:pausecurrent", "Pause music (if playing)", "Media"),

        new("laptop:mode", "Laptop: operating mode…", "Laptop", NeedsArg: true, ArgHint: "Silent, Performance or Turbo"),
        new("laptop:kbeffect", "Laptop keyboard: effect…", "Laptop", NeedsArg: true, ArgHint: "Static, Breathing, Colour cycle or Strobing"),
        new("laptop:kbcolor", "Laptop keyboard: colour…", "Laptop", NeedsArg: true, ArgHint: "#RRGGBB, e.g. #FF0000"),
        new("laptop:kbbright", "Laptop keyboard: brightness…", "Laptop", NeedsArg: true, ArgHint: "Off, Low, Medium or High"),
        new("laptop:chargelimit", "Laptop: stop charging at…", "Laptop", NeedsArg: true, ArgHint: "40–100 (%)"),
        new("mouse:dpi", "Mouse: set DPI…", "Mouse", NeedsArg: true, ArgHint: "e.g. 1600"),

        new("ha:toggle", "Home Assistant: toggle…", "Home Assistant", NeedsArg: true, ArgHint: "Entity, e.g. light.desk"),
        new("ha:on", "Home Assistant: turn on…", "Home Assistant", NeedsArg: true, ArgHint: "Entity, e.g. switch.pc_lights"),
        new("ha:off", "Home Assistant: turn off…", "Home Assistant", NeedsArg: true, ArgHint: "Entity, e.g. light.room"),
        new("ha:service", "Home Assistant: call service…", "Home Assistant", NeedsArg: true, ArgHint: "domain.service entity {json}, e.g. light.turn_on light.desk {\"brightness\":80}"),

        new("ytm:open", "YouTube Music: open", "YouTube Music"),
        new("ytm:like", "YouTube Music: like", "YouTube Music"),
        new("ytm:dislike", "YouTube Music: dislike", "YouTube Music"),
        new("ytm:shuffle", "YouTube Music: shuffle", "YouTube Music"),
        new("ytm:repeat", "YouTube Music: repeat", "YouTube Music"),
        new("ytm:volup", "YouTube Music: volume up", "YouTube Music"),
        new("ytm:voldown", "YouTube Music: volume down", "YouTube Music"),
        new("ytm:mute", "YouTube Music: mute", "YouTube Music"),
        new("ytm:search", "YouTube Music: search", "YouTube Music"),

        new("spotify:like", "Spotify: like / unlike current song", "Spotify"),
        new("spotify:addto", "Spotify: add current song to playlist…", "Spotify", NeedsArg: true, ArgHint: "Playlist link (Share › Copy link)"),
        new("spotify:playlist", "Spotify: play playlist…", "Spotify", NeedsArg: true, ArgHint: "Playlist link"),
        new("spotify:device", "Spotify: play on device…", "Spotify", NeedsArg: true, ArgHint: "Part of the device name, e.g. Echo or phone"),

        new("discord:mute", "Discord: mute / unmute", "Discord"),
        new("discord:deafen", "Discord: deafen / undeafen", "Discord"),
    }.Concat(Integrations.ShareX.Tasks.Select(t => new ActionDef("sharex:" + t.task, "ShareX: " + t.label, "ShareX")))
     .Append(new ActionDef("sharex:custom", "ShareX: other task / workflow…", "ShareX", NeedsArg: true, ArgHint: "Task name (e.g. RectangleRegion) or workflow:My workflow"))
     .ToArray();

    public static ActionDef? Find(string id, IEnumerable<ActionDef>? extra = null) =>
        (extra ?? Enumerable.Empty<ActionDef>()).Concat(Common).FirstOrDefault(a => a.Id == id);
}

/// <summary>Executes actions with correct press / release semantics (keys and clicks are held while the button is held).</summary>
public static class ActionRunner
{
    public static void Run(ActionSpec spec, bool pressed, IDeviceActionHandler? device = null)
    {
        try { RunCore(spec, pressed, device); }
        catch (Exception ex) { Log.Error($"Action {spec} failed", ex); }
    }

    private static void RunCore(ActionSpec spec, bool pressed, IDeviceActionHandler? device)
    {
        var id = spec.Id;
        if (id is "default" or "disabled") return;

        if (id.StartsWith("device:")) { device?.DeviceAction(id, pressed); return; }
        if (id.Split(':')[0] is "deck" or "obs" or "discord" or "scene" or "sound" or "laptop" or "ha" or "spotify" or "sharex" or "ytm" || id is "mouse:dpi" or "media:pausecurrent")
        {
            if (pressed) DeckActions.Run(spec);
            return;
        }

        switch (id)
        {
            case "key":
                if (string.IsNullOrWhiteSpace(spec.Arg)) return;
                var vks = Input.ParseCombo(spec.Arg);
                if (pressed) Input.ComboDown(vks); else Input.ComboUp(vks);
                return;
            case "text":
                if (pressed && spec.Arg != null) Input.TypeText(spec.Arg.Replace("\\n", "\n"));
                return;
            case "launch":
                if (pressed && !string.IsNullOrWhiteSpace(spec.Arg))
                    Process.Start(new ProcessStartInfo(spec.Arg.Trim('"')) { UseShellExecute = true });
                return;
            case "mouse:left": Input.MouseButton(Input.Button.Left, pressed); return;
            case "mouse:right": Input.MouseButton(Input.Button.Right, pressed); return;
            case "mouse:middle": Input.MouseButton(Input.Button.Middle, pressed); return;
            case "mouse:back": Input.MouseButton(Input.Button.Back, pressed); return;
            case "mouse:forward": Input.MouseButton(Input.Button.Forward, pressed); return;
            case "scroll:left": if (pressed) Input.HWheel(-120); return;
            case "scroll:right": if (pressed) Input.HWheel(120); return;
        }

        var def = ActionCatalog.Find(id);
        if (def?.Combo != null)
        {
            var vks = Input.ParseCombo(def.Combo);
            if (pressed) Input.ComboDown(vks); else Input.ComboUp(vks);
            return;
        }
        Log.Warn($"Unknown action '{id}'");
    }
}
