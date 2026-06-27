namespace BlitzBrief.Core.Models;

public enum WorkflowType
{
    Transcription,
    TextImprover,
    DampfAblassen,
    EmojiText,

    /// <summary>
    /// Fest verdrahteter Modus: wirkt wie "Text verbessern" mit Stil "Jörn 2"
    /// (Bereinigen + Kommandos) und ohne GPT-Rewrite – ohne jede Einstellbarkeit.
    /// </summary>
    BlitzBriefEasy,

    /// <summary>
    /// Wie <see cref="BlitzBriefEasy"/> (Jörn 2 ohne GPT-Rewrite), gibt aber zusätzlich den
    /// angefangenen Satz links vom Cursor als Kontext mit, damit whisper-1 ihn korrekt
    /// fortsetzt (Groß-/Kleinschreibung). Transkribiert deshalb per Batch mit whisper-1 –
    /// nur dieses Modell setzt Sätze grammatisch korrekt fort (siehe WorkflowRunner).
    /// </summary>
    BlitzBriefKontext
}

public static class WorkflowTypeExtensions
{
    public static string DisplayName(this WorkflowType type) => type switch
    {
        WorkflowType.Transcription => "BlitzBrief",
        WorkflowType.TextImprover => "Text verbessern",
        WorkflowType.DampfAblassen => "Ärger beruhigen",
        WorkflowType.EmojiText => "Emoji ergänzen",
        WorkflowType.BlitzBriefEasy => "Blitzbrief-Easy",
        WorkflowType.BlitzBriefKontext => "Blitzbrief-Kontext",
        _ => "BlitzBrief"
    };
}
