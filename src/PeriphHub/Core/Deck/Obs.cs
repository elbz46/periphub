using System.IO;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PeriphHub.Core;

/// <summary>
/// Minimal OBS client for obs-websocket 5 (built into OBS Studio 28+):
/// Tools › WebSocket Server Settings › Enable. Connects on demand and stays connected.
/// </summary>
public sealed class Obs
{
    public static Obs Instance { get; } = new();
    private ClientWebSocket? _ws;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private int _requestId;

    public string Status { get; private set; } = "Not connected";

    public static string? Password
    {
        get
        {
            var p = Deck.DeckStore.Config.ObsPasswordProtected;
            if (string.IsNullOrEmpty(p)) return null;
            try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(p), null, DataProtectionScope.CurrentUser)); }
            catch { return null; }
        }
        set
        {
            Deck.DeckStore.Config.ObsPasswordProtected = string.IsNullOrEmpty(value) ? null
                : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser));
            Deck.DeckStore.Save(false);
        }
    }

    public void Disconnect()
    {
        try { _ws?.Abort(); } catch { }
        _ws = null;
        Status = "Not connected";
    }

    private async Task EnsureConnectedAsync()
    {
        if (_ws is { State: WebSocketState.Open }) return;
        Disconnect();
        var cfg = Deck.DeckStore.Config;
        var ws = new ClientWebSocket();
        using var cts = new CancellationTokenSource(4000);
        try { await ws.ConnectAsync(new Uri($"ws://{cfg.ObsHost}:{cfg.ObsPort}"), cts.Token); }
        catch (Exception ex)
        {
            Status = "OBS is not running, or its WebSocket server is off";
            throw new InvalidOperationException($"{Status} ({ex.Message})");
        }

        var hello = await ReceiveAsync(ws, cts.Token);
        var identify = new JsonObject { ["rpcVersion"] = 1, ["eventSubscriptions"] = 0 };
        if (hello?["d"]?["authentication"] is JsonObject auth)
        {
            var pw = Password ?? throw new InvalidOperationException(Status = "OBS needs its WebSocket password (Control deck › OBS)");
            string B64(string s) => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(s)));
            var secret = B64(pw + auth["salt"]);
            identify["authentication"] = B64(secret + auth["challenge"]);
        }
        await SendRawAsync(ws, new JsonObject { ["op"] = 1, ["d"] = identify }, cts.Token);
        var reply = await ReceiveAsync(ws, cts.Token);
        if (reply?["op"]?.GetValue<int>() != 2)
        {
            ws.Abort();
            throw new InvalidOperationException(Status = "OBS refused the connection – check the password");
        }
        _ws = ws;
        Status = $"Connected to OBS at {cfg.ObsHost}:{cfg.ObsPort}";
        _ = Task.Run(() => DrainAsync(ws));
    }

    /// <summary>Reads (and ignores) replies so the socket buffer never fills.</summary>
    private static async Task DrainAsync(ClientWebSocket ws)
    {
        try { while (ws.State == WebSocketState.Open) await ReceiveAsync(ws, CancellationToken.None); }
        catch { }
    }

    private static async Task SendRawAsync(ClientWebSocket ws, JsonNode msg, CancellationToken ct) =>
        await ws.SendAsync(Encoding.UTF8.GetBytes(msg.ToJsonString()), WebSocketMessageType.Text, true, ct);

    private static async Task<JsonNode?> ReceiveAsync(ClientWebSocket ws, CancellationToken ct)
    {
        var buf = new byte[16384];
        using var ms = new MemoryStream();
        WebSocketReceiveResult r;
        do
        {
            r = await ws.ReceiveAsync(buf, ct);
            if (r.MessageType == WebSocketMessageType.Close) throw new WebSocketException("closed");
            ms.Write(buf, 0, r.Count);
        } while (!r.EndOfMessage);
        return JsonNode.Parse(ms.ToArray());
    }

    public async Task SendAsync(string requestType, object? data = null)
    {
        await _lock.WaitAsync();
        try
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    await EnsureConnectedAsync();
                    var msg = new JsonObject
                    {
                        ["op"] = 6,
                        ["d"] = new JsonObject
                        {
                            ["requestType"] = requestType,
                            ["requestId"] = (++_requestId).ToString(),
                            ["requestData"] = data == null ? null : JsonSerializer.SerializeToNode(data),
                        },
                    };
                    await SendRawAsync(_ws!, msg, CancellationToken.None);
                    return;
                }
                catch (WebSocketException) when (attempt == 0) { Disconnect(); }
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"OBS {requestType}: {ex.Message}");
            Toast.Show(ex.Message);
        }
        finally { _lock.Release(); }
    }

    public async Task<string> TestAsync()
    {
        Disconnect();
        await _lock.WaitAsync();
        try { await EnsureConnectedAsync(); }
        catch (Exception ex) { return ex.Message; }
        finally { _lock.Release(); }
        return Status;
    }
}
