namespace BlitzBrief.Core.Models;

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
        WorkflowType.Transcription => "BlitzBrief",
        WorkflowType.TextImprover => "Text verbessern",
        WorkflowType.DampfAblassen => "Ärger beruhigen",
        WorkflowType.EmojiText => "Emoji ergänzen",
        _ => "BlitzBrief"
    };
}
