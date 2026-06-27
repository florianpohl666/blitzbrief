using BlitzBrief.Core;
using BlitzBrief.Core.Models;
using BlitzBrief.Core.Settings;

namespace BlitzBrief.Tests;


public sealed class PromptBuilderTests
{
    [Fact]
    public void WorkflowWhisperPrompt_BlitzBriefKontext_AppendsPrecedingSentenceAtEnd()
    {
        var settings = new AppSettings();
        var prompt = PromptBuilder.BuildWorkflowWhisperPrompt(
            WorkflowType.BlitzBriefKontext, settings, hasEnoughAudio: true, "Das Gericht stellte fest, dass");

        Assert.NotNull(prompt);
        Assert.EndsWith("Das Gericht stellte fest, dass", prompt);
    }

    [Fact]
    public void WorkflowWhisperPrompt_OmitsContext_WhenAudioTooShort()
    {
        var settings = new AppSettings();
        var prompt = PromptBuilder.BuildWorkflowWhisperPrompt(
            WorkflowType.BlitzBriefKontext, settings, hasEnoughAudio: false, "Das Gericht stellte fest, dass");

        Assert.DoesNotContain("Das Gericht stellte fest", prompt ?? "");
    }

    [Fact]
    public void UsesJornCommands_TrueForKontextMode()
    {
        Assert.True(PromptBuilder.UsesJornCommands(WorkflowType.BlitzBriefKontext, new AppSettings()));
    }

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
    public void JornMinimalPrompt_DoesNotContainReformulierung()
    {
        var settings = new TextImprovementSettings { Tone = TextTone.JornMinimal };
        var prompt = PromptBuilder.BuildTextImprovementPrompt(settings, []);

        Assert.Contains("Füllwörter", prompt);
        Assert.Contains("NICHT um", prompt);
        Assert.DoesNotContain("Lektor", prompt);
    }

    [Fact]
    public void JornCommandsPrompt_DelegatesCommandReplacementToCode()
    {
        // Kommando-Ersetzung passiert jetzt in TranscriptionQualityService.ReplaceCommands –
        // der GPT-Prompt enthält keine Kommandoliste mehr, aber noch die Kern-Anweisungen.
        var settings = new TextImprovementSettings { Tone = TextTone.JornCommands };
        var prompt = PromptBuilder.BuildTextImprovementPrompt(settings, []);

        Assert.Contains("Füllwörter", prompt);
        Assert.Contains("NICHT um", prompt);
        Assert.DoesNotContain("Satzende", prompt);
        Assert.DoesNotContain("Doppelpunkt", prompt);
    }

    [Fact]
    public void WhisperPrompt_AddsLanguageDirective_AndShortCommandHint()
    {
        var de = PromptBuilder.BuildWhisperPrompt([], includeCommandHints: true, "de");
        Assert.Contains("auf Deutsch", de);
        Assert.Contains("Diktierbefehle", de);
        Assert.Contains("§", de);          // juristische Zeichen-Vorgabe
        Assert.Contains("juristische", de);
        Assert.DoesNotContain("Auskunftsanspruch", de); // alte lange Beispielsätze sind weg

        var en = PromptBuilder.BuildWhisperPrompt([], includeCommandHints: false, "en");
        Assert.Contains("auf Englisch", en);

        // Automatik ("") ohne Hints und ohne Begriffe -> gar kein Prompt
        Assert.Null(PromptBuilder.BuildWhisperPrompt([], includeCommandHints: false, ""));
    }

    [Fact]
    public void EmojiPrompt_UsesDensity()
    {
        var prompt = PromptBuilder.BuildEmojiPrompt(EmojiDensity.Viel);

        Assert.Contains("großzügig", prompt);
        Assert.Contains("Gib NUR den Text", prompt);
    }
}
