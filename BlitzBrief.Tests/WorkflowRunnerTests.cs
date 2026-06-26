using BlitzBrief.Core;
using BlitzBrief.Core.OpenAI;
using BlitzBrief.Core.Security;
using BlitzBrief.Core.Settings;
using BlitzBrief.Core.Workflow;
using BlitzBrief.Core.Models;

namespace BlitzBrief.Tests;

public sealed class WorkflowRunnerTests
{
    [Fact]
    public async Task ProcessAsync_Transcription_BatchPath_ReturnsCleanedText()
    {
        var client = new FakeOpenAIClient(" hallo welt ", "unused");
        var runner = CreateRunner(client, out _);

        var result = await runner.ProcessAsync(WorkflowType.Transcription, BatchAudio(), CancellationToken.None);

        Assert.Equal("hallo welt", result.Text);
        Assert.True(client.Transcribed);
    }

    [Fact]
    public async Task ProcessAsync_Transcription_RealtimePath_SkipsBatchTranscription()
    {
        var client = new FakeOpenAIClient("SHOULD-NOT-BE-USED", "unused");
        var runner = CreateRunner(client, out _);

        var audio = new RecordedAudio([1, 2, 3, 4], 24000, TimeSpan.FromSeconds(2), RealtimeTranscript: " hallo welt ");
        var result = await runner.ProcessAsync(WorkflowType.Transcription, audio, CancellationToken.None);

        Assert.Equal("hallo welt", result.Text);
        Assert.False(client.Transcribed);
    }

    [Fact]
    public async Task ProcessAsync_TextImprover_TranscribesThenRewrites()
    {
        var client = new FakeOpenAIClient("hallo welt", "Hallo Welt.");
        var runner = CreateRunner(client, out _);

        var result = await runner.ProcessAsync(WorkflowType.TextImprover, BatchAudio(), CancellationToken.None);

        Assert.Equal("Hallo Welt.", result.Text);
        Assert.Contains("Eigennamen", client.LastRewritePrompt);
    }

    [Fact]
    public async Task ProcessAsync_WhenPromptCorruptsShortWord_RetriesWithoutPrompt()
    {
        // Erste Transkription (mit Prompt) liefert ein Prompt-Echo; ohne Prompt das echte Wort.
        var echo = PromptBuilder.BuildWhisperPrompt(["Contoso"], includeCommandHints: true, "de")!;
        var client = new PromptAwareFakeClient(withPrompt: echo, withoutPrompt: "Baum");

        var settings = new AppSettings { CustomTerms = ["Contoso"] };
        settings.TextImprovement.Tone = TextTone.JornCommands;
        settings.TextImprovement.SkipRewrite = true;
        var keyStore = NewKeyStore();
        var runner = new WorkflowRunner(client, keyStore, () => settings);

        var result = await runner.ProcessAsync(WorkflowType.TextImprover, BatchAudio(), CancellationToken.None);

        Assert.Equal("Baum", result.Text);
        Assert.True(client.RetriedWithoutPrompt);
    }

    [Fact]
    public async Task ProcessAsync_RealtimeEcho_RetriesWithoutPromptViaBatch()
    {
        // Realtime lieferte ein Prompt-Echo -> WorkflowRunner soll per Batch ohne Prompt nachtranskribieren.
        var echo = PromptBuilder.BuildWhisperPrompt(["Contoso"], includeCommandHints: true, "de")!;
        var client = new PromptAwareFakeClient(withPrompt: "ignored", withoutPrompt: "Baum");

        var settings = new AppSettings { CustomTerms = ["Contoso"] };
        settings.TextImprovement.Tone = TextTone.JornCommands;
        settings.TextImprovement.SkipRewrite = true;
        var runner = new WorkflowRunner(client, NewKeyStore(), () => settings);

        var audio = new RecordedAudio([1, 2, 3, 4], 24000, TimeSpan.FromSeconds(2),
            RealtimeTranscript: echo, RealtimePrompt: echo);
        var result = await runner.ProcessAsync(WorkflowType.TextImprover, audio, CancellationToken.None);

        Assert.Equal("Baum", result.Text);
        Assert.True(client.RetriedWithoutPrompt);
    }

    [Fact]
    public async Task ProcessAsync_WhenApiKeyMissing_ReturnsHelpfulError()
    {
        var settings = new AppSettings();
        var runner = new WorkflowRunner(new FakeOpenAIClient("x", "x"), NewKeyStore(save: false), () => settings);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runner.ProcessAsync(WorkflowType.Transcription, BatchAudio(), CancellationToken.None));

        Assert.Contains("OpenAI API Key fehlt", ex.Message);
    }

    private static WorkflowRunner CreateRunner(FakeOpenAIClient client, out AppSettings settings)
    {
        var localSettings = new AppSettings { CustomTerms = ["Contoso"] };
        settings = localSettings;
        return new WorkflowRunner(client, NewKeyStore(), () => localSettings);
    }

    private static ApiKeyStore NewKeyStore(bool save = true)
    {
        var keyPath = Path.Combine(Path.GetTempPath(), $"BlitzBrief-key-{Guid.NewGuid():N}.bin");
        var keyStore = new ApiKeyStore(keyPath);
        if (save)
        {
            keyStore.Save("test-openai-key");
        }

        return keyStore;
    }

    // Batch-Pfad: kein Realtime-Transkript -> WorkflowRunner transkribiert das PCM per Batch.
    private static RecordedAudio BatchAudio() =>
        new([1, 2, 3, 4], 24000, TimeSpan.FromSeconds(2));

    // Liefert je nach Prompt unterschiedliche Transkripte – simuliert, dass der
    // Prompt ein kurzes Wort verbiegt und der prompt-freie Retry es korrekt erkennt.
    private sealed class PromptAwareFakeClient(string withPrompt, string withoutPrompt) : IOpenAIClient
    {
        public bool RetriedWithoutPrompt { get; private set; }

        public Task<string> TranscribeAsync(
            Stream audioWav,
            string apiKey,
            string language,
            string? whisperPrompt,
            string model,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(whisperPrompt))
            {
                RetriedWithoutPrompt = true;
                return Task.FromResult(withoutPrompt);
            }

            return Task.FromResult(withPrompt);
        }

        public Task<string> RewriteAsync(
            string text,
            string apiKey,
            string systemPrompt,
            string model,
            double temperature,
            CancellationToken cancellationToken) => Task.FromResult("unused");
    }

    private sealed class FakeOpenAIClient(string transcription, string rewrite) : IOpenAIClient
    {
        public string LastRewritePrompt { get; private set; } = "";
        public bool Transcribed { get; private set; }

        public Task<string> TranscribeAsync(
            Stream audioWav,
            string apiKey,
            string language,
            string? whisperPrompt,
            string model,
            CancellationToken cancellationToken)
        {
            Transcribed = true;
            Assert.True(audioWav.CanRead);
            Assert.Equal("test-openai-key", apiKey);
            Assert.NotNull(whisperPrompt);
            Assert.Contains("Contoso", whisperPrompt);
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
