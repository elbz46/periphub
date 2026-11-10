using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace PeriphHub.Core.Integrations;

/// <summary>
/// Spotify Web API with your own (free) Spotify developer app, signed in once with PKCE
/// (no client secret). Redirect URI to register: http://127.0.0.1:8771/callback
/// </summary>
public sealed class Spotify
{
    public static Spotify Instance { get; } = new();
    public const string RedirectUri = "http://127.0.0.1:8771/callback";
    private const string Scopes = "user-read-currently-playing user-read-playback-state user-modify-playback-state " +
                                  "user-library-read user-library-modify playlist-modify-private playlist-modify-public playlist-read-private";
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private string? _access;
    private DateTime _expires;

    public string Status { get; private set; } = "Not connected";
    public bool Connected => Secrets.Unprotect(Secrets.Config.SpotifyRefreshProtected) != null;

    private static string B64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Opens the browser to sign in, waits for Spotify to redirect back to this PC.</summary>
    public async Task<string> ConnectAsync()
    {
        var clientId = Secrets.Config.SpotifyClientId.Trim();
        if (clientId.Length == 0) return Status = "Paste your Spotify app's Client ID first";
        var verifier = B64Url(RandomNumberGenerator.GetBytes(48));
        var challenge = B64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var state = B64Url(RandomNumberGenerator.GetBytes(12));
        var url = "https://accounts.spotify.com/authorize?response_type=code" +
                  $"&client_id={Uri.EscapeDataString(clientId)}&scope={Uri.EscapeDataString(Scopes)}" +
                  $"&redirect_uri={Uri.EscapeDataString(RedirectUri)}&code_challenge_method=S256&code_challenge={challenge}&state={state}";

        var listener = new TcpListener(IPAddress.Loopback, 8771);
        try { listener.Start(); }
        catch (Exception ex) { return Status = $"Port 8771 is busy: {ex.Message}"; }
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            string? code = null;
            while (code == null)
            {
                using var client = await listener.AcceptTcpClientAsync(cts.Token);
                var stream = client.GetStream();
                var buf = new byte[4096];
                int n = await stream.ReadAsync(buf, cts.Token);
                var line = Encoding.ASCII.GetString(buf, 0, n).Split("\r\n")[0];
                var path = line.Split(' ').ElementAtOrDefault(1) ?? "";
                if (!path.StartsWith("/callback")) { await Reply(stream, "404 Not Found", ""); continue; }
                var q = System.Web.HttpUtility.ParseQueryString(new Uri("http://x" + path).Query);
                if (q["state"] != state || q["code"] == null)
                {
                    await Reply(stream, "200 OK", "<h2>Spotify sign-in was cancelled.</h2>");
                    return Status = "Sign-in cancelled";
                }
                code = q["code"];
                await Reply(stream, "200 OK", "<h2>PeriphHub is connected to Spotify. You can close this tab.</h2>");
            }
            var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code", ["code"] = code!, ["redirect_uri"] = RedirectUri,
                ["client_id"] = clientId, ["code_verifier"] = verifier,
            });
            await StoreTokens(await _http.PostAsync("https://accounts.spotify.com/api/token", form));
            return Status = "Connected to Spotify";
        }
        catch (OperationCanceledException) { return Status = "Timed out waiting for the Spotify sign-in"; }
        catch (Exception ex) { return Status = $"Spotify sign-in failed: {ex.Message}"; }
        finally { listener.Stop(); }
    }

    private static async Task Reply(NetworkStream s, string status, string html)
    {
        var body = Encoding.UTF8.GetBytes($"<html><body style='font-family:sans-serif;padding:40px'>{html}</body></html>");
        await s.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n"));
        await s.WriteAsync(body);
    }

    private async Task StoreTokens(HttpResponseMessage r)
    {
        var json = JsonNode.Parse(await r.Content.ReadAsStringAsync());
        if (!r.IsSuccessStatusCode) throw new InvalidOperationException(json?["error_description"]?.GetValue<string>() ?? r.StatusCode.ToString());
        _access = json!["access_token"]!.GetValue<string>();
        _expires = DateTime.Now.AddSeconds(json["expires_in"]?.GetValue<int>() ?? 3600) - TimeSpan.FromMinutes(2);
        if (json["refresh_token"]?.GetValue<string>() is { } refresh)
        {
            Secrets.Config.SpotifyRefreshProtected = Secrets.Protect(refresh);
            Secrets.Save();
        }
    }

    public void Disconnect()
    {
        Secrets.Config.SpotifyRefreshProtected = null; Secrets.Save();
        _access = null; Status = "Not connected";
    }

    private async Task<string> TokenAsync()
    {
        if (_access != null && DateTime.Now < _expires) return _access;
        var refresh = Secrets.Unprotect(Secrets.Config.SpotifyRefreshProtected)
                      ?? throw new InvalidOperationException("Connect Spotify first (Connections › Spotify)");
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token", ["refresh_token"] = refresh, ["client_id"] = Secrets.Config.SpotifyClientId.Trim(),
        });
        await StoreTokens(await _http.PostAsync("https://accounts.spotify.com/api/token", form));
        return _access!;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod m, string path, JsonNode? body = null)
    {
        var req = new HttpRequestMessage(m, "https://api.spotify.com/v1/" + path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await TokenAsync());
        if (body != null) req.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        var r = await _http.SendAsync(req);
        if (r.StatusCode == HttpStatusCode.Forbidden) throw new InvalidOperationException("Spotify refused – playback control needs Spotify Premium");
        if (r.StatusCode == HttpStatusCode.NotFound && path.StartsWith("me/player")) throw new InvalidOperationException("No active Spotify player – start playing in Spotify first");
        return r;
    }

    private async Task<(string? id, string? uri, string? name)> CurrentAsync()
    {
        var r = await SendAsync(HttpMethod.Get, "me/player/currently-playing");
        if (r.StatusCode == HttpStatusCode.NoContent) return (null, null, null);
        var j = JsonNode.Parse(await r.Content.ReadAsStringAsync())?["item"];
        return (j?["id"]?.GetValue<string>(), j?["uri"]?.GetValue<string>(), j?["name"]?.GetValue<string>());
    }

    private static string? PlaylistId(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        input = input.Trim();
        if (input.StartsWith("spotify:playlist:")) return input[17..];
        var i = input.IndexOf("/playlist/", StringComparison.Ordinal);
        if (i >= 0) return input[(i + 10)..].Split('?', '/')[0];
        return input;
    }

    public Task Run(string id, string? arg) => Task.Run(async () =>
    {
        try
        {
            switch (id)
            {
                case "spotify:like":
                {
                    var (track, _, name) = await CurrentAsync();
                    if (track == null) { Toast.Show("Nothing is playing on Spotify"); return; }
                    var c = JsonNode.Parse(await (await SendAsync(HttpMethod.Get, $"me/tracks/contains?ids={track}")).Content.ReadAsStringAsync()) as JsonArray;
                    bool liked = c?[0]?.GetValue<bool>() == true;
                    await SendAsync(liked ? HttpMethod.Delete : HttpMethod.Put, $"me/tracks?ids={track}");
                    Toast.Show(liked ? $"Removed from Liked Songs: {name}" : $"♥ Liked: {name}");
                    break;
                }
                case "spotify:addto":
                {
                    var pl = PlaylistId(arg);
                    var (_, uri, name) = await CurrentAsync();
                    if (pl == null || uri == null) { Toast.Show("Need a playlist link and a playing song"); return; }
                    var r = await SendAsync(HttpMethod.Post, $"playlists/{pl}/tracks", new JsonObject { ["uris"] = new JsonArray(uri) });
                    Toast.Show(r.IsSuccessStatusCode ? $"Added to playlist: {name}" : "Could not add to that playlist");
                    break;
                }
                case "spotify:playlist":
                {
                    var pl = PlaylistId(arg);
                    if (pl == null) return;
                    await SendAsync(HttpMethod.Put, "me/player/play", new JsonObject { ["context_uri"] = $"spotify:playlist:{pl}" });
                    break;
                }
                case "spotify:device":
                {
                    var j = JsonNode.Parse(await (await SendAsync(HttpMethod.Get, "me/player/devices")).Content.ReadAsStringAsync());
                    var devices = j?["devices"] as JsonArray ?? new JsonArray();
                    var want = (arg ?? "").Trim();
                    var d = devices.FirstOrDefault(x => x?["name"]?.GetValue<string>().Contains(want, StringComparison.OrdinalIgnoreCase) == true);
                    if (d == null)
                    {
                        Toast.Show($"Spotify devices: {string.Join(", ", devices.Select(x => x?["name"]?.GetValue<string>()))}");
                        return;
                    }
                    await SendAsync(HttpMethod.Put, "me/player", new JsonObject { ["device_ids"] = new JsonArray(d["id"]!.GetValue<string>()), ["play"] = true });
                    Toast.Show($"Spotify now playing on {d["name"]}");
                    break;
                }
            }
        }
        catch (Exception ex) { Toast.Show($"Spotify: {ex.Message}"); Log.Warn($"Spotify {id}: {ex.Message}"); }
    });
}
