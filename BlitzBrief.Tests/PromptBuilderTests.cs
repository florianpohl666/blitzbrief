using BlitzBrief.Core;
using BlitzBrief.Core.Settings;

namespace BlitzBrief.Tests;

public sealed class PromptBuilderTests
{
    [Fact]
    public void TextImprovementPrompt_IncludesToneContextAndCustomTerms()
    {
        var settings = new TextImprovementSettings
        {
            Tone = TextTone.Formal,
            Context = "E-Mails im Bereich Beratung"
        };

        var prompt = PromptBuilder.BuildTextImprovementPrompt(settings, ["Contoso", "BlitzBrief"]);

        Assert.Contains("formellen", prompt);
        Assert.Contains("Contoso, BlitzBrief", prompt);
        Assert.Contains("E-Mails im Bereich Beratung", prompt);
    }

    [Fact]
    public void CustomTextImprovementPrompt_IsPreservedAndExtendedWithTerms()
    {
        var settings = new TextImprovementSettings
        {
            SystemPrompt = "Schreibe knapp."
        };

        var prompt = PromptBuilder.BuildTextImprovementPrompt(settings, ["Fabrikam"]);

        Assert.StartsWith("Schreibe knapp.", prompt);
        Assert.Contains("Fabrikam", prompt);
    }

    [Fact]
    public void DampfAblassenDefaultPrompt_ReturnsOnlyFinishedMessage()
    {
        Assert.Contains("Gib NUR die fertige Nachricht", PromptBuilder.DefaultDampfAblassenPrompt);
        Assert.Contains("ruhig", PromptBuilder.DefaultDampfAblassenPrompt);
    }

    [Fact]
    public void EmojiPrompt_UsesDensity()
    {
        var prompt = PromptBuilder.BuildEmojiPrompt(EmojiDensity.Viel);

        Assert.Contains("großzügig", prompt);
        Assert.Contains("Gib NUR den Text", prompt);
    }
}
