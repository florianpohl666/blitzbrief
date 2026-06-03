namespace Blitztext.Core.Models;

public enum WorkflowType
{
    Transcription,
    TextImprover,
    DampfAblassen,
    EmojiText
}

public static class WorkflowTypeExtensions
{
    public static string DisplayName(this WorkflowType type) => type switch
    {
        WorkflowType.Transcription => "Blitztext",
        WorkflowType.TextImprover => "Text verbessern",
        WorkflowType.DampfAblassen => "Ärger beruhigen",
        WorkflowType.EmojiText => "Emoji ergänzen",
        _ => "Blitztext"
    };
}
