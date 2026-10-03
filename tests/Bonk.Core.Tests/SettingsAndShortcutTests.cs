using Bonk.Core.Actions;
using Bonk.Core.Configuration;
using Bonk.Core.Models;

namespace Bonk.Core.Tests;

public sealed class SettingsAndShortcutTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "Bonk.Tests", Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("Ctrl+Shift+M", new ushort[] { 0x11, 0x10, 0x4D })]
    [InlineData("M + ctrl + shift", new ushort[] { 0x11, 0x10, 0x4D })]
    [InlineData("Alt+F24", new ushort[] { 0x12, 0x87 })]
    [InlineData("Media", null)]
    [InlineData("Ctrl+Ctrl+M", null)]
    [InlineData("Ctrl+Shift", null)]
    [InlineData("Ctrl++M", null)]
    [InlineData("M+N", null)]
    public void ParsesAndValidatesShortcuts(string shortcut, ushort[]? expected)
    {
        var valid = KeyboardShortcut.TryParse(shortcut, out var keys, out var error);
        Assert.Equal(expected is not null, valid);
        if (expected is not null) { Assert.Equal(expected, keys); Assert.Null(error); }
        else Assert.NotNull(error);
    }

    [Fact]
    public void SettingsRoundTripMicrophoneTuningAndEveryActionType()
    {
        var store = new SettingsStore(Path.Combine(_directory, "config.json"));
        var settings = new AppSettings { MicrophoneId = "headset-123", TestMode = false, DetectionEnabled = false,
            Detection = new() { Sensitivity = 7.5, TapDebounceMs = 120, MaximumGapMs = 450, CompletionTimeoutMs = 600 } };
        settings.Bindings[1].Action = new() { Type = ActionType.LaunchApplication, Value = @"C:\My Apps\app.exe", Arguments = "--quiet" };
        store.Save(settings);
        var restored = store.Load();
        Assert.Equal("headset-123", restored.MicrophoneId);
        Assert.Equal(settings.Detection, restored.Detection);
        Assert.False(restored.TestMode);
        Assert.False(restored.DetectionEnabled);
        Assert.Equal(settings.Bindings[1].Id, restored.Bindings[1].Id);
        Assert.Equal("--quiet", restored.Bindings[1].Action.Arguments);
        Assert.Equal(@"C:\My Apps\app.exe", restored.Bindings[1].Action.Value);
        Assert.DoesNotContain("triggerLabel", File.ReadAllText(store.FilePath));
        Assert.False(File.Exists(store.FilePath + ".tmp"));
    }

    [Fact]
    public void MissingSettingsUseSafeDefaults()
    {
        var store = new SettingsStore(Path.Combine(_directory, "config.json"));
        var settings = store.Load();
        Assert.True(settings.TestMode);
        Assert.Equal(3, settings.Bindings.Count);
        Assert.Null(store.LoadWarning);
    }

    [Fact]
    public void CorruptSettingsArePreservedBeforeDefaultsAreSaved()
    {
        Directory.CreateDirectory(_directory);
        var store = new SettingsStore(Path.Combine(_directory, "config.json"));
        File.WriteAllText(store.FilePath, "{bad json");
        var settings = store.Load();
        Assert.NotNull(store.LoadWarning);
        var backup = Assert.Single(Directory.GetFiles(_directory, "*.invalid-*"));
        Assert.Equal("{bad json", File.ReadAllText(backup));
        store.Save(settings);
        Assert.Equal(3, store.Load().Bindings.Count);
    }

    [Fact]
    public void DuplicateAndInvalidBindingsAreRemoved()
    {
        var settings = new AppSettings();
        settings.Bindings.Add(new() { Trigger = TapGesture.DoubleTap });
        settings.Bindings[0].Action = new() { Type = ActionType.Media, Value = "NotAKey" };
        settings.Normalize();
        Assert.Equal(2, settings.Bindings.Count);
        Assert.Single(settings.Bindings, b => b.Trigger == TapGesture.DoubleTap);
    }

    [Fact]
    public void MalformedNumericTuningIsNormalized()
    {
        var normalized = new DetectionSettings { Sensitivity = double.NaN, MinimumTapStrength = float.NaN,
            TapDebounceMs = 9000, MaximumGapMs = -1, CompletionTimeoutMs = -1 }.Normalize();
        Assert.Equal(5, normalized.Sensitivity);
        Assert.Equal(0.08f, normalized.MinimumTapStrength);
        Assert.True(normalized.MaximumGapMs > normalized.TapDebounceMs);
        Assert.True(normalized.CompletionTimeoutMs > normalized.MaximumGapMs);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
