namespace Blitztext.Core;

public static class TranscriptionQualityService
{
    private static readonly string[] CommonArtifacts =
    [
        "Untertitel der Amara.org-Community",
        "Danke fürs Zuschauen",
        "Vielen Dank fürs Zuschauen",
        "Thanks for watching"
    ];

    public static bool ShouldRejectRecording(TimeSpan duration) => duration.TotalMilliseconds < 350;

    public static string CleanedTranscript(string text) => string.Join(
        "\n",
        text.Replace("\r\n", "\n")
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line)))
        .Trim();

    public static bool IsLikelyArtifact(string text, TimeSpan duration)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if (duration.TotalSeconds < 1.0 && text.Length < 4)
        {
            return true;
        }

        return duration.TotalSeconds < 2.0 &&
               CommonArtifacts.Any(artifact => text.Contains(artifact, StringComparison.OrdinalIgnoreCase));
    }
}
