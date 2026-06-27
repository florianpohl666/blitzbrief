namespace BlitzBrief.Core;

/// <summary>
/// Extrahiert aus einem Vortext-Blob (Text unmittelbar links vom Cursor) den aktuell
/// angefangenen Satz – also den Teil hinter dem letzten satzbeendenden Zeichen. Endet der
/// Vortext direkt auf einem Satzende (oder ist leer), gibt es keinen offenen Satz → <c>null</c>,
/// und der Kontext-Modus verhält sich wie BlitzBrief-Easy (Großschreibung am Satzanfang).
/// </summary>
public static class SentenceContext
{
    // Satzgrenzen: . ! ? sowie Zeilen-/Absatzumbruch. Doppelpunkt und Semikolon gelten NICHT
    // als Grenze – der Satz läuft dort weiter, damit whisper-1 die Fortsetzung daran erkennt.
    private static readonly char[] Terminators = ['.', '!', '?', '\n', '\r'];

    /// <summary>
    /// Der angefangene Satz hinter dem letzten Satzende in <paramref name="preceding"/>.
    /// whisper-1 wertet nur die letzten ~224 Tokens des Prompts aus; der Teil direkt links
    /// vom Cursor ist für die Fortsetzung am wichtigsten – daher bei Überlänge vorne gekappt.
    /// </summary>
    public static string? CurrentSentence(string? preceding, int maxChars = 300)
    {
        if (string.IsNullOrWhiteSpace(preceding))
        {
            return null;
        }

        var cut = preceding.LastIndexOfAny(Terminators);
        var sentence = (cut >= 0 ? preceding[(cut + 1)..] : preceding).Trim();
        if (sentence.Length == 0)
        {
            return null;
        }

        return sentence.Length > maxChars ? sentence[^maxChars..] : sentence;
    }
}
