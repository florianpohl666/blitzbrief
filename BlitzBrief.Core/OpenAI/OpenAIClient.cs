using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BlitzBrief.Core.OpenAI;

public sealed class OpenAIClient(HttpClient? httpClient = null) : IOpenAIClient
{
    private readonly HttpClient http = httpClient ?? new HttpClient();

    public async Task<string> TranscribeAsync(
        string audioPath,
        string apiKey,
        string language,
        string? whisperPrompt,
        string model,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/audio/transcriptions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        await using var audioStream = File.OpenRead(audioPath);
        using var form = new MultipartFormDataContent();
        using var fileContent = new StreamContent(audioStream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        form.Add(fileContent, "file", Path.GetFileName(audioPath));
        form.Add(new StringContent(model), "model");
        form.Add(new StringContent("text"), "response_format");

        if (!string.IsNullOrWhiteSpace(language))
        {
            form.Add(new StringContent(language.Trim()), "language");
        }

        if (!string.IsNullOrWhiteSpace(whisperPrompt))
        {
            form.Add(new StringContent(whisperPrompt), "prompt");
        }

        request.Content = form;
        using var response = await http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new OpenAIException(ParseError(body) ?? $"OpenAI transcription failed: {(int)response.StatusCode}");
        }

        var text = body.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new OpenAIException("OpenAI returned an empty transcription.");
        }

        return text;
    }

    public async Task<string> RewriteAsync(
        string text,
        string apiKey,
        string systemPrompt,
        string model,
        double temperature,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var payload = new ChatCompletionRequest(
            model,
            temperature,
            [
                new ChatMessage("system", systemPrompt),
                new ChatMessage("user", text)
            ]);

        var json = JsonSerializer.Serialize(payload, OpenAIJsonContext.Default.ChatCompletionRequest);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new OpenAIException(ParseError(body) ?? $"OpenAI rewrite failed: {(int)response.StatusCode}");
        }

        using var document = JsonDocument.Parse(body);
        var content = document.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString()
            ?.Trim();

        if (string.IsNullOrWhiteSpace(content))
        {
            throw new OpenAIException("OpenAI returned an empty rewrite.");
        }

        return content;
    }

    private static string? ParseError(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.GetProperty("error").GetProperty("message").GetString();
        }
        catch
        {
            return null;
        }
    }
}

public sealed class OpenAIException(string message) : Exception(message);

internal sealed record ChatMessage(string Role, string Content);

internal sealed record ChatCompletionRequest(string Model, double Temperature, ChatMessage[] Messages);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ChatCompletionRequest))]
internal sealed partial class OpenAIJsonContext : JsonSerializerContext;
