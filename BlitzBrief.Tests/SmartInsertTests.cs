using BlitzBrief.Core;

namespace BlitzBrief.Tests;

public sealed class SmartInsertTests
{
    // ── Führendes Leerzeichen ────────────────────────────────────────────────
    [Theory]
    [InlineData("dass", true)]   // Buchstabe links → Leerzeichen
    [InlineData("Kosten)", true)] // schließende Klammer → Leerzeichen
    [InlineData("Betrag 5", true)] // Ziffer → Leerzeichen
    [InlineData("Wort ", false)]  // schon Leerzeichen → keins
    [InlineData("Zeile\n", false)] // Zeilenumbruch → keins
    [InlineData("(", false)]      // öffnende Klammer → keins
    [InlineData("/", false)]      // Slash → keins
    public void LeadingSpace_DependsOnCharLeftOfCursor(string preceding, bool expectsLeadingSpace)
    {
        // following leer → kein Einschub, kein Strip; isoliert das führende Leerzeichen.
        var result = SmartInsert.Format("Text", preceding, following: null);
        Assert.Equal(expectsLeadingSpace, result.StartsWith(' '));
    }

    [Fact]
    public void LeadingSpace_NotAddedAtFieldStart()
    {
        Assert.Equal("Text ", SmartInsert.Format("Text", preceding: null, following: null));
        Assert.Equal("Text ", SmartInsert.Format("Text", preceding: "", following: null));
    }

    // ── Nachgestelltes Leerzeichen ───────────────────────────────────────────
    [Theory]
    [InlineData(null, true)]      // Feldende → Leerzeichen (Verkettung)
    [InlineData("", true)]
    [InlineData("Wort", true)]    // Buchstabe rechts klebt → Leerzeichen
    [InlineData(" Wort", false)]  // rechts schon Leerzeichen → keins
    [InlineData(", weil", false)] // anhängendes Satzzeichen → keins
    [InlineData(". Satz", false)] // Punkt rechts → keins
    public void TrailingSpace_DependsOnCharRightOfCursor(string? following, bool expectsTrailingSpace)
    {
        // preceding mit Leerzeichen → kein führendes Leerzeichen; isoliert das nachgestellte.
        var result = SmartInsert.Format("Text", preceding: "Wort ", following: following);
        Assert.Equal(expectsTrailingSpace, result.EndsWith(' '));
    }

    // ── Auto-Punkt bei Satzeinschub ──────────────────────────────────────────
    [Fact]
    public void StripsAutoPeriod_WhenInsertingMidSentence()
    {
        // links offen ("…dass"), rechts läuft klein weiter → Einschub → Punkt weg.
        var result = SmartInsert.Format("die Beklagte zahlt die Kosten.",
            preceding: "Das Gericht hat festgestellt, dass", following: " rechtskräftig ist.");
        Assert.Equal(" die Beklagte zahlt die Kosten", result);
    }

    [Fact]
    public void KeepsPeriod_WhenRightStartsNewCapitalizedSentence()
    {
        var result = SmartInsert.Format("die Beklagte zahlt die Kosten.",
            preceding: "Das Gericht hat festgestellt, dass", following: " Der nächste Satz folgt.");
        Assert.EndsWith("Kosten.", result);
    }

    [Fact]
    public void KeepsPeriod_AtEndOfField()
    {
        var result = SmartInsert.Format("Hallo Welt.", preceding: "Bla ", following: null);
        Assert.Equal("Hallo Welt. ", result);
    }

    [Fact]
    public void KeepsPeriod_WhenLeftSentenceIsClosed()
    {
        // links abgeschlossen → kein offener Satz → kein Einschub, auch wenn rechts klein weiterginge.
        var result = SmartInsert.Format("ein neuer Satz.",
            preceding: "Fertig.", following: " und weiter");
        Assert.EndsWith("Satz.", result);
    }

    [Theory]
    [InlineData("Moment mal...")]
    [InlineData("und so weiter …")]
    [InlineData("wirklich?")]
    [InlineData("genau!")]
    public void DoesNotStrip_EllipsisOrOtherTerminators(string dictation)
    {
        var result = SmartInsert.Format(dictation,
            preceding: "Das Gericht hat festgestellt, dass", following: " rechtskräftig ist.");
        Assert.EndsWith(dictation[^1].ToString(), result.TrimEnd());
    }

    [Fact]
    public void MidSentenceDetection_RequiresOpenLeftAndContinuingRight()
    {
        Assert.True(SmartInsert.IsMidSentenceInsertion("Das Gericht stellte fest, dass", " die Beklagte"));
        Assert.True(SmartInsert.IsMidSentenceInsertion("weil", "festgestellt"));   // ohne Leerzeichen, klein
        Assert.True(SmartInsert.IsMidSentenceInsertion("weil", ". Der"));          // Satzzeichen rechts
        Assert.False(SmartInsert.IsMidSentenceInsertion("dass", " Der Name"));     // Großbuchstabe rechts
        Assert.False(SmartInsert.IsMidSentenceInsertion("Fertig.", " und weiter")); // links geschlossen
        Assert.False(SmartInsert.IsMidSentenceInsertion("dass", null));            // rechts nichts
        Assert.False(SmartInsert.IsMidSentenceInsertion("dass", "\nneue Zeile"));  // Zeilenumbruch rechts
    }
}
