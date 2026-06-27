using BlitzBrief.Core;

namespace BlitzBrief.Tests;

public sealed class SentenceContextTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ReturnsNull_ForBlankInput(string? input)
    {
        Assert.Null(SentenceContext.CurrentSentence(input));
    }

    [Fact]
    public void ReturnsWholeText_WhenNoTerminator()
    {
        // Offener Satz ohne vorangehendes Satzende → ganzer Vortext ist der aktuelle Satz.
        Assert.Equal(
            "Das Gericht hat in seinem Urteil festgestellt, dass",
            SentenceContext.CurrentSentence("Das Gericht hat in seinem Urteil festgestellt, dass"));
    }

    [Fact]
    public void ReturnsTailAfterLastTerminator()
    {
        Assert.Equal("die Beklagte", SentenceContext.CurrentSentence("Erster Satz. die Beklagte"));
    }

    [Theory]
    [InlineData("Frage? antwort", "antwort")]
    [InlineData("Ruf! weiter", "weiter")]
    [InlineData("A. B. die Sache", "die Sache")]
    public void SplitsOnSentenceEndingPunctuation(string input, string expected)
    {
        Assert.Equal(expected, SentenceContext.CurrentSentence(input));
    }

    [Fact]
    public void SplitsOnNewline()
    {
        Assert.Equal("zeile zwei", SentenceContext.CurrentSentence("Zeile eins\nzeile zwei"));
    }

    [Theory]
    [InlineData("Erster Satz. ")]
    [InlineData("Fertig.")]
    [InlineData("Ende!\n")]
    public void ReturnsNull_WhenCaretSitsRightAfterTerminator(string input)
    {
        // Abgeschlossener Satz → kein offener Satz → kein Kontext (Easy-Verhalten, Großschreibung).
        Assert.Null(SentenceContext.CurrentSentence(input));
    }

    [Fact]
    public void ColonAndSemicolonAreNotSentenceBoundaries()
    {
        Assert.Equal("Ergebnis: die Forderung", SentenceContext.CurrentSentence("Ergebnis: die Forderung"));
        Assert.Equal("a; b", SentenceContext.CurrentSentence("a; b"));
    }

    [Fact]
    public void TruncatesToLastMaxChars()
    {
        var sentence = new string('x', 50) + "ende";
        var result = SentenceContext.CurrentSentence(sentence, maxChars: 10);
        Assert.Equal("xxxxxxende", result);
        Assert.Equal(10, result!.Length);
    }
}
