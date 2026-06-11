using BlitzBrief.Core;

namespace BlitzBrief.Tests;

public sealed class TranscriptionQualityServiceTests
{
    [Fact]
    public void ShouldRejectRecording_RejectsVeryShortRecording()
    {
        Assert.True(TranscriptionQualityService.ShouldRejectRecording(TimeSpan.FromMilliseconds(100)));
        Assert.False(TranscriptionQualityService.ShouldRejectRecording(TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void CleanedTranscript_TrimsBlankLines()
    {
        var cleaned = TranscriptionQualityService.CleanedTranscript("  Hallo \r\n\r\n Welt  ");

        Assert.Equal("Hallo\nWelt", cleaned);
    }

    [Fact]
    public void IsLikelyArtifact_RejectsKnownShortArtifact()
    {
        Assert.True(TranscriptionQualityService.IsLikelyArtifact(
            "Danke fürs Zuschauen",
            TimeSpan.FromMilliseconds(900)));
    }
}
