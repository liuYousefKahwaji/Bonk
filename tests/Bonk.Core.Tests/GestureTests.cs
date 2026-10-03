using Bonk.Core.Detection;
using Bonk.Core.Models;

namespace Bonk.Core.Tests;

public sealed class GestureTests
{
    [Theory]
    [InlineData(1, TapGesture.SingleTap)]
    [InlineData(2, TapGesture.DoubleTap)]
    [InlineData(3, TapGesture.TripleTap)]
    public void ResolvesOnlyAfterCompletionTimeout(int count, TapGesture expected)
    {
        var recognizer = new GestureRecognizer(new());
        var gestures = new List<TapGesture>();
        recognizer.Recognized += gestures.Add;
        for (var i = 0; i < count; i++) { recognizer.AddTap(1000 + i * 200); recognizer.Advance(1000 + i * 200 + 100); }
        Assert.Empty(gestures);
        recognizer.Advance(1000 + (count - 1) * 200 + 519);
        Assert.Empty(gestures);
        recognizer.Advance(1000 + (count - 1) * 200 + 520);
        Assert.Equal([expected], gestures);
    }

    [Fact]
    public void MaximumGapIncludesBoundary()
    {
        var recognizer = new GestureRecognizer(new());
        var gestures = new List<TapGesture>();
        recognizer.Recognized += gestures.Add;
        recognizer.AddTap(1000);
        recognizer.AddTap(1400);
        recognizer.Advance(1920);
        Assert.Equal([TapGesture.DoubleTap], gestures);
    }

    [Fact]
    public void TapOutsideGapStartsNewSequenceEvenBeforeCompletionTimeout()
    {
        var recognizer = new GestureRecognizer(new());
        var gestures = new List<TapGesture>();
        recognizer.Recognized += gestures.Add;
        recognizer.AddTap(1000);
        recognizer.AddTap(1401);
        Assert.Equal([TapGesture.SingleTap], gestures);
        recognizer.Advance(1921);
        Assert.Equal([TapGesture.SingleTap, TapGesture.SingleTap], gestures);
    }

    [Fact]
    public void FourTapSequenceIsIgnoredInsteadOfFiringTripleAndSingle()
    {
        var recognizer = new GestureRecognizer(new());
        var gestures = new List<TapGesture>();
        var unsupported = new List<int>();
        recognizer.Recognized += gestures.Add;
        recognizer.UnsupportedSequence += unsupported.Add;
        for (var i = 0; i < 4; i++) recognizer.AddTap(1000 + i * 200);
        recognizer.Advance(2120);
        Assert.Empty(gestures);
        Assert.Equal([4], unsupported);
    }

    [Fact]
    public void ResetCancelsPendingGesture()
    {
        var recognizer = new GestureRecognizer(new());
        var gestures = new List<TapGesture>();
        recognizer.Recognized += gestures.Add;
        recognizer.AddTap(1000);
        recognizer.AddTap(1200);
        recognizer.Reset();
        recognizer.Advance(5000);
        Assert.Empty(gestures);
    }

    [Fact]
    public void CompletionHasRoomForLastTapToDecay()
    {
        var normalized = new DetectionSettings { MaximumGapMs = 400, CompletionTimeoutMs = 400 }.Normalize();
        Assert.True(normalized.CompletionTimeoutMs >= normalized.MaximumGapMs + normalized.MaximumTapDurationMs + 20);
    }
}
