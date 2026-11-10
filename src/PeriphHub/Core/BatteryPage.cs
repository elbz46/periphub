using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;

namespace PeriphHub.Core;

/// <summary>Battery history (7 days, sampled every 5 minutes) for the battery page and time-left estimates.</summary>
public static class BatteryHistory
{
    private static readonly string FilePath = Path.Combine(SettingsStore.DataDir, "battery-history.json");
    private static Dictionary<string, List<(long t, int p)>>? _data;
    private static readonly object Gate = new();

    private static Dictionary<string, List<(long t, int p)>> Data
    {
        get
        {
            if (_data != null) return _data;
            try
            {
                var raw = File.Exists(FilePath) ? JsonSerializer.Deserialize<Dictionary<string, List<long[]>>>(File.ReadAllText(FilePath)) : null;
                _data = raw?.ToDictionary(k => k.Key, v => v.Value.Select(a => (a[0], (int)a[1])).ToList()) ?? new();
            }
            catch { _data = new(); }
            return _data;
        }
    }

    public static void Record(string key, int percent)
    {
        lock (Gate)
        {
            var now = DateTimeOffset.Now.ToUnixTimeSeconds();
            if (!Data.TryGetValue(key, out var list)) Data[key] = list = new();
            if (list.Count > 0 && now - list[^1].t < 240 && list[^1].p == percent) return;
            list.Add((now, percent));
            var cutoff = now - 7 * 86400;
            list.RemoveAll(x => x.t < cutoff);
        }
    }

    public static void Save()
    {
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(SettingsStore.DataDir);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(Data.ToDictionary(k => k.Key, v => v.Value.Select(x => new[] { x.t, (long)x.p }).ToList())));
            }
            catch { }
        }
    }

    public static List<(long t, int p)> Last(string key, TimeSpan span)
    {
        lock (Gate)
        {
            var from = DateTimeOffset.Now.Add(-span).ToUnixTimeSeconds();
            return Data.TryGetValue(key, out var l) ? l.Where(x => x.t >= from).ToList() : new();
        }
    }

    /// <summary>Hours left estimated from the discharge rate over the last 3 hours (null if unknown / charging).</summary>
    public static double? HoursLeft(string key, int current)
    {
        var pts = Last(key, TimeSpan.FromHours(3));
        if (pts.Count < 3) return null;
        // only the latest continuous discharge
        int i = pts.Count - 1;
        while (i > 0 && pts[i - 1].p >= pts[i].p) i--;
        var seg = pts.Skip(i).ToList();
        if (seg.Count < 2) return null;
        double hours = (seg[^1].t - seg[0].t) / 3600.0;
        double drop = seg[0].p - seg[^1].p;
        if (hours < 0.25 || drop < 2) return null;
        return current / (drop / hours);
    }
}

public sealed class BatteryRow : Observable
{
    public BatteryRow(string key) { Key = key; }
    public string Key { get; }
    private string _name = "", _glyph = "", _status = "", _left = "";
    private int _percent;
    private PointCollection _points = new();
    public string Name { get => _name; set => Set(ref _name, value); }
    public string Glyph { get => _glyph; set => Set(ref _glyph, value); }
    public int Percent { get => _percent; set { Set(ref _percent, value); Raise(nameof(BarBrush)); } }
    public string Status { get => _status; set => Set(ref _status, value); }
    public string TimeLeft { get => _left; set => Set(ref _left, value); }
    public PointCollection Points { get => _points; set => Set(ref _points, value); }
    public Brush BarBrush => new SolidColorBrush(_percent <= 15 ? Color.FromRgb(0xE0, 0x52, 0x4A) : _percent <= 35 ? Color.FromRgb(0xF0, 0xB4, 0x29) : Color.FromRgb(0x3C, 0xB3, 0x71));
}

public sealed class BatteryOverviewSetting : DeviceSetting
{
    public BatteryOverviewSetting() : base("batteries", "All batteries") { Persist = false; }
    public ObservableCollection<BatteryRow> Rows { get; } = new();
    private bool _empty = true;
    public bool IsEmpty { get => _empty; set => Set(ref _empty, value); }
}

/// <summary>Every battery in one place with a 24-hour graph and time-left estimate.</summary>
public sealed class BatteryPage : ManagedDevice
{
    private readonly BatteryOverviewSetting _overview;
    private Timer? _recorder;

    public BatteryPage() : base("app:batteries", "Batteries", "", DeviceKind.Other)
    {
        Transport = "All devices · last 24 hours";
        _overview = Add("Batteries", new BatteryOverviewSetting { Description = "Graphs show the last 24 hours. Time left is estimated from how fast the battery has been dropping." });
        _recorder = new Timer(_ => Sample(), null, TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(5));
    }

    public override string Glyph => "";

    private static IEnumerable<(string key, string name, string glyph, int pct, bool charging)> Current()
    {
        var lap = Integrations.SystemStats.LaptopBattery;
        if (lap.hasBattery) yield return ("laptop", "Laptop", "", lap.percent, lap.charging);
        List<ManagedDevice> devs;
        lock (App.Hub.Devices) devs = App.Hub.Devices.Where(d => d.BatteryPercent != null && d.IsOnline).ToList();
        foreach (var d in devs) yield return (d.Key, d.Name, d.Glyph, d.BatteryPercent!.Value, d.Charging);
    }

    private static void Sample()
    {
        foreach (var c in Current()) BatteryHistory.Record(c.key, c.pct);
        BatteryHistory.Save();
    }

    public override Task OnShownAsync()
    {
        Sample();
        var now = DateTimeOffset.Now.ToUnixTimeSeconds();
        var rows = Current().ToList();
        OnUi(() =>
        {
            foreach (var gone in _overview.Rows.Where(r => rows.All(c => c.key != r.Key)).ToList()) _overview.Rows.Remove(gone);
            foreach (var c in rows)
            {
                var row = _overview.Rows.FirstOrDefault(r => r.Key == c.key);
                if (row == null) { row = new BatteryRow(c.key); _overview.Rows.Add(row); }
                row.Name = c.name; row.Glyph = c.glyph; row.Percent = c.pct;
                row.Status = c.charging ? "Charging ⚡" : c.pct <= 15 ? "Low – charge soon" : "On battery";
                var h = c.charging ? null : BatteryHistory.HoursLeft(c.key, c.pct);
                row.TimeLeft = h == null ? "" : h >= 48 ? $"about {h / 24:0} days left" : h >= 1 ? $"about {h:0.#} h left" : $"about {h * 60:0} min left";
                // sparkline 240×40 over 24h
                var pts = BatteryHistory.Last(c.key, TimeSpan.FromHours(24));
                var pc = new PointCollection(pts.Select(p => new Point(240 - (now - p.t) / 86400.0 * 240, 40 - p.p / 100.0 * 40)));
                pc.Freeze();
                row.Points = pc;
            }
            _overview.IsEmpty = _overview.Rows.Count == 0;
        });
        return Task.CompletedTask;
    }

    public override Task PollAsync() => OnShownAsync();
}
