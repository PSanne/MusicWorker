using MusicWorker.Core.Synthesis;
using NAudio.Wave;

namespace MusicWorker.App.Audio;

/// <summary>
/// Spielt Noten sofort hörbar (Bildschirmtastatur, MIDI-Keyboard, Vorhören im Noten-Editor) – über eine
/// eigene, ständig laufende Ausgabe mit kurzer Verzögerung. Fehler (z. B. kein Ausgabegerät) werden
/// geschluckt: Vorhören ist eine Komfortfunktion und darf die Bearbeitung nie stören.
/// </summary>
internal sealed class LiveSynthService : IDisposable
{
    private const int SampleRate = 44100;

    private readonly EngineProvider _provider = new(SampleRate);
    private WaveOutEvent? _output;
    private InstrumentSpec? _instrument;
    private bool _failed;

    /// <summary>Wiedergabegerät (-1 = Windows-Standard).</summary>
    public int DeviceNumber { get; private set; } = -1;

    /// <summary>Instrument wechseln (SoundFonts werden im Hintergrund geladen).</summary>
    public async Task SetInstrumentAsync(InstrumentSpec? instrument)
    {
        if (instrument == _instrument)
        {
            return;
        }

        _instrument = instrument;
        if (instrument is null)
        {
            _provider.Swap(null);
            return;
        }

        try
        {
            var engine = await Task.Run(() => instrument.CreateEngine(SampleRate));
            if (instrument == _instrument)
            {
                _provider.Swap(engine);
            }
        }
        catch (Exception)
        {
            _provider.Swap(new BuiltInSynth(instrument.BuiltIn, SampleRate));
        }
    }

    public void NoteOn(int pitch, int velocity)
    {
        if (EnsureStarted())
        {
            _provider.Engine?.NoteOn(pitch, velocity);
        }
    }

    public void NoteOff(int pitch) => _provider.Engine?.NoteOff(pitch);

    public void AllNotesOff() => _provider.Engine?.AllNotesOff();

    /// <summary>Anderes Wiedergabegerät: Ausgabe beim nächsten Ton neu öffnen.</summary>
    public void SetDevice(int deviceNumber)
    {
        DeviceNumber = deviceNumber;
        CloseOutput();
        _failed = false;
    }

    public void Dispose() => CloseOutput();

    private bool EnsureStarted()
    {
        if (_output is not null)
        {
            return true;
        }

        if (_failed)
        {
            return false;
        }

        try
        {
            _output = new WaveOutEvent { DeviceNumber = DeviceNumber, DesiredLatency = 80, NumberOfBuffers = 3 };
            _output.Init(_provider);
            _output.Play();
            return true;
        }
        catch (Exception)
        {
            _failed = true; // kein Gerät – stumm weiterarbeiten
            CloseOutput();
            return false;
        }
    }

    private void CloseOutput()
    {
        try
        {
            _output?.Stop();
            _output?.Dispose();
        }
        catch (Exception)
        {
            // beim Schließen ist ein Fehler ohne Bedeutung
        }

        _output = null;
    }

    /// <summary>Liefert den Klang des aktuellen Instruments an die Soundkarte (Stille, wenn keins gesetzt ist).</summary>
    private sealed class EngineProvider(int sampleRate) : ISampleProvider
    {
        private IInstrumentEngine? _engine;

        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 2);

        public IInstrumentEngine? Engine => Volatile.Read(ref _engine);

        public void Swap(IInstrumentEngine? engine) => Interlocked.Exchange(ref _engine, engine)?.AllNotesOff();

        public int Read(float[] buffer, int offset, int count)
        {
            var span = buffer.AsSpan(offset, count);
            if (Engine is { } engine)
            {
                engine.Render(span);
            }
            else
            {
                span.Clear();
            }

            return count;
        }
    }
}
