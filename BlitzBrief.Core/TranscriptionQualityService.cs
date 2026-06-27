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
        (new Regex(@"\bAnführungszeichen[,\s]+[Aa]uf\b", RegexOptions.Compiled), "Anführungszeichen auf"),
        (new Regex(@"\bAnführungszeichen[,\s]+[Zz]u\b", RegexOptions.Compiled), "Anführungszeichen zu"),
        (new Regex(@"\bKlammer[,\s]+[Aa]uf\b", RegexOptions.Compiled), "Klammer auf"),
        (new Regex(@"\bKlammer[,\s]+[Zz]u\b", RegexOptions.Compiled), "Klammer zu"),
        (new Regex(@"\bneue[,\s]+[Zz]eile\b", RegexOptions.Compiled), "neue Zeile"),
        (new Regex(@"\bLeerzeile\b", RegexOptions.Compiled), "Leerzeile"),
        (new Regex(@"\bleerzeile\b", RegexOptions.Compiled), "Leerzeile"),
        (new Regex(@"\bneuer[,\s]+[Aa]bsatz\b", RegexOptions.Compiled), "neuer Absatz"),
        (new Regex(@"\bText[,\s]+[Ee]inrücken\b", RegexOptions.Compiled), "Text einrücken"),
    ];

    public static string NormalizeCommands(string text)
    {
        foreach (var (pattern, replacement) in CommandNormalizations)
        {
            text = pattern.Replace(text, replacement);
        }
        return text;
    }

    // Kategorie eines Kommandos – bestimmt, welche Whisper-Pausenzeichen rund um
    // das Kommando verschluckt werden und wie das Zielzeichen umrandet wird.
    private enum CommandKind
    {
        TightPunctuation, // , ; : . ! ?  -> hängt links an: "Wort; nächstes"
        DashPunctuation,  // —            -> Leerzeichen beidseitig: "Wort — nächstes"
        OpenBracket,      // ( "auf       -> hängt rechts an: "Wort (nächstes"
        CloseBracket,     // ) "zu        -> hängt links an: "Wort) nächstes"
        Newline,          // \n \n\n      -> nur Umbruch, davorstehenden Satzpunkt bewahren
        Indent            // Einrückung   -> Leerraum, ohne Umbruch zu schlucken
    }

    private sealed record CommandSpec(Regex Word, string Sentinel, CommandKind Kind, string Output);

    // Whisper markiert die Sprechpause rund um ein Kommandowort mit eigenen
    // Satzzeichen (Komma, Gedankenstrich – U+2013, Punkt). Diese "Padding"-Zeichen
    // werden direkt am Kommando verschluckt – global würden sie auch legitime
    // Satzzeichen im übrigen Text treffen.
    private const string PunctPad = @"[ \t,.–—-]*";   // inkl. Punkt: ". Doppelpunkt." -> ":"
    private const string BracketPad = @"[ \t,–—-]*";  // ohne Punkt: Satzende vor Klammer bewahren
    private const string NewlineLeftPad = @"[ \t,–—-]*";   // ohne Punkt: "wurde." bleibt erhalten
    private const string NewlineRightPad = @"[ \t,.–—-]*"; // inkl. Punkt: Whisper-Autopunkt nach Umbruch
    private const string IndentPad = @"[ ,]*";                  // nur Leerraum/Komma, Umbruch nicht schlucken

    // Mehrwort-Patterns müssen vor ihren Teilwörtern stehen. Die Sentinels sind
    // Private-Use-Zeichen (U+E000…), damit eingesetzte Zielzeichen nicht im selben
    // Lauf erneut als Whisper-Padding interpretiert werden.
    private static readonly CommandSpec[] RawCommands =
    [
        new(new Regex(@"\bAnführungszeichen[,\s]+auf\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "", CommandKind.OpenBracket,       "\""),
        new(new Regex(@"\bAnführungszeichen[,\s]+zu\b",  RegexOptions.Compiled | RegexOptions.IgnoreCase), "", CommandKind.CloseBracket,      "\""),
        new(new Regex(@"\bKlammer[,\s]+auf\b",           RegexOptions.Compiled | RegexOptions.IgnoreCase), "", CommandKind.OpenBracket,       "("),
        new(new Regex(@"\bKlammer[,\s]+zu\b",            RegexOptions.Compiled | RegexOptions.IgnoreCase), "", CommandKind.CloseBracket,      ")"),
        new(new Regex(@"\bneue[,\s]+Zeile\b",            RegexOptions.Compiled | RegexOptions.IgnoreCase), "", CommandKind.Newline,           "\n"),
        new(new Regex(@"\bText[,\s]+einrücken\b",        RegexOptions.Compiled | RegexOptions.IgnoreCase), "", CommandKind.Indent,            "    "),
        new(new Regex(@"\bGedankenstrich\b",             RegexOptions.Compiled | RegexOptions.IgnoreCase), "", CommandKind.DashPunctuation,   "—"),
        new(new Regex(@"\bDoppelpunkt\b",                RegexOptions.Compiled | RegexOptions.IgnoreCase), "", CommandKind.TightPunctuation,  ":"),
        new(new Regex(@"\bSemikolon\b",                  RegexOptions.Compiled | RegexOptions.IgnoreCase), "", CommandKind.TightPunctuation,  ";"),
        new(new Regex(@"\bAusrufezeichen\b",             RegexOptions.Compiled | RegexOptions.IgnoreCase), "", CommandKind.TightPunctuation,  "!"),
        new(new Regex(@"\bFragezeichen\b",               RegexOptions.Compiled | RegexOptions.IgnoreCase), "", CommandKind.TightPunctuation,  "?"),
        new(new Regex(@"\bSatzende\b",                   RegexOptions.Compiled | RegexOptions.IgnoreCase), "", CommandKind.TightPunctuation,  "."),
        new(new Regex(@"\bLeerzeile\b",                  RegexOptions.Compiled | RegexOptions.IgnoreCase), "", CommandKind.Newline,           "\n\n"),
        new(new Regex(@"\bneuer[,\s]+Absatz\b",          RegexOptions.Compiled | RegexOptions.IgnoreCase), "", CommandKind.Newline,           "\n\n"),
        new(new Regex(@"\bKomma\b",                      RegexOptions.Compiled | RegexOptions.IgnoreCase), "", CommandKind.TightPunctuation,  ","),
    ];

    // Echte Sentinels je Kommando vergeben: Private-Use-Zeichen U+E000 + Index.
    // (Im Literal stehen nur Platzhalter, damit dort keine Sonderzeichen nötig sind.)
    private static readonly CommandSpec[] Commands =
        RawCommands.Select((c, i) => c with { Sentinel = ((char)('' + i)).ToString() }).ToArray();

    // Pro Kommando: Pausenzeichen rund um den Sentinel entfernen und das
    // Zielzeichen mit kategoriegerechtem Spacing einsetzen.
    private static readonly (Regex Pattern, string Replacement)[] SentinelStrips =
        Commands.Select(BuildStrip).ToArray();

    private static (Regex Pattern, string Replacement) BuildStrip(CommandSpec c)
    {
        var s = Regex.Escape(c.Sentinel);
        return c.Kind switch
        {
            CommandKind.TightPunctuation => (new Regex($"{PunctPad}{s}{PunctPad}", RegexOptions.Compiled), c.Output + " "),
            CommandKind.DashPunctuation  => (new Regex($"{PunctPad}{s}{PunctPad}", RegexOptions.Compiled), " " + c.Output + " "),
            CommandKind.OpenBracket      => (new Regex($"{BracketPad}{s}{BracketPad}", RegexOptions.Compiled), " " + c.Output),
            CommandKind.CloseBracket     => (new Regex($"{BracketPad}{s}{BracketPad}", RegexOptions.Compiled), c.Output + " "),
            CommandKind.Newline          => (new Regex($"{NewlineLeftPad}{s}{NewlineRightPad}", RegexOptions.Compiled), c.Output),
            CommandKind.Indent           => (new Regex($"{IndentPad}{s}{IndentPad}", RegexOptions.Compiled), c.Output),
            _ => throw new InvalidOperationException($"Unbekannte Kommandokategorie: {c.Kind}")
        };
    }

    public static string ReplaceCommands(string text)
    {
        // 1. Kommandowörter durch eindeutige Sentinels ersetzen.
        foreach (var c in Commands)
            text = c.Word.Replace(text, c.Sentinel);

        // 2. Whisper-Pausenzeichen rund um jeden Sentinel verschlucken und das
        //    Zielzeichen mit korrektem Spacing einsetzen.
        foreach (var (pattern, replacement) in SentinelStrips)
            text = pattern.Replace(text, replacement);

        // 3. Restglättung – Einrückungen am Zeilenanfang bleiben erhalten.
        text = Regex.Replace(text, @"(?<=\S)[ \t]{2,}", " ");        // doppelte Leerzeichen im Fließtext
        text = Regex.Replace(text, @"(?<=\S)[ \t]+([,.;:!?)])", "$1"); // Leerzeichen vor schließenden Zeichen
        return text.Trim();
    }

    public static string ProcessJornCommands(string text) =>
        ReplaceCommands(NormalizeCommands(text));

    // "Paragraf"/"Paragraph" und "Euro" schreibt das Transkriptionsmodell mal als
    // Zeichen (§, €), mal als Wort aus. Sobald eine Ziffer im Spiel ist, ist die
    // Bedeutung eindeutig (Paragraphennummer bzw. Geldbetrag) – dann erzwingen wir
    // deterministisch das Zeichen, unabhängig davon, wie der Prompt gewirkt hat.
    // Ohne folgende Ziffer bleibt der Fließtext unangetastet ("der erste Absatz des
    // Paragrafen regelt das"). Inflektion (-en/-s) wird mitgenommen, damit auch der
    // häufige Genitiv "des Paragraphen 5" greift.
    private static readonly Regex ParagraphSymbol =
        new(@"\bParagra(?:f|ph)(?:en|s)?\s+(?=\d)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Betrag (mit optionalen Tausender-/Dezimaltrennzeichen) direkt vor "Euro";
    // das Zeichen steht nach deutscher Konvention hinter dem Betrag: "1.000,50 €".
    private static readonly Regex EuroSymbol =
        new(@"(\d(?:[.,]?\d)*)\s*Euro\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static string NormalizeSymbols(string text)
    {
        text = ParagraphSymbol.Replace(text, "§ ");
        text = EuroSymbol.Replace(text, "$1 €");
        return text;
    }

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

    // Bei (fast) leerem oder sehr kurzem Audio halluziniert das Transkriptionsmodell teils
    // lange, plausibel klingende Textwände (z.B. eine Standard-AGB oder einen Lehrtext).
    // Solche Halluzinationen sind weder ein bekanntes Kurz-Artefakt (IsLikelyArtifact) noch
    // ein Prompt-Echo (IsPromptEcho) und rutschen sonst ungefiltert durch. Verlässliches
    // Erkennungsmerkmal ist die Sprechrate: selbst sehr schnelle Sprecher bringen keine
    // zweistellige Wörter/Sekunde-Rate über viele Wörter hinweg zustande – eine deutlich
    // höhere Rate ist physikalisch unmöglich und damit ein sicheres Halluzinations-Signal.
    // Die Mindestwortzahl schützt kurze, legitime Diktate, bei denen die Rate verrauscht ist.
    private const double MaxPlausibleWordsPerSecond = 8.0;
    private const int HallucinationMinWords = 12;

    public static bool IsImplausiblyFast(string transcript, TimeSpan duration)
    {
        if (string.IsNullOrWhiteSpace(transcript) || duration.TotalSeconds <= 0)
        {
            return false;
        }

        var words = EchoTokens(transcript).Length;
        if (words < HallucinationMinWords)
        {
            return false;
        }

        return words / duration.TotalSeconds > MaxPlausibleWordsPerSecond;
    }

    // Bei (fast) leerem Audio spiegelt das Transkriptionsmodell den mitgesendeten
    // Prompt zurück. Da wir den Prompt kennen, erkennen wir dieses Echo: stimmt ein
    // großer Wortanteil des Transkripts mit dem Prompt überein, ist es kein echtes
    // Diktat. Die Mindestwortzahl schützt kurze, legitime Diktate (z.B. "Komma").
    private const int EchoMinWords = 6;
    private const double EchoOverlapThreshold = 0.8;
    private static readonly Regex EchoNonWord = new(@"[^\p{L}\p{N}]+", RegexOptions.Compiled);

    public static bool IsPromptEcho(string transcript, string? prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt) || string.IsNullOrWhiteSpace(transcript))
            return false;

        var transcriptWords = EchoTokens(transcript);
        if (transcriptWords.Length < EchoMinWords)
            return false;

        var promptWords = EchoTokens(prompt).ToHashSet(StringComparer.Ordinal);
        if (promptWords.Count == 0)
            return false;

        var matches = transcriptWords.Count(promptWords.Contains);
        return (double)matches / transcriptWords.Length >= EchoOverlapThreshold;
    }

    private static string[] EchoTokens(string text) =>
        EchoNonWord.Replace(text.ToLowerInvariant(), " ")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
}
