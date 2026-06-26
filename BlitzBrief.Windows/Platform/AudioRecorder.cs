using System.IO;
using NAudio.Wave;

namespace BlitzBrief.Windows.Platform;

public sealed record RecordingResult(byte[] Pcm, TimeSpan Duration);
public sealed record AudioInputDeviceInfo(int Index, string Name, int Channels);

/// <summary>
/// Nimmt das Mikrofon durchgehend in einen Ringpuffer auf (Pre-Roll), damit das
/// Scharfschalten beim Hotkey verzögerungsfrei ist und die ~300 ms vor dem
/// Tastendruck nicht verloren gehen. Während der Aufnahme wird das PCM sowohl in
/// einen Speicherpuffer geschrieben (für den Batch-Fallback) als auch über
/// <see cref="FrameAvailable"/> live weitergereicht (für die Realtime-Transkription).
/// </summary>
public sealed class AudioRecorder : IDisposable
{
    // 24 kHz mono PCM16 = Eingabeformat der OpenAI-Realtime-API; für den Batch-Upload ebenso gültig.
    private static readonly WaveFormat Format = new(24000, 16, 1);
    private static int ByteRate => Format.AverageBytesPerSecond;

    /// <summary>Abtastrate der Aufnahme (Hz) – wird für Realtime-Session und WAV-Fallback gebraucht.</summary>
    public static int SampleRate => Format.SampleRate;

    private readonly object sync = new();

    private WaveInEvent? capture;
    private MemoryStream? armedBuffer;
    private long bytesWritten;
    private bool armed;
    private bool disposed;

    // Pre-Roll-Ringpuffer (zirkulär).
    private byte[] ringBuffer = [];
    private int ringWritePos;
    private bool ringFilled;

    private int deviceNumber;
    private bool preRollEnabled;
    private bool capturing;

    /// <summary>Normalisierter Pegel (0..1) pro eingehendem Audio-Block, für die Overlay-Anzeige.</summary>
    public event EventHandler<float>? AudioLevelChanged;

    /// <summary>Live-PCM-Block (16-bit mono, <see cref="SampleRate"/>) während scharfgeschalteter Aufnahme. Jeweils eine eigene Kopie.</summary>
    public event EventHandler<byte[]>? FrameAvailable;

    public static IReadOnlyList<AudioInputDeviceInfo> AvailableDevices()
    {
        var devices = new List<AudioInputDeviceInfo>();
        for (var i = 0; i < WaveInEvent.DeviceCount; i++)
        {
            var caps = WaveInEvent.GetCapabilities(i);
            devices.Add(new AudioInputDeviceInfo(i, caps.ProductName, caps.Channels));
        }

        return devices;
    }

    /// <summary>
    /// Stellt Gerät und Pre-Roll-Einstellungen ein und startet (bei aktivem Pre-Roll)
    /// die durchgehende Aufnahme. Beim App-Start sowie nach Einstellungsänderungen aufrufen.
    /// </summary>
    public void Configure(int device, bool enablePreRoll, int preRollMilliseconds)
    {
        lock (sync)
        {
            if (disposed)
            {
                return;
            }

            var ringSize = Math.Max(0, ByteRate * preRollMilliseconds / 1000);
            deviceNumber = device;
            preRollEnabled = enablePreRoll;
            ringBuffer = enablePreRoll ? new byte[ringSize] : [];
            ResetRing();

            StopCaptureCore();
            if (enablePreRoll)
            {
                StartCaptureCore();
            }
        }
    }

    /// <summary>Schaltet scharf: schreibt den Pre-Roll-Puffer und nimmt live weiter auf. Verzögerungsfrei.</summary>
    public void Arm()
    {
        byte[]? preRoll = null;
        lock (sync)
        {
            if (disposed)
            {
                throw new InvalidOperationException("Aufnahme nicht verfügbar.");
            }

            if (WaveInEvent.DeviceCount == 0)
            {
                throw new InvalidOperationException("Windows meldet kein Aufnahmegerät. Bitte Mikrofon anschließen oder in den Windows-Datenschutzeinstellungen freigeben.");
            }

            if (deviceNumber < 0 || deviceNumber >= WaveInEvent.DeviceCount)
            {
                throw new InvalidOperationException($"Das ausgewählte Mikrofon ist nicht verfügbar: {deviceNumber}. Bitte in den Einstellungen ein anderes Mikrofon wählen.");
            }

            armedBuffer = new MemoryStream();
            bytesWritten = 0;

            if (!capturing)
            {
                // Pre-Roll deaktiviert oder Aufnahme nicht aktiv: Gerät jetzt öffnen.
                StartCaptureCore();
            }
            else if (preRollEnabled)
            {
                preRoll = CollectPreRoll();
            }

            armed = true;
            AppLog.Write($"AudioRecorder.Arm device={deviceNumber} preRoll={preRollEnabled} preRollBytes={preRoll?.Length ?? 0}");
        }

        // Außerhalb des Locks, damit der Consumer (Realtime-Session) nicht unter der Sperre arbeitet.
        if (preRoll is not null)
        {
            FrameAvailable?.Invoke(this, preRoll);
        }
    }

    public RecordingResult Stop()
    {
        lock (sync)
        {
            if (!armed || armedBuffer is null)
            {
                throw new InvalidOperationException("Es läuft keine Aufnahme.");
            }

            armed = false;
            var pcm = armedBuffer.ToArray();
            armedBuffer.Dispose();
            armedBuffer = null;

            // Ohne Pre-Roll wird das Gerät nur während der Aufnahme offen gehalten.
            if (!preRollEnabled)
            {
                StopCaptureCore();
            }

            var duration = TimeSpan.FromSeconds((double)bytesWritten / ByteRate);
            AppLog.Write($"AudioRecorder.Stop durationMs={duration.TotalMilliseconds:N0} bytes={pcm.Length}");
            return new RecordingResult(pcm, duration);
        }
    }

    /// <summary>Bricht eine scharfgeschaltete Aufnahme ab und verwirft den Puffer. Pre-Roll läuft weiter.</summary>
    public void Cancel()
    {
        lock (sync)
        {
            armed = false;
            armedBuffer?.Dispose();
            armedBuffer = null;

            if (!preRollEnabled)
            {
                StopCaptureCore();
            }
        }
    }

    public void Dispose()
    {
        lock (sync)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            armed = false;
            armedBuffer?.Dispose();
            armedBuffer = null;
            StopCaptureCore();
        }
    }

    // --- intern (immer unter sync aufgerufen, außer in den FrameAvailable/Level-Callbacks) ---

    private void StartCaptureCore()
    {
        if (capturing || WaveInEvent.DeviceCount == 0)
        {
            return;
        }

        if (deviceNumber < 0 || deviceNumber >= WaveInEvent.DeviceCount)
        {
            return;
        }

        capture = new WaveInEvent
        {
            DeviceNumber = deviceNumber,
            WaveFormat = Format
        };
        capture.DataAvailable += OnDataAvailable;
        capture.StartRecording();
        capturing = true;
        AppLog.Write($"AudioRecorder capture started deviceCount={WaveInEvent.DeviceCount} device={deviceNumber} preRoll={preRollEnabled}");
    }

    private void StopCaptureCore()
    {
        if (capture is not null)
        {
            capture.DataAvailable -= OnDataAvailable;
            try { capture.StopRecording(); } catch { }
            capture.Dispose();
            capture = null;
        }

        capturing = false;
        ResetRing();
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        byte[]? frame = null;
        lock (sync)
        {
            if (disposed)
            {
                return;
            }

            if (armed)
            {
                armedBuffer?.Write(e.Buffer, 0, e.BytesRecorded);
                bytesWritten += e.BytesRecorded;
                frame = new byte[e.BytesRecorded];
                Array.Copy(e.Buffer, frame, e.BytesRecorded);
            }
            else if (preRollEnabled && ringBuffer.Length > 0)
            {
                PushToRing(e.Buffer, e.BytesRecorded);
            }
        }

        if (frame is not null)
        {
            FrameAvailable?.Invoke(this, frame);
        }

        RaiseLevel(e.Buffer, e.BytesRecorded);
    }

    private void PushToRing(byte[] buffer, int count)
    {
        var size = ringBuffer.Length;
        // Nur die letzten 'size' Bytes sind relevant, wenn der Block größer als der Ring ist.
        var offset = 0;
        if (count > size)
        {
            offset = count - size;
            count = size;
        }

        var firstChunk = Math.Min(count, size - ringWritePos);
        Array.Copy(buffer, offset, ringBuffer, ringWritePos, firstChunk);
        var remaining = count - firstChunk;
        if (remaining > 0)
        {
            Array.Copy(buffer, offset + firstChunk, ringBuffer, 0, remaining);
            ringWritePos = remaining;
            ringFilled = true;
        }
        else
        {
            ringWritePos += firstChunk;
            if (ringWritePos == size)
            {
                ringWritePos = 0;
                ringFilled = true;
            }
        }
    }

    /// <summary>Liefert den Pre-Roll-Inhalt in chronologischer Reihenfolge und schreibt ihn in den Aufnahmepuffer.</summary>
    private byte[] CollectPreRoll()
    {
        if (armedBuffer is null || ringBuffer.Length == 0)
        {
            return [];
        }

        byte[] preRoll;
        if (ringFilled)
        {
            preRoll = new byte[ringBuffer.Length];
            var tail = ringBuffer.Length - ringWritePos;
            Array.Copy(ringBuffer, ringWritePos, preRoll, 0, tail);
            Array.Copy(ringBuffer, 0, preRoll, tail, ringWritePos);
        }
        else
        {
            preRoll = new byte[ringWritePos];
            Array.Copy(ringBuffer, 0, preRoll, 0, ringWritePos);
        }

        armedBuffer.Write(preRoll, 0, preRoll.Length);
        bytesWritten += preRoll.Length;
        return preRoll;
    }

    private void ResetRing()
    {
        ringWritePos = 0;
        ringFilled = false;
    }

    private void RaiseLevel(byte[] buffer, int count)
    {
        var handler = AudioLevelChanged;
        if (handler is null || count < 2)
        {
            return;
        }

        long sumSquares = 0;
        var samples = count / 2;
        for (var i = 0; i + 1 < count; i += 2)
        {
            short sample = (short)(buffer[i] | (buffer[i + 1] << 8));
            sumSquares += (long)sample * sample;
        }

        var rms = Math.Sqrt((double)sumSquares / samples);
        var level = (float)Math.Min(1.0, rms / 32768.0 * 4.0);
        handler(this, level);
    }
}
