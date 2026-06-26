namespace BlitzBrief.Core.OpenAI;

public interface IOpenAIClient
{
    /// <summary>Batch-Transkription eines fertigen WAV-Streams (Fallback, wenn Realtime nicht greift).</summary>
    Task<string> TranscribeAsync(
        Stream audioWav,
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
