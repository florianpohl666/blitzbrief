namespace BlitzBrief.Core;

/// <summary>
/// Ergebnis der Sprachbeginn-Erkennung: ob/wo Sprache beginnt plus die Wahrscheinlichkeiten
/// pro Fenster (für die Debug-Visualisierung). Plattformneutral – die eigentliche Silero-Inferenz
/// (ONNX Runtime) liefert die Wahrscheinlichkeiten und liegt im Windows-Projekt.
/// </summary>
public sealed record SpeechOnset(
    bool Detected,
    int OnsetFrame,
    int OnsetMs,
    int FrameMs,
    float Threshold,
    IReadOnlyList<float> Probabilities);

public static class SpeechOnsetDetection
{
    /// <summary>
    /// Erstes Sprach-Onset aus Wahrscheinlichkeiten pro Fenster: erstes Fenster, ab dem die
    /// Wahrscheinlichkeit für mindestens <paramref name="minSpeechMs"/> über der Schwelle bleibt
    /// (gegen Klick-Fehlauslöser). Davor wird <paramref name="padMs"/> als Sicherheitsrand behalten,
    /// damit der Wortanlaut nicht abgeschnitten wird.
    /// </summary>
    public static SpeechOnset FindOnset(
        IReadOnlyList<float> probs, int frameMs,
        float threshold = 0.5f, int minSpeechMs = 100, int padMs = 100)
    {
        var minFrames = Math.Max(1, minSpeechMs / frameMs);
        var padFrames = Math.Max(0, padMs / frameMs);

        for (var i = 0; i + minFrames <= probs.Count; i++)
        {
            var sustained = true;
            for (var k = 0; k < minFrames; k++)
            {
                if (probs[i + k] < threshold)
                {
                    sustained = false;
                    break;
                }
            }
            if (sustained)
            {
                var onsetFrame = Math.Max(0, i - padFrames);
                return new SpeechOnset(true, onsetFrame, onsetFrame * frameMs, frameMs, threshold, probs);
            }
        }

        return new SpeechOnset(false, 0, 0, frameMs, threshold, probs);
    }
}
