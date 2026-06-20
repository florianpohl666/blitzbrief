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

    public static string? BuildWhisperPrompt(IReadOnlyList<string> customTerms, bool includeCommandHints)
    {
        var parts = new List<string>();

        if (customTerms.Count > 0)
        {
            // Begriffe als Fließtext-Beispiel – Whisper orientiert sich am Schreibstil,
            // keine kommagetrennte Liste die als Kontext ungeeignet ist.
            var termSentences = string.Join(" Satzende ", customTerms);
            parts.Add(termSentences + " Satzende");
        }

        if (includeCommandHints)
        {
            // Mustertext zeigt Kommandowörter als normale Wörter im Fließtext,
            // damit Whisper sie nicht eigenständig in Satzzeichen umwandelt.
            parts.Add(
                "Der Auskunftsanspruch ist begründet Komma da die Voraussetzungen vorliegen Satzende " +
                "Die Eigentumsvormerkung wurde eingetragen Satzende Absatz " +
                "Der Kaufpreisanspruch ist fällig Komma sobald die Übergabe erfolgt Satzende");
        }

        if (parts.Count == 0)
        {
            return null;
        }

        return string.Join(" ", parts);
    }

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
        "Du erhältst ein gesprochenes Transkript. Satzzeichen wurden bereits durch Code ersetzt. Deine Aufgabe:\n" +
        "- Entferne Füllwörter (äh, ähm, halt, irgendwie, eigentlich, sozusagen, quasi, also, ne, oder so)\n" +
        "- Korrigiere offensichtliche Grammatik- und Rechtschreibfehler\n" +
        "- Formuliere NICHT um – behalte Wortwahl und Satzstruktur des Sprechers exakt bei\n" +
        "- Der Text kann ein vollständiger Satz, ein Halbsatz oder nur einzelne Wörter sein – ergänze NICHTS, vervollständige NICHTS\n" +
        "- Behalte Groß-/Kleinschreibung am Anfang des Textes exakt wie im Transkript\n" +
        "- Entferne KEINE vorhandenen Satzzeichen\n" +
        "- Gib NUR den bereinigten Text zurück, keine Erklärungen";
}
