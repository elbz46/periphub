using HidSharp;
using HidSharp.Reports;
using PeriphHub.Core;

namespace PeriphHub.Drivers.Generic;

/// <summary>
/// Reads the standard HID "Battery Strength" value (usage page 0x06, usage 0x20) that many wireless
/// dongles and Bluetooth devices report, from a feature report or an input report.
/// </summary>
public sealed class HidBattery
{
    private const uint BatteryStrength = 0x00060020;
    private readonly HidDevice _dev;
    private readonly Report _report;
    private readonly bool _feature;

    private HidBattery(HidDevice dev, Report report, bool feature) { _dev = dev; _report = report; _feature = feature; }

    public static HidBattery? Find(IEnumerable<HidDevice> devices)
    {
        foreach (var d in devices)
        {
            try
            {
                var desc = d.GetReportDescriptor();
                foreach (var r in desc.FeatureReports)
                    if (r.DataItems.Any(i => i.Usages.GetAllValues().Contains(BatteryStrength))) return new HidBattery(d, r, true);
                foreach (var r in desc.InputReports)
                    if (r.DataItems.Any(i => i.Usages.GetAllValues().Contains(BatteryStrength))) return new HidBattery(d, r, false);
            }
            catch { /* collection cannot be inspected */ }
        }
        return null;
    }

    public int? Read()
    {
        try
        {
            using var s = _dev.Open();
            byte[] buf;
            if (_feature)
            {
                buf = new byte[_dev.GetMaxFeatureReportLength()];
                buf[0] = _report.ReportID;
                s.GetFeature(buf);
                return Parse(buf);
            }
            s.ReadTimeout = 1200;
            buf = new byte[_dev.GetMaxInputReportLength()];
            for (int i = 0; i < 6; i++)
            {
                int n = s.Read(buf, 0, buf.Length);
                if (n > 0 && (_report.ReportID == 0 || buf[0] == _report.ReportID) && Parse(buf) is int v) return v;
            }
        }
        catch (TimeoutException) { }
        catch (Exception ex) { Log.Warn($"HID battery read failed: {ex.Message}"); }
        return null;
    }

    private int? Parse(byte[] buf)
    {
        int? result = null;
        _report.Read(buf, 0, (buffer, bitOffset, item, index) =>
        {
            if (result != null || !item.Usages.GetAllValues().Contains(BatteryStrength)) return;
            int raw = (int)item.ReadRaw(buffer, bitOffset, 0);
            int min = item.LogicalMinimum, max = item.LogicalMaximum;
            if (max <= min) { min = 0; max = 100; }
            result = Math.Clamp((int)Math.Round((raw - min) * 100.0 / (max - min)), 0, 100);
        });
        return result;
    }
}
