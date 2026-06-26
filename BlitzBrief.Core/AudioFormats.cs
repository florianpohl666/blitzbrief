namespace BlitzBrief.Core;

/// <summary>Hilfsfunktionen für Roh-PCM ↔ WAV (für den Batch-Upload-Fallback ohne Temp-Datei).</summary>
public static class AudioFormats
{
    /// <summary>Verpackt 16-bit-PCM (little-endian) in einen WAV-Container im Speicher.</summary>
    public static byte[] PcmToWav(byte[] pcm, int sampleRate, int channels = 1, int bitsPerSample = 16)
    {
        using var ms = new MemoryStream(pcm.Length + 44);
        using var bw = new BinaryWriter(ms);
        var byteRate = sampleRate * channels * bitsPerSample / 8;
        var blockAlign = (short)(channels * bitsPerSample / 8);

        bw.Write("RIFF"u8.ToArray());
        bw.Write(36 + pcm.Length);
        bw.Write("WAVE"u8.ToArray());
        bw.Write("fmt "u8.ToArray());
        bw.Write(16);              // fmt-Chunk-Größe
        bw.Write((short)1);        // PCM
        bw.Write((short)channels);
        bw.Write(sampleRate);
        bw.Write(byteRate);
        bw.Write(blockAlign);
        bw.Write((short)bitsPerSample);
        bw.Write("data"u8.ToArray());
        bw.Write(pcm.Length);
        bw.Write(pcm);
        bw.Flush();
        return ms.ToArray();
    }
}
