using Bonk.Core.Models;

namespace Bonk.Core.Detection;

public sealed class GestureRecognizer
{
    private DetectionSettings _settings;
    private int _count;
    private long _lastTap;
    public event Action<TapGesture>? Recognized;
    public event Action<int>? UnsupportedSequence;

    public GestureRecognizer(DetectionSettings settings) => _settings = settings.Normalize();
    public void Configure(DetectionSettings settings) { _settings = settings.Normalize(); Reset(); }

    public void AddTap(long timestampMs)
    {
        if (_count > 0 && timestampMs - _lastTap < 0) { Reset(); return; }
        if (_count > 0 && timestampMs - _lastTap > _settings.MaximumGapMs) Complete();
        _count++;
        _lastTap = timestampMs;
    }

    // Called by a timer as well as the capture pipeline: silence may produce no packets.
    public void Advance(long timestampMs)
    {
        if (_count > 0 && timestampMs - _lastTap >= _settings.CompletionTimeoutMs) Complete();
    }

    private void Complete()
    {
        var count = _count;
        Reset();
        if (count is >= 1 and <= 3) Recognized?.Invoke((TapGesture)count);
        else UnsupportedSequence?.Invoke(count);
    }

    public void Reset() => _count = 0;
}
