using Blitztext.Core.Models;
using Blitztext.Core.OpenAI;
using Blitztext.Core.Security;
using Blitztext.Core.Settings;

namespace Blitztext.Core.Workflow;

public sealed class WorkflowRunner(
    IOpenAIClient openAIClient,
    ApiKeyStore apiKeyStore,
    Func<AppSettings> settingsProvider)
{
    public async Task<string> ProcessAsync(
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
            var customTerms = recordingDuration.TotalSeconds >= 0.9 ? settings.CustomTerms : [];
            var rawText = await openAIClient.TranscribeAsync(
                audioPath,
                apiKey,
                settings.Language,
                customTerms,
                settings.TranscriptionModel,
                cancellationToken);

            var cleaned = TranscriptionQualityService.CleanedTranscript(rawText);
            if (TranscriptionQualityService.IsLikelyArtifact(cleaned, recordingDuration))
            {
                throw new InvalidOperationException("Keine Aufnahme erkannt.");
            }

            return type switch
            {
                WorkflowType.Transcription => cleaned,
                WorkflowType.TextImprover => await RewriteAsync(
                    cleaned,
                    apiKey,
                    PromptBuilder.BuildTextImprovementPrompt(settings.TextImprovement, settings.CustomTerms),
                    settings.RewriteModel,
                    0.3,
                    cancellationToken),
                WorkflowType.DampfAblassen => await RewriteAsync(
                    cleaned,
                    apiKey,
                    settings.DampfAblassen.SystemPrompt,
                    "gpt-4o",
                    0.4,
                    cancellationToken),
                WorkflowType.EmojiText => await RewriteAsync(
                    cleaned,
                    apiKey,
                    PromptBuilder.BuildEmojiPrompt(settings.EmojiText.EmojiDensity),
                    settings.RewriteModel,
                    0.3,
                    cancellationToken),
                _ => cleaned
            };
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
