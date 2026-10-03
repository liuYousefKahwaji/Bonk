using Bonk.Core.Models;

namespace Bonk.Core.Detection;

public interface ITriggerDetector
{
    event Action<long>? TapDetected;
    void Process(ReadOnlySpan<float> samples, long timestampMs, double frameDurationMs);
    void Reset();
}

public sealed class TapDetector : ITriggerDetector
{
    private readonly AudioAnalyzer _analyzer = new();
    private DetectionSettings _settings;
    private float _previousPeak;
    private long _candidateStarted = -1;
    private long _lastTap = long.MinValue / 2;
    private long _startedAt = -1;
    private float _candidateReleaseLevel;
    private int _quietFrames;
    public event Action<long>? TapDetected;
    public DetectionSnapshot Snapshot { get; private set; }

    public TapDetector(DetectionSettings settings) => _settings = settings.Normalize();
    public void Configure(DetectionSettings settings) { _settings = settings.Normalize(); Reset(); }

    public void Process(ReadOnlySpan<float> samples, long timestampMs, double frameDurationMs)
    {
        var levels = AudioAnalyzer.Analyze(samples);
        if (_startedAt < 0) _startedAt = timestampMs;
        var threshold = Math.Max(_settings.MinimumTapStrength, _analyzer.NoiseFloor * (float)_settings.NoiseMultiplier);
        var warmingUp = timestampMs - _startedAt < 500;
        var coolingDown = timestampMs - _lastTap < _settings.TapDebounceMs;

        if (_candidateStarted >= 0)
        {
            var elapsed = timestampMs - _candidateStarted;
            _quietFrames = levels.Peak < _candidateReleaseLevel ? _quietFrames + 1 : 0;
            // Confirm only after the transient decays. Sustained speech/music is rejected.
            if (elapsed > _settings.MaximumTapDurationMs)
                _candidateStarted = -1;
            else if (_quietFrames >= 2)
            {
                var tapTime = _candidateStarted;
                _candidateStarted = -1;
                _lastTap = tapTime;
                TapDetected?.Invoke(tapTime);
            }
        }
        else if (!warmingUp && !coolingDown && levels.Peak >= threshold
            && levels.Peak >= Math.Max(_previousPeak * 1.7f, threshold * 0.65f)
            && levels.CrestFactor >= _settings.MinimumCrestFactor)
        {
            _candidateStarted = timestampMs;
            // Strong taps may ring above the trigger threshold while they decay.
            _candidateReleaseLevel = Math.Max(threshold * 0.55f, levels.Peak * 0.24f);
            _quietFrames = 0;
        }
        else if (!coolingDown)
            _analyzer.UpdateNoise(levels.Rms, frameDurationMs);

        _previousPeak = levels.Peak;
        Snapshot = new(timestampMs, levels.Peak, levels.Rms, _analyzer.NoiseFloor, threshold, _candidateStarted >= 0);
    }

    public void Reset()
    {
        _analyzer.Reset();
        _previousPeak = 0;
        _candidateStarted = _startedAt = -1;
        _lastTap = long.MinValue / 2;
        _quietFrames = 0;
        Snapshot = default;
    }
}
