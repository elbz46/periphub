using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace PeriphHub.Core.Integrations;

/// <summary>
/// YouTube Music installed as an app (PWA from Chrome or Edge). Play/pause/next/previous already work
/// through Windows media controls; for like, dislike, shuffle, repeat and volume PeriphHub briefly brings
/// the YouTube Music window forward, presses YouTube Music's own keyboard shortcut and goes straight back.
/// </summary>
public static class YouTubeMusic
{
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc cb, IntPtr l);
    private delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, IntPtr pid);
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint a, uint b, bool attach);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    private const int SW_RESTORE = 9, SW_SHOWMINNOACTIVE = 7;

    public static readonly (string id, string label, string defaultKey)[] Commands =
    {
        ("like", "Like (thumbs up)", "Shift+Plus"),
        ("dislike", "Dislike (thumbs down)", "Shift+Minus"),
        ("shuffle", "Shuffle", "S"),
        ("repeat", "Repeat", "R"),
        ("volup", "Volume up", "Plus"),
        ("voldown", "Volume down", "Minus"),
        ("mute", "Mute", "M"),
        ("search", "Search", "/"),
    };

    public static string KeyFor(string id) =>
        Secrets.Config.YtmKeys.TryGetValue(id, out var k) && !string.IsNullOrWhiteSpace(k) ? k : Commands.First(c => c.id == id).defaultKey;

    /// <summary>The YouTube Music app window (Chrome / Edge PWA, or a browser tab as a fallback).</summary>
    public static IntPtr FindWindow()
    {
        IntPtr app = IntPtr.Zero, tab = IntPtr.Zero;
        EnumWindows((h, _) =>
        {
            if (!IsWindowVisible(h) && !IsIconic(h)) return true;
            var cls = new StringBuilder(64); GetClassName(h, cls, 64);
            if (cls.ToString() != "Chrome_WidgetWin_1") return true;
            var title = new StringBuilder(256); GetWindowText(h, title, 256);
            var t = title.ToString();
            if (!t.Contains("YouTube Music", StringComparison.OrdinalIgnoreCase)) return true;
            // Browser windows end with " - Google Chrome" / " - Microsoft Edge"; the installed app does not.
            if (t.EndsWith("Google Chrome") || t.EndsWith("Microsoft Edge") || t.Contains("Microsoft​ Edge")) { if (tab == IntPtr.Zero) tab = h; }
            else if (app == IntPtr.Zero) app = h;
            return true;
        }, IntPtr.Zero);
        return app != IntPtr.Zero ? app : tab;
    }

    private static bool Focus(IntPtr h)
    {
        var fg = GetForegroundWindow();
        uint fgThread = GetWindowThreadProcessId(fg, IntPtr.Zero), me = GetCurrentThreadId();
        // Windows only lets the active app change focus; joining its input queue (and tapping Alt) is the standard workaround.
        keybd_event(0x12, 0, 0, UIntPtr.Zero); keybd_event(0x12, 0, 2, UIntPtr.Zero);
        if (fgThread != me) AttachThreadInput(me, fgThread, true);
        var ok = SetForegroundWindow(h);
        if (fgThread != me) AttachThreadInput(me, fgThread, false);
        return ok;
    }

    public static Task RunAsync(string command) => Task.Run(async () =>
    {
        if (command == "open") { Open(); return; }
        var win = FindWindow();
        if (win == IntPtr.Zero) { Toast.Show("YouTube Music isn't open"); return; }
        var previous = GetForegroundWindow();
        bool wasMin = IsIconic(win);
        try
        {
            if (wasMin) ShowWindow(win, SW_RESTORE);
            if (!Focus(win)) { await Task.Delay(80); Focus(win); }
            await Task.Delay(wasMin ? 220 : 90);
            if (GetForegroundWindow() != win) { Toast.Show("Couldn't reach YouTube Music – click its window once and try again"); return; }
            Input.Tap(Input.ParseCombo(KeyFor(command)));
            await Task.Delay(80);
        }
        finally
        {
            if (wasMin) ShowWindow(win, SW_SHOWMINNOACTIVE);
            if (previous != IntPtr.Zero && previous != win) Focus(previous);
        }
        if (command is "like" or "dislike") Toast.Show(command == "like" ? $"👍 {MediaController.Instance.Title}" : $"👎 {MediaController.Instance.Title}");
    });

    /// <summary>Opens the installed YouTube Music app (its Start-menu shortcut), or the website.</summary>
    public static void Open()
    {
        var win = FindWindow();
        if (win != IntPtr.Zero)
        {
            if (IsIconic(win)) ShowWindow(win, SW_RESTORE);
            Focus(win);
            return;
        }
        var lnk = FindShortcut();
        try
        {
            Process.Start(new ProcessStartInfo(lnk ?? "https://music.youtube.com") { UseShellExecute = true });
        }
        catch (Exception ex) { Toast.Show($"YouTube Music: {ex.Message}"); }
    }

    public static string? FindShortcut()
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        };
        foreach (var r in roots)
        {
            try
            {
                var f = Directory.EnumerateFiles(r, "YouTube Music*.lnk", SearchOption.AllDirectories).FirstOrDefault();
                if (f != null) return f;
            }
            catch { }
        }
        return null;
    }
}
