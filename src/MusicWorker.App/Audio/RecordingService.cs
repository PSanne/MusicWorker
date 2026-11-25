using MusicWorker.Core.Audio;
using MusicWorker.Core.Dsp;
using MusicWorker.Core.Recording;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace MusicWorker.App.Audio;

/// <summary>Ein Aufnahmegerät (Mikrofon, Line-Eingang …) bzw. bei ASIO ein Eingang des Audio-Interfaces.</summary>
public sealed record InputDevice(int Number, string Name, int Channels)
{
    public override string ToString() => Name;
}

/// <summary>
/// Nimmt vom gewählten Eingang auf. Der Eingang läuft schon, sobald eine Spur scharfgeschaltet ist
/// (für Pegelanzeige und Mithören); gespeichert wird erst ab <see cref="StartCapture"/>.
/// Treiber: Standard (MME), WASAPI (geteilt oder exklusiv) oder ASIO – bei ASIO kommen die Samples
/// über <see cref="Feed"/> aus der laufenden ASIO-Wiedergabe.
/// </summary>
internal sealed class RecordingService : IDisposable
{
    private static readonly Guid FloatSubFormat = new("00000003-0000-0010-8000-00aa00389b71");

    private readonly object _lock = new();
    private readonly List<float[]> _chunks = [];
    private IWaveIn? _input;
    private WaveFormat? _sourceFormat;
    private StreamingResampler? _resampler;
    private bool _external;
    private int _channels = 2;
    private int _sampleRate = 44100;
    private bool _capturing;
    private float _peak;

    /// <summary>Fehler beim Aufnehmen (z. B. Gerät entfernt).</summary>
    public event EventHandler<string>? Failed;

    public int DeviceNumber { get; set; }

    /// <summary>Treiber und Puffergröße (gilt beim nächsten Öffnen).</summary>
    public AudioEngineSettings Engine { get; set; } = AudioEngineSettings.Default;

    /// <summary>Puffer zum Mithören (Stereo, Projekt-Abtastrate).</summary>
    public MonitorRing Monitor { get; } = new(96000);

    /// <summary>Eingang in <see cref="Monitor"/> schreiben (zum Mithören).</summary>
    public bool MonitorEnabled { get; set; }

    /// <summary>Hinweis vom letzten Öffnen (z. B. Rückfall von „exklusiv“ auf „geteilt“), sonst null.</summary>
    public string? Notice { get; private set; }

    public bool IsOpen => _input is not null || _external;

    /// <summary>Läuft der Eingang über ASIO (Samples kommen per <see cref="Feed"/>)?</summary>
    public bool IsExternal => _external;

    public int Channels => _channels;

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
        channels = Math.Clamp(channels, 1, 2);
        if (IsOpen && sampleRate == _sampleRate && channels == _channels)
        {
            return null;
        }

        Close();
        _sampleRate = sampleRate;
        _channels = channels;
        Notice = null;
        Monitor.Clear();
        Monitor.MaxLatencyFrames = Math.Max(256, sampleRate * Math.Clamp(Engine.BufferMs, 3, 200) * 2 / 1000);
        try
        {
            switch (Engine.Kind)
            {
                case AudioDriverKind.Asio:
                    _external = true;
                    return null;
                case AudioDriverKind.Wasapi:
                case AudioDriverKind.WasapiExclusive:
                    try
                    {
                        StartWasapi(Engine.Kind == AudioDriverKind.WasapiExclusive);
                    }
                    catch (Exception ex) when (Engine.Kind == AudioDriverKind.WasapiExclusive)
                    {
                        CloseDevice();
                        Notice = $"Exklusiver Eingang nicht möglich ({ex.Message}) – es wird WASAPI geteilt verwendet.";
                        StartWasapi(exclusive: false);
                    }

                    return null;
                default:
                    var input = new WaveInEvent
                    {
                        DeviceNumber = DeviceNumber,
                        WaveFormat = new WaveFormat(sampleRate, 16, channels),
                        BufferMilliseconds = 25,
                        NumberOfBuffers = 4,
                    };
                    Start(input, input.WaveFormat);
                    return null;
            }
        }
        catch (Exception ex)
        {
            Close();
            return ex.Message;
        }
    }

    private void StartWasapi(bool exclusive)
    {
        var device = AudioDrivers.CaptureDevice(DeviceNumber);
        var capture = new WasapiCapture(device, true, Math.Clamp(Engine.BufferMs, 3, 200));
        if (exclusive)
        {
            capture.ShareMode = AudioClientShareMode.Exclusive;
            capture.WaveFormat = new WaveFormat(_sampleRate, 16, _channels);
        }

        Start(capture, capture.WaveFormat); // geteilt: Format des Geräts, wird unten umgerechnet
    }

    private void Start(IWaveIn input, WaveFormat format)
    {
        _sourceFormat = format;
        _resampler = format.SampleRate != _sampleRate ? new StreamingResampler(format.SampleRate, _sampleRate, format.Channels) : null;
        _input = input;
        input.DataAvailable += OnData;
        input.RecordingStopped += OnStopped;
        input.StartRecording();
    }

    /// <summary>Samples von außen (ASIO), verschachtelt mit <paramref name="channels"/> Kanälen in Projekt-Abtastrate.</summary>
    public void Feed(float[] samples, int channels)
    {
        if (!_external)
        {
            return;
        }

        var copy = SampleConversion.Remap((float[])samples.Clone(), channels, _channels);
        Process(copy);
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
            var samples = new float[Math.Max(_channels, total - (total % _channels))];
            int position = 0;
            foreach (var chunk in _chunks)
            {
                int n = Math.Min(chunk.Length, samples.Length - position);
                Array.Copy(chunk, 0, samples, position, n);
                position += n;
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

        _external = false;
        CloseDevice();
        Monitor.Clear();
    }

    private void CloseDevice()
    {
        var input = _input;
        _input = null;
        _resampler = null;
        if (input is null)
        {
            return;
        }

        try
        {
            input.DataAvailable -= OnData;
            input.RecordingStopped -= OnStopped;
            input.StopRecording();
            input.Dispose();
        }
        catch (Exception)
        {
            // Gerät eventuell schon entfernt
        }
    }

    public void Dispose() => Close();

    private void OnData(object? sender, WaveInEventArgs e)
    {
        if (_sourceFormat is not { } format || e.BytesRecorded <= 0)
        {
            return;
        }

        bool isFloat = format.Encoding == WaveFormatEncoding.IeeeFloat ||
            (format is WaveFormatExtensible extensible && extensible.SubFormat == FloatSubFormat);
        var samples = SampleConversion.ToFloat(e.Buffer.AsSpan(0, e.BytesRecorded), format.BitsPerSample, isFloat);
        if (_resampler is { } resampler)
        {
            samples = resampler.Process(samples);
        }

        Process(SampleConversion.Remap(samples, format.Channels, _channels));
    }

    private void Process(float[] samples)
    {
        float peak = 0;
        foreach (float v in samples)
        {
            peak = Math.Max(peak, Math.Abs(v));
        }

        float current;
        do
        {
            current = Volatile.Read(ref _peak);
        }
        while (peak > current && Interlocked.CompareExchange(ref _peak, peak, current) != current);

        if (MonitorEnabled)
        {
            Monitor.Write(SampleConversion.ToStereo(samples, _channels));
        }

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
