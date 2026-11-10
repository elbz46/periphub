using System.Diagnostics;
using Microsoft.Win32;

namespace PeriphHub.Core.Scenes;

/// <summary>Runs scenes and watches for their automatic triggers.</summary>
public sealed class SceneEngine
{
    public static SceneEngine Instance { get; } = new();
    private readonly HashSet<string> _runningApps = new(StringComparer.OrdinalIgnoreCase);
    private Timer? _appTimer;
    private bool? _docked, _plugged;
    private readonly Dictionary<ManagedDevice, bool> _headsets = new();
    private DateTime _started;

    public event Action<string>? SceneRan;

    public void Start()
    {
        _started = DateTime.Now;
        _docked = Docked();
        _plugged = Plugged();
        SystemEvents.DisplaySettingsChanged += (_, _) => Debounce(ref _dockTimer, 3000, CheckDock);
        SystemEvents.PowerModeChanged += (_, e) => { if (e.Mode == PowerModes.StatusChange) Debounce(ref _powerTimer, 1500, CheckPower); };
        _appTimer = new Timer(_ => CheckApps(), null, 2000, 2000);

        App.Hub.Devices.CollectionChanged += (_, _) => WatchHeadsets();
        WatchHeadsets();

        // Apply the current situation once after start-up (e.g. the PC booted while docked).
        _ = Task.Run(async () =>
        {
            await Task.Delay(8000);
            await FireAsync("startup");
            await FireAsync(_docked == true ? "docked" : "undocked");
            await FireAsync(_plugged == true ? "plugged" : "battery");
        });
    }

    private Timer? _dockTimer, _powerTimer;
    private static void Debounce(ref Timer? t, int ms, Action a)
    {
        t?.Dispose();
        t = new Timer(_ => a(), null, ms, Timeout.Infinite);
    }

    private static bool Docked() => System.Windows.Forms.Screen.AllScreens.Length > 1;
    private static bool Plugged() => System.Windows.Forms.SystemInformation.PowerStatus.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online;

    private void CheckDock()
    {
        var d = Docked();
        if (d == _docked) return;
        _docked = d;
        _ = FireAsync(d ? "docked" : "undocked");
    }

    private void CheckPower()
    {
        var p = Plugged();
        if (p == _plugged) return;
        _plugged = p;
        _ = FireAsync(p ? "plugged" : "battery");
    }

    private void CheckApps()
    {
        var wanted = SceneStore.Scenes.Where(s => s.Enabled && s.Trigger == "app" && !string.IsNullOrWhiteSpace(s.TriggerArg)).ToList();
        if (wanted.Count == 0) return;
        foreach (var scene in wanted)
        {
            var exe = scene.TriggerArg!.Trim();
            var name = exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? exe[..^4] : exe;
            bool running;
            try { var ps = Process.GetProcessesByName(name); running = ps.Length > 0; foreach (var p in ps) p.Dispose(); }
            catch { continue; }
            bool was = _runningApps.Contains(exe);
            if (running && !was) { _runningApps.Add(exe); _ = RunAsync(scene, $"{exe} started"); }
            else if (!running && was)
            {
                _runningApps.Remove(exe);
                if (!string.IsNullOrWhiteSpace(scene.OnExitScene)) _ = RunByNameAsync(scene.OnExitScene!, $"{exe} closed");
            }
        }
    }

    private void WatchHeadsets()
    {
        List<ManagedDevice> list;
        lock (App.Hub.Devices) list = App.Hub.Devices.Where(d => d.Kind == DeviceKind.Headset).ToList();
        foreach (var d in list)
        {
            if (_headsets.ContainsKey(d)) continue;
            _headsets[d] = d.IsOnline;
            if (d.IsOnline && DateTime.Now - _started > TimeSpan.FromSeconds(10)) _ = FireAsync("headset-on");
            d.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName != nameof(ManagedDevice.IsOnline)) return;
                if (_headsets.TryGetValue(d, out var was) && was == d.IsOnline) return;
                _headsets[d] = d.IsOnline;
                _ = FireAsync(d.IsOnline ? "headset-on" : "headset-off");
            };
        }
    }

    private async Task FireAsync(string trigger)
    {
        foreach (var s in SceneStore.Scenes.Where(s => s.Enabled && s.Trigger == trigger).ToList())
            await RunAsync(s, trigger);
    }

    public Task RunByNameAsync(string name, string? why = null)
    {
        var s = SceneStore.Scenes.FirstOrDefault(x => x.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));
        if (s == null) { Toast.Show($"No scene called “{name}”"); return Task.CompletedTask; }
        return RunAsync(s, why);
    }

    public async Task RunAsync(SceneModel scene, string? why = null)
    {
        Log.Info($"Scene {scene.Name}{(why == null ? "" : $" ({why})")}");
        foreach (var step in scene.Steps.ToList())
        {
            if (step.Id == "scene:run" && string.Equals(step.Arg, scene.Name, StringComparison.OrdinalIgnoreCase)) continue; // no self-loops
            ActionRunner.Run(step.ToSpec(), true);
            ActionRunner.Run(step.ToSpec(), false);
            await Task.Delay(Math.Max(step.DelayMs, 30));
        }
        Toast.Show($"{scene.Icon} {scene.Name}");
        SceneRan?.Invoke(scene.Name);
    }
}
