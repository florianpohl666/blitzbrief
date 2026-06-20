using BlitzBrief.Core.Models;

namespace BlitzBrief.Core.Settings;

public sealed class AppSettings
{
    public string Language { get; set; } = "de";
    public HotkeyMode HotkeyMode { get; set; } = HotkeyMode.Toggle;
    public Dictionary<WorkflowType, string> WorkflowHotkeys { get; set; } = DefaultHotkeys();
    public List<string> CustomTerms { get; set; } = [];
    public TextImprovementSettings TextImprovement { get; set; } = new();
    public DampfAblassenSettings DampfAblassen { get; set; } = new();
    public EmojiTextSettings EmojiText { get; set; } = new();
    public string TranscriptionModel { get; set; } = "gpt-4o-mini-transcribe";
    public string RewriteModel { get; set; } = "gpt-4o-mini";
    public int AudioInputDeviceNumber { get; set; } = 0;
    public bool AutoPaste { get; set; } = true;
    public bool AutoPasteDelay { get; set; } = false;

    /// <summary>
    /// Hält das Mikrofon durchgehend in einem kurzen Ringpuffer aktiv, damit das Diktat
    /// verzögerungsfrei startet und Wortanfänge nicht verloren gehen. Bedeutet: Mikrofon ist dauerhaft aktiv.
    /// </summary>
    public bool PreRollEnabled { get; set; } = true;
    public int PreRollMilliseconds { get; set; } = 300;

    /// <summary>Diktat per Doppeltipp auf einen Modifier starten/stoppen (Wispr-Flow-Stil).</summary>
    public bool DoubleTapEnabled { get; set; } = true;
    public ModifierKey DoubleTapModifier { get; set; } = ModifierKey.Ctrl;
    public bool DebugMode { get; set; } = false;

    public static Dictionary<WorkflowType, string> DefaultHotkeys() => new()
    {
        [WorkflowType.Transcription] = "Ctrl+Shift+Space",
        [WorkflowType.TextImprover] = "Ctrl+Shift+1",
        [WorkflowType.DampfAblassen] = "Ctrl+Shift+2",
        [WorkflowType.EmojiText] = "Ctrl+Shift+3"
    };
}

public sealed class TextImprovementSettings
{
    public string SystemPrompt { get; set; } = "";
    public string Context { get; set; } = "";
    public TextTone Tone { get; set; } = TextTone.Neutral;
}

public enum ModifierKey
{
    Ctrl,
    Alt,
    Shift
}

public enum TextTone
{
    Formal,
    Neutral,
    Casual,
    JornMinimal,
    JornCommands
}

public sealed class DampfAblassenSettings
{
    public string SystemPrompt { get; set; } = PromptBuilder.DefaultDampfAblassenPrompt;
}

public sealed class EmojiTextSettings
{
    public EmojiDensity EmojiDensity { get; set; } = EmojiDensity.Mittel;
}

public enum EmojiDensity
{
    Wenig,
    Mittel,
    Viel
}
