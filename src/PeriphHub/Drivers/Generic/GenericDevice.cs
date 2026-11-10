using PeriphHub.Core;

namespace PeriphHub.Drivers.Generic;

/// <summary>
/// Any other mouse / keyboard / headset. One entry per physical device, even when it is connected
/// through several paths at once (e.g. USB dongle and Bluetooth).
/// </summary>
public sealed class GenericDevice : ManagedDevice
{
    private readonly InfoSetting _conn;
    private readonly InfoSetting _ids;
    private readonly Dictionary<string, (string transport, string[] ids, Func<Task<int?>>? battery)> _sources = new();
    private readonly object _gate = new();

    public GenericDevice(string key, string name, string vendor, DeviceKind kind) : base(key, name, vendor, kind)
    {
        if (kind == DeviceKind.Headset) EnableAudio();
        _conn = Add("Device", new InfoSetting("info.conn", "Connected through"));
        if (vendor == "MSI")
        {
            Notice = "Battery level and key remapping / macros (see “Key remapping & macros”) work for this keyboard. " +
                     "MSI keeps its lighting protocol private, so RGB control needs one capture from your PC first. See “Keyboard lighting” below.";
            Add("Keyboard lighting", new InfoSetting("msi.rgb", "RGB lighting",
                "Not supported yet for this model. Lighting set in MSI Center is stored in the keyboard and keeps working. " +
                "To add support, capture MSI Center changing one colour with USBPcap (steps in the README) and send the file."));
        }
        _ids = Add("Diagnostics", new InfoSetting("diag.ids", "Hardware IDs (useful to add full support)"));
    }

    public void SetConnection(string source, string transport, IEnumerable<string> ids, Func<Task<int?>>? battery)
    {
        lock (_gate) _sources[source] = (transport, ids.ToArray(), battery);
        Refresh();
    }

    public void RemoveConnection(string source)
    {
        bool removed;
        lock (_gate) removed = _sources.Remove(source);
        if (removed) Refresh();
    }

    public IReadOnlyList<string> Sources { get { lock (_gate) return _sources.Keys.ToList(); } }

    private void Refresh()
    {
        List<(string transport, string[] ids, Func<Task<int?>>? battery)> list;
        lock (_gate) list = _sources.Values.ToList();
        IsOnline = list.Count > 0;
        var transports = list.Select(x => x.transport).Distinct().ToList();
        Transport = string.Join(" + ", transports);
        _conn.Text = transports.Count == 0 ? "Not connected" : string.Join("\n", transports);
        _ids.Text = string.Join("\n", list.SelectMany(x => x.ids).Distinct());
        if (!IsOnline) BatteryPercent = null;
    }

    public override async Task PollAsync()
    {
        List<Func<Task<int?>>> readers;
        lock (_gate) readers = _sources.Values.Where(x => x.battery != null).Select(x => x.battery!).ToList();
        foreach (var r in readers)
        {
            try
            {
                if (await r() is int pct) { BatteryPercent = pct; return; }
            }
            catch (Exception ex) { Log.Warn($"{Name}: battery read failed: {ex.Message}"); }
        }
    }
}
