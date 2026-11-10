using System.Runtime.InteropServices;

namespace PeriphHub.Core;

/// <summary>A Windows sound device (speaker/headphones or microphone).</summary>
public sealed record AudioEndpoint(string Id, string Name, bool IsCapture);

/// <summary>
/// Windows Core Audio: finds the sound devices that belong to a headset and controls their volume,
/// mute and "default device" state. Works for any headset (USB dongle, Bluetooth or cable).
/// </summary>
public static class AudioEndpoints
{
    private enum EDataFlow { Render = 0, Capture = 1, All = 2 }
    private enum ERole { Console = 0, Multimedia = 1, Communications = 2 }
    private const int DEVICE_STATE_ACTIVE = 1, CLSCTX_ALL = 23;

    [StructLayout(LayoutKind.Sequential)]
    private struct PROPERTYKEY { public Guid fmtid; public int pid; }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROPVARIANT { public ushort vt; public ushort r1, r2, r3; public IntPtr p; public IntPtr p2; }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorCom { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(EDataFlow flow, int mask, out IMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(EDataFlow flow, ERole role, out IMMDevice device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        [PreserveSig] int OpenPropertyStore(int access, out IPropertyStore store);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out int state);
    }

    [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PROPERTYKEY key);
        [PreserveSig] int GetValue(ref PROPERTYKEY key, out PROPVARIANT value);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float levelDb, ref Guid ctx);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid ctx);
        [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint ch, float levelDb, ref Guid ctx);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint ch, float level, ref Guid ctx);
        [PreserveSig] int GetChannelVolumeLevel(uint ch, out float levelDb);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint ch, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid ctx);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    // Undocumented but stable since Windows 7; used by every "switch audio device" tool.
    [ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
    private class PolicyConfigCom { }

    [ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat(string id, IntPtr format);
        [PreserveSig] int GetDeviceFormat(string id, int isDefault, IntPtr format);
        [PreserveSig] int ResetDeviceFormat(string id);
        [PreserveSig] int SetDeviceFormat(string id, IntPtr endpointFormat, IntPtr mixFormat);
        [PreserveSig] int GetProcessingPeriod(string id, int isDefault, IntPtr defaultPeriod, IntPtr minPeriod);
        [PreserveSig] int SetProcessingPeriod(string id, IntPtr period);
        [PreserveSig] int GetShareMode(string id, IntPtr mode);
        [PreserveSig] int SetShareMode(string id, IntPtr mode);
        [PreserveSig] int GetPropertyValue(string id, IntPtr key, IntPtr value);
        [PreserveSig] int SetPropertyValue(string id, IntPtr key, IntPtr value);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, ERole role);
        [PreserveSig] int SetEndpointVisibility(string id, int visible);
    }

    [DllImport("ole32.dll")] private static extern int PropVariantClear(ref PROPVARIANT pv);

    private static readonly Guid VolumeIid = typeof(IAudioEndpointVolume).GUID;
    private static PROPERTYKEY FriendlyName = new() { fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), pid = 14 };

    private static IMMDeviceEnumerator Enumerator() => (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();

    public static List<AudioEndpoint> All()
    {
        var list = new List<AudioEndpoint>();
        try
        {
            var en = Enumerator();
            foreach (var flow in new[] { EDataFlow.Render, EDataFlow.Capture })
            {
                if (en.EnumAudioEndpoints(flow, DEVICE_STATE_ACTIVE, out var col) != 0) continue;
                col.GetCount(out var n);
                for (uint i = 0; i < n; i++)
                {
                    if (col.Item(i, out var dev) != 0) continue;
                    dev.GetId(out var id);
                    string name = id;
                    if (dev.OpenPropertyStore(0, out var store) == 0 && store.GetValue(ref FriendlyName, out var pv) == 0)
                    {
                        if (pv.vt == 31 && pv.p != IntPtr.Zero) name = Marshal.PtrToStringUni(pv.p) ?? id;
                        PropVariantClear(ref pv);
                    }
                    list.Add(new AudioEndpoint(id, name, flow == EDataFlow.Capture));
                }
            }
        }
        catch (Exception ex) { Log.Warn($"Audio device list failed: {ex.Message}"); }
        return list;
    }

    private static string Norm(string s) => new string(s.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    /// <summary>The model token of a name, e.g. "g321" in "G321 LIGHTSPEED Wireless Gaming Headset".</summary>
    private static string? ModelToken(string name) =>
        name.Split(' ', '-', '(', ')').Select(Norm).FirstOrDefault(t => t.Length >= 3 && t.Any(char.IsDigit) && t.Any(char.IsLetter));

    /// <summary>Speaker and microphone endpoints that belong to a device with this name.</summary>
    public static (AudioEndpoint? output, AudioEndpoint? mic) FindFor(string deviceName)
    {
        var n = Norm(deviceName);
        var token = ModelToken(deviceName);
        var matches = All().Where(e =>
        {
            var en = Norm(e.Name);
            return (n.Length >= 4 && en.Contains(n)) || (token != null && en.Contains(token));
        }).ToList();
        static bool HandsFree(AudioEndpoint e) => Norm(e.Name).Contains("handsfree");
        var output = matches.Where(e => !e.IsCapture).OrderBy(HandsFree).FirstOrDefault();
        var mic = matches.Where(e => e.IsCapture).OrderBy(e => !HandsFree(e)).FirstOrDefault()
                  ?? matches.FirstOrDefault(e => e.IsCapture);
        return (output, mic);
    }

    private static IAudioEndpointVolume? Volume(string id)
    {
        var en = Enumerator();
        if (en.GetDevice(id, out var dev) != 0) return null;
        var iid = VolumeIid;
        return dev.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out var o) == 0 ? (IAudioEndpointVolume)o : null;
    }

    public static int? GetVolume(string id)
    {
        try { var v = Volume(id); if (v != null && v.GetMasterVolumeLevelScalar(out var l) == 0) return (int)Math.Round(l * 100); }
        catch { }
        return null;
    }

    public static bool? GetMute(string id)
    {
        try { var v = Volume(id); if (v != null && v.GetMute(out var m) == 0) return m; }
        catch { }
        return null;
    }

    public static void SetVolume(string id, int percent)
    {
        var v = Volume(id) ?? throw new InvalidOperationException("This sound device is not available right now.");
        var ctx = Guid.Empty;
        Marshal.ThrowExceptionForHR(v.SetMasterVolumeLevelScalar(Math.Clamp(percent, 0, 100) / 100f, ref ctx));
    }

    public static void SetMute(string id, bool mute)
    {
        var v = Volume(id) ?? throw new InvalidOperationException("This sound device is not available right now.");
        var ctx = Guid.Empty;
        Marshal.ThrowExceptionForHR(v.SetMute(mute, ref ctx));
    }

    public static void SetDefault(string id)
    {
        var pc = (IPolicyConfig)new PolicyConfigCom();
        foreach (var role in new[] { ERole.Console, ERole.Multimedia, ERole.Communications })
            Marshal.ThrowExceptionForHR(pc.SetDefaultEndpoint(id, role));
    }
}

/// <summary>The "Sound" section for a headset page: volume, mute, microphone, default device.</summary>
public sealed class AudioControls
{
    private readonly Func<string> _name;
    private AudioEndpoint? _out, _mic;
    public RangeSetting Volume { get; }
    public ToggleSetting Mute { get; }
    public RangeSetting MicLevel { get; }
    public ToggleSetting MicMute { get; }
    public InfoSetting Status { get; }
    public CommandSetting Defaults { get; }

    public AudioControls(Func<string> deviceName)
    {
        _name = deviceName;
        Volume = new RangeSetting("sound.volume", "Headset volume", 0, 100, 1, 50, v => Task.Run(() => { if (_out != null) AudioEndpoints.SetVolume(_out.Id, v); }))
        { Unit = "%", Persist = false, DebounceMs = 40 };
        Mute = new ToggleSetting("sound.mute", "Mute headset", false, v => Task.Run(() => { if (_out != null) AudioEndpoints.SetMute(_out.Id, v); })) { Persist = false };
        MicLevel = new RangeSetting("sound.mic", "Microphone level", 0, 100, 1, 50, v => Task.Run(() => { if (_mic != null) AudioEndpoints.SetVolume(_mic.Id, v); }))
        { Unit = "%", Persist = false, DebounceMs = 40 };
        MicMute = new ToggleSetting("sound.micmute", "Mute microphone", false, v => Task.Run(() => { if (_mic != null) AudioEndpoints.SetMute(_mic.Id, v); })) { Persist = false };
        Status = new InfoSetting("sound.devices", "Windows sound devices");
        Defaults = new CommandSetting("sound.default", "Use this headset for",
            new CommandItem("Sound output (default speaker)", () => Task.Run(() =>
            {
                if (_out == null) throw new InvalidOperationException("The headset is not a sound device in Windows right now.");
                AudioEndpoints.SetDefault(_out.Id); Toast.Show($"Sound now plays through {_out.Name}");
            })),
            new CommandItem("Microphone (default mic)", () => Task.Run(() =>
            {
                if (_mic == null) throw new InvalidOperationException("No microphone found for this headset.");
                AudioEndpoints.SetDefault(_mic.Id); Toast.Show($"Microphone set to {_mic.Name}");
            })));
    }

    public IEnumerable<DeviceSetting> Settings => new DeviceSetting[] { Volume, Mute, MicLevel, MicMute, Defaults, Status };

    public Task RefreshAsync() => Task.Run(() =>
    {
        (_out, _mic) = AudioEndpoints.FindFor(_name());
        Volume.IsEnabled = Mute.IsEnabled = _out != null;
        MicLevel.IsEnabled = MicMute.IsEnabled = _mic != null;
        if (_out != null)
        {
            if (AudioEndpoints.GetVolume(_out.Id) is int v) Volume.SetFromDevice(v);
            if (AudioEndpoints.GetMute(_out.Id) is bool m) Mute.SetFromDevice(m);
        }
        if (_mic != null)
        {
            if (AudioEndpoints.GetVolume(_mic.Id) is int v) MicLevel.SetFromDevice(v);
            if (AudioEndpoints.GetMute(_mic.Id) is bool m) MicMute.SetFromDevice(m);
        }
        Status.Text = _out == null && _mic == null
            ? "Not connected as a sound device right now."
            : string.Join("\n", new[] { _out?.Name, _mic?.Name }.Where(s => s != null));
    });
}
