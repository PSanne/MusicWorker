using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusicWorker.App.Audio;
using MusicWorker.Core.Recording;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace MusicWorker.App.ViewModels;

/// <summary>Aufnahme mit geringer Latenz: Treiberwahl (WASAPI, ASIO), Puffergröße und Mithören.</summary>
public partial class MixerViewModel
{
    private readonly PlaybackService _idle = new(); // Ausgang zum Mithören, solange nichts abgespielt wird

    /// <summary>Treiber für Wiedergabe und Aufnahme im Mischpult.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAsio), nameof(CanChooseBuffer), nameof(EngineText))]
    private AudioDriverChoice? _selectedAudioDriver;

    /// <summary>Puffergröße in Millisekunden (kleiner = weniger Verzögerung, aber anfälliger für Aussetzer).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EngineText))]
    private int _bufferMs = 20;

    /// <summary>Den Eingang über Kopfhörer mithören (über den Fader der scharfgeschalteten Spur).</summary>
    [ObservableProperty]
    private bool _isInputMonitoringEnabled;

    [ObservableProperty]
    private IReadOnlyList<AudioDriverChoice> _audioDriverChoices = AudioDrivers.Available();

    public IReadOnlyList<int> BufferChoices { get; } = [5, 10, 20, 40, 80];

    public bool IsAsio => SelectedAudioDriver?.Kind == AudioDriverKind.Asio;

    /// <summary>Bei ASIO stellt der Treiber selbst den Puffer ein.</summary>
    public bool CanChooseBuffer => !IsAsio;

    /// <summary>Aktuelle Einstellung für Wiedergabe und Aufnahme.</summary>
    public AudioEngineSettings Engine => new(SelectedAudioDriver?.Kind ?? AudioDriverKind.Standard, SelectedAudioDriver?.AsioDriver, BufferMs);

    public string EngineText => Engine.Kind switch
    {
        AudioDriverKind.Standard => "Standard-Treiber (ca. 60 ms Verzögerung)",
        AudioDriverKind.Asio => "Puffer im ASIO-Treiber einstellen – die Verzögerung wird automatisch ausgeglichen",
        _ => $"ca. {AudioDrivers.EstimatedRoundTripMs(Engine):0} ms Verzögerung",
    };

    /// <summary>Treiber nach Namen wählen (aus den Einstellungen).</summary>
    public void SelectAudioDriver(string? name, int bufferMs)
    {
        _bufferMsRestoring = true;
        BufferMs = BufferChoices.Contains(bufferMs) ? bufferMs : 20;
        SelectedAudioDriver = AudioDriverChoices.FirstOrDefault(d => d.Name == name) ?? AudioDriverChoices[0];
        _bufferMsRestoring = false;
        ApplyEngine(adjustLatency: false);
    }

    private bool _bufferMsRestoring;

    [RelayCommand]
    private void RefreshAudioDrivers()
    {
        string? current = SelectedAudioDriver?.Name;
        AudioDriverChoices = AudioDrivers.Available();
        SelectedAudioDriver = AudioDriverChoices.FirstOrDefault(d => d.Name == current) ?? AudioDriverChoices[0];
    }

    /// <summary>Einstellungsfenster des ASIO-Treibers (Puffergröße, Eingänge).</summary>
    [RelayCommand]
    private void ShowAsioPanel()
    {
        if (SelectedAudioDriver is not { Kind: AudioDriverKind.Asio, AsioDriver: { } driver })
        {
            StatusText = "Die ASIO-Einstellungen gibt es nur, wenn oben ein ASIO-Treiber gewählt ist.";
            return;
        }

        StopPlayback();
        _idle.Stop();
        _recorder.Close();
        if (AudioDrivers.ShowAsioControlPanel(driver) is { } error)
        {
            StatusText = $"ASIO-Einstellungen ließen sich nicht öffnen: {error}";
        }

        UpdateInputMonitoring();
    }

    partial void OnSelectedAudioDriverChanged(AudioDriverChoice? value)
    {
        if (!_bufferMsRestoring)
        {
            ApplyEngine(adjustLatency: true);
        }
    }

    partial void OnBufferMsChanged(int value)
    {
        if (!_bufferMsRestoring)
        {
            ApplyEngine(adjustLatency: true);
        }
    }

    partial void OnIsInputMonitoringEnabledChanged(bool value)
    {
        UpdateInputMonitoring();
        if (value)
        {
            StatusText = "Mithören ist an: Bitte Kopfhörer benutzen, sonst pfeift es (Rückkopplung). Die Lautstärke regelt der Fader der scharfgeschalteten Spur.";
        }
    }

    /// <summary>Neue Treiber-Einstellung übernehmen: Ein- und Ausgang werden neu geöffnet.</summary>
    private void ApplyEngine(bool adjustLatency)
    {
        StopPlayback();
        _idle.Stop();
        _recorder.Close();
        var engine = Engine;
        _playback.Engine = engine;
        _idle.Engine = engine;
        _recorder.Engine = engine;
        RefreshInputDevices();
        if (adjustLatency)
        {
            RecordingLatencyMs = AudioDrivers.EstimatedRoundTripMs(engine);
            OnPropertyChanged(nameof(RecordingLatencyMs));
            StatusText = engine.Kind == AudioDriverKind.Standard
                ? "Standard-Treiber: funktioniert mit jeder Soundkarte, hat aber etwas mehr Verzögerung."
                : $"{SelectedAudioDriver?.Name}: {EngineText}. Der Latenzausgleich für Aufnahmen wurde auf {RecordingLatencyMs:0} ms gesetzt.";
        }

        UpdateInputMonitoring();
    }

    /// <summary>Mischt den mitgehörten Eingang in einen Ausgabestrom.</summary>
    private ISampleProvider WithMonitor(ISampleProvider source) =>
        new MonitorMixSampleProvider(source, _recorder.Monitor, MonitorGain);

    /// <summary>Mithör-Lautstärke: Fader der ersten scharfgeschalteten Audiospur (stumm = 0).</summary>
    private float MonitorGain()
    {
        if (!IsInputMonitoringEnabled)
        {
            return 0;
        }

        var track = Tracks.FirstOrDefault(t => t.IsArmed && !t.IsNoteTrack);
        return track is null || track.Mute ? 0 : (float)Math.Pow(10, track.VolumeDb / 20);
    }

    /// <summary>
    /// Ausgang zum Mithören starten oder beenden, solange nichts abgespielt wird. Bei ASIO läuft er immer, sobald
    /// eine Spur scharfgeschaltet ist – sonst kämen keine Eingangsdaten (Pegelanzeige).
    /// </summary>
    private void UpdateIdleOutput()
    {
        bool armed = Tracks.Any(t => t.IsArmed && !t.IsNoteTrack);
        _recorder.MonitorEnabled = IsInputMonitoringEnabled && armed;
        bool needed = armed && _recorder.IsOpen && (IsInputMonitoringEnabled || _recorder.IsExternal) && _playback.State == PlaybackState.Stopped;
        if (!needed)
        {
            _idle.Stop();
            return;
        }

        if (_idle.State != PlaybackState.Stopped)
        {
            return;
        }

        try
        {
            _idle.DeviceNumber = _playback.DeviceNumber;
            _idle.AsioInput = (SelectedInputDevice?.Number ?? 0, SelectedInputDevice?.Channels ?? 2);
            var silence = new SilenceProvider(WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, 2)).ToSampleProvider();
            _idle.PlayStream(WithMonitor(silence), 0);
            if (_idle.Notice is { } notice)
            {
                StatusText = notice;
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Mithören ließ sich nicht starten: {ex.Message}";
        }
    }

    /// <summary>Vor dem Abspielen: den Leerlauf-Ausgang freigeben (bei ASIO und WASAPI exklusiv gibt es nur einen).</summary>
    private void PrepareOutputForPlayback()
    {
        _idle.Stop();
        _playback.AsioInput = (SelectedInputDevice?.Number ?? 0, SelectedInputDevice?.Channels ?? 2);
        _recorder.Monitor.Clear();
    }
}

/// <summary>Mischt den Mithör-Puffer in einen Datenstrom (Lautstärke wird bei jedem Block neu abgefragt).</summary>
internal sealed class MonitorMixSampleProvider(ISampleProvider source, MonitorRing ring, Func<float> gain) : ISampleProvider
{
    public WaveFormat WaveFormat => source.WaveFormat;

    public int Read(float[] buffer, int offset, int count)
    {
        int read = source.Read(buffer, offset, count);
        float g = gain();
        if (g > 0 && WaveFormat.Channels == 2)
        {
            ring.MixInto(buffer.AsSpan(offset, read), g);
        }

        return read;
    }
}
