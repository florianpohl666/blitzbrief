using BlitzBrief.Core.Models;
using BlitzBrief.Core.OpenAI;
using BlitzBrief.Core.Security;
using BlitzBrief.Core.Settings;

namespace BlitzBrief.Core.Workflow;

public sealed class WorkflowRunner(
    IOpenAIClient openAIClient,
    ApiKeyStore apiKeyStore,
    Func<AppSettings> settingsProvider)
{
    // Unterhalb dieser Aufnahmedauer keine Prompt-Hinweise senden – sonst
    // spiegelt das Modell den Prompt bei (fast) leerem Audio zurück.
    private const double MinPromptAudioSeconds = 0.9;

    // Der Kontext-Modus braucht whisper-1: NUR dieses Modell setzt den angefangenen Satz
    // grammatisch korrekt fort (gpt-4o-(mini-)transcribe schreibt Satzanfänge per Formatter
    // immer groß und ignoriert jeden Fortsetzungs-Prompt – per Spike + OpenAI-Doku belegt).
    // whisper-1 ist batch-only; der Realtime-Pfad wird dafür automatisch übersprungen.
    public const string KontextTranscriptionModel = "whisper-1";

    /// <summary>Transkriptionsmodell für den Workflow – whisper-1 im Kontext-Modus, das eingestellte
    /// gpt-4o-(mini-)transcribe im Kontext-GPT-Modus, sonst die allgemeine Einstellung.</summary>
    public static string TranscriptionModelFor(WorkflowType type, AppSettings settings) => type switch
    {
        WorkflowType.BlitzBriefKontext => KontextTranscriptionModel,
        WorkflowType.BlitzBriefKontextGpt => settings.KontextGptModel,
        _ => settings.TranscriptionModel
    };

    public async Task<WorkflowResult> ProcessAsync(
        WorkflowType type,
        RecordedAudio audio,
        CancellationToken cancellationToken)
    {
        var settings = settingsProvider();
        var apiKey = apiKeyStore.Load();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("OpenAI API Key fehlt. Bitte in den Einstellungen hinterlegen.");
        }

        if (TranscriptionQualityService.ShouldRejectRecording(audio.Duration))
        {
            throw new EmptyRecordingException("Keine Aufnahme erkannt.");
        }

        var hasEnoughAudio = audio.Duration.TotalSeconds >= MinPromptAudioSeconds;
        var useCommandHints = PromptBuilder.UsesJornCommands(type, settings) && hasEnoughAudio;
        var model = TranscriptionModelFor(type, settings);

        // Transkript besorgen: bevorzugt aus dem Realtime-Stream, sonst per Batch-Upload.
        // echoPrompt = der Prompt, mit dem das aktuelle Transkript entstanden ist (für die Echo-Prüfung).
        // usedRealtime = kam das finale Transkript aus dem Realtime-Stream (für die Debug-Anzeige).
        var usedRealtime = audio.RealtimeTranscript is not null;
        string cleaned;
        string? echoPrompt;
        if (audio.RealtimeTranscript is not null)
        {
            cleaned = TranscriptionQualityService.CleanedTranscript(audio.RealtimeTranscript);
            echoPrompt = audio.RealtimePrompt;

            // Kontext-GPT auch im Realtime-Pfad gegen Leakage des Lücken-Kontexts absichern.
            if (type == WorkflowType.BlitzBriefKontextGpt)
            {
                cleaned = TranscriptionQualityService.StripLeakedContext(cleaned, audio.PrecedingContext, audio.FollowingContext);
            }
        }
        else
        {
            // Kontext-Modus: angefangener Satz links vom Cursor wird an den Prompt gehängt.
            // Kontext-GPT-Modus: stattdessen Kontext LINKS UND RECHTS mit Einfügelücke (gpt-4o-transcribe
            // versteht die Beschreibung). Das führende-Stille-Trimmen (Silero) passiert im Windows-Projekt
            // VOR dieser Stelle – hier kommt bereits beschnittenes PCM an.
            echoPrompt = type == WorkflowType.BlitzBriefKontextGpt
                ? PromptBuilder.BuildKontextGapPrompt(type, settings, hasEnoughAudio, audio.PrecedingContext, audio.FollowingContext)
                : PromptBuilder.BuildWorkflowWhisperPrompt(type, settings, hasEnoughAudio, audio.PrecedingContext);
            var rawText = await TranscribeBatchAsync(audio, apiKey, settings.Language, echoPrompt, model, cancellationToken);
            cleaned = TranscriptionQualityService.CleanedTranscript(rawText);

            // Kontext-GPT: falls das Modell den mitgegebenen Cursor-Kontext mitgeschrieben hat
            // (Leakage), die durchgesickerten Nachbarwörter an den Rändern entfernen.
            if (type == WorkflowType.BlitzBriefKontextGpt)
            {
                cleaned = TranscriptionQualityService.StripLeakedContext(cleaned, audio.PrecedingContext, audio.FollowingContext);
            }
        }

        // Der Prompt (Eigenbegriffe/Kommando-Hinweise) kann kurze Einzelwörter verbiegen,
        // sodass sie als Artefakt/Prompt-Echo aussortiert würden. In dem Fall einmal OHNE
        // Prompt per Batch neu transkribieren, statt echtes Diktat still zu verwerfen.
        if (!string.IsNullOrEmpty(echoPrompt) &&
            (TranscriptionQualityService.IsLikelyArtifact(cleaned, audio.Duration) ||
             TranscriptionQualityService.IsPromptEcho(cleaned, echoPrompt)))
        {
            var retryText = await TranscribeBatchAsync(audio, apiKey, settings.Language, null, model, cancellationToken);
            cleaned = TranscriptionQualityService.CleanedTranscript(retryText);
            usedRealtime = false; // finales Transkript kam aus dem Batch-Retry, nicht aus Realtime
        }

        // Nach dem Retry kann kein Prompt-Echo mehr vorliegen (kein Prompt gesendet);
        // bleibt der echte Leerfall ODER eine reine Halluzination: eine lange, plausibel
        // klingende Textwand (z.B. Standard-AGB), die das Modell bei fast leerem Audio
        // erfindet. Sie ist kein Kurz-Artefakt und kein Prompt-Echo, verrät sich aber über
        // die physikalisch unmögliche Sprechrate. Ein prompt-loser Retry würde hier nur
        // erneut halluzinieren – daher direkt verwerfen.
        if (TranscriptionQualityService.IsLikelyArtifact(cleaned, audio.Duration) ||
            TranscriptionQualityService.IsImplausiblyFast(cleaned, audio.Duration))
        {
            throw new EmptyRecordingException("Keine Aufnahme erkannt.");
        }

        // §-/€-Zeichen deterministisch erzwingen, sobald eine Ziffer folgt – das
        // Transkriptionsmodell schreibt sie sonst mal als Zeichen, mal als Wort aus.
        // Gilt für alle Modi, daher vor der Transkriptions-Rückgabe und vor stage1.
        cleaned = TranscriptionQualityService.NormalizeSymbols(cleaned);

        if (type == WorkflowType.Transcription)
        {
            return new WorkflowResult(cleaned, UsedRealtime: usedRealtime);
        }

        var stage1 = useCommandHints
            ? TranscriptionQualityService.ProcessJornCommands(cleaned)
            : cleaned;

        var jornMode = settings.TextImprovement.Tone is TextTone.JornMinimal or TextTone.JornCommands;
        var rewritten = type switch
        {
            // Fest verdrahtet: Transkription + Jörn-2-Kommandoersetzung (in stage1), ohne GPT-Rewrite.
            // Kontext-Modi zusätzlich mit Cursor-Kontext im Prompt (oben), Nachverarbeitung identisch.
            WorkflowType.BlitzBriefEasy or WorkflowType.BlitzBriefKontext or WorkflowType.BlitzBriefKontextGpt => stage1,
            WorkflowType.TextImprover when settings.TextImprovement.SkipRewrite => stage1,
            WorkflowType.TextImprover => await RewriteAsync(
                stage1,
                apiKey,
                PromptBuilder.BuildTextImprovementPrompt(settings.TextImprovement, settings.CustomTerms),
                settings.RewriteModel,
                jornMode ? 0.0 : 0.3,
                cancellationToken),
            WorkflowType.DampfAblassen => await RewriteAsync(
                stage1,
                apiKey,
                settings.DampfAblassen.SystemPrompt,
                "gpt-4o",
                0.4,
                cancellationToken),
            WorkflowType.EmojiText => await RewriteAsync(
                stage1,
                apiKey,
                PromptBuilder.BuildEmojiPrompt(settings.EmojiText.EmojiDensity),
                settings.RewriteModel,
                0.3,
                cancellationToken),
            _ => stage1
        };

        return new WorkflowResult(rewritten, Stage1Transcript: stage1, RawTranscript: cleaned, UsedRealtime: usedRealtime);
    }

    private async Task<string> TranscribeBatchAsync(
        RecordedAudio audio,
        string apiKey,
        string language,
        string? prompt,
        string model,
        CancellationToken cancellationToken)
    {
        var wav = AudioFormats.PcmToWav(audio.Pcm, audio.SampleRate);
        using var stream = new MemoryStream(wav, writable: false);
        return await openAIClient.TranscribeAsync(stream, apiKey, language, prompt, model, cancellationToken);
    }

    private async Task<string> RewriteAsync(
        string text,
        string apiKey,
        string prompt,
        string model,
        double temperature,
        CancellationToken cancellationToken)
    {
        var rewritten = await openAIClient.RewriteAsync(text, apiKey, prompt, model, temperature, cancellationToken);
        return TranscriptionQualityService.CleanedTranscript(rewritten);
    }
}

public sealed record WorkflowResult(
    string Text,
    string? Stage1Transcript = null,
    string? RawTranscript = null,
    bool UsedRealtime = false);
