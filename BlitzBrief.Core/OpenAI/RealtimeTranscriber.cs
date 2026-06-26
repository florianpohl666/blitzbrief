using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace BlitzBrief.Core.OpenAI;

/// <summary>Erzeugt Echtzeit-Transkriptions-Sitzungen (OpenAI Realtime GA, WebSocket).</summary>
public interface IRealtimeTranscriber
{
    IRealtimeTranscriptionSession CreateSession(string apiKey, string model, string language, string? prompt, int sampleRate);
}

/// <summary>
/// Eine laufende Sitzung: Audio wird per <see cref="Append"/> während des Sprechens gestreamt,
/// <see cref="CompleteAsync"/> committet und liefert das finale Transkript.
/// </summary>
public interface IRealtimeTranscriptionSession : IAsyncDisposable
{
    void Append(byte[] pcm);
    Task<string> CompleteAsync(CancellationToken cancellationToken);
    void Abort();
}

public sealed class RealtimeTranscriptionException(string message, Exception? inner = null) : Exception(message, inner);

public sealed class RealtimeTranscriber : IRealtimeTranscriber
{
    public IRealtimeTranscriptionSession CreateSession(string apiKey, string model, string language, string? prompt, int sampleRate)
        => new RealtimeTranscriptionSession(apiKey, model, language, prompt, sampleRate);
}

/// <summary>
/// Streamt 24-kHz-PCM live an die OpenAI-Realtime-API und sammelt die Transkript-Deltas.
/// Wir verwenden nur das Endergebnis (Event ...input_audio_transcription.completed); die
/// Deltas werden ignoriert, weil die gesamte Nachverarbeitung den Volltext braucht.
/// </summary>
internal sealed class RealtimeTranscriptionSession : IRealtimeTranscriptionSession
{
    private static readonly Uri Endpoint = new("wss://api.openai.com/v1/realtime?intent=transcription");
    private static readonly TimeSpan FinalizeTimeout = TimeSpan.FromSeconds(30);

    private readonly ClientWebSocket socket = new();
    private readonly Channel<byte[]> frames = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions { SingleReader = true });
    private readonly TaskCompletionSource<string> result = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> configured = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource cts = new();
    private readonly string apiKey;
    private readonly string model;
    private readonly string language;
    private readonly string? prompt;
    private readonly int sampleRate;

    public RealtimeTranscriptionSession(string apiKey, string model, string language, string? prompt, int sampleRate)
    {
        this.apiKey = apiKey;
        this.model = model;
        this.language = language;
        this.prompt = prompt;
        this.sampleRate = sampleRate;

        // Fehler im Hintergrund-Pipeline immer beobachten (vermeidet UnobservedTaskException,
        // falls die Sitzung abgebrochen wird, ohne dass CompleteAsync aufgerufen wurde).
        _ = result.Task.ContinueWith(static t => { _ = t.Exception; }, CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
        _ = Task.Run(RunAsync);
    }

    public void Append(byte[] pcm) => frames.Writer.TryWrite(pcm);

    public async Task<string> CompleteAsync(CancellationToken cancellationToken)
    {
        frames.Writer.TryComplete();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(FinalizeTimeout);
        try
        {
            return await result.Task.WaitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Abort();
            throw new RealtimeTranscriptionException("Zeitüberschreitung beim Echtzeit-Transkript.");
        }
    }

    public void Abort()
    {
        try { cts.Cancel(); } catch { }
        try { socket.Abort(); } catch { }
        result.TrySetException(new RealtimeTranscriptionException("Echtzeit-Sitzung abgebrochen."));
    }

    private async Task RunAsync()
    {
        try
        {
            socket.Options.SetRequestHeader("Authorization", "Bearer " + apiKey);
            await socket.ConnectAsync(Endpoint, cts.Token);

            var receiver = Task.Run(ReceiveLoopAsync);
            await SendAsync(BuildSessionUpdate());

            // Erst nach session.updated Audio senden (sonst kennt der Server das Format noch nicht).
            await Task.WhenAny(configured.Task, result.Task);
            if (result.Task.IsCompleted)
            {
                return; // Fehler bereits gesetzt (vom Empfangs-Loop) – CompleteAsync beobachtet ihn.
            }

            await foreach (var frame in frames.Reader.ReadAllAsync(cts.Token))
            {
                await SendAsync(new { type = "input_audio_buffer.append", audio = Convert.ToBase64String(frame) });
            }

            await SendAsync(new { type = "input_audio_buffer.commit" });
            await receiver;
        }
        catch (OperationCanceledException)
        {
            result.TrySetException(new RealtimeTranscriptionException("Echtzeit-Sitzung abgebrochen."));
        }
        catch (Exception ex)
        {
            result.TrySetException(new RealtimeTranscriptionException("Echtzeit-Transkription fehlgeschlagen: " + ex.Message, ex));
        }
    }

    private async Task ReceiveLoopAsync()
    {
        var buffer = new byte[64 * 1024];
        var message = new StringBuilder();
        try
        {
            while (socket.State == WebSocketState.Open)
            {
                message.Clear();
                WebSocketReceiveResult chunk;
                do
                {
                    chunk = await socket.ReceiveAsync(buffer, cts.Token);
                    if (chunk.MessageType == WebSocketMessageType.Close)
                    {
                        result.TrySetException(new RealtimeTranscriptionException("Verbindung wurde geschlossen."));
                        return;
                    }

                    message.Append(Encoding.UTF8.GetString(buffer, 0, chunk.Count));
                } while (!chunk.EndOfMessage);

                using var doc = JsonDocument.Parse(message.ToString());
                var type = doc.RootElement.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";

                if (type.EndsWith("session.updated", StringComparison.Ordinal))
                {
                    configured.TrySetResult(true);
                }
                else if (type == "error")
                {
                    var msg = doc.RootElement.TryGetProperty("error", out var err) && err.TryGetProperty("message", out var m)
                        ? m.GetString()
                        : "unbekannt";
                    result.TrySetException(new RealtimeTranscriptionException("OpenAI Realtime Fehler: " + msg));
                    return;
                }
                else if (type.Contains("input_audio_transcription", StringComparison.Ordinal) &&
                         type.EndsWith("completed", StringComparison.Ordinal))
                {
                    var text = doc.RootElement.TryGetProperty("transcript", out var tr) ? tr.GetString() ?? "" : "";
                    result.TrySetResult(text);
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Abbruch – Ergebnis wurde anderweitig gesetzt.
        }
        catch (Exception ex)
        {
            result.TrySetException(new RealtimeTranscriptionException("Empfangsfehler: " + ex.Message, ex));
        }
    }

    private async Task SendAsync(object payload)
    {
        var json = JsonSerializer.Serialize(payload);
        await socket.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, cts.Token);
    }

    private object BuildSessionUpdate()
    {
        var transcription = new Dictionary<string, object?> { ["model"] = model };
        if (!string.IsNullOrWhiteSpace(language))
        {
            transcription["language"] = language.Trim();
        }

        if (!string.IsNullOrWhiteSpace(prompt))
        {
            transcription["prompt"] = prompt;
        }

        return new
        {
            type = "session.update",
            session = new
            {
                type = "transcription",
                audio = new
                {
                    input = new
                    {
                        format = new { type = "audio/pcm", rate = sampleRate },
                        transcription,
                        turn_detection = (object?)null
                    }
                }
            }
        };
    }

    public async ValueTask DisposeAsync()
    {
        try { cts.Cancel(); } catch { }
        try
        {
            if (socket.State == WebSocketState.Open)
            {
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
            }
        }
        catch { }
        socket.Dispose();
        cts.Dispose();
    }
}
