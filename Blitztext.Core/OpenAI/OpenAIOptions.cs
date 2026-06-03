namespace Blitztext.Core.OpenAI;

public sealed record OpenAIOptions(
    string TranscriptionModel = "gpt-4o-mini-transcribe",
    string RewriteModel = "gpt-4o-mini");
