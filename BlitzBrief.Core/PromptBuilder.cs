using BlitzBrief.Core.Models;
using BlitzBrief.Core.Settings;

namespace BlitzBrief.Core;

public static class PromptBuilder
{
    public const string LegacyDampfAblassenPrompt =
        "Du erhaeltst ein emotional gesprochenes Transkript. Erkenne zuerst das eigentliche Ziel, Anliegen und den wahren Frust der Person. " +
        "Formuliere daraus eine klare, respektvolle und wirksame Nachricht, mit der die Person ihr Ziel eher erreicht. " +
        "Bewahre relevante Fakten, konkrete Probleme, Grenzen, Erwartungen und die noetige Dringlichkeit. " +
        "Entferne Beleidigungen, Drohungen, Sarkasmus, Unterstellungen und unnoetige Eskalation. " +
        "Wenn mehrere Vorwuerfe genannt werden, verdichte sie auf die entscheidenden Kernpunkte. " +
        "Der Ton soll ruhig, menschlich, bestimmt und loesungsorientiert sein. Gib NUR die fertige Nachricht zurueck.";

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

        var normalized = prompt.Trim();
        return normalized == LegacyDampfAblassenPrompt ||
               normalized.Contains("Du erhaeltst ein emotional gesprochenes Transkript", StringComparison.Ordinal) ||
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
    /// Baut den Transkriptions-Prompt für einen Workflow. Wird sowohl beim Realtime-Start
    /// (Dauer noch unbekannt -> hasEnoughAudio: true) als auch im Batch-Fallback (Dauer bekannt)
    /// genutzt, damit beide Pfade identisch primen.
    /// </summary>
    public static string? BuildWorkflowWhisperPrompt(WorkflowType type, AppSettings settings, bool hasEnoughAudio)
    {
        var useCommandHints = type == WorkflowType.TextImprover &&
                              settings.TextImprovement.Tone == TextTone.JornCommands &&
                              hasEnoughAudio;
        var customTerms = hasEnoughAudio ? settings.CustomTerms : (IReadOnlyList<string>)[];
        return BuildWhisperPrompt(customTerms, useCommandHints, settings.Language);
    }

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
