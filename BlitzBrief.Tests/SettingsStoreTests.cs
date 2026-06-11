using BlitzBrief.Core.Models;
using BlitzBrief.Core.Settings;

namespace BlitzBrief.Tests;

public sealed class SettingsStoreTests
{
    [Fact]
    public async Task LoadAsync_WhenFileMissing_ReturnsDefaults()
    {
        var path = Path.Combine(Path.GetTempPath(), $"BlitzBrief-settings-{Guid.NewGuid():N}.json");
        var store = new SettingsStore(path);

        var settings = await store.LoadAsync();

        Assert.Equal("de", settings.Language);
        Assert.Equal("gpt-4o-mini-transcribe", settings.TranscriptionModel);
        Assert.Equal("Ctrl+Shift+Space", settings.WorkflowHotkeys[WorkflowType.Transcription]);
        Assert.Empty(settings.CustomTerms);
    }

    [Fact]
    public async Task SaveAsync_DoesNotPersistApiKeyField()
    {
        var path = Path.Combine(Path.GetTempPath(), $"BlitzBrief-settings-{Guid.NewGuid():N}.json");
        var store = new SettingsStore(path);
        var settings = new AppSettings();
        settings.CustomTerms.Add("Contoso");

        await store.SaveAsync(settings);

        var json = await File.ReadAllTextAsync(path);
        Assert.Contains("Contoso", json);
        Assert.DoesNotContain("apiKey", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("server", json, StringComparison.OrdinalIgnoreCase);
    }
}
