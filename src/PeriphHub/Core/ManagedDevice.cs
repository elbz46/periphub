using System.Collections.ObjectModel;

namespace PeriphHub.Core;

public enum DeviceKind { Mouse, Keyboard, Headset, Laptop, Receiver, Other }

/// <summary>A physical device as shown in the app, independent of how it is currently connected.</summary>
public abstract class ManagedDevice : Observable, IDisposable
{
    protected ManagedDevice(string key, string name, string vendor, DeviceKind kind)
    {
        Key = key; _name = name; Vendor = vendor; Kind = kind;
    }

    /// <summary>Stable identity used for saved settings (same over USB and Bluetooth).</summary>
    public string Key { get; }
    public string Vendor { get; }
    public DeviceKind Kind { get; protected set; }

    private string _name;
    public string Name { get => _name; protected set => Set(ref _name, value); }

    private string _transport = "";
    public string Transport { get => _transport; set { if (Set(ref _transport, value)) { RaiseStatus(); Raise(nameof(ConnectionLine)); } } }

    private bool _online = true;
    public bool IsOnline { get => _online; set { if (Set(ref _online, value)) { RaiseStatus(); Raise(nameof(ConnectionLine)); Raise(nameof(HasBattery)); } } }

    private int? _battery;
    private bool _lowWarned;
    public int? BatteryPercent
    {
        get => _battery;
        set
        {
            if (!Set(ref _battery, value)) return;
            RaiseBattery();
            if (value is int b && b <= 15 && !Charging && !_lowWarned) { _lowWarned = true; Toast.Show($"{Name} battery is low ({b}%)"); }
            if (value is int b2 && b2 > 25) _lowWarned = false;
        }
    }

    private bool _charging;
    public bool Charging { get => _charging; set { if (Set(ref _charging, value)) RaiseBattery(); } }

    private void RaiseStatus() { Raise(nameof(StatusLine)); Raise(nameof(HeaderLine)); }

    /// <summary>"Vendor · connection · battery" for the page header.</summary>
    public string HeaderLine => string.Join(" · ", new[] { Vendor, StatusLine }.Where(x => !string.IsNullOrEmpty(x)));

    private void RaiseBattery()
    {
        RaiseStatus(); Raise(nameof(BatteryText)); Raise(nameof(BatteryGlyph)); Raise(nameof(HasBattery));
    }

    public bool HasBattery => _battery != null && IsOnline;
    public string BatteryText => _battery is int b ? $"{b}%" : "";
    /// <summary>Segoe Fluent battery icon for the level (Battery0..Battery10, or the charging set).</summary>
    public string BatteryGlyph => _battery is int b
        ? ((char)((Charging ? 0xE85B : 0xE850) + Math.Clamp((b + 5) / 10, 0, 10))).ToString()
        : "";

    /// <summary>How the device is connected, for the sidebar (battery is shown separately there).</summary>
    public string ConnectionLine => !IsOnline ? "Disconnected" : Transport;

    public string StatusLine
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(Transport)) parts.Add(Transport);
            if (!IsOnline) parts.Add("asleep / out of range");
            else if (BatteryPercent is int b) parts.Add(Charging ? $"{b}% ⚡" : $"{b}%");
            else if (Charging) parts.Add("charging ⚡");
            return string.Join(" · ", parts);
        }
    }

    public virtual string Glyph => Kind switch
    {
        DeviceKind.Mouse => "",
        DeviceKind.Keyboard => "",
        DeviceKind.Headset => "",
        DeviceKind.Laptop => "",
        DeviceKind.Receiver => "",
        _ => "",
    };

    public ObservableCollection<SettingGroup> Groups { get; } = new();

    /// <summary>Banner shown at the top of the device page (limitations, tips).</summary>
    private string? _notice;
    public string? Notice { get => _notice; set => Set(ref _notice, value); }

    protected SettingGroup Group(string title)
    {
        var g = Groups.FirstOrDefault(x => x.Title == title);
        if (g == null) { g = new SettingGroup(title); OnUi(() => Groups.Add(g)); }
        return g;
    }

    protected T Add<T>(string group, T setting) where T : DeviceSetting
    {
        setting.Owner = this;
        var g = Group(group);
        OnUi(() => g.Settings.Add(setting));
        return setting;
    }

    protected static void OnUi(Action a)
    {
        var d = System.Windows.Application.Current?.Dispatcher;
        if (d == null || d.CheckAccess()) a(); else d.Invoke(a);
    }

    public IEnumerable<DeviceSetting> AllSettings => Groups.SelectMany(g => g.Settings);

    /// <summary>Pushes every stored setting to the hardware. Called on first sight and on each reconnect.</summary>
    public virtual async Task ApplySavedAsync()
    {
        foreach (var s in AllSettings.ToList())
            await s.ApplySavedAsync();
    }

    /// <summary>Periodic refresh (battery etc.).</summary>
    public virtual Task PollAsync() => Task.CompletedTask;

    /// <summary>Windows sound controls (volume, mute, mic, default device) for headsets.</summary>
    protected AudioControls? Audio { get; private set; }

    protected void EnableAudio()
    {
        if (Audio != null) return;
        Audio = new AudioControls(() => Name);
        foreach (var s in Audio.Settings) Add("Sound", s);
        EnableMiniPlayer();
        _ = Audio.RefreshAsync();
    }

    public Task RefreshAudioAsync() => Audio?.RefreshAsync() ?? Task.CompletedTask;

    /// <summary>Called when the page is opened, to show fresh values.</summary>
    public virtual Task OnShownAsync() => RefreshAudioAsync();

    /// <summary>Adds the now-playing card (used on headset pages).</summary>
    protected void EnableMiniPlayer() => Add("Sound", new MediaPlayerSetting("sound.player"));

    public virtual void Dispose() { }
}
