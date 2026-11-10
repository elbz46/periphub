using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PeriphHub.Core.Deck;

/// <summary>Bridge from the Wi-Fi server to the deck window (runs calls on the UI thread).</summary>
public interface IPhoneDeck
{
    object PhoneState();
    void PhonePress(int index, bool down);
    void PhoneNav(string dir);
    byte[]? PhoneImage(int index);
}

/// <summary>
/// Tiny web server so a phone on the same Wi-Fi can use the deck. Every request must carry the
/// secret pairing code from the QR code; nothing is reachable without it.
/// </summary>
public sealed class PhoneServer
{
    public static PhoneServer Instance { get; } = new();
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    public IPhoneDeck? Deck { get; set; }
    public string Status { get; private set; } = "Off";
    public event Action? StatusChanged;

    public static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant();

    public string? Url
    {
        get
        {
            var ip = LocalIp();
            var c = DeckStore.Config;
            return ip == null ? null : $"http://{ip}:{c.PhonePort}/?t={c.PhoneToken}";
        }
    }

    /// <summary>The PC's address on the home network (Wi-Fi or Ethernet with a gateway).</summary>
    public static string? LocalIp()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .OrderBy(n => n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? 0 : 1)
                .Select(n => n.GetIPProperties())
                .Where(p => p.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any)))
                .SelectMany(p => p.UnicastAddresses)
                .Select(a => a.Address)
                .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a))?.ToString();
        }
        catch { return null; }
    }

    public void Apply()
    {
        Stop();
        var c = DeckStore.Config;
        if (!c.PhoneEnabled) { SetStatus("Off"); return; }
        if (string.IsNullOrEmpty(c.PhoneToken)) { c.PhoneToken = NewToken(); DeckStore.Save(false); }
        try
        {
            _listener = new TcpListener(IPAddress.Any, c.PhonePort);
            _listener.Start();
            _cts = new CancellationTokenSource();
            _ = AcceptLoop(_listener, _cts.Token);
            SetStatus(Url is { } u ? $"Running – open {u.Split('?')[0]} on your phone (or scan the QR code)" : "Running, but this PC is not on a network");
        }
        catch (Exception ex)
        {
            SetStatus($"Could not start on port {c.PhonePort}: {ex.Message}");
            Log.Warn(Status);
        }
    }

    public void Stop()
    {
        try { _cts?.Cancel(); _listener?.Stop(); } catch { }
        _listener = null;
    }

    private void SetStatus(string s) { Status = s; StatusChanged?.Invoke(); }

    private async Task AcceptLoop(TcpListener l, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await l.AcceptTcpClientAsync(ct); }
            catch { return; }
            _ = Task.Run(() => Handle(client));
        }
    }

    private async Task Handle(TcpClient client)
    {
        using (client)
        {
            try
            {
                client.ReceiveTimeout = 5000;
                var stream = client.GetStream();
                var head = new StringBuilder();
                var buf = new byte[1];
                while (head.Length < 8192)
                {
                    int n = await stream.ReadAsync(buf);
                    if (n == 0) return;
                    head.Append((char)buf[0]);
                    if (head.Length >= 4 && head.ToString(head.Length - 4, 4) == "\r\n\r\n") break;
                }
                var first = head.ToString().Split("\r\n")[0].Split(' ');
                if (first.Length < 2) return;
                var method = first[0];
                var uri = new Uri("http://x" + first[1]);
                var q = System.Web.HttpUtility.ParseQueryString(uri.Query);
                var (status, type, body) = Route(method, uri.AbsolutePath, q);
                var header = $"HTTP/1.1 {status}\r\nContent-Type: {type}\r\nContent-Length: {body.Length}\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(header));
                await stream.WriteAsync(body);
            }
            catch (Exception ex) { Log.Warn($"Phone deck request failed: {ex.Message}"); }
        }
    }

    private (string status, string type, byte[] body) Route(string method, string path, System.Collections.Specialized.NameValueCollection q)
    {
        var token = DeckStore.Config.PhoneToken;
        bool ok = token.Length > 0 && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(q["t"] ?? ""), Encoding.UTF8.GetBytes(token));
        static (string, string, byte[]) Text(string status, string text) => (status, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes(text));
        if (!ok) return Text("403 Forbidden", "Scan the QR code shown in PeriphHub › Control deck › Phone.");
        var deck = Deck;
        if (deck == null) return Text("503 Service Unavailable", "Deck not ready");
        T Ui<T>(Func<T> f) => System.Windows.Application.Current.Dispatcher.Invoke(f);
        void UiDo(Action a) => System.Windows.Application.Current.Dispatcher.Invoke(a);

        switch (path)
        {
            case "/":
                using (var s = typeof(PhoneServer).Assembly.GetManifestResourceStream("phone.html")!)
                using (var ms = new MemoryStream()) { s.CopyTo(ms); return ("200 OK", "text/html; charset=utf-8", ms.ToArray()); }
            case "/api/state":
                return ("200 OK", "application/json", JsonSerializer.SerializeToUtf8Bytes(Ui(deck.PhoneState)));
            case "/api/press" when method == "POST" && int.TryParse(q["i"], out var i):
                UiDo(() => deck.PhonePress(i, q["d"] == "1"));
                return Text("200 OK", "ok");
            case "/api/nav" when method == "POST":
                UiDo(() => deck.PhoneNav(q["dir"] ?? ""));
                return Text("200 OK", "ok");
            case "/api/img" when int.TryParse(q["i"], out var ii):
                var bytes = Ui(() => deck.PhoneImage(ii));
                return bytes == null ? Text("404 Not Found", "") : ("200 OK", "image/png", bytes);
            default:
                return Text("404 Not Found", "Not found");
        }
    }
}
