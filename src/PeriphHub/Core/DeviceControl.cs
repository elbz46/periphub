namespace PeriphHub.Core;

/// <summary>Changes a device setting by id from actions and scenes, so the page, the hardware and the saved value all update.</summary>
public static class DeviceControl
{
    public static void Set<T>(string settingId, string? value) where T : ManagedDevice
    {
        value = (value ?? "").Trim();
        var devices = App.Hub.Devices.OfType<T>().Where(d => d.IsOnline).ToList();
        if (devices.Count == 0) { Log.Info($"No {typeof(T).Name} connected for {settingId}"); return; }
        foreach (var d in devices)
        {
            var s = d.AllSettings.FirstOrDefault(x => x.Id == settingId);
            switch (s)
            {
                case ChoiceSetting c:
                    var opt = c.Options.FirstOrDefault(o => o.Key.Equals(value, StringComparison.OrdinalIgnoreCase))
                              ?? c.Options.FirstOrDefault(o => o.Label.Equals(value, StringComparison.OrdinalIgnoreCase))
                              ?? c.Options.FirstOrDefault(o => o.Label.StartsWith(value, StringComparison.OrdinalIgnoreCase));
                    if (opt != null) c.Value = opt.Key; else Toast.Show($"“{value}” is not an option for {c.Label}");
                    break;
                case RangeSetting r when int.TryParse(value.TrimEnd('%'), out var n): r.Value = r.Snap(n); break;
                case ToggleSetting t: t.Value = value.ToLowerInvariant() is "on" or "true" or "1" or "yes"; break;
                case ColorSetting col when value.StartsWith('#') && value.Length == 7: col.Value = value.ToUpperInvariant(); break;
                case null: Log.Info($"{d.Name} has no setting {settingId}"); break;
            }
        }
    }
}
