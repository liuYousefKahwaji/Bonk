using Bonk.Core.Detection;
using Bonk.Core.Models;

namespace Bonk.Core.Tests;

public sealed class DetectionTests
{
    private static float[] Noise(float amplitude = 0.003f) => Enumerable.Range(0, 480).Select(i => i % 2 == 0 ? amplitude : -amplitude).ToArray();
    private static float[] Impulse(float amplitude = 0.75f) { var frame = Noise(); frame[100] = amplitude; return frame; }

    [Fact]
    public void AnalyzerMeasuresPeakRmsAndCrest()
    {
        var levels = AudioAnalyzer.Analyze([0.5f, -0.5f, 0, 0]);
        Assert.Equal(0.5f, levels.Peak);
        Assert.Equal(Math.Sqrt(0.125), levels.Rms, precision: 6);
        Assert.Equal(Math.Sqrt(2), levels.CrestFactor, precision: 6);
    }

    [Fact]
    public void AnalyzerHandlesSilenceAndNonFiniteSamples()
    {
        Assert.Equal(default, AudioAnalyzer.Analyze([]));
        var levels = AudioAnalyzer.Analyze([float.NaN, float.PositiveInfinity, 0]);
        Assert.Equal(0, levels.Peak);
        Assert.Equal(0, levels.Rms);
    }

    [Fact]
    public void ShortImpulseEmitsOriginalTimestampAfterDecay()
    {
        var detector = WarmDetector();
        var taps = new List<long>();
        detector.TapDetected += taps.Add;
        detector.Process(Impulse(), 700, 10);
        Assert.Empty(taps);
        detector.Process(Noise(), 710, 10);
        Assert.Empty(taps);
        detector.Process(Noise(), 720, 10);
        Assert.Equal([700L], taps);
    }

    [Fact]
    public void WarmupSuppressesStartupTransient()
    {
        var detector = new TapDetector(new());
        var count = 0;
        detector.TapDetected += _ => count++;
        detector.Process(Impulse(), 0, 10);
        detector.Process(Noise(), 10, 10);
        detector.Process(Noise(), 20, 10);
        Assert.Equal(0, count);
    }

    [Fact]
    public void RingingWithinDebounceDoesNotBecomeAnotherTap()
    {
        var detector = WarmDetector();
        var taps = new List<long>();
        detector.TapDetected += taps.Add;
        for (var t = 700; t < 900; t += 10)
            detector.Process(t is 700 or 740 or 780 ? Impulse() : Noise(), t, 10);
        Assert.Single(taps);
    }

    [Fact]
    public void SeparateTapsOutsideDebounceArePreserved()
    {
        var detector = WarmDetector();
        var taps = new List<long>();
        detector.TapDetected += taps.Add;
        for (var t = 700; t < 1000; t += 10)
            detector.Process(t is 700 or 900 ? Impulse() : Noise(), t, 10);
        Assert.Equal([700L, 900L], taps);
    }

    [Fact]
    public void SustainedHighCrestAudioIsRejected()
    {
        var detector = WarmDetector();
        var taps = new List<long>();
        detector.TapDetected += taps.Add;
        var sustained = Enumerable.Range(0, 480).Select(i => i % 8 == 0 ? 0.6f : 0f).ToArray();
        for (var t = 700; t < 950; t += 10) detector.Process(sustained, t, 10);
        for (var t = 950; t < 1100; t += 10) detector.Process(Noise(), t, 10);
        Assert.Empty(taps);
    }

    [Fact]
    public void LoudSmoothAudioIsRejected()
    {
        var detector = WarmDetector();
        var count = 0;
        detector.TapDetected += _ => count++;
        var tone = Enumerable.Range(0, 480).Select(i => (float)(0.8 * Math.Sin(2 * Math.PI * i / 48))).ToArray();
        for (var t = 700; t < 1500; t += 10) detector.Process(tone, t, 10);
        for (var t = 1500; t < 1600; t += 10) detector.Process(Noise(), t, 10);
        Assert.Equal(0, count);
    }

    [Fact]
    public void GradualRiseIsRejected()
    {
        var detector = WarmDetector();
        var count = 0;
        detector.TapDetected += _ => count++;
        for (var i = 0; i < 40; i++) detector.Process(Impulse(0.02f + i * 0.015f), 700 + i * 10, 10);
        detector.Process(Noise(), 1100, 10);
        detector.Process(Noise(), 1110, 10);
        Assert.Equal(0, count);
    }

    [Fact]
    public void BackgroundLevelRaisesThreshold()
    {
        var detector = new TapDetector(new() { MinimumTapStrength = 0.05f });
        var count = 0;
        detector.TapDetected += _ => count++;
        var noise = Noise(0.04f);
        for (var t = 0; t < 5000; t += 10) detector.Process(noise, t, 10);
        Assert.True(detector.Snapshot.NoiseFloor > 0.03);
        Assert.True(detector.Snapshot.Threshold > 0.18);
        detector.Process(Impulse(0.14f), 5000, 10);
        detector.Process(noise, 5010, 10);
        detector.Process(noise, 5020, 10);
        Assert.Equal(0, count);
    }

    [Fact]
    public void ResetDiscardsPendingTransient()
    {
        var detector = WarmDetector();
        var count = 0;
        detector.TapDetected += _ => count++;
        detector.Process(Impulse(), 700, 10);
        detector.Reset();
        detector.Process(Noise(), 710, 10);
        detector.Process(Noise(), 720, 10);
        Assert.Equal(0, count);
    }

    [Fact]
    public void SteadyStateProcessingDoesNotAllocatePerFrame()
    {
        var detector = WarmDetector();
        var frame = Noise();
        for (var i = 0; i < 1000; i++) detector.Process(frame, 1000 + i * 10, 10);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10000; i++) detector.Process(frame, 20000 + i * 10, 10);
        Assert.InRange(GC.GetAllocatedBytesForCurrentThread() - before, 0, 1024);
    }

    [Fact]
    public void DoubleTapPipelineResolvesDiscordShortcutExactlyOnce()
    {
        var settings = new AppSettings();
        var detector = WarmDetector();
        var recognizer = new GestureRecognizer(settings.Detection);
        var shortcuts = new List<string>();
        detector.TapDetected += recognizer.AddTap;
        recognizer.Recognized += gesture => shortcuts.Add(settings.Bindings.Single(b => b.Trigger == gesture).Action.Shortcut);
        for (var t = 700; t < 1200; t += 10) detector.Process(t is 700 or 900 ? Impulse() : Noise(), t, 10);
        Assert.Empty(shortcuts);
        recognizer.Advance(1420);
        recognizer.Advance(1900);
        Assert.Equal(["Ctrl+Shift+M"], shortcuts);
    }

    [Fact]
    public void QuietHeadsetTapAboveMinimumIsDetected()
    {
        var detector = WarmDetector();
        var taps = new List<long>();
        detector.TapDetected += taps.Add;
        detector.Process(Impulse(0.09f), 700, 10);
        detector.Process(Noise(), 710, 10);
        detector.Process(Noise(), 720, 10);
        Assert.Equal([700L], taps);
    }

    [Fact]
    public void StrongTapCanDecayIntoRingingAboveAbsoluteReleaseThreshold()
    {
        var detector = WarmDetector();
        var taps = new List<long>();
        detector.TapDetected += taps.Add;
        detector.Process(Impulse(0.8f), 700, 10);
        detector.Process(Noise(0.12f), 710, 10);
        detector.Process(Noise(0.10f), 720, 10);
        Assert.Equal([700L], taps);
    }

    [Fact]
    public void BroadMechanicalImpulseWithLowerCrestIsDetected()
    {
        var detector = WarmDetector();
        var taps = new List<long>();
        detector.TapDetected += taps.Add;
        var broad = Enumerable.Range(0, 480).Select(i => i < 160 ? 0.35f : 0.003f).ToArray();
        detector.Process(broad, 700, 10);
        detector.Process(Noise(), 710, 10);
        detector.Process(Noise(), 720, 10);
        Assert.Equal([700L], taps);
    }

    private static TapDetector WarmDetector()
    {
        var detector = new TapDetector(new());
        var noise = Noise();
        for (var t = 0; t < 700; t += 10) detector.Process(noise, t, 10);
        return detector;
    }
}
