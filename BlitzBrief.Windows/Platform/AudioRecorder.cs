using System.IO;
using NAudio.Wave;

namespace BlitzBrief.Windows.Platform;

public sealed record RecordingResult(string Path, TimeSpan Duration);
public sealed record AudioInputDeviceInfo(int Index, string Name, int Channels);

public sealed class AudioRecorder : IDisposable
{
    private WaveInEvent? capture;
    private WaveFileWriter? writer;
    private DateTimeOffset startedAt;
    private string? currentPath;

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

    public void Start(int deviceNumber)
    {
        Cancel();
        if (WaveInEvent.DeviceCount == 0)
        {
            throw new InvalidOperationException("Windows meldet kein Aufnahmegerät. Bitte Mikrofon anschließen oder in den Windows-Datenschutzeinstellungen freigeben.");
        }

        if (deviceNumber < 0 || deviceNumber >= WaveInEvent.DeviceCount)
        {
            throw new InvalidOperationException($"Das ausgewählte Mikrofon ist nicht verfügbar: {deviceNumber}. Bitte in den Einstellungen ein anderes Mikrofon wählen.");
        }

        currentPath = Path.Combine(Path.GetTempPath(), $"BlitzBrief-{Guid.NewGuid():N}.wav");
        capture = new WaveInEvent
        {
            DeviceNumber = deviceNumber,
            WaveFormat = new WaveFormat(16000, 16, 1)
        };
        AppLog.Write($"AudioRecorder.Start deviceCount={WaveInEvent.DeviceCount} device={deviceNumber} path={currentPath}");
        writer = new WaveFileWriter(currentPath, capture.WaveFormat);
        capture.DataAvailable += (_, e) => writer?.Write(e.Buffer, 0, e.BytesRecorded);
        capture.RecordingStopped += (_, _) =>
        {
            FinishRecordingResources();
        };
        startedAt = DateTimeOffset.UtcNow;
        capture.StartRecording();
        AppLog.Write("AudioRecorder.StartRecording returned.");
    }

    public RecordingResult Stop()
    {
        if (capture is null || currentPath is null)
        {
            throw new InvalidOperationException("Es läuft keine Aufnahme.");
        }

        var path = currentPath;
        var duration = DateTimeOffset.UtcNow - startedAt;
        capture.StopRecording();
        FinishRecordingResources();
        currentPath = null;
        var length = File.Exists(path) ? new FileInfo(path).Length : 0;
        AppLog.Write($"AudioRecorder.Stop path={path} durationMs={duration.TotalMilliseconds:N0} bytes={length}");
        return new RecordingResult(path, duration);
    }

    public void Cancel()
    {
        var path = currentPath;
        currentPath = null;
        try
        {
            capture?.StopRecording();
            FinishRecordingResources();
        }
        catch
        {
            capture?.Dispose();
            writer?.Dispose();
            capture = null;
            writer = null;
        }

        if (path is not null && File.Exists(path))
        {
            try { File.Delete(path); } catch { }
        }
    }

    public void Dispose() => Cancel();

    private void FinishRecordingResources()
    {
        writer?.Flush();
        writer?.Dispose();
        writer = null;
        capture?.Dispose();
        capture = null;
    }
}
