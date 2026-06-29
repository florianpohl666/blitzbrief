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
    public void KontextGapPrompt_PlacesGapBetweenLeftAndRight_AndKeepsCommandHints()
    {
        var settings = new AppSettings();
        var prompt = PromptBuilder.BuildKontextGapPrompt(
            WorkflowType.BlitzBriefKontextGpt, settings, hasEnoughAudio: true, "Ich bin ein", "alter Mann.");

        Assert.NotNull(prompt);
        Assert.Contains("\"Ich bin ein ___ alter Mann.\"", prompt);     // Lücke zwischen links und rechts
        Assert.Contains("nur diesen eingefügten Text", prompt);          // Anti-Leakage-Anweisung
        Assert.Contains("Diktierbefehle", prompt);                       // Jörn-/Juristik-Hinweise bleiben (getrennt)
    }

    [Fact]
    public void KontextGapPrompt_WithoutAnyContext_FallsBackToHintsOnly()
    {
        var settings = new AppSettings();
        var prompt = PromptBuilder.BuildKontextGapPrompt(
            WorkflowType.BlitzBriefKontextGpt, settings, hasEnoughAudio: true, null, null);

        Assert.NotNull(prompt);
        Assert.DoesNotContain("___", prompt);                            // ohne Kontext keine Einfügelücke
    }

    [Fact]
    public void WorkflowWhisperPrompt_IncludesContextEvenWhenAudioShort_ButNotCommandHints()
    {
        // Fix für die Großschreibung kurzer Einschübe: der Vortext muss AUCH bei kurzem Audio
        // mit – sonst hat whisper-1 keinen Fortsetzungs-Kontext und schreibt das erste Wort groß.
        var settings = new AppSettings();
        var prompt = PromptBuilder.BuildWorkflowWhisperPrompt(
            WorkflowType.BlitzBriefKontext, settings, hasEnoughAudio: false, "Das Gericht stellte fest, dass");

        Assert.NotNull(prompt);
        // Fragment wird angehängt …
        Assert.EndsWith("Das Gericht stellte fest, dass", prompt);
        // … hinter einem punkt-beendeten Satz (Sprachvorgabe als Anker), damit whisper klein fortsetzt …
        Assert.Contains("auf Deutsch.", prompt);
        // … aber die echo-anfälligen Kommando-Hinweise bleiben bei kurzem Audio weg.
        Assert.DoesNotContain("Diktierbefehle", prompt);
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
