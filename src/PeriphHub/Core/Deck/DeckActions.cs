using System.Diagnostics;
using System.Windows.Media;

namespace PeriphHub.Core;

/// <summary>Deck UI hooks (implemented by the deck window).</summary>
public interface IDeckHost
{
    void Toggle();
    void GoToPage(string name);
    void NextPage();
    void PreviousPage();
}

/// <summary>PeriphHub-specific actions usable from deck buttons, mouse buttons and remapped keys.</summary>
public static class DeckActions
{
    public static IDeckHost? Host { get; set; }
    private static readonly List<MediaPlayer> Playing = new();

    /// <summary>Pastes text instantly via the clipboard, then puts the old clipboard back.</summary>
    private static void Paste(string text)
    {
        System.Windows.IDataObject? old = null;
        try { old = System.Windows.Clipboard.GetDataObject(); } catch { }
        try { System.Windows.Clipboard.SetText(text.Replace("\r\n", "\n").Replace("\\n", "\n").Replace("\n", Environment.NewLine)); }
        catch { Input.TypeText(text); return; }
        Input.Tap(Input.ParseCombo("Ctrl+V"));
        var d = System.Windows.Application.Current.Dispatcher;
        _ = Task.Delay(600).ContinueWith(_ => d.BeginInvoke(() =>
        {
            try { if (old != null) System.Windows.Clipboard.SetDataObject(old, true); } catch { }
        }));
    }

    private static void SwitchSound(string? want, bool capture) => Task.Run(() =>
    {
        want = (want ?? "").Trim();
        var e = AudioEndpoints.All().FirstOrDefault(x => x.IsCapture == capture && x.Name.Contains(want, StringComparison.OrdinalIgnoreCase));
        if (e == null) { Toast.Show($"No {(capture ? "microphone" : "sound output")} matching “{want}”"); return; }
        AudioEndpoints.SetDefault(e.Id);
    });

    public static void Run(ActionSpec spec)
    {
        var d = System.Windows.Application.Current?.Dispatcher;
        void Ui(Action a) { if (d == null || d.CheckAccess()) a(); else d.BeginInvoke(a); }
        try
        {
            switch (spec.Id)
            {
                case "deck:toggle": Ui(() => Host?.Toggle()); break;
                case "deck:page": Ui(() => Host?.GoToPage(spec.Arg ?? "")); break;
                case "deck:nextpage": Ui(() => Host?.NextPage()); break;
                case "deck:prevpage": Ui(() => Host?.PreviousPage()); break;
                case "deck:playpause": _ = MediaController.Instance.PlayPauseAsync(); break;
                case "deck:next": _ = MediaController.Instance.NextAsync(); break;
                case "deck:prev": _ = MediaController.Instance.PreviousAsync(); break;
                case "deck:taskmgr": Input.Tap(Input.ParseCombo("Ctrl+Shift+Escape")); break;
                case "obs:scene": _ = Obs.Instance.SendAsync("SetCurrentProgramScene", new { sceneName = spec.Arg ?? "" }); break;
                case "obs:record": _ = Obs.Instance.SendAsync("ToggleRecord"); break;
                case "obs:pause": _ = Obs.Instance.SendAsync("ToggleRecordPause"); break;
                case "obs:stream": _ = Obs.Instance.SendAsync("ToggleStream"); break;
                case "obs:replay": _ = Obs.Instance.SendAsync("SaveReplayBuffer"); break;
                case "obs:vcam": _ = Obs.Instance.SendAsync("ToggleVirtualCam"); break;
                case "obs:mute": _ = Obs.Instance.SendAsync("ToggleInputMute", new { inputName = spec.Arg ?? "" }); break;
                case "scene:run": _ = Scenes.SceneEngine.Instance.RunByNameAsync(spec.Arg ?? ""); break;
                case "deck:paste": Ui(() => Paste(spec.Arg ?? "")); break;
                case "sound:output": SwitchSound(spec.Arg, capture: false); break;
                case "sound:input": SwitchSound(spec.Arg, capture: true); break;
                case "sound:volume":
                    Task.Run(() => { if (AudioEndpoints.DefaultOutputId() is { } id && int.TryParse(spec.Arg, out var v)) AudioEndpoints.SetVolume(id, v); });
                    break;
                case "sound:micmute":
                    Task.Run(() => { if (AudioEndpoints.DefaultInputId() is { } id) AudioEndpoints.SetMute(id, (spec.Arg ?? "").Trim().ToLowerInvariant() is "on" or "true" or "1" or "mute"); });
                    break;
                case "media:pausecurrent": if (MediaController.Instance.IsPlaying) _ = MediaController.Instance.PlayPauseAsync(); break;
                case "laptop:mode": Ui(() => DeviceControl.Set<Drivers.Asus.AsusLaptopDevice>("perf.mode", spec.Arg)); break;
                case "laptop:kbeffect": Ui(() => DeviceControl.Set<Drivers.Asus.AsusLaptopDevice>("kb.mode", spec.Arg)); break;
                case "laptop:kbcolor": Ui(() => DeviceControl.Set<Drivers.Asus.AsusLaptopDevice>("kb.color", spec.Arg)); break;
                case "laptop:kbbright": Ui(() => DeviceControl.Set<Drivers.Asus.AsusLaptopDevice>("kb.brightness", spec.Arg)); break;
                case "laptop:chargelimit": Ui(() => DeviceControl.Set<Drivers.Asus.AsusLaptopDevice>("battery.limit", spec.Arg)); break;
                case "mouse:dpi": Ui(() => DeviceControl.Set<Drivers.Logitech.LogitechDevice>("dpi", spec.Arg)); break;
                case "ha:toggle": _ = Integrations.HomeAssistant.Instance.ToggleAsync((spec.Arg ?? "").Trim()); break;
                case "ha:on": _ = Integrations.HomeAssistant.Instance.TurnAsync((spec.Arg ?? "").Trim(), true); break;
                case "ha:off": _ = Integrations.HomeAssistant.Instance.TurnAsync((spec.Arg ?? "").Trim(), false); break;
                case "ha:service": _ = Integrations.HomeAssistant.Instance.ServiceAsync(spec.Arg ?? ""); break;
                case "sharex:custom": Integrations.ShareX.Run(spec.Arg ?? ""); break;
                case var sx when sx.StartsWith("sharex:"): Integrations.ShareX.Run(sx[7..]); break;
                case var ytm when ytm.StartsWith("ytm:"): _ = Integrations.YouTubeMusic.RunAsync(ytm[4..]); break;
                case var sp when sp.StartsWith("spotify:"): _ = Integrations.Spotify.Instance.Run(sp, spec.Arg); break;
                case "discord:mute": Input.Tap(Input.ParseCombo(Deck.DeckStore.Config.DiscordMuteKey)); break;
                case "discord:deafen": Input.Tap(Input.ParseCombo(Deck.DeckStore.Config.DiscordDeafenKey)); break;
                case "deck:micmute":
                    Task.Run(() =>
                    {
                        if (AudioEndpoints.DefaultInputId() is not { } id) return;
                        var now = !(AudioEndpoints.GetMute(id) ?? false);
                        AudioEndpoints.SetMute(id, now);
                        Toast.Show(now ? "Microphone muted" : "Microphone on");
                    });
                    break;
                case "deck:outmute":
                    Task.Run(() => { if (AudioEndpoints.DefaultOutputId() is { } id) AudioEndpoints.SetMute(id, !(AudioEndpoints.GetMute(id) ?? false)); });
                    break;
                case "deck:volup":
                case "deck:voldown":
                    Task.Run(() =>
                    {
                        if (AudioEndpoints.DefaultOutputId() is not { } id) return;
                        var v = AudioEndpoints.GetVolume(id) ?? 50;
                        AudioEndpoints.SetVolume(id, v + (spec.Id == "deck:volup" ? 5 : -5));
                    });
                    break;
                case "deck:output":
                    Task.Run(() =>
                    {
                        var want = (spec.Arg ?? "").Trim();
                        var e = AudioEndpoints.All().FirstOrDefault(x => !x.IsCapture && x.Name.Contains(want, StringComparison.OrdinalIgnoreCase));
                        if (e == null) { Toast.Show($"No sound device matching “{want}”"); return; }
                        AudioEndpoints.SetDefault(e.Id);
                        Toast.Show($"Sound: {e.Name}");
                    });
                    break;
                case "deck:outputcycle":
                    Task.Run(() =>
                    {
                        var outs = AudioEndpoints.All().Where(x => !x.IsCapture).ToList();
                        if (outs.Count < 2) { Toast.Show("Only one sound output is connected"); return; }
                        var cur = AudioEndpoints.DefaultOutputId();
                        var i = outs.FindIndex(x => x.Id == cur);
                        var next = outs[(i + 1) % outs.Count];
                        AudioEndpoints.SetDefault(next.Id);
                        Toast.Show($"Sound: {next.Name}");
                    });
                    break;
                case "deck:sound":
                    Ui(() =>
                    {
                        if (string.IsNullOrWhiteSpace(spec.Arg)) return;
                        var p = new MediaPlayer();
                        p.Open(new Uri(spec.Arg.Trim('"')));
                        p.MediaEnded += (_, _) => { p.Close(); Playing.Remove(p); };
                        Playing.Add(p);
                        p.Play();
                    });
                    break;
            }
        }
        catch (Exception ex) { Log.Error($"Deck action {spec} failed", ex); }
    }
}
