using NAudio.Midi;

namespace MusicWorker.App.Audio;

/// <summary>Empfängt Noten von einem angeschlossenen MIDI-Keyboard.</summary>
internal sealed class MidiInputService : IDisposable
{
    private MidiIn? _input;

    /// <summary>Taste gedrückt: Tonhöhe, Anschlag (kommt aus dem MIDI-Thread).</summary>
    public event Action<int, int>? NoteOn;

    /// <summary>Taste losgelassen: Tonhöhe (kommt aus dem MIDI-Thread).</summary>
    public event Action<int>? NoteOff;

    /// <summary>Namen der angeschlossenen MIDI-Eingänge.</summary>
    public static IReadOnlyList<string> Devices()
    {
        try
        {
            return Enumerable.Range(0, MidiIn.NumberOfDevices).Select(i => MidiIn.DeviceInfo(i).ProductName).ToArray();
        }
        catch (Exception)
        {
            return [];
        }
    }

    /// <summary>Öffnet einen Eingang (-1 = keiner). Liefert eine Fehlermeldung oder null.</summary>
    public string? Open(int index)
    {
        Close();
        if (index < 0)
        {
            return null;
        }

        try
        {
            _input = new MidiIn(index);
            _input.MessageReceived += OnMessage;
            _input.Start();
            return null;
        }
        catch (Exception ex)
        {
            Close();
            return ex.Message;
        }
    }

    public void Dispose() => Close();

    private void OnMessage(object? sender, MidiInMessageEventArgs e)
    {
        switch (e.MidiEvent)
        {
            case NoteOnEvent on when on.CommandCode == MidiCommandCode.NoteOn && on.Velocity > 0:
                NoteOn?.Invoke(on.NoteNumber, on.Velocity);
                break;
            case NoteEvent off when off.CommandCode == MidiCommandCode.NoteOff || (off.CommandCode == MidiCommandCode.NoteOn && off.Velocity == 0):
                NoteOff?.Invoke(off.NoteNumber);
                break;
        }
    }

    private void Close()
    {
        if (_input is null)
        {
            return;
        }

        try
        {
            _input.MessageReceived -= OnMessage;
            _input.Stop();
            _input.Dispose();
        }
        catch (Exception)
        {
            // Gerät eventuell schon getrennt
        }

        _input = null;
    }
}
