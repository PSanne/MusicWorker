using MusicWorker.Core.Audio;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace MusicWorker.App.Audio;

/// <summary>Spielt einen Bereich eines <see cref="AudioBuffer"/> über die Standard-Soundkarte ab.</summary>
public sealed class PlaybackService : IDisposable
{
    private IWavePlayer? _output;
    private CountingSampleProvider? _counter; // nur für ASIO (kennt keine Positionsabfrage)
    private int _streamRate = 44100;
    private float[] _asioInput = [];
    private AudioBufferSampleProvider? _provider;
    private int _startFrame;
    private int _endFrame;
    private int _loopStartFrame;
    private bool _loop;

    // Für die Positionsberechnung nach einem Sprung: Wo wurde hingesprungen, und wie viel war da schon gespielt?
    private int _seekFrame;
    private long _playedAtSeek;

    /// <summary>
    /// Die Wiedergabe ist zu Ende (Bereichsende erreicht oder Fehler).
    /// Wird im UI-Thread ausgelöst, weil NAudio den SynchronizationContext des Aufrufers nutzt.
    /// </summary>
    public event EventHandler<StoppedEventArgs>? PlaybackEnded;

    public PlaybackState State => _output?.PlaybackState ?? PlaybackState.Stopped;

    /// <summary>Soundkarte (-1 = Windows-Standardgerät). Gilt ab der nächsten Wiedergabe.</summary>
    public int DeviceNumber { get; set; } = -1;

    /// <summary>Treiber für <see cref="PlayStream"/> (Mischpult). Der Editor spielt immer über den Standard-Treiber.</summary>
    public AudioEngineSettings Engine { get; set; } = AudioEngineSettings.Default;

    /// <summary>Bei ASIO: erster Eingang (0-basiert) und Anzahl der Eingänge, die mit aufgenommen werden.</summary>
    public (int Offset, int Channels) AsioInput { get; set; } = (0, 2);

    /// <summary>Bei ASIO: neue Eingangs-Samples (verschachtelt, Kanalzahl) – im Treiber-Thread, der Empfänger muss kopieren.</summary>
    public event Action<float[], int>? InputAvailable;

    /// <summary>Hinweis vom letzten Start (z. B. Rückfall auf einen anderen Treiber), sonst null.</summary>
    public string? Notice { get; private set; }

    /// <summary>Tatsächlich benutzter Treiber der laufenden Wiedergabe.</summary>
    public AudioDriverKind ActiveDriver { get; private set; }

    /// <summary>Ausgangsverzögerung der laufenden Wiedergabe in Frames (geschätzt).</summary>
    public int OutputLatencyFrames { get; private set; }

    /// <summary>Seit dem Start abgespielte Frames (für laufend berechnete Datenströme).</summary>
    public long PlayedFrames => OutputFrames();

    /// <summary>Schleife während der Wiedergabe ein-/ausschalten.</summary>
    public bool Loop
    {
        get => _loop;
        set
        {
            _loop = value;
            if (_provider is not null)
            {
                _provider.Loop = value;
                _loop = _provider.Loop;
            }
        }
    }

    /// <summary>Frame, das gerade tatsächlich zu hören ist (berücksichtigt die Puffer-Latenz).</summary>
    public int CurrentFrame
    {
        get
        {
            if (_output is null)
            {
                return _startFrame;
            }

            long played = OutputFrames() - _playedAtSeek;
            long firstPass = _endFrame - _seekFrame;          // vom Start/Sprung bis zum Ende
            long cycle = _endFrame - _loopStartFrame;         // danach: Schleife ab dem Rücksprungpunkt
            if (_loop && cycle > 0 && played >= firstPass)
            {
                return _loopStartFrame + (int)((played - firstPass) % cycle);
            }

            return (int)Math.Min(_seekFrame + played, _endFrame);
        }
    }

    /// <summary>
    /// Startet die Wiedergabe von <paramref name="startFrame"/> bis <paramref name="endFrame"/>.
    /// </summary>
    /// <param name="loop">Bereich endlos wiederholen (z. B. zum Vorhören von Effekten).</param>
    /// <param name="effects">Optionale Live-Effekte, die zwischen Puffer und Soundkarte geschaltet werden.</param>
    public void Play(
        AudioBuffer buffer,
        int startFrame,
        int endFrame,
        bool loop = false,
        Func<ISampleProvider, ISampleProvider>? effects = null,
        int? loopStartFrame = null)
    {
        Stop();

        var bufferProvider = new AudioBufferSampleProvider(buffer, startFrame, endFrame, loop, loopStartFrame);
        ISampleProvider provider = bufferProvider;
        if (effects is not null)
        {
            provider = effects(provider);
        }

        var output = new WaveOutEvent { DesiredLatency = 120, DeviceNumber = DeviceNumber };
        output.PlaybackStopped += OnPlaybackStopped;
        output.Init(provider);

        _streamRate = provider.WaveFormat.SampleRate;
        _counter = null;
        ActiveDriver = AudioDriverKind.Standard;
        _output = output;
        _provider = bufferProvider;
        _startFrame = startFrame;
        _endFrame = bufferProvider.EndFrame;
        _loopStartFrame = bufferProvider.LoopStartFrame;
        _loop = bufferProvider.Loop;
        _seekFrame = startFrame;
        _playedAtSeek = 0;
        output.Play();
    }

    /// <summary>
    /// Spielt einen laufend berechneten Datenstrom ab (z. B. das Mischpult). Die Position zählt ab
    /// <paramref name="startFrame"/>; nach einem Sprung im Datenstrom <see cref="MarkSeek"/> aufrufen.
    /// </summary>
    public void PlayStream(ISampleProvider provider, int startFrame)
    {
        Stop();

        _streamRate = provider.WaveFormat.SampleRate;
        var output = CreateStreamOutput(provider);
        output.PlaybackStopped += OnPlaybackStopped;

        _output = output;
        _provider = null;
        _startFrame = startFrame;
        _endFrame = int.MaxValue;
        _loop = false;
        _seekFrame = startFrame;
        _playedAtSeek = 0;
        output.Play();
    }

    /// <summary>Ausgabe für den gewählten Treiber – bei Problemen mit Rückfall auf den nächst einfacheren.</summary>
    private IWavePlayer CreateStreamOutput(ISampleProvider provider)
    {
        Notice = null;
        _counter = null;
        var engine = Engine;
        int bufferMs = Math.Clamp(engine.BufferMs, 3, 200);
        if (engine.Kind == AudioDriverKind.Asio && engine.AsioDriver is { } driver)
        {
            AsioOut? asio = null;
            try
            {
                asio = new AsioOut(driver);
                if (!asio.IsSampleRateSupported(_streamRate))
                {
                    throw new NotSupportedException($"Der ASIO-Treiber unterstützt {_streamRate} Hz nicht – bitte im Projekt eine andere Abtastrate verwenden oder einen anderen Treiber wählen.");
                }

                _counter = new CountingSampleProvider(provider);
                var (offset, channels) = AsioInput;
                asio.InputChannelOffset = Math.Clamp(offset, 0, Math.Max(0, asio.DriverInputChannelCount - 1));
                int record = Math.Clamp(channels, 0, Math.Max(0, asio.DriverInputChannelCount - asio.InputChannelOffset));
                asio.InitRecordAndPlayback(new SampleToWaveProvider(_counter), record, 0);
                if (record > 0)
                {
                    int inputChannels = record;
                    asio.AudioAvailable += (_, e) => OnAsioAudio(e, inputChannels);
                }

                OutputLatencyFrames = asio.PlaybackLatency;
                ActiveDriver = AudioDriverKind.Asio;
                return asio;
            }
            catch (Exception ex)
            {
                asio?.Dispose();
                _counter = null;
                Notice = $"ASIO ließ sich nicht starten ({ex.Message}) – es wird WASAPI verwendet.";
                engine = engine with { Kind = AudioDriverKind.Wasapi };
            }
        }

        if (engine.Kind is AudioDriverKind.Wasapi or AudioDriverKind.WasapiExclusive)
        {
            bool exclusive = engine.Kind == AudioDriverKind.WasapiExclusive;
            try
            {
                return CreateWasapi(provider, exclusive, bufferMs);
            }
            catch (Exception ex) when (exclusive)
            {
                Notice = $"WASAPI exklusiv ist nicht möglich ({ex.Message}) – es wird WASAPI geteilt verwendet.";
                try
                {
                    return CreateWasapi(provider, exclusive: false, bufferMs);
                }
                catch (Exception)
                {
                    // weiter mit Standard
                }
            }
            catch (Exception ex)
            {
                Notice = $"WASAPI ließ sich nicht starten ({ex.Message}) – es wird der Standard-Treiber verwendet.";
            }
        }

        var standard = new WaveOutEvent { DesiredLatency = 120, DeviceNumber = DeviceNumber };
        standard.Init(provider);
        OutputLatencyFrames = _streamRate * 120 / 1000;
        ActiveDriver = AudioDriverKind.Standard;
        return standard;
    }

    private WasapiOut CreateWasapi(ISampleProvider provider, bool exclusive, int bufferMs)
    {
        var device = AudioDrivers.RenderDevice(DeviceNumber);
        var output = new WasapiOut(device, exclusive ? AudioClientShareMode.Exclusive : AudioClientShareMode.Shared, true, bufferMs);
        try
        {
            output.Init(provider);
        }
        catch
        {
            output.Dispose();
            throw;
        }

        OutputLatencyFrames = _streamRate * bufferMs / 1000;
        ActiveDriver = exclusive ? AudioDriverKind.WasapiExclusive : AudioDriverKind.Wasapi;
        return output;
    }

    private void OnAsioAudio(AsioAudioAvailableEventArgs e, int channels)
    {
        int needed = e.SamplesPerBuffer * channels;
        if (_asioInput.Length != needed)
        {
            _asioInput = new float[needed];
        }

        e.GetAsInterleavedSamples(_asioInput);
        InputAvailable?.Invoke(_asioInput, channels);
    }

    /// <summary>Abgespielte Frames in der Abtastrate des Datenstroms.</summary>
    private long OutputFrames()
    {
        switch (_output)
        {
            case null:
                return 0;
            case IWavePosition position:
                var format = position.OutputWaveFormat;
                long frames = position.GetPosition() / Math.Max(1, format.BlockAlign);
                return format.SampleRate == _streamRate || format.SampleRate <= 0 ? frames : frames * _streamRate / format.SampleRate;
            default:
                return Math.Max(0, (_counter?.Frames ?? 0) - OutputLatencyFrames);
        }
    }

    /// <summary>Teilt mit, dass der Datenstrom an <paramref name="frame"/> gesprungen ist (für die Positionsanzeige).</summary>
    public void MarkSeek(int frame)
    {
        if (_output is null)
        {
            return;
        }

        _seekFrame = frame;
        _playedAtSeek = OutputFrames();
    }

    /// <summary>
    /// Springt während Wiedergabe oder Pause an eine andere Stelle, ohne neu zu starten.
    /// (Was schon an die Soundkarte übergeben wurde – etwa 0,1 s –, ist noch kurz zu hören.)
    /// </summary>
    public void Seek(int frame)
    {
        if (_output is null || _provider is null)
        {
            return;
        }

        _provider.Seek(frame);
        _seekFrame = Math.Clamp(frame, 0, _provider.EndFrame);
        _endFrame = _provider.EndFrame;
        _loopStartFrame = _provider.LoopStartFrame;
        if (_seekFrame < _startFrame)
        {
            _startFrame = 0;
        }

        _playedAtSeek = OutputFrames();
    }

    public void Pause() => _output?.Pause();

    public void Resume() => _output?.Play();

    public void Stop()
    {
        var output = _output;
        if (output is null)
        {
            return;
        }

        _output = null;
        _provider = null;
        output.PlaybackStopped -= OnPlaybackStopped;
        output.Stop();
        output.Dispose();
    }

    public void Dispose() => Stop();

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        if (sender is not IWavePlayer output || !ReferenceEquals(output, _output))
        {
            return;
        }

        _output = null;
        _provider = null;
        output.PlaybackStopped -= OnPlaybackStopped;
        output.Dispose();
        PlaybackEnded?.Invoke(this, e);
    }
}

/// <summary>Zählt, wie viele Frames schon ausgelesen wurden (Position für Treiber ohne eigene Positionsabfrage).</summary>
internal sealed class CountingSampleProvider(ISampleProvider source) : ISampleProvider
{
    private long _samples;

    public WaveFormat WaveFormat => source.WaveFormat;

    public long Frames => Interlocked.Read(ref _samples) / Math.Max(1, WaveFormat.Channels);

    public int Read(float[] buffer, int offset, int count)
    {
        int read = source.Read(buffer, offset, count);
        Interlocked.Add(ref _samples, read);
        return read;
    }
}
