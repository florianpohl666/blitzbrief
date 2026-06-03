using Blitztext.Core.OpenAI;
using Blitztext.Core.Security;
using Blitztext.Core.Settings;
using Blitztext.Core.Workflow;
using Blitztext.Core.Models;

namespace Blitztext.Tests;

public sealed class WorkflowRunnerTests
{
    [Fact]
    public async Task ProcessAsync_Transcription_CopiesTranscribedTextAndDeletesAudio()
    {
        var audioPath = CreateTempAudio();
        var runner = CreateRunner(new FakeOpenAIClient(" hallo welt ", "unused"), out _);

        var result = await runner.ProcessAsync(WorkflowType.Transcription, audioPath, TimeSpan.FromSeconds(2), CancellationToken.None);

        Assert.Equal("hallo welt", result);
        Assert.False(File.Exists(audioPath));
    }

    [Fact]
    public async Task ProcessAsync_TextImprover_TranscribesThenRewrites()
    {
        var audioPath = CreateTempAudio();
        var client = new FakeOpenAIClient("hallo welt", "Hallo Welt.");
        var runner = CreateRunner(client, out _);

        var result = await runner.ProcessAsync(WorkflowType.TextImprover, audioPath, TimeSpan.FromSeconds(2), CancellationToken.None);

        Assert.Equal("Hallo Welt.", result);
        Assert.Contains("Eigennamen", client.LastRewritePrompt);
    }

    [Fact]
    public async Task ProcessAsync_WhenApiKeyMissing_ReturnsHelpfulError()
    {
        var audioPath = CreateTempAudio();
        var settings = new AppSettings();
        var keyPath = Path.Combine(Path.GetTempPath(), $"blitztext-key-{Guid.NewGuid():N}.bin");
        var runner = new WorkflowRunner(new FakeOpenAIClient("x", "x"), new ApiKeyStore(keyPath), () => settings);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runner.ProcessAsync(WorkflowType.Transcription, audioPath, TimeSpan.FromSeconds(2), CancellationToken.None));

        Assert.Contains("OpenAI API Key fehlt", ex.Message);
    }

    private static WorkflowRunner CreateRunner(FakeOpenAIClient client, out AppSettings settings)
    {
        var localSettings = new AppSettings
        {
            CustomTerms = ["Contoso"]
        };
        settings = localSettings;
        var keyPath = Path.Combine(Path.GetTempPath(), $"blitztext-key-{Guid.NewGuid():N}.bin");
        var keyStore = new ApiKeyStore(keyPath);
        keyStore.Save("test-openai-key");
        return new WorkflowRunner(client, keyStore, () => localSettings);
    }

    private static string CreateTempAudio()
    {
        var path = Path.Combine(Path.GetTempPath(), $"blitztext-test-{Guid.NewGuid():N}.wav");
        File.WriteAllBytes(path, [1, 2, 3, 4]);
        return path;
    }

    private sealed class FakeOpenAIClient(string transcription, string rewrite) : IOpenAIClient
    {
        public string LastRewritePrompt { get; private set; } = "";

        public Task<string> TranscribeAsync(
            string audioPath,
            string apiKey,
            string language,
            IReadOnlyList<string> customTerms,
            string model,
            CancellationToken cancellationToken)
        {
            Assert.True(File.Exists(audioPath));
            Assert.Equal("test-openai-key", apiKey);
            Assert.Contains("Contoso", customTerms);
            return Task.FromResult(transcription);
        }

        public Task<string> RewriteAsync(
            string text,
            string apiKey,
            string systemPrompt,
            string model,
            double temperature,
            CancellationToken cancellationToken)
        {
            LastRewritePrompt = systemPrompt;
            return Task.FromResult(rewrite);
        }
    }
}
