namespace BlitzBrief.Core.Workflow;

/// <summary>
/// Aufgenommenes Audio plus – falls vorhanden – das bereits per Realtime-Stream gewonnene
/// Transkript. <see cref="Pcm"/> (16-bit mono, <see cref="SampleRate"/>) bleibt immer erhalten,
/// damit der Batch-Fallback ohne erneute Aufnahme transkribieren kann.
/// </summary>
public sealed record RecordedAudio(
    byte[] Pcm,
    int SampleRate,
    TimeSpan Duration,
    string? RealtimeTranscript = null,
    string? RealtimePrompt = null,
    string? PrecedingContext = null);
