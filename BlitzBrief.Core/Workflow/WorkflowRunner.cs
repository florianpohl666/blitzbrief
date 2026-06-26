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
        var useCommandHints = type == WorkflowType.TextImprover &&
                              settings.TextImprovement.Tone == TextTone.JornCommands &&
                              hasEnoughAudio;

        // Transkript besorgen: bevorzugt aus dem Realtime-Stream, sonst per Batch-Upload.
        // echoPrompt = der Prompt, mit dem das aktuelle Transkript entstanden ist (für die Echo-Prüfung).
        string cleaned;
        string? echoPrompt;
        if (audio.RealtimeTranscript is not null)
        {
            cleaned = TranscriptionQualityService.CleanedTranscript(audio.RealtimeTranscript);
            echoPrompt = audio.RealtimePrompt;
        }
        else
        {
            echoPrompt = PromptBuilder.BuildWorkflowWhisperPrompt(type, settings, hasEnoughAudio);
            var rawText = await TranscribeBatchAsync(audio, apiKey, settings.Language, echoPrompt, settings.TranscriptionModel, cancellationToken);
            cleaned = TranscriptionQualityService.CleanedTranscript(rawText);
        }

        // Der Prompt (Eigenbegriffe/Kommando-Hinweise) kann kurze Einzelwörter verbiegen,
        // sodass sie als Artefakt/Prompt-Echo aussortiert würden. In dem Fall einmal OHNE
        // Prompt per Batch neu transkribieren, statt echtes Diktat still zu verwerfen.
        if (!string.IsNullOrEmpty(echoPrompt) &&
            (TranscriptionQualityService.IsLikelyArtifact(cleaned, audio.Duration) ||
             TranscriptionQualityService.IsPromptEcho(cleaned, echoPrompt)))
        {
            var retryText = await TranscribeBatchAsync(audio, apiKey, settings.Language, null, settings.TranscriptionModel, cancellationToken);
            cleaned = TranscriptionQualityService.CleanedTranscript(retryText);
        }

        // Nach dem Retry kann kein Prompt-Echo mehr vorliegen (kein Prompt gesendet);
        // bleibt nur der echte Leerfall.
        if (TranscriptionQualityService.IsLikelyArtifact(cleaned, audio.Duration))
        {
            throw new EmptyRecordingException("Keine Aufnahme erkannt.");
        }

        // §-/€-Zeichen deterministisch erzwingen, sobald eine Ziffer folgt – das
        // Transkriptionsmodell schreibt sie sonst mal als Zeichen, mal als Wort aus.
        // Gilt für alle Modi, daher vor der Transkriptions-Rückgabe und vor stage1.
        cleaned = TranscriptionQualityService.NormalizeSymbols(cleaned);

        if (type == WorkflowType.Transcription)
        {
            return new WorkflowResult(cleaned);
        }

        var stage1 = useCommandHints
            ? TranscriptionQualityService.ProcessJornCommands(cleaned)
            : cleaned;

        var jornMode = settings.TextImprovement.Tone is TextTone.JornMinimal or TextTone.JornCommands;
        var rewritten = type switch
        {
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

        return new WorkflowResult(rewritten, Stage1Transcript: stage1, RawTranscript: cleaned);
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

public sealed record WorkflowResult(string Text, string? Stage1Transcript = null, string? RawTranscript = null);
