namespace PeriphHub.Core.Integrations;

/// <summary>Sign-in / setup for Home Assistant, Spotify and ShareX.</summary>
public sealed class ConnectionsPage : ManagedDevice
{
    public ConnectionsPage() : base("app:connections", "Connections", "", DeviceKind.Other)
    {
        Transport = "Home Assistant · Spotify · ShareX";
        var c = Secrets.Config;

        // ---- Home Assistant
        var haStatus = Add("Home Assistant", new InfoSetting("ha.status", "Status", c.HaTokenProtected == null ? "Not set up" : "Token saved – press Test")
        {
            Description = "Use deck / phone buttons to toggle lights and switches, run HA scenes and scripts, and show live states (temperatures, doors…).",
        });
        Add("Home Assistant", new TextSetting("ha.url", "Home Assistant address", c.HaUrl, v =>
        {
            c.HaUrl = v.Trim().TrimEnd('/'); Secrets.Save(); HomeAssistant.Instance.Reset();
            return Task.CompletedTask;
        }) { Persist = false, Hint = "e.g. http://192.168.1.50:8123 or https://homeassistant.local:8123" });
        Add("Home Assistant", new ToggleSetting("ha.cert", "Accept a self-signed certificate (home network)", c.HaIgnoreCert, v =>
        {
            c.HaIgnoreCert = v; Secrets.Save(); HomeAssistant.Instance.Reset();
            return Task.CompletedTask;
        }) { Persist = false, Description = "Needed if your Proxmox/HA uses HTTPS with its own certificate." });
        Add("Home Assistant", new CommandSetting("ha.cmds", "Access token",
            new CommandItem("Set access token…", () =>
            {
                var t = Secrets.Ask("Home Assistant token",
                    "In Home Assistant: click your profile (bottom left) › Security › Long-lived access tokens › Create token. Paste it here:");
                if (!string.IsNullOrWhiteSpace(t))
                {
                    c.HaTokenProtected = Secrets.Protect(t.Trim()); Secrets.Save(); HomeAssistant.Instance.Reset();
                    haStatus.Text = "Token saved (encrypted) – press Test";
                }
                return Task.CompletedTask;
            }),
            new CommandItem("Test", async () => haStatus.Text = await HomeAssistant.Instance.TestAsync()),
            new CommandItem("List my entities", async () =>
            {
                var all = await HomeAssistant.Instance.AllAsync();
                haStatus.Text = $"{all.Count} entities. Some you can use:\n" + string.Join("\n",
                    all.Where(e => e.EntityId.Split('.')[0] is "light" or "switch" or "scene" or "script" or "sensor" or "climate" or "fan" or "cover")
                       .Take(40).Select(e => $"{e.EntityId}   ({e.Name}: {e.State}{e.Unit})"));
            })));

        // ---- Spotify
        var spStatus = Add("Spotify", new InfoSetting("sp.status", "Status", Spotify.Instance.Connected ? "Connected" : "Not connected")
        {
            Description = "Adds: like the current song, add it to a playlist, play a playlist, move playback to another device. " +
                          "(Play/pause/next already work without this.) Control features need Spotify Premium.",
        });
        Add("Spotify", new InfoSetting("sp.how", "One-time setup",
            "1. Go to developer.spotify.com/dashboard, log in, “Create app” (any name).\n" +
            $"2. Redirect URI: {Spotify.RedirectUri}   ·   tick “Web API”.\n" +
            "3. Open the app's Settings, copy the Client ID into the box below, then press Connect."));
        Add("Spotify", new TextSetting("sp.client", "Client ID", c.SpotifyClientId, v => { c.SpotifyClientId = v.Trim(); Secrets.Save(); return Task.CompletedTask; })
        { Persist = false, Hint = "32 letters/numbers from your Spotify app" });
        Add("Spotify", new CommandSetting("sp.cmds", "Account",
            new CommandItem("Connect (opens the browser)", async () => spStatus.Text = await Spotify.Instance.ConnectAsync()),
            new CommandItem("Disconnect", () => { Spotify.Instance.Disconnect(); spStatus.Text = "Not connected"; return Task.CompletedTask; })));

        // ---- ShareX
        var sxStatus = Add("ShareX", new InfoSetting("sx.status", "ShareX", ShareX.Find() is { } f ? $"Found: {f}" : "Not found – install ShareX or set its location below")
        {
            Description = "Deck / phone / mouse buttons can start any ShareX task: screenshots, recording, GIFs, OCR, colour picker, scrolling capture…",
        });
        Add("ShareX", new TextSetting("sx.path", "ShareX location (optional)", c.ShareXPath, v =>
        {
            c.ShareXPath = v.Trim().Trim('"'); Secrets.Save();
            sxStatus.Text = ShareX.Find() is { } p ? $"Found: {p}" : "Not found";
            return Task.CompletedTask;
        }) { Persist = false, Hint = @"Leave empty to find it automatically (also works with the Steam version)" });
        Add("ShareX", new CommandSetting("sx.cmds", "Try it",
            new CommandItem("Region screenshot", () => { ShareX.Run("RectangleRegion"); return Task.CompletedTask; }),
            new CommandItem("Open screenshots folder", () => { ShareX.Run("OpenScreenshotsFolder"); return Task.CompletedTask; })));
    }

    public override string Glyph => "";
}
