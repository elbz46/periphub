using System.Collections.Concurrent;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace PeriphHub.Core.Integrations;

public sealed record HaState(string EntityId, string State, string Name, string? Unit);

/// <summary>
/// Home Assistant REST API client (long-lived access token from your HA profile page).
/// Entities shown on the deck are refreshed every few seconds while they are visible.
/// </summary>
public sealed class HomeAssistant
{
    public static HomeAssistant Instance { get; } = new();
    private HttpClient? _http;
    private string? _builtFor;
    private readonly ConcurrentDictionary<string, HaState> _states = new();
    private readonly ConcurrentDictionary<string, DateTime> _wanted = new();
    private Timer? _timer;

    public static readonly HashSet<string> Toggleable = new() { "light", "switch", "fan", "input_boolean", "cover", "media_player", "automation", "climate", "lock", "siren", "humidifier", "vacuum" };

    public string Status { get; private set; } = "Not set up";

    private HttpClient Http()
    {
        var c = Secrets.Config;
        var key = $"{c.HaUrl}|{c.HaTokenProtected}|{c.HaIgnoreCert}";
        if (_http != null && _builtFor == key) return _http;
        var token = Secrets.Unprotect(c.HaTokenProtected) ?? throw new InvalidOperationException("Home Assistant token not set (Connections › Home Assistant)");
        var handler = new SocketsHttpHandler();
        if (c.HaIgnoreCert) handler.SslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;
        _http = new HttpClient(handler) { BaseAddress = new Uri(c.HaUrl.TrimEnd('/') + "/"), Timeout = TimeSpan.FromSeconds(6) };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        _builtFor = key;
        return _http;
    }

    public void Reset() { _http = null; _builtFor = null; _states.Clear(); }

    public async Task<string> TestAsync()
    {
        try
        {
            var r = await Http().GetAsync("api/");
            Status = r.IsSuccessStatusCode ? $"Connected to {Secrets.Config.HaUrl}" :
                r.StatusCode == System.Net.HttpStatusCode.Unauthorized ? "Token rejected – create a new long-lived token" : $"HA answered {(int)r.StatusCode}";
        }
        catch (Exception ex) { Status = $"Can't reach Home Assistant: {ex.Message}"; }
        return Status;
    }

    /// <summary>All entities (for pickers).</summary>
    public async Task<List<HaState>> AllAsync()
    {
        var json = JsonNode.Parse(await Http().GetStringAsync("api/states")) as JsonArray ?? new JsonArray();
        return json.Select(Parse).Where(s => s != null).Select(s => s!).OrderBy(s => s.EntityId).ToList();
    }

    private static HaState? Parse(JsonNode? n)
    {
        if (n == null) return null;
        var id = n["entity_id"]?.GetValue<string>() ?? "";
        var a = n["attributes"];
        return new HaState(id, n["state"]?.GetValue<string>() ?? "?", a?["friendly_name"]?.GetValue<string>() ?? id,
            a?["unit_of_measurement"]?.GetValue<string>());
    }

    /// <summary>Cached state; registers the entity for background refresh.</summary>
    public HaState? Get(string entityId)
    {
        _wanted[entityId] = DateTime.Now;
        _timer ??= new Timer(async _ => await RefreshAsync(), null, 0, 3000);
        return _states.TryGetValue(entityId, out var s) ? s : null;
    }

    private int _busy;
    private async Task RefreshAsync()
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1) return;
        try
        {
            foreach (var (id, at) in _wanted.ToArray())
            {
                if (DateTime.Now - at > TimeSpan.FromSeconds(15)) { _wanted.TryRemove(id, out _); continue; }
                try
                {
                    var r = await Http().GetAsync($"api/states/{id}");
                    if (r.IsSuccessStatusCode && Parse(JsonNode.Parse(await r.Content.ReadAsStringAsync())) is { } st) _states[id] = st;
                }
                catch { }
            }
        }
        finally { _busy = 0; }
    }

    public async Task CallAsync(string domain, string service, JsonObject data)
    {
        try
        {
            var r = await Http().PostAsync($"api/services/{domain}/{service}", new StringContent(data.ToJsonString(), Encoding.UTF8, "application/json"));
            if (!r.IsSuccessStatusCode) Toast.Show($"Home Assistant: {domain}.{service} failed ({(int)r.StatusCode})");
            var id = data["entity_id"]?.GetValue<string>();
            if (id != null) { await Task.Delay(400); _wanted[id] = DateTime.Now; await RefreshAsync(); }
        }
        catch (Exception ex) { Toast.Show($"Home Assistant: {ex.Message}"); Log.Warn($"HA {domain}.{service}: {ex.Message}"); }
    }

    public Task ToggleAsync(string entityId)
    {
        var domain = entityId.Split('.')[0];
        var data = new JsonObject { ["entity_id"] = entityId };
        return domain switch
        {
            "scene" or "script" => CallAsync(domain, "turn_on", data),
            "button" or "input_button" => CallAsync(domain, "press", data),
            "lock" => CallAsync("lock", Get(entityId)?.State == "locked" ? "unlock" : "lock", data),
            "cover" => CallAsync("cover", "toggle", data),
            _ => CallAsync("homeassistant", "toggle", data),
        };
    }

    public Task TurnAsync(string entityId, bool on) =>
        CallAsync("homeassistant", on ? "turn_on" : "turn_off", new JsonObject { ["entity_id"] = entityId });

    /// <summary>"light.turn_on light.desk {\"brightness\":120}" — domain.service, optional entity, optional JSON data.</summary>
    public Task ServiceAsync(string spec)
    {
        spec = spec.Trim();
        var brace = spec.IndexOf('{');
        var json = brace >= 0 ? JsonNode.Parse(spec[brace..]) as JsonObject ?? new JsonObject() : new JsonObject();
        var head = (brace >= 0 ? spec[..brace] : spec).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (head.Length == 0 || !head[0].Contains('.')) { Toast.Show("Home Assistant: use “domain.service entity_id”"); return Task.CompletedTask; }
        var ds = head[0].Split('.');
        if (head.Length > 1) json["entity_id"] = head[1];
        return CallAsync(ds[0], ds[1], json);
    }

    public static string IconFor(HaState? s, string entityId)
    {
        var domain = entityId.Split('.')[0];
        bool on = s?.State is "on" or "open" or "playing" or "unlocked" or "home";
        return domain switch
        {
            "light" => on ? "💡" : "🔅",
            "switch" or "input_boolean" => on ? "🟢" : "⚪",
            "fan" => "🌀",
            "cover" => on ? "🪟" : "▤",
            "lock" => s?.State == "locked" ? "🔒" : "🔓",
            "climate" => "🌡",
            "media_player" => on ? "▶" : "⏸",
            "scene" => "🎬",
            "script" => "📜",
            "person" or "device_tracker" => s?.State == "home" ? "🏠" : "🚶",
            _ => "",
        };
    }
}
