using Blitztext.Core.Settings;

namespace Blitztext.Core.OpenAI;

public interface IOpenAIClient
{
    Task<string> TranscribeAsync(
        string audioPath,
        string apiKey,
        string language,
        IReadOnlyList<string> customTerms,
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
