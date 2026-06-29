using BlitzBrief.Core;

namespace BlitzBrief.Tests;

public sealed class SpeechOnsetDetectionTests
{
    private const int FrameMs = 32; // Silero v5 @16k: 512 Samples = 32 ms

    private static float[] Probs(int silenceFrames, int speechFrames)
    {
        var p = new float[silenceFrames + speechFrames];
        for (var i = silenceFrames; i < p.Length; i++) p[i] = 0.9f;
        return p;
    }

    [Fact]
    public void DetectsOnset_AfterLeadingSilence_WithPad()
    {
        // 20 Frames Stille (640 ms), dann Sprache. minSpeech 100ms (~4 Frames), Pad 100ms (~3 Frames).
        var r = SpeechOnsetDetection.FindOnset(Probs(20, 30), FrameMs, minSpeechMs: 100, padMs: 100);

        Assert.True(r.Detected);
        // Sprache beginnt bei Frame 20; abzüglich 3 Frames Pad -> 17.
        Assert.Equal(17, r.OnsetFrame);
        Assert.Equal(17 * FrameMs, r.OnsetMs);
    }

    [Fact]
    public void NoOnset_WhenAllSilent()
    {
        var r = SpeechOnsetDetection.FindOnset(new float[40], FrameMs);
        Assert.False(r.Detected);
        Assert.Equal(0, r.OnsetMs);
    }

    [Fact]
    public void IgnoresShortSpike_BelowMinSpeech()
    {
        // Einzelner hoher Frame (32 ms) < minSpeech (100 ms) -> kein Onset.
        var p = new float[40];
        p[10] = 0.95f;
        var r = SpeechOnsetDetection.FindOnset(p, FrameMs, minSpeechMs: 100);
        Assert.False(r.Detected);
    }

    [Fact]
    public void OnsetAtStart_StaysAtZero_WithPad()
    {
        // Sprache ab Frame 0: Pad darf nicht negativ werden.
        var r = SpeechOnsetDetection.FindOnset(Probs(0, 20), FrameMs, padMs: 100);
        Assert.True(r.Detected);
        Assert.Equal(0, r.OnsetFrame);
    }

    [Fact]
    public void RespectsThreshold()
    {
        // Werte knapp unter Schwelle -> kein Onset.
        var p = new float[40];
        for (var i = 10; i < 40; i++) p[i] = 0.4f;
        var r = SpeechOnsetDetection.FindOnset(p, FrameMs, threshold: 0.5f);
        Assert.False(r.Detected);
    }
}
