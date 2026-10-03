namespace Bonk.Core.Detection;

public readonly record struct FrameLevels(float Peak, float Rms, float CrestFactor);
public readonly record struct DetectionSnapshot(long TimestampMs, float Peak, float Rms, float NoiseFloor, float Threshold, bool Candidate);

public sealed class AudioAnalyzer
{
    public float NoiseFloor { get; private set; } = 0.002f;

    public static FrameLevels Analyze(ReadOnlySpan<float> samples)
    {
        if (samples.IsEmpty) return default;
        float peak = 0;
        double sum = 0;
        foreach (var sample in samples)
        {
            var value = float.IsFinite(sample) ? Math.Clamp(sample, -1f, 1f) : 0;
            peak = Math.Max(peak, Math.Abs(value));
            sum += value * value;
        }
        var rms = (float)Math.Sqrt(sum / samples.Length);
        return new(peak, rms, peak / Math.Max(rms, 0.00001f));
    }

    public void UpdateNoise(float rms, double frameDurationMs)
    {
        // Fast enough to track a changing room; slow enough not to learn individual transients.
        var timeConstant = rms > NoiseFloor ? 1200 : 2500;
        var alpha = (float)(1 - Math.Exp(-frameDurationMs / timeConstant));
        NoiseFloor = Math.Max(0.0001f, NoiseFloor + alpha * (rms - NoiseFloor));
    }

    public void Reset() => NoiseFloor = 0.002f;
}
