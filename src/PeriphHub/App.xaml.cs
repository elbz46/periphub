using System.Threading;
using System.Windows;
using PeriphHub.Core;
using PeriphHub.Drivers.Asus;
using PeriphHub.Drivers.Generic;
using PeriphHub.Drivers.Logitech;
using PeriphHub.UI;

namespace PeriphHub;

public partial class App : Application
{
    private Mutex? _mutex;
    private EventWaitHandle? _showSignal;
    private TrayIcon? _tray;
    public static DeviceHub Hub { get; } = new();
    public static KeyRemapDevice? Keys { get; private set; }
    public static AppSettingsPage? AppPage { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        _mutex = new Mutex(true, @"Local\PeriphHub.SingleInstance", out bool first);
        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\PeriphHub.Show");
        if (!first)
        {
            _showSignal.Set();
            Shutdown();
            return;
        }
        base.OnStartup(e);

        DispatcherUnhandledException += (_, ex) => { Log.Error("Unhandled UI exception", ex.Exception); ex.Handled = true; };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) => Log.Error("Unhandled exception", ex.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, ex) => { Log.Error("Unobserved task exception", ex.Exception); ex.SetObserved(); };

        Log.Info($"PeriphHub {typeof(App).Assembly.GetName().Version} starting on {Environment.OSVersion}");

        var window = new MainWindow();
        MainWindow = window;
        _tray = new TrayIcon(window);
        Toast.Requested += t => _tray.ShowToast(t);

        Keys = new KeyRemapDevice();
        AppPage = new AppSettingsPage();
        Hub.AddProvider(new AsusProvider());
        Hub.AddProvider(new LogitechProvider());
        Hub.AddProvider(new GenericProvider());
        Hub.Add(Keys);
        var music = new MusicPage { Demo = e.Args.Contains("--demo") };
        Hub.Add(music);
        if (music.Demo) MediaController.Instance.LoadDemo(DemoArt());
        else _ = MediaController.Instance.StartAsync();
        _ = Keys.ApplySavedAsync();
        Keys.Start();
        if (e.Args.Contains("--demo"))
        {
            Hub.Add(new DemoDevice("MX Master 2S (demo)", DeviceKind.Mouse));
            Hub.Add(new DemoDevice("G321 LIGHTSPEED (demo)", DeviceKind.Headset));
        }
        Hub.Start();

        if (!e.Args.Contains("--minimized")) window.Show();

        var listener = new Thread(() =>
        {
            while (_showSignal.WaitOne())
                Dispatcher.BeginInvoke(window.ShowAndActivate);
        }) { IsBackground = true };
        listener.Start();
    }

    private static System.Windows.Media.Imaging.BitmapImage? DemoArt()
    {
        try
        {
            // Simple generated cover so previews have artwork.
            var dv = new System.Windows.Media.DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                var g = new System.Windows.Media.LinearGradientBrush(
                    System.Windows.Media.Color.FromRgb(200, 20, 40), System.Windows.Media.Color.FromRgb(20, 10, 30), 45);
                dc.DrawRectangle(g, null, new Rect(0, 0, 256, 256));
                dc.DrawEllipse(null, new System.Windows.Media.Pen(System.Windows.Media.Brushes.White, 6), new Point(128, 128), 70, 70);
            }
            var rt = new System.Windows.Media.Imaging.RenderTargetBitmap(256, 256, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            rt.Render(dv);
            var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
            enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rt));
            var ms = new System.IO.MemoryStream(); enc.Save(ms); ms.Position = 0;
            var img = new System.Windows.Media.Imaging.BitmapImage();
            img.BeginInit(); img.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad; img.StreamSource = ms; img.EndInit(); img.Freeze();
            return img;
        }
        catch { return null; }
    }

    public void Quit()
    {
        SettingsStore.Instance.SaveNow();
        _tray?.Dispose();
        Hub.Dispose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SettingsStore.Instance.SaveNow();
        base.OnExit(e);
    }
}
