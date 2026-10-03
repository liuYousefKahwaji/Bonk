using System.Windows;

namespace Bonk.App;

public partial class App : Application
{
    private Mutex? _instance;
    private bool _ownsMutex;
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // A second copy would duplicate every macro. Scope to this Windows session.
        _instance = new Mutex(true, @"Local\Bonk.MicrophoneMacros", out var first);
        _ownsMutex = first;
        if (!first && e.Args.Contains("--restart"))
        {
            // The elevated replacement may start before its parent has finished cleanup.
            for (var attempt = 0; attempt < 100 && !_ownsMutex; attempt++)
            {
                await Task.Delay(100);
                try { _ownsMutex = _instance.WaitOne(0); }
                catch (AbandonedMutexException) { _ownsMutex = true; }
            }
            first = _ownsMutex;
        }
        if (!first)
        {
            MessageBox.Show("Bonk is already running. Open it from the microphone icon in your system tray.", "Bonk");
            Shutdown();
            return;
        }
        var configIndex = Array.IndexOf(e.Args, "--config");
        var configPath = configIndex >= 0 && configIndex + 1 < e.Args.Length ? Path.GetFullPath(e.Args[configIndex + 1]) : null;
        var window = new UI.MainWindow(e.Args.Contains("--tray"), configPath);
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_ownsMutex) _instance?.ReleaseMutex();
        _instance?.Dispose();
        base.OnExit(e);
    }
}
