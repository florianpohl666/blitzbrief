using System.Text.RegularExpressions;

namespace BlitzBrief.Core;

public static class TranscriptionQualityService
{
    // Häufige Whisper-Varianten der Kommandowörter → Normalform.
    // Reihenfolge ist relevant: längere/spezifischere Patterns zuerst.
    private static readonly (Regex Pattern, string Replacement)[] CommandNormalizations =
    [
        (new Regex(@"\bSatz\s+[Ee]nde\b", RegexOptions.Compiled), "Satzende"),
        (new Regex(@"\bsatzende\b", RegexOptions.Compiled), "Satzende"),
        (new Regex(@"\bDoppel\s*[Pp]unkt\b", RegexOptions.Compiled), "Doppelpunkt"),
        (new Regex(@"\bdoppelpunkt\b", RegexOptions.Compiled), "Doppelpunkt"),
        (new Regex(@"\bkomma\b", RegexOptions.Compiled), "Komma"),
        (new Regex(@"\bSemikolon\b", RegexOptions.Compiled), "Semikolon"),
        (new Regex(@"\bsemikolon\b", RegexOptions.Compiled), "Semikolon"),
        (new Regex(@"\bAusrufezeichen\b", RegexOptions.Compiled), "Ausrufezeichen"),
        (new Regex(@"\bausrufezeichen\b", RegexOptions.Compiled), "Ausrufezeichen"),
        (new Regex(@"\bFragezeichen\b", RegexOptions.Compiled), "Fragezeichen"),
        (new Regex(@"\bfragezeichen\b", RegexOptions.Compiled), "Fragezeichen"),
        (new Regex(@"\bGedanken\s*[Ss]trich\b", RegexOptions.Compiled), "Gedankenstrich"),
        (new Regex(@"\bgedankenstrich\b", RegexOptions.Compiled), "Gedankenstrich"),
        (new Regex(@"\bAnführungszeichen\s+[Aa]uf\b", RegexOptions.Compiled), "Anführungszeichen auf"),
        (new Regex(@"\bAnführungszeichen\s+[Zz]u\b", RegexOptions.Compiled), "Anführungszeichen zu"),
        (new Regex(@"\bKlammer\s+[Aa]uf\b", RegexOptions.Compiled), "Klammer auf"),
        (new Regex(@"\bKlammer\s+[Zz]u\b", RegexOptions.Compiled), "Klammer zu"),
        (new Regex(@"\bneue\s+[Zz]eile\b", RegexOptions.Compiled), "neue Zeile"),
        (new Regex(@"\bLeerzeile\b", RegexOptions.Compiled), "Leerzeile"),
        (new Regex(@"\bleerzeile\b", RegexOptions.Compiled), "Leerzeile"),
        (new Regex(@"\bAbsatz\b", RegexOptions.Compiled), "Absatz"),
        (new Regex(@"\babsatz\b", RegexOptions.Compiled), "Absatz"),
        (new Regex(@"\bText\s+[Ee]inrücken\b", RegexOptions.Compiled), "Text einrücken"),
    ];

    public static string NormalizeCommands(string text)
    {
        foreach (var (pattern, replacement) in CommandNormalizations)
        {
            text = pattern.Replace(text, replacement);
        }
        return text;
    }

    // Multi-word patterns müssen vor ihren Teilwörtern stehen.
    private static readonly (Regex Pattern, string Replacement)[] CommandReplacements =
    [
        (new Regex(@"\bAnführungszeichen\s+auf\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "\""),
        (new Regex(@"\bAnführungszeichen\s+zu\b",  RegexOptions.Compiled | RegexOptions.IgnoreCase), "\""),
        (new Regex(@"\bKlammer\s+auf\b",            RegexOptions.Compiled | RegexOptions.IgnoreCase), "("),
        (new Regex(@"\bKlammer\s+zu\b",             RegexOptions.Compiled | RegexOptions.IgnoreCase), ")"),
        (new Regex(@"\bneue\s+Zeile\b",             RegexOptions.Compiled | RegexOptions.IgnoreCase), "\n"),
        (new Regex(@"\bText\s+einrücken\b",         RegexOptions.Compiled | RegexOptions.IgnoreCase), "    "),
        (new Regex(@"\bGedankenstrich\b",           RegexOptions.Compiled | RegexOptions.IgnoreCase), "—"),
        (new Regex(@"\bDoppelpunkt\b",              RegexOptions.Compiled | RegexOptions.IgnoreCase), ":"),
        (new Regex(@"\bSemikolon\b",                RegexOptions.Compiled | RegexOptions.IgnoreCase), ";"),
        (new Regex(@"\bAusrufezeichen\b",           RegexOptions.Compiled | RegexOptions.IgnoreCase), "!"),
        (new Regex(@"\bFragezeichen\b",             RegexOptions.Compiled | RegexOptions.IgnoreCase), "?"),
        (new Regex(@"\bSatzende\b",                 RegexOptions.Compiled | RegexOptions.IgnoreCase), "."),
        (new Regex(@"\bLeerzeile\b",                RegexOptions.Compiled | RegexOptions.IgnoreCase), "\n\n"),
        (new Regex(@"\bAbsatz\b",                   RegexOptions.Compiled | RegexOptions.IgnoreCase), "\n\n"),
        (new Regex(@"\bKomma\b",                    RegexOptions.Compiled | RegexOptions.IgnoreCase), ","),
    ];

    public static string ReplaceCommands(string text)
    {
        foreach (var (pattern, replacement) in CommandReplacements)
            text = pattern.Replace(text, replacement);

        // Leerzeichen vor schließenden Satzzeichen entfernen, die durch Ersetzung entstanden sind.
        text = Regex.Replace(text, @" +([,.;:!?)])", "$1");
        return text;
    }

    public static string ProcessJornCommands(string text) =>
        ReplaceCommands(NormalizeCommands(text));

    private static readonly string[] CommonArtifacts =
    [
        "Untertitel der Amara.org-Community",
        "Danke fürs Zuschauen",
        "Vielen Dank fürs Zuschauen",
        "Thanks for watching"
    ];

    public static bool ShouldRejectRecording(TimeSpan duration) => duration.TotalMilliseconds < 350;

    public static string CleanedTranscript(string text) => string.Join(
        "\n",
        text.Replace("\r\n", "\n")
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line)))
        .Trim();

    public static bool IsLikelyArtifact(string text, TimeSpan duration)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if (duration.TotalSeconds < 1.0 && text.Length < 4)
        {
            return true;
        }

        return duration.TotalSeconds < 2.0 &&
               CommonArtifacts.Any(artifact => text.Contains(artifact, StringComparison.OrdinalIgnoreCase));
    }
}
