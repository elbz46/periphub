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
