using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PeriphHub.Core.Integrations;

/// <summary>CPU / RAM use and laptop sensors for deck tiles (sampled at most once a second, only while used).</summary>
public static class SystemStats
{
    [StructLayout(LayoutKind.Sequential)] private struct FILETIME { public uint Low, High; public ulong Value => ((ulong)High << 32) | Low; }
    [DllImport("kernel32.dll")] private static extern bool GetSystemTimes(out FILETIME idle, out FILETIME kernel, out FILETIME user);

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength, dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }
    [DllImport("kernel32.dll")] private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX m);

    private static ulong _lastIdle, _lastTotal;
    private static DateTime _cpuAt, _sensorAt;
    private static int _cpu;
    private static (int cpuFan, int gpuFan, int cpuTemp, int gpuTemp) _sensors = (-1, -1, -1, -1);

    public static int CpuPercent
    {
        get
        {
            if (DateTime.Now - _cpuAt < TimeSpan.FromMilliseconds(900)) return _cpu;
            _cpuAt = DateTime.Now;
            if (!GetSystemTimes(out var idle, out var kernel, out var user)) return _cpu;
            ulong total = kernel.Value + user.Value;
            ulong dIdle = idle.Value - _lastIdle, dTotal = total - _lastTotal;
            if (_lastTotal != 0 && dTotal > 0) _cpu = (int)Math.Round(100.0 * (dTotal - dIdle) / dTotal);
            _lastIdle = idle.Value; _lastTotal = total;
            return _cpu;
        }
    }

    public static (int percent, double usedGb, double totalGb) Memory
    {
        get
        {
            var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            if (!GlobalMemoryStatusEx(ref m)) return (0, 0, 0);
            return ((int)m.dwMemoryLoad, (m.ullTotalPhys - m.ullAvailPhys) / 1073741824.0, m.ullTotalPhys / 1073741824.0);
        }
    }

    public static (int cpuFan, int gpuFan, int cpuTemp, int gpuTemp) Sensors
    {
        get
        {
            if (DateTime.Now - _sensorAt < TimeSpan.FromSeconds(2)) return _sensors;
            _sensorAt = DateTime.Now;
            var asus = App.Hub.Devices.OfType<Drivers.Asus.AsusLaptopDevice>().FirstOrDefault();
            if (asus != null) _ = Task.Run(() => _sensors = asus.ReadSensors());
            return _sensors;
        }
    }

    public static (int percent, bool charging, bool hasBattery) LaptopBattery
    {
        get
        {
            var p = SystemInformation.PowerStatus;
            bool has = p.BatteryChargeStatus != BatteryChargeStatus.NoSystemBattery && p.BatteryLifePercent <= 1;
            return ((int)Math.Round(p.BatteryLifePercent * 100), p.PowerLineStatus == PowerLineStatus.Online, has);
        }
    }

    public static readonly (string id, string label)[] Kinds =
    {
        ("cputemp", "CPU temperature"), ("gputemp", "GPU temperature"), ("cpufan", "CPU fan"), ("gpufan", "GPU fan"),
        ("cpu", "CPU usage"), ("ram", "Memory usage"), ("battery", "Laptop battery"),
    };

    /// <summary>(value text, label) for a stat tile.</summary>
    public static (string value, string label) Describe(string? kind)
    {
        var s = Sensors;
        string T(int c) => c < 0 ? "—" : $"{c}°";
        string F(int r) => r < 0 ? "—" : r >= 1000 ? $"{r / 1000.0:0.0}k" : r.ToString();
        switch (kind)
        {
            case "cputemp": return (T(s.cpuTemp), "CPU °C");
            case "gputemp": return (T(s.gpuTemp), "GPU °C");
            case "cpufan": return (F(s.cpuFan), "CPU fan rpm");
            case "gpufan": return (F(s.gpuFan), "GPU fan rpm");
            case "cpu": return ($"{CpuPercent}%", "CPU");
            case "ram": var m = Memory; return ($"{m.percent}%", $"RAM {m.usedGb:0.0}/{m.totalGb:0} GB");
            case "battery": var b = LaptopBattery; return (b.hasBattery ? $"{b.percent}%" : "—", b.charging ? "Laptop ⚡" : "Laptop");
            default: return ("—", "Stat");
        }
    }
}
