using System.Collections.ObjectModel;

namespace PeriphHub.Core;

public sealed class AppVolume : Observable
{
    private readonly CancellationTokenSource _none = new();
    private CancellationTokenSource? _debounce;
    internal bool Loading;
    public AppVolume(string name) { Name = name; }
    public string Name { get; }

    private int _volume;
    public int Volume
    {
        get => _volume;
        set { if (Set(ref _volume, value) && !Loading) Push(); }
    }

    private bool _muted;
    public bool Muted
    {
        get => _muted;
        set { if (Set(ref _muted, value) && !Loading) _ = Task.Run(() => AudioEndpoints.SetAppMute(Name, value)); }
    }

    private async void Push()
    {
        _debounce?.Cancel();
        var cts = _debounce = new CancellationTokenSource();
        try
        {
            await Task.Delay(40, cts.Token);
            var v = _volume;
            await Task.Run(() => AudioEndpoints.SetAppVolume(Name, v));
        }
        catch (OperationCanceledException) { }
    }
}

/// <summary>Per-application volume (like the Windows volume mixer).</summary>
public sealed class MixerSetting : DeviceSetting
{
    public MixerSetting() : base("mixer", "App volumes") { Persist = false; Description = "Apps currently playing sound on your default speaker or headset."; }
    public ObservableCollection<AppVolume> Apps { get; } = new();
    private bool _empty = true;
    public bool IsEmpty { get => _empty; private set => Set(ref _empty, value); }

    public async Task RefreshAsync()
    {
        var sessions = await Task.Run(AudioEndpoints.AppSessions);
        var d = System.Windows.Application.Current?.Dispatcher;
        void Apply()
        {
            foreach (var gone in Apps.Where(a => sessions.All(s => s.Name != a.Name)).ToList()) Apps.Remove(gone);
            foreach (var s in sessions)
            {
                var row = Apps.FirstOrDefault(a => a.Name == s.Name);
                if (row == null) { row = new AppVolume(s.Name); Apps.Add(row); }
                row.Loading = true; row.Volume = s.Volume; row.Muted = s.Muted; row.Loading = false;
            }
            IsEmpty = Apps.Count == 0;
        }
        if (d == null || d.CheckAccess()) Apply(); else await d.InvokeAsync(Apply);
    }
}

/// <summary>"Music &amp; sound": now playing, transport controls, master and per-app volume.</summary>
public sealed class MusicPage : ManagedDevice
{
    private readonly RangeSetting _master;
    private readonly ToggleSetting _masterMute;
    private readonly InfoSetting _output;
    private readonly MixerSetting _mixer;
    private string? _outId;

    public MusicPage() : base("app:music", "Music & sound", "", DeviceKind.Other)
    {
        Transport = "Controls any app that plays media";
        Add("Now playing", new MediaPlayerSetting("player")
        { Description = "Works with Spotify, YouTube and other sites in your browser, Media Player, Apple Music, VLC and more." });
        _output = Add("Volume", new InfoSetting("out", "Playing through"));
        _master = Add("Volume", new RangeSetting("master", "Volume", 0, 100, 1, 50,
            v => Task.Run(() => { if (_outId != null) AudioEndpoints.SetVolume(_outId, v); })) { Unit = "%", Persist = false, DebounceMs = 40 });
        _masterMute = Add("Volume", new ToggleSetting("mastermute", "Mute", false,
            v => Task.Run(() => { if (_outId != null) AudioEndpoints.SetMute(_outId, v); })) { Persist = false });
        _mixer = Add("Volume", new MixerSetting());
    }

    public override string Glyph => "";

    public override async Task OnShownAsync()
    {
        await Task.Run(() =>
        {
            _outId = AudioEndpoints.DefaultOutputId();
            var name = _outId == null ? null : AudioEndpoints.All().FirstOrDefault(e => e.Id == _outId)?.Name;
            _output.Text = name ?? "No sound device";
            if (_outId != null)
            {
                if (AudioEndpoints.GetVolume(_outId) is int v) _master.SetFromDevice(v);
                if (AudioEndpoints.GetMute(_outId) is bool m) _masterMute.SetFromDevice(m);
            }
        });
        await _mixer.RefreshAsync();
    }

    public override Task PollAsync() => OnShownAsync();
}
