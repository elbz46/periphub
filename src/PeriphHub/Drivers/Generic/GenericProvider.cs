using System.Text.RegularExpressions;
using HidSharp;
using PeriphHub.Core;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;

namespace PeriphHub.Drivers.Generic;

/// <summary>
/// Picks up everything that has no dedicated driver: Bluetooth mice / keyboards / headsets and
/// known-vendor USB devices such as MSI keyboards. A device reachable through several paths
/// (USB dongle, Bluetooth HID, Bluetooth audio) is shown once.
/// </summary>
public sealed partial class GenericProvider : IDeviceProvider
{
    // The battery level Windows shows in Settings › Bluetooth & devices.
    private const string BtBatteryKey = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";

    private static readonly Dictionary<int, string> Vendors = new()
    {
        [0x0DB0] = "MSI", [0x1462] = "MSI", [0x1532] = "Razer", [0x1B1C] = "Corsair", [0x1038] = "SteelSeries",
        [0x0951] = "HyperX", [0x03F0] = "HyperX", [0x3434] = "Keychron", [0x258A] = "Generic (Sinowealth)",
    };

    private sealed record Identity(string Key, string Name, string Vendor, DeviceKind Kind);

    /// <summary>Known models, so every connection path of one product maps to the same entry.</summary>
    private static readonly (int vid, int pid, string alias, Identity id)[] Models =
    {
        (0x0DB0, 0x1620, "strikepro", new("msi:strike-pro-wireless", "MSI Strike Pro Wireless", "MSI", DeviceKind.Keyboard)),
        (0x0DB0, 0x2C0E, "strikepro", new("msi:strike-pro-wireless", "MSI Strike Pro Wireless", "MSI", DeviceKind.Keyboard)),
    };

    [GeneratedRegex(@"^(G\d{3}|MX\s|Logi|POP\s|Lift|Pebble|ERGO|Signature|[KM]\d{3})", RegexOptions.IgnoreCase)]
    private static partial Regex LogitechName();

    public string Name => "Generic";

    public async Task ScanAsync(DeviceHub hub)
    {
        ScanHid(hub);
        try { await ScanBluetoothAsync(hub); }
        catch (Exception ex) { Log.Warn($"Bluetooth scan failed: {ex.Message}"); }
    }

    // ------------------------------------------------------------------ identity helpers
    private static string Normalize(string s) => new string(s.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    private static string VendorFromName(string name)
    {
        var n = Normalize(name);
        if (n.Contains("msi") || n.Contains("strikepro")) return "MSI";
        if (LogitechName().IsMatch(name) || n.Contains("logitech")) return "Logitech";
        if (n.Contains("razer")) return "Razer";
        if (n.Contains("corsair")) return "Corsair";
        if (n.Contains("steelseries") || n.Contains("arctis")) return "SteelSeries";
        if (n.Contains("hyperx")) return "HyperX";
        return "";
    }

    private static Identity? KnownByName(string name)
    {
        var n = Normalize(name);
        return Models.FirstOrDefault(m => n.Contains(m.alias)).id;
    }

    private static DeviceKind GuessKind(string name, IEnumerable<HidDevice>? hids = null)
    {
        var n = name.ToLowerInvariant();
        if (n.Contains("headset") || n.Contains("headphone") || n.Contains("buds") || n.Contains("earbud") || n.Contains("immerse") || n.Contains("arctis")) return DeviceKind.Headset;
        if (n.Contains("keyboard") || n.Contains("strike") || n.Contains("vigor") || n.Contains("forge") || Regex.IsMatch(n, @"\bgk\d")) return DeviceKind.Keyboard;
        if (n.Contains("mouse") || n.Contains("clutch") || n.StartsWith("mx ") || Regex.IsMatch(n, @"\bgm\d")) return DeviceKind.Mouse;
        if (hids != null)
        {
            try
            {
                var usages = hids.SelectMany(h => h.GetReportDescriptor().DeviceItems.SelectMany(i => i.Usages.GetAllValues())).ToHashSet();
                if (usages.Contains(0x00010006)) return DeviceKind.Keyboard;
                if (usages.Contains(0x00010002)) return DeviceKind.Mouse;
            }
            catch { }
        }
        return DeviceKind.Other;
    }

    private static GenericDevice GetOrCreate(DeviceHub hub, Identity id)
    {
        var dev = hub.Find<GenericDevice>(id.Key);
        if (dev == null)
        {
            lock (hub.Devices)
                dev = hub.Devices.OfType<GenericDevice>().FirstOrDefault(d => Normalize(d.Name) == Normalize(id.Name));
        }
        if (dev == null)
        {
            dev = new GenericDevice(id.Key, id.Name, id.Vendor, id.Kind);
            hub.Add(dev);
            _ = dev.ApplySavedAsync();
        }
        return dev;
    }

    /// <summary>True when a dedicated driver (e.g. Logitech HID++) already shows this device.</summary>
    private static bool HandledElsewhere(DeviceHub hub, string name)
    {
        var n = Normalize(name);
        if (n.Length < 3) return false;
        List<ManagedDevice> others;
        lock (hub.Devices) others = hub.Devices.Where(d => d is not GenericDevice && d.Kind != DeviceKind.Laptop && d.Vendor != "All keyboards").ToList();
        var match = others.Any(d => d.IsOnline && Normalize(d.Name) is { Length: >= 3 } m && (m.Contains(n) || n.Contains(m)));
        if (match)
        {
            List<GenericDevice> dupes;
            lock (hub.Devices) dupes = hub.Devices.OfType<GenericDevice>().Where(d => Normalize(d.Name) == n).ToList();
            foreach (var d in dupes) hub.Remove(d);
        }
        return match;
    }

    // ------------------------------------------------------------------ USB / Bluetooth HID
    private static void ScanHid(DeviceHub hub)
    {
        var seen = new HashSet<(GenericDevice, string)>();
        var groups = DeviceList.Local.GetHidDevices().Where(d => Vendors.ContainsKey(d.VendorID)).GroupBy(d => (d.VendorID, d.ProductID));
        foreach (var g in groups)
        {
            var first = g.First();
            var path = first.DevicePath.ToLowerInvariant();
            bool bt = path.Contains("00001124") || path.Contains("00001812");
            string product = "";
            try { product = first.GetProductName()?.Trim() ?? ""; } catch { }

            var id = Models.FirstOrDefault(m => m.vid == first.VendorID && m.pid == first.ProductID).id
                     ?? (product.Length > 0 ? KnownByName(product) : null);
            if (id == null)
            {
                var vendor = Vendors[first.VendorID];
                var name = product.Length > 0 ? product : $"{vendor} device {first.ProductID:X4}";
                id = new Identity($"hid:{first.VendorID:X4}:{first.ProductID:X4}", name, vendor, GuessKind(name, g));
            }
            if (HandledElsewhere(hub, id.Name)) continue;

            var dev = GetOrCreate(hub, id);
            var source = $"hid:{first.VendorID:X4}:{first.ProductID:X4}";
            var battery = HidBattery.Find(g);
            dev.SetConnection(source, bt ? "Bluetooth" : "USB (wireless dongle or cable)",
                g.Select(d => $"VID {d.VendorID:X4}  PID {d.ProductID:X4}  {SafeUsage(d)}"),
                battery == null ? null : () => Task.Run(battery.Read));
            seen.Add((dev, source));
        }

        // Unplugged / switched off: drop that connection path; with none left the device leaves the list.
        List<GenericDevice> all;
        lock (hub.Devices) all = hub.Devices.OfType<GenericDevice>().ToList();
        foreach (var d in all)
            foreach (var s in d.Sources.Where(s => s.StartsWith("hid:") && !seen.Contains((d, s))).ToList())
                d.RemoveConnection(s);
    }

    private static string SafeUsage(HidDevice d)
    {
        try
        {
            var u = d.GetReportDescriptor().DeviceItems.SelectMany(i => i.Usages.GetAllValues()).FirstOrDefault();
            return $"usage {u >> 16:X4}:{u & 0xFFFF:X4}  in {d.GetMaxInputReportLength()} out {d.GetMaxOutputReportLength()} feat {d.GetMaxFeatureReportLength()}";
        }
        catch { return ""; }
    }

    // ------------------------------------------------------------------ Bluetooth (paired devices)
    private static async Task ScanBluetoothAsync(DeviceHub hub)
    {
        var selectors = new[]
        {
            (BluetoothDevice.GetDeviceSelectorFromPairingState(true), false),
            (BluetoothLEDevice.GetDeviceSelectorFromPairingState(true), true),
        };
        var props = new[] { "System.Devices.Aep.IsConnected", "System.Devices.Aep.DeviceAddress", "System.Devices.Aep.ContainerId" };
        foreach (var (sel, isLe) in selectors)
        {
            var found = await DeviceInformation.FindAllAsync(sel, props, DeviceInformationKind.AssociationEndpoint);
            foreach (var di in found)
            {
                var name = di.Name?.Trim() ?? "";
                if (name.Length == 0) continue;
                var connected = di.Properties.TryGetValue("System.Devices.Aep.IsConnected", out var c) && c is true;
                var addr = di.Properties.TryGetValue("System.Devices.Aep.DeviceAddress", out var a) ? a?.ToString() ?? di.Id : di.Id;
                var container = di.Properties.TryGetValue("System.Devices.Aep.ContainerId", out var ct) && ct is Guid gid ? gid : (Guid?)null;
                var source = $"bt:{addr}";

                var id = KnownByName(name);
                if (id == null)
                {
                    var kind = GuessKind(name);
                    var vendor = VendorFromName(name);
                    if (kind == DeviceKind.Other && vendor.Length == 0) continue; // phones, speakers, watches…
                    id = new Identity($"bt:{Normalize(name)}", name, vendor, kind);
                }

                if (!connected)
                {
                    hub.Find<GenericDevice>(id.Key)?.RemoveConnection(source);
                    continue;
                }
                if (HandledElsewhere(hub, name)) continue;

                var dev = GetOrCreate(hub, id);
                var aepId = di.Id;
                dev.SetConnection(source, "Bluetooth", new[] { $"Bluetooth {(isLe ? "LE" : "classic")} {addr}" },
                    () => ReadBluetoothBatteryAsync(container, aepId, isLe));
            }
        }
        List<GenericDevice> devs;
        lock (hub.Devices) devs = hub.Devices.OfType<GenericDevice>().ToList();
        foreach (var d in devs) await d.PollAsync();
    }

    private static async Task<int?> ReadBluetoothBatteryAsync(Guid? container, string aepId, bool isLe)
    {
        // 1) The value Windows itself shows (works for Bluetooth headsets and most keyboards / mice).
        if (container is Guid g)
        {
            var nodes = await DeviceInformation.FindAllAsync($"System.Devices.ContainerId:=\"{{{g}}}\"",
                new[] { BtBatteryKey }, DeviceInformationKind.Device);
            foreach (var n in nodes)
                if (n.Properties.TryGetValue(BtBatteryKey, out var v) && v is byte b && b <= 100) return b;
        }
        // 2) Bluetooth LE Battery Service.
        if (isLe)
        {
            try
            {
                using var le = await BluetoothLEDevice.FromIdAsync(aepId);
                if (le == null) return null;
                var svc = await le.GetGattServicesForUuidAsync(GattServiceUuids.Battery, BluetoothCacheMode.Uncached);
                foreach (var s in svc.Services)
                {
                    using (s)
                    {
                        var chars = await s.GetCharacteristicsForUuidAsync(GattCharacteristicUuids.BatteryLevel);
                        foreach (var ch in chars.Characteristics)
                        {
                            var r = await ch.ReadValueAsync(BluetoothCacheMode.Uncached);
                            if (r.Status == GattCommunicationStatus.Success && r.Value.Length > 0)
                                return Windows.Storage.Streams.DataReader.FromBuffer(r.Value).ReadByte();
                        }
                    }
                }
            }
            catch (Exception ex) { Log.Warn($"BLE battery read failed: {ex.Message}"); }
        }
        return null;
    }

    public void Dispose() { }
}
