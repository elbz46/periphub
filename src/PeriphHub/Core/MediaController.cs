using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media.Imaging;
using Windows.Media.Control;
using WinMedia = Windows.Media.Control.GlobalSystemMediaTransportControlsSession;

namespace PeriphHub.Core;

public sealed record MediaSource(string Id, string Name);

/// <summary>
/// Now-playing info and transport controls for whatever app plays media (Spotify, browsers, YouTube,
/// Media Player, games…) through Windows' system media controls.
/// </summary>
public sealed class MediaController : Observable
{
    public static MediaController Instance { get; } = new();

    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private WinMedia? _session;
    private string? _pinnedId;
    private readonly System.Windows.Threading.DispatcherTimer _tick;
    private TimeSpan _pos, _end;
    private DateTimeOffset _posAt;
    private CancellationTokenSource? _seek;

    private MediaController()
    {
        _tick = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _tick.Tick += (_, _) => { if (IsPlaying) { Raise(nameof(PositionSeconds)); Raise(nameof(PositionText)); } };
    }

    public async Task StartAsync()
    {
        try
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _manager.CurrentSessionChanged += (_, _) => _ = PickSessionAsync();
            _manager.SessionsChanged += (_, _) => _ = PickSessionAsync();
            await PickSessionAsync();
            _tick.Start();
        }
        catch (Exception ex) { Log.Warn($"Media controls unavailable: {ex.Message}"); }
    }

    // ------------------------------------------------------------------ state for the UI
    private string _title = "Nothing playing";
    public string Title { get => _title; private set => Set(ref _title, value); }
    private string _artist = "Start music or a video in any app";
    public string Artist { get => _artist; private set => Set(ref _artist, value); }
    private string _app = "";
    public string AppName { get => _app; private set => Set(ref _app, value); }
    private BitmapImage? _art;
    public BitmapImage? Art { get => _art; private set { Set(ref _art, value); Raise(nameof(HasArt)); } }
    public bool HasArt => _art != null;
    private bool _playing;
    public bool IsPlaying { get => _playing; private set { Set(ref _playing, value); Raise(nameof(PlayGlyph)); } }
    public string PlayGlyph => _playing ? "" : ""; // Pause / Play
    private bool _has;
    public bool HasSession { get => _has; private set => Set(ref _has, value); }
    private bool _shuffle;
    public bool Shuffle { get => _shuffle; private set => Set(ref _shuffle, value); }
    private string _repeat = "Off";
    public string RepeatText { get => _repeat; private set => Set(ref _repeat, value); }
    private bool _canSeek;
    public bool CanSeek { get => _canSeek; private set => Set(ref _canSeek, value); }

    public ObservableCollection<MediaSource> Sources { get; } = new();
    private MediaSource? _selectedSource;
    public MediaSource? SelectedSource
    {
        get => _selectedSource;
        set
        {
            if (value == null || value == _selectedSource) return;
            _selectedSource = value; Raise();
            _pinnedId = value.Id;
            _ = PickSessionAsync();
        }
    }

    public double DurationSeconds => Math.Max(1, _end.TotalSeconds);
    private TimeSpan CurrentPosition =>
        IsPlaying ? _pos + (DateTimeOffset.Now - _posAt) : _pos;
    public double PositionSeconds
    {
        get => Math.Clamp(CurrentPosition.TotalSeconds, 0, DurationSeconds);
        set => SeekDebounced(TimeSpan.FromSeconds(value));
    }
    public string PositionText => _end.TotalSeconds < 1 ? "" : $"{Fmt(CurrentPosition)} / {Fmt(_end)}";
    private static string Fmt(TimeSpan t) => t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");

    // ------------------------------------------------------------------ session tracking
    private static string PrettyApp(string aumid)
    {
        var a = aumid.ToLowerInvariant();
        if (a.Contains("spotify")) return "Spotify";
        if (a.Contains("chrome")) return "Google Chrome";
        if (a.Contains("msedge") || a.Contains("microsoftedge")) return "Microsoft Edge";
        if (a.Contains("firefox")) return "Firefox";
        if (a.Contains("opera")) return "Opera";
        if (a.Contains("brave")) return "Brave";
        if (a.Contains("zunemusic") || a.Contains("media")) return "Media Player";
        if (a.Contains("applemusic") || a.Contains("itunes")) return "Apple Music";
        if (a.Contains("vlc")) return "VLC";
        if (a.Contains("discord")) return "Discord";
        var name = aumid.Split('!', '\\', '/').Last();
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        return name.Length == 0 ? "App" : char.ToUpperInvariant(name[0]) + name[1..];
    }

    private async Task PickSessionAsync()
    {
        if (_manager == null) return;
        var all = _manager.GetSessions().ToList();
        OnUiThread(() =>
        {
            Sources.Clear();
            foreach (var s in all) Sources.Add(new MediaSource(s.SourceAppUserModelId, PrettyApp(s.SourceAppUserModelId)));
        });
        var next = all.FirstOrDefault(s => s.SourceAppUserModelId == _pinnedId) ?? _manager.GetCurrentSession() ?? all.FirstOrDefault();
        if (!ReferenceEquals(next, _session))
        {
            if (_session != null)
            {
                _session.MediaPropertiesChanged -= OnProps;
                _session.PlaybackInfoChanged -= OnPlayback;
                _session.TimelinePropertiesChanged -= OnTimeline;
            }
            _session = next;
            if (_session != null)
            {
                _session.MediaPropertiesChanged += OnProps;
                _session.PlaybackInfoChanged += OnPlayback;
                _session.TimelinePropertiesChanged += OnTimeline;
            }
        }
        OnUiThread(() =>
        {
            _selectedSource = Sources.FirstOrDefault(s => s.Id == _session?.SourceAppUserModelId);
            Raise(nameof(SelectedSource));
        });
        HasSession = _session != null;
        if (_session == null)
        {
            Title = "Nothing playing"; Artist = "Start music or a video in any app"; AppName = ""; Art = null; IsPlaying = false;
            _end = TimeSpan.Zero; RaiseTimeline();
            return;
        }
        AppName = PrettyApp(_session.SourceAppUserModelId);
        await LoadPropsAsync();
        LoadPlayback();
        LoadTimeline();
    }

    private void OnProps(WinMedia s, MediaPropertiesChangedEventArgs e) => _ = LoadPropsAsync();
    private void OnPlayback(WinMedia s, PlaybackInfoChangedEventArgs e) => LoadPlayback();
    private void OnTimeline(WinMedia s, TimelinePropertiesChangedEventArgs e) => LoadTimeline();

    private async Task LoadPropsAsync()
    {
        var s = _session;
        if (s == null) return;
        try
        {
            var p = await s.TryGetMediaPropertiesAsync();
            Title = string.IsNullOrWhiteSpace(p.Title) ? "Unknown title" : p.Title;
            Artist = string.Join(" — ", new[] { p.Artist, p.AlbumTitle }.Where(x => !string.IsNullOrWhiteSpace(x)));
            BitmapImage? img = null;
            if (p.Thumbnail != null)
            {
                using var ras = await p.Thumbnail.OpenReadAsync();
                using var net = ras.AsStreamForRead();
                var ms = new MemoryStream();
                await net.CopyToAsync(ms);
                ms.Position = 0;
                img = new BitmapImage();
                img.BeginInit(); img.CacheOption = BitmapCacheOption.OnLoad; img.StreamSource = ms; img.DecodePixelWidth = 256; img.EndInit();
                img.Freeze();
            }
            Art = img;
        }
        catch (Exception ex) { Log.Warn($"Now playing info failed: {ex.Message}"); }
    }

    private void LoadPlayback()
    {
        var s = _session;
        if (s == null) return;
        try
        {
            var i = s.GetPlaybackInfo();
            IsPlaying = i.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            Shuffle = i.IsShuffleActive == true;
            RepeatText = i.AutoRepeatMode switch
            {
                Windows.Media.MediaPlaybackAutoRepeatMode.List => "All",
                Windows.Media.MediaPlaybackAutoRepeatMode.Track => "One",
                _ => "Off",
            };
            CanSeek = i.Controls.IsPlaybackPositionEnabled;
            LoadTimeline();
        }
        catch { }
    }

    private void LoadTimeline()
    {
        var s = _session;
        if (s == null) return;
        try
        {
            var t = s.GetTimelineProperties();
            _end = t.EndTime - t.StartTime;
            _pos = t.Position - t.StartTime;
            _posAt = t.LastUpdatedTime.Year > 2000 ? t.LastUpdatedTime : DateTimeOffset.Now;
            if (_pos > _end) _pos = _end;
            RaiseTimeline();
        }
        catch { }
    }

    private void RaiseTimeline()
    {
        Raise(nameof(DurationSeconds)); Raise(nameof(PositionSeconds)); Raise(nameof(PositionText));
    }

    private static void OnUiThread(Action a)
    {
        var d = System.Windows.Application.Current?.Dispatcher;
        if (d == null || d.CheckAccess()) a(); else d.Invoke(a);
    }

    // ------------------------------------------------------------------ commands
    private WinMedia? Target => _session;

    public async Task PlayPauseAsync()
    {
        if (Target is { } s) await s.TryTogglePlayPauseAsync();
        else Input.Tap(Input.ParseCombo("PlayPause"));
    }

    public async Task NextAsync()
    {
        if (Target is { } s) await s.TrySkipNextAsync();
        else Input.Tap(Input.ParseCombo("NextTrack"));
    }

    public async Task PreviousAsync()
    {
        if (Target is { } s) await s.TrySkipPreviousAsync();
        else Input.Tap(Input.ParseCombo("PrevTrack"));
    }

    public async Task ToggleShuffleAsync()
    {
        if (Target is { } s) await s.TryChangeShuffleActiveAsync(!Shuffle);
    }

    public async Task CycleRepeatAsync()
    {
        if (Target is not { } s) return;
        var next = RepeatText switch
        {
            "Off" => Windows.Media.MediaPlaybackAutoRepeatMode.List,
            "All" => Windows.Media.MediaPlaybackAutoRepeatMode.Track,
            _ => Windows.Media.MediaPlaybackAutoRepeatMode.None,
        };
        await s.TryChangeAutoRepeatModeAsync(next);
    }

    private async void SeekDebounced(TimeSpan to)
    {
        if (Math.Abs((to - CurrentPosition).TotalSeconds) < 1.5) return;
        _seek?.Cancel();
        var cts = _seek = new CancellationTokenSource();
        try
        {
            await Task.Delay(250, cts.Token);
            if (Target is { } s)
            {
                var t = s.GetTimelineProperties();
                await s.TryChangePlaybackPositionAsync((t.StartTime + to).Ticks);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Log.Warn($"Seek failed: {ex.Message}"); }
    }
}

/// <summary>Shows the now-playing card on a page.</summary>
public sealed class MediaPlayerSetting : DeviceSetting
{
    public MediaPlayerSetting(string id) : base(id, "Now playing") { Persist = false; }
    public MediaController Player => MediaController.Instance;
}
