namespace PeriphHub.Core;

/// <summary>Moves Windows sound / mic to a headset when it connects, and back (plus pause) when it disconnects.</summary>
public static class HeadsetSwitch
{
    private static string? _prevOut, _prevIn;

    public static async Task OnChangedAsync(ManagedDevice headset, bool online)
    {
        var cfg = Integrations.Secrets.Config;
        try
        {
            if (online && cfg.HeadsetAutoSwitch)
            {
                // Bluetooth / dongle audio devices appear a few seconds after the headset itself.
                for (int i = 0; i < 20; i++)
                {
                    var (output, mic) = AudioEndpoints.FindFor(headset.Name);
                    if (output != null)
                    {
                        var curOut = AudioEndpoints.DefaultOutputId();
                        var curIn = AudioEndpoints.DefaultInputId();
                        if (curOut != output.Id) { _prevOut = curOut; AudioEndpoints.SetDefault(output.Id); }
                        if (mic != null && curIn != mic.Id) { _prevIn = curIn; AudioEndpoints.SetDefault(mic.Id); }
                        Toast.Show($"🎧 Sound switched to {headset.Name}");
                        return;
                    }
                    await Task.Delay(1000);
                }
            }
            else if (!online)
            {
                if (cfg.HeadsetPauseOnDisconnect && MediaController.Instance.IsPlaying) await MediaController.Instance.PlayPauseAsync();
                if (cfg.HeadsetSwitchBack)
                {
                    await Task.Delay(1500);
                    var active = AudioEndpoints.All();
                    if (_prevOut != null && active.Any(e => e.Id == _prevOut)) AudioEndpoints.SetDefault(_prevOut);
                    if (_prevIn != null && active.Any(e => e.Id == _prevIn)) AudioEndpoints.SetDefault(_prevIn);
                    if (_prevOut != null) Toast.Show("🔈 Sound switched back");
                    _prevOut = _prevIn = null;
                }
            }
        }
        catch (Exception ex) { Log.Warn($"Headset sound switch: {ex.Message}"); }
    }
}
