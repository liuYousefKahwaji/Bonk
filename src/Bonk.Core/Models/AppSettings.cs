namespace Bonk.Core.Models;

public sealed class AppSettings
{
    public string? MicrophoneId { get; set; }
    public bool DetectionEnabled { get; set; } = true;
    public bool TestMode { get; set; } = true;
    public bool StartWithWindows { get; set; }
    public DetectionSettings Detection { get; set; } = new();
    public List<Binding> Bindings { get; set; } =
    [
        new() { Trigger = TapGesture.SingleTap, Action = new() { Type = ActionType.Media, Value = "PlayPause" } },
        new() { Trigger = TapGesture.DoubleTap, Action = new() { Shortcut = "Ctrl+Shift+M" } },
        new() { Trigger = TapGesture.TripleTap, Action = new() { Shortcut = "Ctrl+Shift+D" } }
    ];

    public void Normalize()
    {
        Detection = (Detection ?? new()).Normalize();
        Bindings ??= [];
        // A gesture has one action. Ignore invalid or duplicate entries from hand-edited JSON.
        Bindings = Bindings.Where(b => b is not null && Enum.IsDefined(b.Trigger)
                && b.Action is not null && b.Action.Validate() is null)
            .DistinctBy(b => b.Trigger).ToList();
    }
}

public sealed record DetectionSettings
{
    // Higher sensitivity means a smaller multiplier above the adaptive noise floor.
    public double Sensitivity { get; init; } = 5;
    public float MinimumTapStrength { get; init; } = 0.08f;
    public int TapDebounceMs { get; init; } = 100;
    public int MaximumGapMs { get; init; } = 400;
    public int CompletionTimeoutMs { get; init; } = 520;
    public int MaximumTapDurationMs { get; init; } = 90;
    public float MinimumCrestFactor { get; init; } = 1.6f;
    public double NoiseMultiplier => 11 - Sensitivity;

    public DetectionSettings Normalize() => this with
    {
        Sensitivity = double.IsFinite(Sensitivity) ? Math.Clamp(Sensitivity, 1, 10) : 5,
        MinimumTapStrength = float.IsFinite(MinimumTapStrength) ? Math.Clamp(MinimumTapStrength, 0.01f, 0.95f) : 0.08f,
        TapDebounceMs = Math.Clamp(TapDebounceMs, 50, 300),
        MaximumGapMs = Math.Clamp(MaximumGapMs, Math.Clamp(TapDebounceMs, 50, 300) + 20, 1000),
        CompletionTimeoutMs = Math.Clamp(CompletionTimeoutMs,
            Math.Clamp(MaximumGapMs, Math.Clamp(TapDebounceMs, 50, 300) + 20, 1000)
                + Math.Clamp(MaximumTapDurationMs, 20, 150) + 20, 1500),
        MaximumTapDurationMs = Math.Clamp(MaximumTapDurationMs, 20, 150),
        MinimumCrestFactor = float.IsFinite(MinimumCrestFactor) ? Math.Clamp(MinimumCrestFactor, 1.1f, 5f) : 1.6f
    };
}
