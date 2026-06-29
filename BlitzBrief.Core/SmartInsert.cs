namespace BlitzBrief.Core;

/// <summary>
/// Formt das fertige Diktat für den Kontext-Modus passend zur Einfügestelle:
/// führendes/nachgestelltes Leerzeichen anhand der Zeichen links/rechts vom Cursor und
/// Entfernen eines automatisch gesetzten Schlusspunkts bei einem Satzeinschub. whisper-1 setzt
/// diesen Punkt immer und lässt sich per Prompt nicht davon abbringen (per Spike bestätigt,
/// siehe Memory kontext-mode-whisper1) – daher hier deterministisch.
/// </summary>
public static class SmartInsert
{
    // Zeichen, nach denen KEIN führendes Leerzeichen kommt (öffnende Klammern/Anführung, Slash).
    private const string Openers = "([{„«“‹/";

    // Zeichen, die nach links kleben (schließende/anhängende Satzzeichen). Symmetrisch genutzt:
    // rechts vom Cursor unterdrücken sie das nachgestellte Leerzeichen; als ERSTES Zeichen des
    // Diktats unterdrücken sie das führende Leerzeichen (z.B. ",dass" statt "Wort , dass").
    private const string RightHuggers = ".,;:!?)]}»”›…";

    // Satzzeichen, die – direkt rechts vom Cursor – signalisieren, dass der Satz dort weiterläuft.
    private const string ContinuingPunctuation = ",;:.!?…";

    public static string Format(string dictation, string? preceding, string? following)
    {
        var text = dictation;

        if (IsMidSentenceInsertion(preceding, following))
        {
            text = StripTrailingAutoPeriod(text);
        }

        if (NeedsLeadingSpace(preceding) && !StartsWithRightHugger(text))
        {
            text = " " + text;
        }

        if (NeedsTrailingSpace(following))
        {
            text += " ";
        }

        return text;
    }

    /// <summary>
    /// Satzeinschub = links ein offener Satz (kein Satzende direkt vor dem Cursor) UND rechts
    /// läuft der Satz weiter (Kleinbuchstabe oder anhängendes Satzzeichen). Nur dann wird der
    /// Auto-Punkt entfernt; bei Großbuchstabe/Ziffer/Leer rechts bleibt er erhalten.
    /// </summary>
    public static bool IsMidSentenceInsertion(string? preceding, string? following) =>
        SentenceContext.CurrentSentence(preceding) is not null && RightContinuesSentence(following);

    private static bool RightContinuesSentence(string? following)
    {
        if (string.IsNullOrEmpty(following))
        {
            return false;
        }

        // Führende Leerzeichen/Tabs überspringen, aber NICHT über einen Zeilenumbruch hinweg
        // (neue Zeile = nicht mehr derselbe Satz).
        var i = 0;
        while (i < following.Length && (following[i] == ' ' || following[i] == '\t'))
        {
            i++;
        }

        if (i >= following.Length || following[i] is '\n' or '\r')
        {
            return false;
        }

        var c = following[i];
        return char.IsLower(c) || ContinuingPunctuation.IndexOf(c) >= 0;
    }

    private static string StripTrailingAutoPeriod(string text)
    {
        // Nur einen einzelnen End-Punkt entfernen; Ellipse (... oder …) sowie ?/! bewahren.
        if (text.EndsWith("...", StringComparison.Ordinal) || text.EndsWith('…'))
        {
            return text;
        }

        return text.EndsWith('.') ? text[..^1].TrimEnd() : text;
    }

    private static bool NeedsLeadingSpace(string? preceding)
    {
        if (string.IsNullOrEmpty(preceding))
        {
            return false;
        }

        var c = preceding[^1];
        return !char.IsWhiteSpace(c) && Openers.IndexOf(c) < 0;
    }

    /// <summary>
    /// Beginnt das Diktat mit einem nach links klebenden Satzzeichen (Komma, Punkt, Doppelpunkt,
    /// schließende Klammer/Anführung …)? Dann darf kein führendes Leerzeichen davor. Öffnende
    /// Anführungszeichen, §, Gedankenstrich usw. zählen NICHT dazu und behalten ihr Leerzeichen.
    /// </summary>
    private static bool StartsWithRightHugger(string text) =>
        !string.IsNullOrEmpty(text) && RightHuggers.IndexOf(text[0]) >= 0;

    private static bool NeedsTrailingSpace(string? following)
    {
        // Am Feld-/Dokumentende (rechts nichts) ein Leerzeichen anhängen, damit das nächste
        // Diktat nahtlos anschließt (bisheriges Verhalten der App).
        if (string.IsNullOrEmpty(following))
        {
            return true;
        }

        var c = following[0];
        return !char.IsWhiteSpace(c) && RightHuggers.IndexOf(c) < 0;
    }
}
