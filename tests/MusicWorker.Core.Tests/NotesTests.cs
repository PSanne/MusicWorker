using MusicWorker.Core.Notes;
using MusicWorker.Core.Synthesis;

namespace MusicWorker.Core.Tests;

public class NotesTests
{
    private const int Q = MusicalTime.TicksPerQuarter;

    [Fact]
    public void Musical_time_converts_between_ticks_and_frames()
    {
        Assert.Equal(22050, MusicalTime.TicksToFrames(Q, 120, 44100)); // Viertel bei 120 BPM = 0,5 s
        Assert.Equal(Q, MusicalTime.FramesToTicks(22050, 120, 44100));
        Assert.Equal(Q / 2, MusicalTime.GridTicks(8));
        Assert.Equal(Q / 3, MusicalTime.GridTicks(8, triplet: true));
        Assert.Equal("C4", Note.PitchName(60));
        Assert.Equal("A#3", Note.PitchName(58));
        Assert.Equal(440, Note.Frequency(69), 6);
    }

    [Fact]
    public void Quantize_moves_starts_to_the_grid_with_strength()
    {
        Note[] notes = [new(60, 130, 100), new(62, 250, 100)];

        var full = NoteTools.Quantize(notes, Q / 4); // Sechzehntel = 120 Ticks
        Assert.Equal([120L, 240L], full.Select(n => n.Start));

        var half = NoteTools.Quantize(notes, Q / 4, strength: 0.5);
        Assert.Equal([125L, 245L], half.Select(n => n.Start));

        var ends = NoteTools.Quantize([new Note(60, 130, 100)], Q / 4, ends: true);
        Assert.Equal(120, ends[0].Start);
        Assert.Equal(120, ends[0].Length); // Ende 230 → 240
    }

    [Fact]
    public void Humanize_is_repeatable_and_stays_in_range()
    {
        var notes = Enumerable.Range(0, 50).Select(i => new Note(60, i * Q, Q / 2, 100)).ToArray();

        var a = NoteTools.Humanize(notes, 10, 15, seed: 7);
        var b = NoteTools.Humanize(notes, 10, 15, seed: 7);

        Assert.Equal(a, b);
        Assert.All(a.Zip(NoteTools.Sorted(notes)), p =>
        {
            Assert.InRange(p.First.Start - p.Second.Start, -10, 10);
            Assert.InRange(p.First.Velocity, 85, 115);
        });
        Assert.Contains(a, n => n.Velocity != 100);
    }

    [Fact]
    public void Transpose_and_lengths()
    {
        Note[] notes = [new(60, 0, 100), new(126, 0, 100)];

        Assert.Equal([67, 127], NoteTools.Transpose(notes, 7).Select(n => n.Pitch));
        Assert.Equal([50L, 50L], NoteTools.ScaleLengths(notes, 0.5).Select(n => n.Length));
        Assert.Equal([240L, 240L], NoteTools.SetLengths(notes, 240).Select(n => n.Length));
    }

    [Fact]
    public void Legato_extends_each_note_to_the_next()
    {
        Note[] notes = [new(60, 0, 50), new(62, 480, 50), new(64, 960, 50)];

        var legato = NoteTools.Legato(notes);

        Assert.Equal([480L, 480L, 50L], legato.Select(n => n.Length));
    }

    [Fact]
    public void Velocity_tools()
    {
        Note[] notes = [new(60, 0, 10, 100), new(62, 480, 10, 40), new(64, 960, 10, 80)];

        Assert.All(NoteTools.SetVelocity(notes, 90), n => Assert.Equal(90, n.Velocity));
        Assert.Equal([50, 20, 40], NoteTools.ScaleVelocity(notes, 0.5).Select(n => n.Velocity));
        Assert.Equal([20, 60, 100], NoteTools.VelocityRamp(notes, 20, 100).Select(n => n.Velocity));
    }

    [Fact]
    public void Voices_are_split_from_the_top_down()
    {
        // Akkord C-E-G plus Melodie-Note darüber
        Note[] chord = [new(48, 0, 480), new(52, 0, 480), new(55, 0, 480), new(72, 0, 480)];

        var two = NoteTools.SplitVoices(chord, 2);
        Assert.Equal(0, two.Single(n => n.Pitch == 72).Voice); // Melodie
        Assert.All(two.Where(n => n.Pitch < 72), n => Assert.Equal(1, n.Voice));

        var four = NoteTools.SplitVoices(chord, 4);
        Assert.Equal([3, 2, 1, 0], four.OrderBy(n => n.Pitch).Select(n => n.Voice));

        var hands = NoteTools.SplitAtPitch(chord, 60);
        Assert.Equal(3, hands.Count(n => n.Voice == 1));
    }

    [Fact]
    public void Split_and_merge_are_inverse()
    {
        Note[] notes = [new(60, 0, 300), new(62, 400, 300), new(64, 800, 100)];

        var (left, right) = NoteTools.Split(notes, 500);
        Assert.Equal(2, left.Count);
        Assert.Equal(100, left[1].Length);          // 400 … 500
        Assert.Equal([0L, 300L], right.Select(n => n.Start));

        var merged = NoteTools.Merge(left, right, 500);
        Assert.Equal(4, merged.Count);               // die geteilte Note besteht jetzt aus zwei Teilen
        Assert.Equal(NoteTools.EndOf(notes), NoteTools.EndOf(merged));
    }

    [Fact]
    public void Midi_file_round_trip_keeps_notes_tempo_and_instruments()
    {
        var song = new MidiSong(96, 3,
        [
            new MidiTrackData("Klavier", 0, 0, [new Note(60, 0, 480, 90), new Note(64, 480, 240, 70), new Note(67, 480, 240, 70)]),
            new MidiTrackData("Drums", 9, 0, [new Note(36, 0, 60, 120), new Note(38, 480, 60, 110)]),
        ]);

        var read = MidiFile.Read(MidiFile.Write(song));

        Assert.Equal(96, read.Bpm, 1);
        Assert.Equal(3, read.BeatsPerBar);
        Assert.Equal(2, read.Tracks.Count);
        Assert.Equal("Klavier", read.Tracks[0].Name);
        Assert.Equal(song.Tracks[0].Notes, read.Tracks[0].Notes);
        Assert.True(read.Tracks[1].IsDrums);
        Assert.Equal(song.Tracks[1].Notes, read.Tracks[1].Notes);
    }

    [Fact]
    public void Midi_reader_handles_running_status_note_on_zero_and_other_resolutions()
    {
        // Format 0, 96 Ticks pro Viertel, laufender Status, Note-An mit Anschlag 0 als Note-Aus
        byte[] data =
        [
            .."MThd"u8, 0, 0, 0, 6, 0, 0, 0, 1, 0, 96,
            .."MTrk"u8, 0, 0, 0, 18,
            0x00, 0x90, 60, 100,   // C an
            0x60, 60, 0,           // 96 Ticks später: C aus (laufender Status, Anschlag 0)
            0x00, 64, 80,          // E an
            0x30, 0x80, 64, 0,     // 48 Ticks später: E aus
            0x00, 0xFF, 0x2F, 0x00,
        ];

        var song = MidiFile.Read(data);

        var notes = song.Tracks.Single().Notes;
        Assert.Equal(new Note(60, 0, Q, 100), notes[0]);
        Assert.Equal(new Note(64, Q, Q / 2, 80), notes[1]);
        Assert.Equal(120, song.Bpm);
    }

    [Theory]
    [InlineData(BuiltInInstrument.Organ, 57)]     // A3 = 220 Hz
    [InlineData(BuiltInInstrument.Strings, 57)]
    [InlineData(BuiltInInstrument.Piano, 57)]
    [InlineData(BuiltInInstrument.Guitar, 57)]
    [InlineData(BuiltInInstrument.Bass, 45)]      // A2 = 110 Hz
    [InlineData(BuiltInInstrument.ElectricPiano, 57)]
    public void Built_in_instruments_play_the_right_pitch(BuiltInInstrument instrument, int pitch)
    {
        var audio = NoteRenderer.Render([new Note(pitch, 0, Q * 2)], new InstrumentSpec(instrument), 120, 44100);
        var mono = audio.ExtractChannels([0]);

        double expected = Note.Frequency(pitch);
        double measured = AutocorrelationPitch(mono.Slice(4410, 8192), 40, 1000);
        if (instrument is BuiltInInstrument.Organ or BuiltInInstrument.Bass && measured < expected * 0.75)
        {
            measured *= 2; // Orgel (16'-Register) und Bass (Sub-Oszillator) haben bewusst eine tiefere Oktave
        }

        Assert.InRange(measured, expected * 0.97, expected * 1.03);
        Assert.InRange(Signals.Peak(audio), 0.02f, 1f);
    }

    /// <summary>Grundfrequenz per Autokorrelation (robust auch bei obertonreichen Klängen).</summary>
    private static double AutocorrelationPitch(Audio.AudioBuffer mono, double minHz, double maxHz)
    {
        var x = mono.Samples.ToArray();
        int minLag = (int)(mono.SampleRate / maxHz);
        int maxLag = (int)(mono.SampleRate / minHz);
        double Correlation(int lag)
        {
            double sum = 0;
            for (int i = 0; i + lag < x.Length; i++)
            {
                sum += x[i] * x[i + lag];
            }

            return sum / (x.Length - lag);
        }

        var values = Enumerable.Range(minLag, maxLag - minLag + 1).Select(Correlation).ToArray();
        double best = values.Max();
        // erstes Maximum, das fast so hoch ist wie das größte (vermeidet Oktavfehler nach unten)
        int index = Enumerable.Range(1, values.Length - 2).First(i => values[i] >= 0.9 * best && values[i] >= values[i - 1] && values[i] >= values[i + 1]);
        return mono.SampleRate / (double)(minLag + index);
    }

    [Fact]
    public void Notes_release_and_the_render_ends_after_the_tail()
    {
        var audio = NoteRenderer.Render([new Note(60, 0, Q)], new InstrumentSpec(BuiltInInstrument.Organ), 120, 44100, minimumFrames: 44100);

        Assert.InRange(audio.FrameCount, 44100, 44100 + 44100); // Ausklang der Orgel ist kurz → Stille abgeschnitten
        Assert.True(Math.Abs(audio.GetSample(10000, 0)) + Math.Abs(audio.GetSample(10001, 0)) > 0);
        Assert.Equal(0f, audio.GetSample(audio.FrameCount - 1, 0), 3);
    }

    [Fact]
    public void Drum_kit_plays_one_shots()
    {
        Note[] beat = [new(36, 0, 10, 120), new(38, Q, 10, 110), new(42, Q * 2, 10, 90), new(49, Q * 3, 10, 100)];

        var audio = NoteRenderer.Render(beat, new InstrumentSpec(BuiltInInstrument.Drums), 120, 44100);

        foreach (var note in beat)
        {
            int frame = (int)MusicalTime.TicksToFrames(note.Start, 120, 44100);
            float peak = Enumerable.Range(frame, 2000).Max(i => Math.Abs(audio.GetSample(i, 0)));
            Assert.True(peak > 0.01f, $"Trommel {note.Pitch} ist stumm");
        }
    }

    [Fact]
    public void Synth_is_polyphonic_and_voices_end()
    {
        var synth = new BuiltInSynth(BuiltInInstrument.Strings, 44100);
        synth.NoteOn(60, 100);
        synth.NoteOn(64, 100);
        synth.NoteOn(67, 100);
        var buffer = new float[2048];
        synth.Render(buffer);
        Assert.Equal(3, synth.ActiveVoices);

        synth.AllNotesOff();
        for (int i = 0; i < 100; i++)
        {
            synth.Render(buffer);
        }

        Assert.Equal(0, synth.ActiveVoices);
    }

    [Fact]
    public void General_midi_programs_map_to_instruments()
    {
        Assert.Equal(BuiltInInstrument.Piano, Instruments.FromGeneralMidi(0, 0));
        Assert.Equal(BuiltInInstrument.Guitar, Instruments.FromGeneralMidi(25, 0));
        Assert.Equal(BuiltInInstrument.Strings, Instruments.FromGeneralMidi(48, 0));
        Assert.Equal(BuiltInInstrument.Drums, Instruments.FromGeneralMidi(0, 9));
        Assert.All(Instruments.All.Where(i => i != BuiltInInstrument.Drums),
            i => Assert.Equal(i, Instruments.FromGeneralMidi(Instruments.ToGeneralMidi(i), 0)));
    }

    [Fact]
    public void Missing_sound_font_falls_back_to_the_built_in_instrument()
    {
        var spec = new InstrumentSpec(BuiltInInstrument.Bells, SoundFontPath: "gibt-es-nicht.sf2");

        Assert.IsType<BuiltInSynth>(spec.CreateEngine(44100));
    }
}
