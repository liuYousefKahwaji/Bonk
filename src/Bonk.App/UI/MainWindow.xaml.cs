using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Bonk.App.Actions;
using Bonk.App.Audio;
using Bonk.App.Services;
using Bonk.Core.Configuration;
using Bonk.Core.Detection;
using Bonk.Core.Models;
using Forms = System.Windows.Forms;

namespace Bonk.App.UI;

public partial class MainWindow : Window
{
    private readonly SettingsStore _store;
    private readonly AppSettings _settings;
    private readonly AudioDeviceService _devices = new();
    private readonly MicrophoneCapture _capture;
    private readonly TapDetector _detector;
    private readonly GestureRecognizer _recognizer;
    private readonly object _pipelineLock = new();
    private readonly ConcurrentQueue<PipelineEvent> _events = new();
    private readonly ObservableCollection<string> _log = [];
    private readonly DispatcherTimer _meterTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private readonly DispatcherTimer _maintenanceTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private readonly Forms.NotifyIcon _tray;
    private readonly Forms.ToolStripMenuItem _trayToggle;
    private readonly Forms.ToolStripMenuItem _trayTest;
    private readonly bool _startInTray;
    private bool _ready;
    private bool _refreshingDevices;
    private bool _updatingSliders;
    private bool _maintenanceBusy;
    private bool _editing;
    private bool _switchingMicrophone;
    private bool _closing;
    private bool _quit;
    private bool _executing;
    private long _pauseUntil;
    private long _epoch;
    private string _lastStatus = "";
    private enum EventKind { Tap, Gesture, Unsupported }
    private readonly record struct PipelineEvent(EventKind Kind, int Count, long Epoch, DateTime Time);

    public MainWindow(bool startInTray = false, string? settingsPath = null)
    {
        InitializeComponent();
        _startInTray = startInTray;
        _store = new(settingsPath);
        _settings = _store.Load();
        _detector = new(_settings.Detection);
        _recognizer = new(_settings.Detection);
        _capture = new(_devices);
        EventLog.ItemsSource = _log;

        _capture.FrameAvailable += ProcessFrame;
        _capture.StreamReset += ResetPipeline;
        _detector.TapDetected += timestamp =>
        {
            Enqueue(EventKind.Tap, 1);
            _recognizer.AddTap(timestamp);
        };
        _recognizer.Recognized += gesture => Enqueue(EventKind.Gesture, (int)gesture);
        _recognizer.UnsupportedSequence += count => Enqueue(EventKind.Unsupported, count);
        _meterTimer.Tick += MeterTick;
        _maintenanceTimer.Tick += async (_, _) => await MaintainCaptureAsync();
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); SaveSettings(); };

        _trayToggle = new("Pause detection", null, (_, _) => Dispatcher.Invoke(ToggleEnabled));
        _trayTest = new("Test mode", null, (_, _) => Dispatcher.Invoke(() => TestModeBox.IsChecked = !(_settings.TestMode)));
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open Bonk", null, (_, _) => Dispatcher.Invoke(OpenWindow));
        menu.Items.Add(_trayToggle);
        menu.Items.Add("Pause for 5 minutes", null, (_, _) => Dispatcher.Invoke(PauseTemporarily));
        menu.Items.Add(_trayTest);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => Dispatcher.Invoke(Quit));
        // Extract the executable's icon when available; a built-in icon is a safe fallback.
        _tray = new() { Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? System.Drawing.SystemIcons.Information,
            Text = "Bonk · microphone macros", ContextMenuStrip = menu, Visible = true };
        _tray.DoubleClick += (_, _) => Dispatcher.Invoke(OpenWindow);

        SetSliders(_settings.Detection);
        TestModeBox.IsChecked = _settings.TestMode;
        try { _settings.StartWithWindows = StartupService.IsEnabled(); }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
        { Log($"Cannot read Windows startup setting: {ex.Message}"); }
        StartupBox.IsChecked = _settings.StartWithWindows;
        RefreshDeviceList();
        RefreshBindings();
        _ready = true;
        UpdateStatus();
        Log(_settings.TestMode ? "Test mode is on. Gestures will be logged without actions." : "Bonk is ready. Actions are enabled.");
        if (_store.LoadWarning is { } warning) Log(warning);
    }

    private bool CanDetect => _settings.DetectionEnabled && _pauseUntil == 0 && !_editing && !_switchingMicrophone && !_closing;

    private void ProcessFrame(ReadOnlySpan<float> samples, long timestampMs, double durationMs)
    {
        lock (_pipelineLock)
        {
            // Even while paused, show the meter. No actions or sequences are accumulated.
            if (CanDetect) _detector.Process(samples, timestampMs, durationMs);
            else
            {
                var levels = AudioAnalyzer.Analyze(samples);
                _pausedSnapshot = new(timestampMs, levels.Peak, levels.Rms, _detector.Snapshot.NoiseFloor,
                    _detector.Snapshot.Threshold, false);
            }
        }
    }
    private DetectionSnapshot _pausedSnapshot;

    private void Enqueue(EventKind kind, int count)
    {
        if (_events.Count < 256) _events.Enqueue(new(kind, count, _epoch, DateTime.Now));
    }

    private void ResetPipeline()
    {
        lock (_pipelineLock)
        {
            _epoch++;
            _detector.Reset();
            _recognizer.Reset();
        }
    }

    private void MeterTick(object? sender, EventArgs e)
    {
        if (_closing) return;
        if (_pauseUntil > 0 && MicrophoneCapture.NowMs >= _pauseUntil)
        {
            lock (_pipelineLock) { _pauseUntil = 0; ResetPipeline(); }
            Log("Temporary pause ended.");
            UpdateStatus();
        }
        DetectionSnapshot snapshot;
        lock (_pipelineLock)
        {
            if (CanDetect) _recognizer.Advance(MicrophoneCapture.NowMs);
            snapshot = CanDetect ? _detector.Snapshot : _pausedSnapshot;
        }
        if (!_capture.IsRunning) snapshot = default;
        InputMeter.Value = snapshot.Peak;
        InputText.Text = $"{snapshot.Peak:P0}";
        NoiseText.Text = $"Noise floor  {snapshot.NoiseFloor:F3}";
        ThresholdText.Text = $"Threshold  {snapshot.Threshold:F3}";
        History.Push(snapshot);
        if (_lastStatus != _capture.Status)
        {
            _lastStatus = _capture.Status;
            CaptureStatus.Text = _lastStatus;
            Log(_lastStatus);
        }
        while (_events.TryDequeue(out var item))
        {
            if (item.Epoch != _epoch || !CanDetect) continue;
            if (item.Kind == EventKind.Tap) Log("Tap detected", item.Time);
            else if (item.Kind == EventKind.Unsupported) Log($"{item.Count} taps ignored (use 1, 2, or 3).", item.Time);
            else HandleGesture((TapGesture)item.Count, item.Time);
        }
    }

    private async void HandleGesture(TapGesture gesture, DateTime time)
    {
        var label = gesture switch { TapGesture.SingleTap => "Single tap", TapGesture.DoubleTap => "Double tap", _ => "Triple tap" };
        LastGestureText.Text = label + (_settings.TestMode ? " · test" : "");
        var binding = _settings.Bindings.FirstOrDefault(b => b.Trigger == gesture && b.Enabled);
        if (binding is null) { Log($"{label} → no enabled binding", time); return; }
        if (_settings.TestMode) { Log($"{label} → {binding.ActionLabel} [test only]", time); return; }
        if (_executing) { Log($"{label} ignored: an action is still starting.", time); return; }
        _executing = true;
        try
        {
            await Task.Run(() => ActionExecutor.Execute(binding.Action));
            Log($"{label} → {binding.ActionLabel}", time);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or ArgumentException or IOException or UnauthorizedAccessException)
        {
            Log($"Action failed: {ex.Message}");
            LastGestureText.Text = "Action blocked — see log";
        }
        finally { _executing = false; }
    }

    private async void WindowLoaded(object sender, RoutedEventArgs e)
    {
        if (_startInTray) Hide();
        _meterTimer.Start();
        _maintenanceTimer.Start();
        await MaintainCaptureAsync();
    }

    private async Task MaintainCaptureAsync()
    {
        if (_maintenanceBusy || _closing) return;
        _maintenanceBusy = true;
        try
        {
            RefreshDeviceList();
            string? requested;
            do
            {
                requested = _settings.MicrophoneId;
                await _capture.ReconcileAsync(requested);
            } while (!_closing && requested != _settings.MicrophoneId);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or IOException)
        { CaptureStatus.Text = $"Microphone recovery failed: {ex.Message}"; Log(CaptureStatus.Text); }
        finally
        {
            lock (_pipelineLock)
            {
                if (_switchingMicrophone) { _switchingMicrophone = false; ResetPipeline(); }
            }
            _maintenanceBusy = false;
        }
    }

    private void RefreshDeviceList()
    {
        _refreshingDevices = true;
        try
        {
            var list = _devices.GetDevices().ToList();
            if (_settings.MicrophoneId is { } saved && list.All(d => d.Id != saved))
                list.Add(new(saved, "Selected microphone (disconnected)"));
            var existing = MicrophoneBox.ItemsSource as List<AudioDeviceInfo>;
            if (existing is null || !existing.SequenceEqual(list))
            {
                MicrophoneBox.ItemsSource = list;
                MicrophoneBox.SelectedItem = list.First(d => d.Id == _settings.MicrophoneId);
            }
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
        { Log($"Could not list microphones: {ex.Message}"); }
        finally { _refreshingDevices = false; }
    }

    private async void MicrophoneChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || _refreshingDevices || MicrophoneBox.SelectedItem is not AudioDeviceInfo device) return;
        lock (_pipelineLock) { _settings.MicrophoneId = device.Id; _switchingMicrophone = true; ResetPipeline(); }
        ScheduleSave();
        await MaintainCaptureAsync();
    }
    private async void RefreshMicrophones(object sender, RoutedEventArgs e) => await MaintainCaptureAsync();

    private void SetSliders(DetectionSettings settings)
    {
        _updatingSliders = true;
        SensitivitySlider.Value = settings.Sensitivity;
        StrengthSlider.Value = settings.MinimumTapStrength;
        DebounceSlider.Value = settings.TapDebounceMs;
        GapSlider.Value = settings.MaximumGapMs;
        TimeoutSlider.Value = settings.CompletionTimeoutMs;
        SensitivityValue.Text = $"{settings.Sensitivity:F1} / 10";
        StrengthValue.Text = $"{settings.MinimumTapStrength:P0}";
        DebounceValue.Text = $"{settings.TapDebounceMs} ms";
        GapValue.Text = $"{settings.MaximumGapMs} ms";
        TimeoutValue.Text = $"{settings.CompletionTimeoutMs} ms";
        _updatingSliders = false;
    }

    private void DetectionSettingChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_ready || _updatingSliders) return;
        var detection = (_settings.Detection with
        {
            Sensitivity = SensitivitySlider.Value, MinimumTapStrength = (float)StrengthSlider.Value,
            TapDebounceMs = (int)DebounceSlider.Value, MaximumGapMs = (int)GapSlider.Value,
            CompletionTimeoutMs = (int)TimeoutSlider.Value
        }).Normalize();
        lock (_pipelineLock)
        {
            _settings.Detection = detection;
            _detector.Configure(detection);
            _recognizer.Configure(detection);
            _epoch++;
        }
        SetSliders(detection);
        ScheduleSave();
    }

    private void ResetTuning(object sender, RoutedEventArgs e)
    {
        lock (_pipelineLock)
        {
            _settings.Detection = new();
            _detector.Configure(_settings.Detection);
            _recognizer.Configure(_settings.Detection);
            _epoch++;
        }
        SetSliders(_settings.Detection);
        ScheduleSave();
        Log("Detection tuning reset.");
    }

    private void ToggleDetection(object sender, RoutedEventArgs e) => ToggleEnabled();
    private void ToggleEnabled()
    {
        lock (_pipelineLock)
        {
            if (_pauseUntil > 0) { _pauseUntil = 0; _settings.DetectionEnabled = true; }
            else _settings.DetectionEnabled = !_settings.DetectionEnabled;
            ResetPipeline();
        }
        UpdateStatus();
        ScheduleSave();
        Log(_settings.DetectionEnabled ? "Detection resumed." : "Detection paused.");
    }

    private void PauseTemporarily()
    {
        lock (_pipelineLock) { _pauseUntil = MicrophoneCapture.NowMs + 5 * 60 * 1000; ResetPipeline(); }
        UpdateStatus();
        Log("Detection paused for 5 minutes.");
    }

    private void TestModeChanged(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        lock (_pipelineLock) { _settings.TestMode = TestModeBox.IsChecked == true; ResetPipeline(); }
        UpdateStatus();
        ScheduleSave();
        Log(_settings.TestMode ? "Test mode on. Actions will only be logged." : "Test mode off. Actions are live.");
    }

    private void UpdateStatus()
    {
        var paused = !_settings.DetectionEnabled || _pauseUntil > 0;
        PauseButton.Content = paused ? "Resume detection" : "Pause detection";
        _trayToggle.Text = paused ? "Resume detection" : "Pause detection";
        _trayToggle.Checked = !paused;
        _trayTest.Checked = _settings.TestMode;
        _tray.Text = paused ? "Bonk · paused" : _settings.TestMode ? "Bonk · test mode" : "Bonk · listening";
        if (paused) LastGestureText.Text = _pauseUntil > 0 ? "Paused for 5 minutes" : "Detection paused";
        else LastGestureText.Text = _settings.TestMode ? "Listening · test mode" : "Listening for taps";
    }

    private void RefreshBindings()
    {
        BindingsList.ItemsSource = _settings.Bindings.OrderBy(b => b.Trigger).ToList();
        AddBindingButton.IsEnabled = _settings.Bindings.Count < 3;
    }
    private void BindingEnabledChanged(object sender, RoutedEventArgs e)
    { if (_ready) { ResetPipeline(); ScheduleSave(); } }
    private void AddBinding(object sender, RoutedEventArgs e) => ShowBindingEditor(null);
    private void EditBinding(object sender, RoutedEventArgs e)
    { if (sender is Button { DataContext: Binding binding }) ShowBindingEditor(binding); }

    private void ShowBindingEditor(Binding? binding)
    {
        lock (_pipelineLock) { _editing = true; ResetPipeline(); }
        try
        {
            var editor = new BindingEditor(binding, _settings.Bindings.Where(b => b.Id != binding?.Id).Select(b => b.Trigger)) { Owner = this };
            if (editor.ShowDialog() != true) return;
            if (binding is not null) _settings.Bindings.Remove(binding);
            if (!editor.Removed && editor.Result is { } result) _settings.Bindings.Add(result);
            RefreshBindings();
            ScheduleSave();
        }
        finally { lock (_pipelineLock) { _editing = false; ResetPipeline(); } }
    }

    private void StartupChanged(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        try
        {
            StartupService.SetEnabled(StartupBox.IsChecked == true);
            _settings.StartWithWindows = StartupBox.IsChecked == true;
            ScheduleSave();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException or InvalidOperationException)
        {
            _ready = false;
            StartupBox.IsChecked = _settings.StartWithWindows;
            _ready = true;
            Log($"Could not update startup: {ex.Message}");
        }
    }

    private void ScheduleSave() { _saveTimer.Stop(); _saveTimer.Start(); }
    private void SaveSettings()
    {
        try { _store.Save(_settings); SaveStatus.Text = "Offline. No audio recordings. Settings saved automatically."; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { SaveStatus.Text = "Settings could not be saved. See the log."; Log($"Save failed: {ex.Message}"); }
    }

    private void Log(string message, DateTime? time = null)
    {
        _log.Insert(0, $"{(time ?? DateTime.Now):HH:mm:ss.fff}  {message}");
        while (_log.Count > 100) _log.RemoveAt(_log.Count - 1);
    }
    private void ClearLog(object sender, RoutedEventArgs e) => _log.Clear();
    private void SendToTray(object sender, RoutedEventArgs e) => Hide();
    private void OpenWindow() { Show(); WindowState = WindowState.Normal; Activate(); }
    private void WindowStateChanged(object? sender, EventArgs e) { if (WindowState == WindowState.Minimized) Hide(); }
    private void Quit() { _quit = true; Close(); }

    private async void WindowClosing(object? sender, CancelEventArgs e)
    {
        if (!_quit) { e.Cancel = true; Hide(); SaveSettings(); return; }
        if (_closing) return;
        e.Cancel = true;
        lock (_pipelineLock) { _closing = true; ResetPipeline(); }
        _meterTimer.Stop();
        _maintenanceTimer.Stop();
        _saveTimer.Stop();
        SaveSettings();
        while (_maintenanceBusy) await Task.Delay(50);
        await _capture.DisposeAsync();
        _devices.Dispose();
        _tray.Visible = false;
        _tray.Icon?.Dispose();
        _tray.Dispose();
        Application.Current.Shutdown();
    }

    private void RestartAsAdministrator(object sender, RoutedEventArgs e)
    {
        try { SaveSettings(); StartupService.RestartElevated(_store.FilePath); Quit(); }
        catch (Win32Exception ex) { Log($"Administrator restart cancelled or blocked: {ex.Message}"); }
    }
}
