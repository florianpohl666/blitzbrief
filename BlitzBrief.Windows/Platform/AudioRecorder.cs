using System.IO;
using NAudio.Wave;

namespace BlitzBrief.Windows.Platform;

public sealed record RecordingResult(string Path, TimeSpan Duration);
public sealed record AudioInputDeviceInfo(int Index, string Name, int Channels);

/// <summary>
/// Nimmt das Mikrofon durchgehend in einen Ringpuffer auf (Pre-Roll), damit das
/// Scharfschalten beim Hotkey verzögerungsfrei ist und die ~300 ms vor dem
/// Tastendruck nicht verloren gehen. Ohne Pre-Roll wird das Gerät erst beim
/// Scharfschalten geöffnet (altes Verhalten).
/// </summary>
public sealed class AudioRecorder : IDisposable
{
    private static readonly WaveFormat Format = new(16000, 16, 1);
    private static int ByteRate => Format.AverageBytesPerSecond;

    private readonly object sync = new();

    private WaveInEvent? capture;
    private WaveFileWriter? writer;
    private string? currentPath;
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

            currentPath = Path.Combine(Path.GetTempPath(), $"BlitzBrief-{Guid.NewGuid():N}.wav");
            writer = new WaveFileWriter(currentPath, Format);
            bytesWritten = 0;

            if (!capturing)
            {
                // Pre-Roll deaktiviert oder Aufnahme nicht aktiv: Gerät jetzt öffnen.
                StartCaptureCore();
            }
            else if (preRollEnabled)
            {
                WritePreRoll();
            }

            armed = true;
            AppLog.Write($"AudioRecorder.Arm device={deviceNumber} preRoll={preRollEnabled} path={currentPath}");
        }
    }

    public RecordingResult Stop()
    {
        lock (sync)
        {
            if (!armed || currentPath is null)
            {
                throw new InvalidOperationException("Es läuft keine Aufnahme.");
            }

            var path = currentPath;
            armed = false;
            var bytes = bytesWritten;

            FinishWriter();
            currentPath = null;

            // Ohne Pre-Roll wird das Gerät nur während der Aufnahme offen gehalten.
            if (!preRollEnabled)
            {
                StopCaptureCore();
            }

            var duration = TimeSpan.FromSeconds((double)bytes / ByteRate);
            var length = File.Exists(path) ? new FileInfo(path).Length : 0;
            AppLog.Write($"AudioRecorder.Stop path={path} durationMs={duration.TotalMilliseconds:N0} bytes={length}");
            return new RecordingResult(path, duration);
        }
    }

    /// <summary>Bricht eine scharfgeschaltete Aufnahme ab und verwirft die Datei. Pre-Roll läuft weiter.</summary>
    public void Cancel()
    {
        string? path;
        lock (sync)
        {
            path = currentPath;
            currentPath = null;
            armed = false;
            FinishWriter();

            if (!preRollEnabled)
            {
                StopCaptureCore();
            }
        }

        if (path is not null && File.Exists(path))
        {
            try { File.Delete(path); } catch { }
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
            FinishWriter();
            StopCaptureCore();

            if (currentPath is not null && File.Exists(currentPath))
            {
                try { File.Delete(currentPath); } catch { }
            }

            currentPath = null;
        }
    }

    // --- intern (immer unter sync aufgerufen, außer im DataAvailable-Callback) ---

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
        lock (sync)
        {
            if (disposed)
            {
                return;
            }

            if (armed)
            {
                writer?.Write(e.Buffer, 0, e.BytesRecorded);
                bytesWritten += e.BytesRecorded;
            }
            else if (preRollEnabled && ringBuffer.Length > 0)
            {
                PushToRing(e.Buffer, e.BytesRecorded);
            }
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

    private void WritePreRoll()
    {
        if (writer is null || ringBuffer.Length == 0)
        {
            return;
        }

        if (ringFilled)
        {
            var tail = ringBuffer.Length - ringWritePos;
            writer.Write(ringBuffer, ringWritePos, tail);
            writer.Write(ringBuffer, 0, ringWritePos);
            bytesWritten += ringBuffer.Length;
        }
        else
        {
            writer.Write(ringBuffer, 0, ringWritePos);
            bytesWritten += ringWritePos;
        }
    }

    private void ResetRing()
    {
        ringWritePos = 0;
        ringFilled = false;
    }

    private void FinishWriter()
    {
        if (writer is not null)
        {
            try
            {
                writer.Flush();
                writer.Dispose();
            }
            catch { }
            writer = null;
        }
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
