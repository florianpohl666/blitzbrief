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
    public async Task<WorkflowResult> ProcessAsync(
        WorkflowType type,
        string audioPath,
        TimeSpan recordingDuration,
        CancellationToken cancellationToken)
    {
        var settings = settingsProvider();
        var apiKey = apiKeyStore.Load();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("OpenAI API Key fehlt. Bitte in den Einstellungen hinterlegen.");
        }

        if (TranscriptionQualityService.ShouldRejectRecording(recordingDuration))
        {
            throw new InvalidOperationException("Keine Aufnahme erkannt.");
        }

        try
        {
            var useCommandHints = type == WorkflowType.TextImprover &&
                                  settings.TextImprovement.Tone == TextTone.JornCommands;
            var customTerms = recordingDuration.TotalSeconds >= 0.9 ? settings.CustomTerms : (IReadOnlyList<string>)[];
            var whisperPrompt = PromptBuilder.BuildWhisperPrompt(customTerms, includeCommandHints: useCommandHints);

            var rawText = await openAIClient.TranscribeAsync(
                audioPath,
                apiKey,
                settings.Language,
                whisperPrompt,
                settings.TranscriptionModel,
                cancellationToken);

            var cleaned = TranscriptionQualityService.CleanedTranscript(rawText);
            if (TranscriptionQualityService.IsLikelyArtifact(cleaned, recordingDuration))
            {
                throw new InvalidOperationException("Keine Aufnahme erkannt.");
            }

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

            return new WorkflowResult(rewritten, Stage1Transcript: stage1);
        }
        finally
        {
            try
            {
                if (File.Exists(audioPath))
                {
                    File.Delete(audioPath);
                }
            }
            catch
            {
                // Temporary audio cleanup is best-effort.
            }
        }
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

public sealed record WorkflowResult(string Text, string? Stage1Transcript = null);
