using System.Diagnostics;
using System.IO;

namespace PeriphHub.Core.Integrations;

/// <summary>
/// Runs ShareX tasks through its command line (ShareX forwards them to the running copy),
/// e.g. "ShareX.exe -RectangleRegion".
/// </summary>
public static class ShareX
{
    public static readonly (string task, string label)[] Tasks =
    {
        ("RectangleRegion", "Screenshot: region"),
        ("PrintScreen", "Screenshot: full screen"),
        ("ActiveWindow", "Screenshot: active window"),
        ("ActiveMonitor", "Screenshot: active monitor"),
        ("LastRegion", "Screenshot: last region again"),
        ("ScrollingCapture", "Scrolling capture"),
        ("ScreenRecorder", "Record video (region)"),
        ("ScreenRecorderActiveWindow", "Record video (active window)"),
        ("ScreenRecorderGIF", "Record GIF (region)"),
        ("StopScreenRecording", "Stop recording"),
        ("PauseScreenRecording", "Pause / resume recording"),
        ("AbortScreenRecording", "Cancel recording"),
        ("OCR", "Grab text from screen (OCR)"),
        ("ColorPicker", "Colour picker"),
        ("ScreenColorPicker", "Pick colour from screen"),
        ("Ruler", "Ruler"),
        ("QRCode", "QR code"),
        ("ImageEditor", "Image editor"),
        ("ClipboardUpload", "Upload clipboard"),
        ("OpenScreenshotsFolder", "Open screenshots folder"),
        ("OpenHistory", "Open history"),
        ("OpenMainWindow", "Open ShareX"),
    };

    public static string? Find()
    {
        var configured = Secrets.Config.ShareXPath.Trim('"', ' ');
        if (configured.Length > 0 && File.Exists(configured)) return configured;
        try
        {
            foreach (var p in Process.GetProcessesByName("ShareX"))
                using (p) { var f = p.MainModule?.FileName; if (f != null && File.Exists(f)) return f; }
        }
        catch { }
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ShareX", "ShareX.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam", "steamapps", "common", "ShareX", "ShareX.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "ShareX", "ShareX.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "ShareX", "ShareX.exe"),
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    public static void Run(string task)
    {
        var exe = Find();
        if (exe == null) { Toast.Show("ShareX not found – set its location in Connections › ShareX"); return; }
        var arg = task.StartsWith("workflow:", StringComparison.OrdinalIgnoreCase)
            ? $"-workflow \"{task[9..].Trim()}\""
            : "-" + task.TrimStart('-').Trim();
        try { Process.Start(new ProcessStartInfo(exe, arg) { UseShellExecute = false }); }
        catch (Exception ex) { Toast.Show($"ShareX: {ex.Message}"); }
    }
}
