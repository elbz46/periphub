using System.Security.Cryptography;
using System.Text;

namespace PeriphHub.Core.Integrations;

/// <summary>Settings for the integrations. Tokens are encrypted with Windows DPAPI (only your Windows account can read them).</summary>
public sealed class IntegrationsConfig
{
    public string HaUrl { get; set; } = "http://homeassistant.local:8123";
    public string? HaTokenProtected { get; set; }
    public bool HaIgnoreCert { get; set; } = true;

    public string SpotifyClientId { get; set; } = "";
    public string? SpotifyRefreshProtected { get; set; }

    public string ShareXPath { get; set; } = "";

    public bool HeadsetAutoSwitch { get; set; } = true;
    public bool HeadsetSwitchBack { get; set; } = true;
    public bool HeadsetPauseOnDisconnect { get; set; } = true;
}

public static class Secrets
{
    private static IntegrationsConfig? _cfg;
    public static IntegrationsConfig Config => _cfg ??= SettingsStore.Instance.GetApp<IntegrationsConfig>("integrations") ?? new IntegrationsConfig();
    public static void Save() => SettingsStore.Instance.SetApp("integrations", Config);

    public static string? Protect(string? plain) => string.IsNullOrEmpty(plain) ? null
        : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), null, DataProtectionScope.CurrentUser));

    public static string? Unprotect(string? blob)
    {
        if (string.IsNullOrEmpty(blob)) return null;
        try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(blob), null, DataProtectionScope.CurrentUser)); }
        catch { return null; }
    }

    /// <summary>Small password / token prompt.</summary>
    public static string? Ask(string title, string prompt)
    {
        var box = new System.Windows.Controls.PasswordBox { Margin = new System.Windows.Thickness(0, 8, 0, 14), MinWidth = 380 };
        var ok = new System.Windows.Controls.Button { Content = "Save", IsDefault = true, Padding = new System.Windows.Thickness(16, 5, 16, 5) };
        var panel = new System.Windows.Controls.StackPanel { Margin = new System.Windows.Thickness(20) };
        panel.Children.Add(new System.Windows.Controls.TextBlock { Text = prompt, TextWrapping = System.Windows.TextWrapping.Wrap, MaxWidth = 380 });
        panel.Children.Add(box);
        panel.Children.Add(ok);
        var w = new System.Windows.Window
        {
            Title = title, Content = panel, SizeToContent = System.Windows.SizeToContent.WidthAndHeight,
            WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen, ResizeMode = System.Windows.ResizeMode.NoResize,
        };
        ok.Click += (_, _) => w.DialogResult = true;
        box.Loaded += (_, _) => box.Focus();
        return w.ShowDialog() == true ? box.Password : null;
    }
}
