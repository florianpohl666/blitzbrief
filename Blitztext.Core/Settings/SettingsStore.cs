using System.Text.Json;
using System.Text.Json.Serialization;
using Blitztext.Core;

namespace Blitztext.Core.Settings;

public sealed class SettingsStore(string? settingsPath = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public string SettingsPath { get; } = settingsPath ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Blitztext",
        "settings.json");

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(SettingsPath))
        {
            return new AppSettings();
        }

        await using var stream = File.OpenRead(SettingsPath);
        var settings = await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions, cancellationToken);
        return Normalize(settings ?? new AppSettings());
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        await using var stream = File.Create(SettingsPath);
        await JsonSerializer.SerializeAsync(stream, Normalize(settings), JsonOptions, cancellationToken);
    }

    private static AppSettings Normalize(AppSettings settings)
    {
        settings.Language = string.IsNullOrWhiteSpace(settings.Language) ? "de" : settings.Language.Trim();
        settings.WorkflowHotkeys ??= AppSettings.DefaultHotkeys();
        foreach (var (type, hotkey) in AppSettings.DefaultHotkeys())
        {
            if (!settings.WorkflowHotkeys.ContainsKey(type) || string.IsNullOrWhiteSpace(settings.WorkflowHotkeys[type]))
            {
                settings.WorkflowHotkeys[type] = hotkey;
            }
        }

        settings.CustomTerms ??= [];
        settings.TextImprovement ??= new TextImprovementSettings();
        settings.DampfAblassen ??= new DampfAblassenSettings();
        if (PromptBuilder.ShouldReplaceLegacyDampfPrompt(settings.DampfAblassen.SystemPrompt))
        {
            settings.DampfAblassen.SystemPrompt = PromptBuilder.DefaultDampfAblassenPrompt;
        }

        settings.EmojiText ??= new EmojiTextSettings();
        settings.TranscriptionModel = string.IsNullOrWhiteSpace(settings.TranscriptionModel)
            ? "gpt-4o-mini-transcribe"
            : settings.TranscriptionModel.Trim();
        settings.RewriteModel = string.IsNullOrWhiteSpace(settings.RewriteModel)
            ? "gpt-4o-mini"
            : settings.RewriteModel.Trim();
        if (settings.AudioInputDeviceNumber < 0)
        {
            settings.AudioInputDeviceNumber = 0;
        }

        return settings;
    }
}
