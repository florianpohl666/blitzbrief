using BlitzBrief.Core;

namespace BlitzBrief.Tests;

public sealed class TranscriptionLeakageTests
{
    [Theory]
    // Sauberes Diktat bleibt unangetastet.
    [InlineData("ganz", "Ich bin ein", "alter Mann.", "ganz")]
    // Rechter Kontext mitgeschrieben -> am Ende abgeschnitten.
    [InlineData("ganz alter Mann", "Ich bin ein", "alter Mann.", "ganz")]
    // Linker Kontext mitgeschrieben -> am Anfang abgeschnitten.
    [InlineData("Ich bin ein ganz", "Ich bin ein", "alter Mann.", "ganz")]
    // Beidseitig durchgesickert.
    [InlineData("Ich bin ein ganz alter Mann", "Ich bin ein", "alter Mann.", "ganz")]
    // Ohne Kontext kann nichts geschnitten werden.
    [InlineData("ganz", null, null, "ganz")]
    public void StripLeakedContext_RemovesBoundaryEcho(string output, string? left, string? right, string expected)
    {
        Assert.Equal(expected, TranscriptionQualityService.StripLeakedContext(output, left, right));
    }

    [Fact]
    public void StripLeakedContext_KeepsSingleWordThatMatchesContext()
    {
        // Konservativ: ein EINZELNES Wort, das zufällig einem Kontextwort gleicht, wird NICHT geschnitten.
        Assert.Equal("Mann", TranscriptionQualityService.StripLeakedContext("Mann", "Ich bin ein", "alter Mann."));
    }

    [Fact]
    public void StripLeakedContext_WhenOutputIsAllContext_KeepsOriginal()
    {
        // Wäre nach dem Schnitt nichts mehr übrig, lieber das Original behalten als alles zerstören.
        Assert.Equal("alter Mann", TranscriptionQualityService.StripLeakedContext("alter Mann", "Ich bin ein", "alter Mann."));
    }
}
