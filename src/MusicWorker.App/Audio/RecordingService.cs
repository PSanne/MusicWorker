using MusicWorker.Core.Audio;
using NAudio.Wave;

namespace MusicWorker.App.Audio;

/// <summary>Ein Aufnahmegerät (Mikrofon, Line-Eingang …).</summary>
public sealed record InputDevice(int Number, string Name, int Channels)
{
    public override string ToString() => Name;
}

/// <summary>
/// Nimmt vom gewählten Eingang auf. Der Eingang läuft schon, sobald eine Spur scharfgeschaltet ist
/// („Mithören“ nur für die Pegelanzeige); gespeichert wird erst ab <see cref="StartCapture"/>.
/// </summary>
internal sealed class RecordingService : IDisposable
{
    private readonly object _lock = new();
    private readonly List<float[]> _chunks = [];
    private WaveInEvent? _input;
    private int _channels = 2;
    private int _sampleRate = 44100;
    private bool _capturing;
    private float _peak;

    /// <summary>Fehler beim Aufnehmen (z. B. Gerät entfernt).</summary>
    public event EventHandler<string>? Failed;

    public int DeviceNumber { get; set; }

    public bool IsOpen => _input is not null;

    public bool IsCapturing
    {
        get
        {
            lock (_lock)
            {
                return _capturing;
            }
        }
    }

    /// <summary>Alle Aufnahmegeräte (leer, wenn keins vorhanden ist).</summary>
    public static IReadOnlyList<InputDevice> Devices()
    {
        try
        {
            return Enumerable.Range(0, WaveInEvent.DeviceCount)
                .Select(i => WaveInEvent.GetCapabilities(i))
                .Select((c, i) => new InputDevice(i, c.ProductName, Math.Clamp(c.Channels, 1, 2)))
                .ToArray();
        }
        catch (Exception)
        {
            return [];
        }
    }

    /// <summary>Spitzenpegel seit der letzten Abfrage (0 … 1).</summary>
    public float TakePeak() => Interlocked.Exchange(ref _peak, 0f);

    /// <summary>Eingang öffnen (zum Mithören/Pegel anzeigen). Liefert eine Fehlermeldung oder null.</summary>
    public string? Open(int sampleRate, int channels)
    {
        if (_input is not null && sampleRate == _sampleRate && channels == _channels)
        {
            return null;
        }

        Close();
        try
        {
            _sampleRate = sampleRate;
            _channels = Math.Clamp(channels, 1, 2);
            _input = new WaveInEvent
            {
                DeviceNumber = DeviceNumber,
                WaveFormat = new WaveFormat(sampleRate, 16, _channels),
                BufferMilliseconds = 25,
                NumberOfBuffers = 4,
            };
            _input.DataAvailable += OnData;
            _input.RecordingStopped += OnStopped;
            _input.StartRecording();
            return null;
        }
        catch (Exception ex)
        {
            Close();
            return ex.Message;
        }
    }

    /// <summary>Ab jetzt mitschneiden.</summary>
    public void StartCapture()
    {
        lock (_lock)
        {
            _chunks.Clear();
            _capturing = true;
        }
    }

    /// <summary>Mitschnitt beenden und als Audio liefern (der Eingang bleibt offen).</summary>
    public AudioBuffer StopCapture()
    {
        lock (_lock)
        {
            _capturing = false;
            int total = _chunks.Sum(c => c.Length);
            var samples = new float[Math.Max(_channels, total)];
            int position = 0;
            foreach (var chunk in _chunks)
            {
                chunk.CopyTo(samples, position);
                position += chunk.Length;
            }

            _chunks.Clear();
            return new AudioBuffer(samples, _sampleRate, _channels);
        }
    }

    public void Close()
    {
        lock (_lock)
        {
            _capturing = false;
        }

        if (_input is null)
        {
            return;
        }

        try
        {
            _input.DataAvailable -= OnData;
            _input.RecordingStopped -= OnStopped;
            _input.StopRecording();
            _input.Dispose();
        }
        catch (Exception)
        {
            // Gerät eventuell schon entfernt
        }

        _input = null;
    }

    public void Dispose() => Close();

    private void OnData(object? sender, WaveInEventArgs e)
    {
        int count = e.BytesRecorded / 2;
        var samples = new float[count];
        float peak = 0;
        for (int i = 0; i < count; i++)
        {
            float v = BitConverter.ToInt16(e.Buffer, i * 2) / 32768f;
            samples[i] = v;
            peak = Math.Max(peak, Math.Abs(v));
        }

        float current;
        do
        {
            current = Volatile.Read(ref _peak);
        }
        while (peak > current && Interlocked.CompareExchange(ref _peak, peak, current) != current);

        lock (_lock)
        {
            if (_capturing)
            {
                _chunks.Add(samples);
            }
        }
    }

    private void OnStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is { } ex)
        {
            Failed?.Invoke(this, ex.Message);
        }
    }
}
