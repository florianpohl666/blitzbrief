using BlitzBrief.Core.Models;
using BlitzBrief.Core.Settings;

namespace BlitzBrief.Core;

public static class PromptBuilder
{
    public const string DefaultDampfAblassenPrompt =
        "Du erhältst ein emotional gesprochenes Transkript. Erkenne zuerst das eigentliche Ziel, Anliegen und den wahren Frust der Person. " +
        "Formuliere daraus eine klare, respektvolle und wirksame Nachricht, mit der die Person ihr Ziel eher erreicht. " +
        "Bewahre relevante Fakten, konkrete Probleme, Grenzen, Erwartungen und die nötige Dringlichkeit. " +
        "Entferne Beleidigungen, Drohungen, Sarkasmus, Unterstellungen und unnötige Eskalation. " +
        "Wenn mehrere Vorwürfe genannt werden, verdichte sie auf die entscheidenden Kernpunkte. " +
        "Der Ton soll ruhig, menschlich, bestimmt und lösungsorientiert sein. Gib NUR die fertige Nachricht zurück.";

    public static bool ShouldReplaceLegacyDampfPrompt(string? prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return true;
        }

        // Erkennungsmerkmale des alten Prompts: ASCII-Umschreibungen (ae/oe/ue) statt Umlauten.
        var normalized = prompt.Trim();
        return normalized.Contains("Du erhaeltst ein emotional gesprochenes Transkript", StringComparison.Ordinal) ||
               normalized.Contains("noetige Dringlichkeit", StringComparison.Ordinal) ||
               normalized.Contains("loesungsorientiert", StringComparison.Ordinal) ||
               normalized.Contains("Gib NUR die fertige Nachricht zurueck", StringComparison.Ordinal);
    }

    public static string? BuildWhisperPrompt(IReadOnlyList<string> customTerms, bool includeCommandHints, string language = "")
    {
        var parts = new List<string>();

        // Sprachvorgabe verstärkt den language-Parameter. gpt-4o-(mini-)transcribe
        // folgt Anweisungen, das stabilisiert vor allem kurze Wörter gegen das
        // Abdriften in fremde Sprachen. Bei "automatisch" ("") keine Vorgabe.
        var languageName = LanguageDirectiveName(language);
        if (languageName is not null)
        {
            parts.Add($"Transkribiere ausschließlich auf {languageName}.");
        }

        if (customTerms.Count > 0)
        {
            // Explizite Schreibanweisung biast nachweislich stärker als das frühere
            // "Begriff Satzende"-Fließtextformat (per A/B-Spike gegen Realtime+Batch bestätigt).
            parts.Add("Verwende für folgende Eigennamen und Fachbegriffe exakt diese Schreibweise: " +
                      string.Join(", ", customTerms) + ".");
        }

        if (includeCommandHints)
        {
            // Kurze Anweisung statt langer Beispielsätze: gpt-4o-mini-transcribe folgt
            // Anweisungen, und ein kurzer Prompt verbiegt kurze Diktate kaum noch.
            // (Die früheren langen Beispielsätze wurden bei Einzelwörtern als
            // Prompt-Echo zurückgespiegelt – siehe Retry-Schutz im WorkflowRunner.)
            // Der juristische Kontext primt die Schreibweise von Zeichen wie § und €,
            // die ohne Stilkontext sonst als "Paragraf"/"Euro" transkribiert werden.
            parts.Add(
                "Es handelt sich um formelle juristische Texte in deutscher Sprache; " +
                "verwende juristische Schreibweise und Zeichen (z. B. § statt Paragraf, € statt Euro). " +
                "Diktierbefehle wörtlich als Wörter transkribieren, nicht in Satzzeichen umwandeln: " +
                "Komma, Satzende, Doppelpunkt, Semikolon, Gedankenstrich, Klammer auf, Klammer zu, neue Zeile, neuer Absatz, Leerzeile.");
        }

        if (parts.Count == 0)
        {
            return null;
        }

        return string.Join(" ", parts);
    }

    /// <summary>
    /// Baut den Transkriptions-Prompt für einen Workflow: „Blitzbrief-Kontext (GPT)" bekommt den
    /// beidseitigen Lücken-Prompt, alle anderen den Whisper-Prompt (ggf. mit Vortext). Wird sowohl
    /// beim Realtime-Start (Dauer noch unbekannt -> hasEnoughAudio: true) als auch im Batch-Fallback
    /// (Dauer bekannt) genutzt, damit beide Pfade identisch primen.
    /// </summary>
    public static string? BuildTranscriptionPrompt(
        WorkflowType type, AppSettings settings, bool hasEnoughAudio, string? preceding, string? following) =>
        type == WorkflowType.BlitzBriefKontextGpt
            ? BuildKontextGapPrompt(type, settings, hasEnoughAudio, preceding, following)
            : BuildWorkflowWhisperPrompt(type, settings, hasEnoughAudio, preceding);

    /// <summary>Die üblichen Hinweise (Sprache, Eigenbegriffe, Kommandos) plus – im Kontext-Modus –
    /// der angefangene Satz links vom Cursor ans Prompt-Ende (whisper-1-Fortsetzung).</summary>
    public static string? BuildWorkflowWhisperPrompt(
        WorkflowType type, AppSettings settings, bool hasEnoughAudio, string? precedingSentence = null)
    {
        var useCommandHints = UsesJornCommands(type, settings) && hasEnoughAudio;
        var customTerms = hasEnoughAudio ? settings.CustomTerms : (IReadOnlyList<string>)[];
        var basePrompt = BuildWhisperPrompt(customTerms, useCommandHints, settings.Language);

        // Vortext (angefangener Satz links vom Cursor) ans ENDE hängen – auch bei KURZEM Audio.
        // whisper-1 wertet nur die letzten ~224 Tokens aus, daher der rohe Satz ganz hinten: so
        // konditioniert er die Fortsetzung (Groß-/Kleinschreibung). Wichtig: whisper-1 setzt nur
        // dann klein fort, wenn vor dem offenen Fragment ein punkt-beendeter Satz steht – die
        // Sprachvorgabe ("…auf Deutsch.") liefert genau diesen Anker (ein nacktes Fragment reicht
        // nicht). Früher war dies an hasEnoughAudio gekoppelt (Echo-Sorge), wodurch kurze
        // Einzelwörter OHNE Kontext großgeschrieben wurden. Per Spike widerlegt: whisper-1 echot
        // das Fragment bei (fast) leerem Audio NICHT (liefert "" bzw. eine bekannte Floskel, die
        // der Qualitäts-Schutz ohnehin verwirft) – Mitsenden ist also unbedenklich. Nur die
        // echo-anfälligen Kommando-Hinweise/Eigenbegriffe bleiben an hasEnoughAudio gekoppelt
        // (oben). Siehe Memory kontext-grossschreibung-bug bzw. kontext-mode-whisper1.
        if (!string.IsNullOrWhiteSpace(precedingSentence))
        {
            var fragment = precedingSentence.Trim();
            return string.IsNullOrEmpty(basePrompt) ? fragment : basePrompt + " " + fragment;
        }

        return basePrompt;
    }

    /// <summary>
    /// Baut den Transkriptions-Prompt für „Blitzbrief-Kontext (GPT)": die üblichen Hinweise
    /// (Sprache, Eigenbegriffe, Jörn-/Juristik-Kommandos) plus – getrennt davon – die
    /// Cursor-Nachbarschaft mit einer Einfügelücke. So sieht das instruktionsfähige
    /// gpt-4o-(mini-)transcribe, wohin das Diktat gehört (links/rechts vom Cursor).
    /// </summary>
    // Die OpenAI-Realtime-API begrenzt den Transkriptions-Prompt auf 1024 Zeichen; mit Reserve
    // cappen wir auf 1000. (Gilt nur für den PROMPT/Kontext – Diktat und Transkript sind unbegrenzt.)
    private const int MaxKontextGapPromptChars = 1000;

    public static string? BuildKontextGapPrompt(
        WorkflowType type, AppSettings settings, bool hasEnoughAudio, string? left, string? right)
    {
        var useCommandHints = UsesJornCommands(type, settings) && hasEnoughAudio;
        var customTerms = hasEnoughAudio ? settings.CustomTerms : (IReadOnlyList<string>)[];
        var hints = BuildWhisperPrompt(customTerms, useCommandHints, settings.Language);

        var l = (left ?? "").Trim();
        var r = (right ?? "").Trim();

        // Ohne jeden Kontext gibt es nichts einzufügen → wie eine normale Transkription primen.
        if (l.Length == 0 && r.Length == 0)
        {
            return hints;
        }

        string Build(string ll, string rr)
        {
            var gap =
                "An der mit ___ markierten Stelle wird gesprochener Text in einen bestehenden Text eingefügt:\n" +
                $"\"{ll} ___ {rr}\"\n" +
                "Transkribiere ausschließlich die Audioaufnahme – den Text, der an die Stelle ___ gehört – " +
                "wortgetreu auf Deutsch. Gib nur diesen eingefügten Text aus, nicht den umgebenden Kontext.";
            return string.IsNullOrEmpty(hints) ? gap : hints + "\n\n" + gap;
        }

        var prompt = Build(l, r);
        if (prompt.Length > MaxKontextGapPromptChars)
        {
            // Überhang zuerst rechts, dann links kürzen – cursor-nahe Teile behalten (rechts den
            // Anfang, links das Ende), weil sie die Einfügung am stärksten prägen.
            var over = prompt.Length - MaxKontextGapPromptChars + 8; // kleine Reserve gegen Trim-Rundung
            var cutR = Math.Min(over, r.Length);
            r = (cutR >= r.Length ? "" : r[..(r.Length - cutR)]).TrimEnd();
            over -= cutR;
            if (over > 0 && l.Length > 0)
            {
                var cutL = Math.Min(over, l.Length);
                l = (cutL >= l.Length ? "" : l[cutL..]).TrimStart();
            }

            prompt = Build(l, r);
            if (prompt.Length > MaxKontextGapPromptChars)
            {
                prompt = prompt[..MaxKontextGapPromptChars]; // harte Notbremse
            }
        }

        return prompt;
    }

    /// <summary>
    /// Trifft die Jörn-2-Verarbeitung zu (juristischer Kommando-Prompt beim Transkribieren
    /// und anschließende Kommandoersetzung)? Gilt für "Text verbessern" mit Stil "Jörn 2" und
    /// für die fest verdrahteten Kontext-Modi.
    /// </summary>
    public static bool UsesJornCommands(WorkflowType type, AppSettings settings) =>
        type is WorkflowType.BlitzBriefEasy or WorkflowType.BlitzBriefKontext or WorkflowType.BlitzBriefKontextGpt ||
        (type == WorkflowType.TextImprover && settings.TextImprovement.Tone == TextTone.JornCommands);

    private static string? LanguageDirectiveName(string language) => language.Trim().ToLowerInvariant() switch
    {
        "de" => "Deutsch",
        "en" => "Englisch",
        _ => null
    };

    public static string BuildTextImprovementPrompt(TextImprovementSettings settings, IReadOnlyList<string> customTerms)
    {
        var prompt = string.IsNullOrWhiteSpace(settings.SystemPrompt)
            ? BuildDefaultImprovementPrompt(settings.Tone)
            : settings.SystemPrompt.Trim();

        if (customTerms.Count > 0)
        {
            prompt += $"\n\nWichtig: Diese Eigennamen und Fachbegriffe müssen exakt so geschrieben werden: {string.Join(", ", customTerms)}";
        }

        if (!string.IsNullOrWhiteSpace(settings.Context))
        {
            prompt += $"\n\nKontext: {settings.Context.Trim()}";
        }

        return prompt;
    }

    public static string BuildEmojiPrompt(EmojiDensity density)
    {
        var densityInstruction = density switch
        {
            EmojiDensity.Wenig => "Setze nur vereinzelt Emojis ein, maximal 1-2 pro Absatz.",
            EmojiDensity.Viel => "Setze großzügig Emojis ein, gerne mehrere pro Satz.",
            _ => "Setze regelmäßig passende Emojis ein, etwa alle 1-2 Sätze."
        };

        return "Du erhältst ein gesprochenes Transkript. Gib den Text möglichst originalgetreu zurück, aber füge passende Emojis ein. " +
               $"{densityInstruction} Korrigiere offensichtliche Sprach- und Grammatikfehler. Behalte den Stil und die Bedeutung bei. " +
               "Gib NUR den Text mit Emojis zurück, keine Erklärungen.";
    }

    private static string BuildDefaultImprovementPrompt(TextTone tone)
    {
        if (tone == TextTone.JornMinimal)
            return BuildJornMinimalPrompt();
        if (tone == TextTone.JornCommands)
            return BuildJornCommandsPrompt();

        var prompt = "Du bist ein Lektor und Schreibassistent. Verbessere den folgenden Text:\n" +
                     "- Korrigiere Rechtschreibung und Grammatik\n" +
                     "- Verbessere die Formulierung und den Lesefluss\n" +
                     "- Behalte die ursprüngliche Bedeutung bei\n" +
                     "- Gib NUR den verbesserten Text zurück, keine Erklärungen";

        prompt += tone switch
        {
            TextTone.Formal => "\n- Verwende einen formellen, professionellen Ton",
            TextTone.Casual => "\n- Verwende einen lockeren, natürlichen Ton",
            _ => "\n- Verwende einen neutralen, klaren Ton"
        };

        return prompt;
    }

    private static string BuildJornMinimalPrompt() =>
        "Du erhältst ein gesprochenes Transkript. Deine Aufgabe:\n" +
        "- Entferne Füllwörter (äh, ähm, halt, irgendwie, eigentlich, sozusagen, quasi, also, ne, oder so)\n" +
        "- Korrigiere Grammatik- und Rechtschreibfehler\n" +
        "- Formuliere NICHT um - behalte Wortwahl und Satzstruktur des Sprechers exakt bei\n" +
        "- Gib NUR den bereinigten Text zurück, keine Erklärungen";

    private static string BuildJornCommandsPrompt() =>
        "Du erhältst einen juristischen Text. Formatierungen und Satzzeichen bitte beibehalten. Deine Aufgabe:\n" +
        "- Entferne Füllwörter (äh, ähm, halt, irgendwie, eigentlich, sozusagen, quasi, also, ne, oder so)\n" +
        "- Korrigiere offensichtliche Grammatik- und Rechtschreibfehler\n" +
        "- Formuliere NICHT um – behalte Wortwahl und Satzstruktur des Sprechers exakt bei\n" +
        "- Der Text kann ein vollständiger Satz, ein Halbsatz oder nur einzelne Wörter sein – ergänze NICHTS, vervollständige NICHTS\n" +
        "- Schreibe das erste Wort klein, wenn es kein vollständiger Satz ist und es kein Substantiv ist.\n" +
        "- Entferne KEINE vorhandenen Satzzeichen\n" +
        "- Entferne KEINE vorhandenen Zeilenumbrüche\n" +
        "- Gib NUR den bereinigten Text zurück, keine Erklärungen";
}
