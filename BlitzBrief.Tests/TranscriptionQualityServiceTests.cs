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

    [Fact]
    public void IsPromptEcho_ErkenntZurueckgespiegeltenWhisperPrompt()
    {
        var prompt = PromptBuilder.BuildWhisperPrompt([], includeCommandHints: true);

        // Whisper liefert bei leerem Audio den Prompt zurück – mal verbatim (Kommando-
        // wörter als Wörter), mal vom Modell schon in Satzzeichen umgewandelt.
        var echoVerbatim = prompt!;
        var echoProcessed = TranscriptionQualityService.ProcessJornCommands(prompt!);

        Assert.True(TranscriptionQualityService.IsPromptEcho(echoVerbatim, prompt));
        Assert.True(TranscriptionQualityService.IsPromptEcho(echoProcessed, prompt));
    }

    [Fact]
    public void IsPromptEcho_LaesstEchteDiktateDurch()
    {
        var prompt = PromptBuilder.BuildWhisperPrompt([], includeCommandHints: true);

        Assert.False(TranscriptionQualityService.IsPromptEcho("der Beklagte hat den Vertrag nicht erfüllt", prompt));
        Assert.False(TranscriptionQualityService.IsPromptEcho("Vertragsstrafe", prompt));
        Assert.False(TranscriptionQualityService.IsPromptEcho("Komma", prompt));
    }

    [Fact]
    public void IsPromptEcho_OhnePromptOderText_FalschesFalse()
    {
        Assert.False(TranscriptionQualityService.IsPromptEcho("ein langer satz der genug woerter hat", null));
        Assert.False(TranscriptionQualityService.IsPromptEcho("ein langer satz der genug woerter hat", ""));
        Assert.False(TranscriptionQualityService.IsPromptEcho("", "irgendein prompt mit vielen woertern hier drin"));
    }

    [Fact]
    public void ProcessJornCommands_NeuerAbsatz_LoestUmbruchAus()
    {
        var result = TranscriptionQualityService.ProcessJornCommands(
            "Erster Teil, neuer Absatz, zweiter Teil.");

        Assert.Equal("Erster Teil\n\nzweiter Teil.", result);
    }

    [Fact]
    public void ProcessJornCommands_BlossesAbsatz_BleibtFliesstext()
    {
        // "Absatz" allein ist kein Kommando mehr – es kommt im juristischen Fließtext
        // häufig vor und darf keinen Umbruch auslösen.
        var result = TranscriptionQualityService.ProcessJornCommands(
            "Der erste Absatz des Paragrafen regelt das.");

        Assert.Equal("Der erste Absatz des Paragrafen regelt das.", result);
        Assert.DoesNotContain("\n", result);
    }

    [Fact]
    public void ProcessJornCommands_WhisperArtifacts_AreRemovedCorrectly()
    {
        // Whisper-Rohtext wie vom Nutzer gemeldet: Kommandowörter in Kommas eingebettet,
        // automatisch Punkte nach Zeilenumbruch-Kommandos eingefügt.
        const string whisperRaw =
            "Klageschrift Meyer gegen Müller, neue Zeile. " +
            "Dem Beklagten wird zur Last gelegt, dass die Semmel zu klein gebacken wurde. Neue Zeile. Neue Zeile. " +
            "Verursachender Auslöser, Gedankenstrich, ist, Komma, der zu trockene Teig, Semikolon. " +
            "Der Teig hatte zu wenig Wasser, Komma, was den Teig zu leicht werden ließ, Doppelpunkt. " +
            "Der Teig, Klammer auf, und damit auch das Produkt, Klammer zu, war zu leicht.";

        var result = TranscriptionQualityService.ProcessJornCommands(whisperRaw);

        Assert.Contains("Meyer gegen Müller\n", result);  // Zeilenumbruch korrekt
        Assert.DoesNotContain("\n.", result);              // Kein Whisper-Autopunkt am Zeilenanfang
        Assert.DoesNotContain(", —", result);             // Kein Komma vor Gedankenstrich
        Assert.DoesNotContain(",;", result);              // Kein Komma vor Semikolon
        Assert.DoesNotContain(",:", result);              // Kein Komma vor Doppelpunkt
        Assert.DoesNotContain(",,", result);              // Kein Doppelkomma
        Assert.Contains("—", result);
        Assert.Contains(";", result);
        Assert.Contains(":", result);
        Assert.Contains("(", result);
        Assert.Contains(")", result);
    }

    [Fact]
    public void ProcessJornCommands_WhisperPausenzeichen_FuehrenNichtZuDopplungen()
    {
        // Vom Nutzer gemeldeter Fall: Whisper markiert die Sprechpause rund um ein
        // Kommandowort mal mit Gedankenstrich (–), mal mit Komma, mal mit Punkt.
        // Diese Pausenzeichen dürfen nicht zusätzlich zum gesetzten Zeichen bleiben.
        const string whisperRaw =
            "Klageschrift Maja gegen Müller Neue Zeile. " +
            "Dem Beklagten wird gemäß § 103 zur Last gelegt, dass im vorliegenden Fall die Semmel zu klein gebacken wurde. Neue Zeile Neue Zeile. " +
            "Verursachender Auslöser – Gedankenstrich – ist der zu trockene Teig, Semikolon, der Teig hatte zu wenig Wasser, was den Teig zu leicht werden ließ. Doppelpunkt. " +
            "Der Teig – Klammer auf – und damit auch das Produkt – Klammer zu – war zu leicht.";

        const string expected =
            "Klageschrift Maja gegen Müller\n" +
            "Dem Beklagten wird gemäß § 103 zur Last gelegt, dass im vorliegenden Fall die Semmel zu klein gebacken wurde.\n\n" +
            "Verursachender Auslöser — ist der zu trockene Teig; der Teig hatte zu wenig Wasser, was den Teig zu leicht werden ließ: Der Teig (und damit auch das Produkt) war zu leicht.";

        var result = TranscriptionQualityService.ProcessJornCommands(whisperRaw);

        Assert.Equal(expected, result);
        Assert.DoesNotContain("– —", result);   // kein en-dash neben em-dash
        Assert.DoesNotContain("— –", result);
        Assert.DoesNotContain(";,", result);     // kein Komma nach Semikolon
        Assert.DoesNotContain(".:", result);     // kein Punkt vor Doppelpunkt
        Assert.DoesNotContain("– (", result);    // keine Whisper-Striche um Klammern
        Assert.DoesNotContain(") –", result);
    }

    [Theory]
    [InlineData("Gemäß Paragraf 103 ist das geregelt.", "Gemäß § 103 ist das geregelt.")]
    [InlineData("siehe Paragraph 5 Absatz 2", "siehe § 5 Absatz 2")]
    [InlineData("des Paragraphen 90a", "des § 90a")]                 // Genitiv + Buchstabenzusatz
    [InlineData("PARAGRAF 7", "§ 7")]                                 // Großschreibung
    [InlineData("Paragraf   12", "§ 12")]                             // Mehrfach-Leerzeichen kollabiert
    public void NormalizeSymbols_Paragraf_VorZiffer_WirdZeichen(string input, string expected)
    {
        Assert.Equal(expected, TranscriptionQualityService.NormalizeSymbols(input));
    }

    [Theory]
    [InlineData("Der erste Absatz des Paragrafen regelt das.")]      // kein folgender Wert -> Fließtext
    [InlineData("Er nannte den Paragraphen unklar.")]
    public void NormalizeSymbols_ParagrafOhneZiffer_BleibtFliesstext(string input)
    {
        Assert.Equal(input, TranscriptionQualityService.NormalizeSymbols(input));
    }

    [Theory]
    [InlineData("Das kostet 100 Euro.", "Das kostet 100 €.")]
    [InlineData("Eine Forderung von 1.000,50 Euro.", "Eine Forderung von 1.000,50 €.")]
    [InlineData("Streitwert 25 euro", "Streitwert 25 €")]            // klein geschrieben
    public void NormalizeSymbols_BetragVorEuro_WirdZeichen(string input, string expected)
    {
        Assert.Equal(expected, TranscriptionQualityService.NormalizeSymbols(input));
    }

    [Theory]
    [InlineData("Wir reisen nach Europa.")]                          // "Euro" als Wortteil
    [InlineData("Er zahlte mehrere Euro in bar.")]                   // kein Betrag davor
    public void NormalizeSymbols_EuroOhneBetrag_BleibtUnberuehrt(string input)
    {
        Assert.Equal(input, TranscriptionQualityService.NormalizeSymbols(input));
    }
}
