using MusicWorker.Core.Notes;

namespace MusicWorker.Core.Tests;

public class CompositionTests
{
    private const int Q = MusicalTime.TicksPerQuarter;

    [Fact]
    public void Chords_have_the_right_tones()
    {
        Assert.Equal([60, 64, 67], Chords.Build(60, ChordQuality.Major));
        Assert.Equal([57, 60, 64], Chords.Build(57, ChordQuality.Minor));
        Assert.Equal([55, 59, 62, 65], Chords.Build(55, ChordQuality.Seventh));
        Assert.Equal([64, 67, 72], Chords.Build(60, ChordQuality.Major, inversion: 1));
        Assert.Equal([67, 72, 76], Chords.Build(60, ChordQuality.Major, inversion: 2));
    }

    [Fact]
    public void Chord_names_use_german_note_names()
    {
        Assert.Equal("Am", Chords.Name(57, ChordQuality.Minor));
        Assert.Equal("G7", Chords.Name(55, ChordQuality.Seventh));
        Assert.Equal("H°", Chords.Name(59, ChordQuality.Diminished));
        Assert.Equal("B", Chords.Name(58, ChordQuality.Major));
    }

    [Theory]
    [InlineData(0, false, 0, 0, ChordQuality.Major)] // C-Dur: I = C
    [InlineData(0, false, 1, 2, ChordQuality.Minor)] // ii = Dm
    [InlineData(0, false, 4, 7, ChordQuality.Major)] // V = G
    [InlineData(0, false, 5, 9, ChordQuality.Minor)] // vi = Am
    [InlineData(0, false, 6, 11, ChordQuality.Diminished)] // vii° = H°
    [InlineData(9, true, 0, 9, ChordQuality.Minor)] // a-Moll: i = Am
    [InlineData(9, true, 5, 5, ChordQuality.Major)] // VI = F
    public void Diatonic_chords_fit_the_key(int tonic, bool minor, int degree, int root, ChordQuality quality)
    {
        Assert.Equal((root, quality), Chords.Diatonic(tonic, minor, degree));
    }

    [Fact]
    public void Diatonic_sevenths_are_correct()
    {
        Assert.Equal((7, ChordQuality.Seventh), Chords.Diatonic(0, false, 4, seventh: true));
        Assert.Equal((0, ChordQuality.MajorSeventh), Chords.Diatonic(0, false, 0, seventh: true));
        Assert.Equal((2, ChordQuality.MinorSeventh), Chords.Diatonic(0, false, 1, seventh: true));
    }

    [Fact]
    public void Progression_places_one_chord_per_step_with_smooth_voice_leading()
    {
        var pop = Chords.Progressions[0]; // I – V – vi – IV
        var notes = Chords.Progression(pop, tonic: 0, minor: false, octave: 4, start: 0, chordTicks: Q * 4);

        Assert.Equal(12, notes.Count);
        var chords = notes.GroupBy(n => n.Start).OrderBy(g => g.Key).Select(g => g.Select(n => n.Pitch % 12).OrderBy(p => p).ToArray()).ToArray();
        Assert.Equal([0, 4, 7], chords[0]); // C
        Assert.Equal([2, 7, 11], chords[1]); // G
        Assert.Equal([0, 4, 9], chords[2]); // Am
        Assert.Equal([0, 5, 9], chords[3]); // F

        // Enge Führung: kein Akkord springt mehr als eine Quarte
        var centers = notes.GroupBy(n => n.Start).OrderBy(g => g.Key).Select(g => g.Average(n => n.Pitch)).ToArray();
        for (int i = 1; i < centers.Length; i++)
        {
            Assert.True(Math.Abs(centers[i] - centers[i - 1]) <= 5, $"Sprung {centers[i - 1]} → {centers[i]}");
        }
    }

    [Fact]
    public void Progression_with_bass_adds_the_root_below()
    {
        var notes = Chords.Progression(Chords.Progressions[0], 0, false, 4, 0, Q * 4, withBass: true);
        var first = notes.Where(n => n.Start == 0).OrderBy(n => n.Pitch).ToArray();

        Assert.Equal(4, first.Length);
        Assert.Equal(48, first[0].Pitch); // C3
    }

    [Fact]
    public void Arpeggiator_plays_chord_tones_one_after_another()
    {
        var chord = Chords.Notes(60, ChordQuality.Major, 0, Q * 2);
        var arp = Arpeggiator.Apply(chord, Q / 2, ArpeggioPattern.Up);

        Assert.Equal(4, arp.Count);
        Assert.Equal([60, 64, 67, 60], arp.Select(n => n.Pitch));
        Assert.Equal([0L, Q / 2, Q, Q * 3 / 2], arp.Select(n => n.Start));
        Assert.All(arp, n => Assert.True(n.Length < Q / 2));
    }

    [Fact]
    public void Arpeggiator_patterns()
    {
        var chord = Chords.Notes(60, ChordQuality.Major, 0, Q * 2);

        Assert.Equal([67, 64, 60, 67], Arpeggiator.Apply(chord, Q / 2, ArpeggioPattern.Down).Select(n => n.Pitch));
        Assert.Equal([60, 64, 67, 64, 60, 64, 67, 64], Arpeggiator.Apply(chord, Q / 4, ArpeggioPattern.UpDown).Select(n => n.Pitch));
        Assert.Equal([60, 67, 64, 67], Arpeggiator.Apply(chord, Q / 2, ArpeggioPattern.Alberti).Select(n => n.Pitch));
        Assert.Equal([60, 64, 67, 72, 76, 79, 60, 64], Arpeggiator.Apply(chord, Q / 4, ArpeggioPattern.Up, octaves: 2).Select(n => n.Pitch));
    }

    [Fact]
    public void Arpeggiator_leaves_single_notes_alone()
    {
        Note[] melody = [new(60, 0, Q), new(62, Q, Q)];

        Assert.Equal(melody, Arpeggiator.Apply(melody, Q / 4, ArpeggioPattern.Up));
    }

    [Fact]
    public void Drum_pattern_round_trips_through_notes()
    {
        var rock = DrumPattern.Presets[0].Pattern;
        long step = Q / 4;
        var notes = rock.ToNotes(Q * 4, step, repeats: 2);

        Assert.Equal(rock.Count * 2, notes.Count);
        Assert.Contains(notes, n => n.Pitch == 36 && n.Start == Q * 4);
        Assert.Contains(notes, n => n.Pitch == 38 && n.Start == Q * 5);

        var read = DrumPattern.FromNotes(notes, Q * 4, step);
        for (int s = 0; s < DrumPattern.Sounds.Count; s++)
        {
            for (int i = 0; i < 16; i++)
            {
                Assert.Equal(rock[s, i], read[s, i]);
            }
        }
    }

    [Fact]
    public void All_drum_presets_have_a_kick_and_fit_their_length()
    {
        int kick = DrumPattern.Sounds.ToList().FindIndex(s => s.Pitch == 36);
        foreach (var (name, pattern) in DrumPattern.Presets)
        {
            Assert.True(Enumerable.Range(0, pattern.StepCount).Any(i => pattern[kick, i]), name);
            Assert.True(pattern.StepCount is 12 or 16, name);
        }
    }
}
