using System.IO;
using System.Reflection;
using BlitzBrief.Core;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace BlitzBrief.Windows.Platform;

/// <summary>Diagnose fürs Debug-Fenster: ob das Modell verfügbar war, das Onset-Ergebnis und wie viel getrimmt wurde.</summary>
public sealed record SpeechTrimInfo(bool ModelAvailable, SpeechOnset Onset, int TrimmedMs);

/// <summary>
/// Silero-VAD (v5, ONNX Runtime CPU) zur robusten Sprachbeginn-Erkennung. Lädt das eingebettete
/// Modell, läuft 512-Sample-Fenster (32 ms) bei 16 kHz mit fortgeführtem State. Jeder Schritt
/// bekommt 64 Samples Kontext aus dem Vorfenster vorangestellt (v5-Vertrag, sonst bleibt die
/// Wahrscheinlichkeit ~0). Schlägt das Laden fehl (Packaging/Plattform), ist <see cref="Available"/>
/// false und es wird nicht getrimmt.
/// </summary>
public sealed class SileroSpeechDetector : IDisposable
{
    public const int FrameMs = 32;       // 512 Samples @ 16 kHz
    private const int TargetRate = 16000;
    private const int Window = 512;
    private const int Context = 64;      // v5: 64 Samples Vorlauf aus dem Vorfenster vor die 512 neuen
    private const int StateLen = 2 * 1 * 128;

    private readonly InferenceSession? session;
    private int[] srDims = [];            // sr = Skalar; bei Bedarf auf [1] umgestellt (Robustheit)

    public bool Available => session is not null;

    public SileroSpeechDetector()
    {
        try
        {
            var model = LoadModelBytes();
            session = new InferenceSession(model);
            WarmUp();
            AppLog.Write("Silero VAD geladen (ONNX Runtime).");
        }
        catch (Exception ex)
        {
            AppLog.Write($"Silero VAD nicht verfügbar (Trim deaktiviert): {ex.Message}");
            session = null;
        }
    }

    private static byte[] LoadModelBytes()
    {
        var asm = Assembly.GetExecutingAssembly();
        var name = asm.GetManifestResourceNames()
            .First(n => n.EndsWith("silero_vad.onnx", StringComparison.OrdinalIgnoreCase));
        using var stream = asm.GetManifestResourceStream(name)!;
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    private void WarmUp() => Probabilities(new float[Window * 3]);

    /// <summary>16-bit-mono-PCM -> Sprachbeginn (per <see cref="SpeechOnsetDetection"/>).</summary>
    public SpeechOnset Detect(byte[] pcm, int sampleRate)
    {
        if (session is null)
        {
            return new SpeechOnset(false, 0, 0, FrameMs, 0.5f, []);
        }

        try
        {
            var samples = ToFloat(pcm);
            var s16 = sampleRate == TargetRate ? samples : Resample(samples, sampleRate, TargetRate);
            var probs = Probabilities(s16);
            return SpeechOnsetDetection.FindOnset(probs, FrameMs);
        }
        catch (Exception ex)
        {
            // Inferenzfehler darf das Diktat nie brechen → kein Trim.
            AppLog.Write($"Silero Detect fehlgeschlagen (kein Trim): {ex.Message}");
            return new SpeechOnset(false, 0, 0, FrameMs, 0.5f, []);
        }
    }

    private float[] Probabilities(float[] s16)
    {
        try
        {
            return RunAll(s16, srDims);
        }
        catch (Exception) when (srDims.Length == 0)
        {
            // Manche Modell-/Runtime-Kombis erwarten sr als Rang-1-Tensor statt Skalar.
            srDims = [1];
            return RunAll(s16, srDims);
        }
    }

    private float[] RunAll(float[] s16, int[] srShape)
    {
        var probs = new List<float>(s16.Length / Window + 1);
        var state = new float[StateLen];
        // Modelleingang = 64 Samples Kontext aus dem Vorfenster + 512 neue Samples (= 576). Ohne
        // diesen Vorlauf liefert Silero v5 durchgehend ~0, der State driftet weg und es wird nie
        // ein Onset erkannt. Erstes Fenster: Kontext = Stille (Nullen).
        var input = new float[Context + Window];
        for (var off = 0; off + Window <= s16.Length; off += Window)
        {
            Array.Copy(s16, off, input, Context, Window);

            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("input", new DenseTensor<float>(input, [1, Context + Window])),
                NamedOnnxValue.CreateFromTensor("state", new DenseTensor<float>(state, [2, 1, 128])),
                NamedOnnxValue.CreateFromTensor("sr", new DenseTensor<long>(new long[] { TargetRate }, srShape)),
            };

            using var results = session!.Run(inputs);
            probs.Add(results.First(v => v.Name == "output").AsEnumerable<float>().First());
            state = results.First(v => v.Name == "stateN").AsEnumerable<float>().ToArray();

            // Kontext fürs nächste Fenster = letzte 64 Samples dieses Fensters.
            Array.Copy(s16, off + Window - Context, input, 0, Context);
        }
        return [.. probs];
    }

    private static float[] ToFloat(byte[] pcm)
    {
        var n = pcm.Length / 2;
        var f = new float[n];
        for (var i = 0; i < n; i++)
        {
            f[i] = (short)(pcm[i * 2] | (pcm[i * 2 + 1] << 8)) / 32768f;
        }
        return f;
    }

    private static float[] Resample(float[] src, int from, int to)
    {
        var ratio = (double)from / to;
        var n = (int)(src.Length / ratio);
        var dst = new float[n];
        for (var j = 0; j < n; j++)
        {
            var x = j * ratio;
            var i = (int)x;
            var frac = x - i;
            dst[j] = i + 1 < src.Length ? (float)(src[i] * (1 - frac) + src[i + 1] * frac) : src[i];
        }
        return dst;
    }

    public void Dispose() => session?.Dispose();
}
