using BlitzBrief.Core.Models;

namespace BlitzBrief.Core.Settings;

public sealed class AppSettings
{
    public string Language { get; set; } = "de";
    public HotkeyMode HotkeyMode { get; set; } = HotkeyMode.Hold;
    public Dictionary<WorkflowType, string> WorkflowHotkeys { get; set; } = DefaultHotkeys();
    public List<string> CustomTerms { get; set; } = [];
    public TextImprovementSettings TextImprovement { get; set; } = new();
    public DampfAblassenSettings DampfAblassen { get; set; } = new();
    public EmojiTextSettings EmojiText { get; set; } = new();
    public string TranscriptionModel { get; set; } = "gpt-4o-mini-transcribe";
    public string RewriteModel { get; set; } = "gpt-4o-mini";
    public int AudioInputDeviceNumber { get; set; } = 0;
    public bool AutoPaste { get; set; } = true;

    /// <summary>
    /// Streamt das Audio während des Sprechens an die OpenAI-Realtime-API (geringere Latenz),
    /// statt erst nach dem Stopp die ganze Datei hochzuladen. Fällt bei Fehlern automatisch auf
    /// den Batch-Upload zurück. Greift nur für Realtime-fähige Modelle (gpt-4o(-mini)-transcribe).
    /// </summary>
    public bool UseRealtimeTranscription { get; set; } = true;

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
        // Easy/Kontext sind reine Modifier-Hotkeys (Ctrl+Win bzw. Ctrl+Shift). Damit Ctrl+Shift
        // nicht bei jeder anderen Kombination mitfeuert, dürfen die übrigen Hotkeys NICHT auf
        // Ctrl+Shift basieren – sie liegen daher auf Ctrl+Alt.
        [WorkflowType.Transcription] = "Ctrl+Alt+Space",
        [WorkflowType.BlitzBriefEasy] = "Ctrl+Win",
        [WorkflowType.BlitzBriefKontext] = "Ctrl+Shift",
        [WorkflowType.TextImprover] = "Ctrl+Alt+1",
        [WorkflowType.DampfAblassen] = "Ctrl+Alt+2",
        [WorkflowType.EmojiText] = "Ctrl+Alt+3"
    };
}

public sealed class TextImprovementSettings
{
    public string SystemPrompt { get; set; } = "";
    public string Context { get; set; } = "";
    public TextTone Tone { get; set; } = TextTone.Neutral;
    public bool SkipRewrite { get; set; } = false;
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
