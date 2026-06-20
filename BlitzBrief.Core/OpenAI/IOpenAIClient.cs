using BlitzBrief.Core.Settings;

namespace BlitzBrief.Core.OpenAI;

public interface IOpenAIClient
{
    Task<string> TranscribeAsync(
        string audioPath,
        string apiKey,
        string language,
        string? whisperPrompt,
        string model,
        CancellationToken cancellationToken);

    Task<string> RewriteAsync(
        string text,
        string apiKey,
        string systemPrompt,
        string model,
        double temperature,
        CancellationToken cancellationToken);
}
