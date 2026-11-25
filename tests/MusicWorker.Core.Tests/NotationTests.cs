using System.IO.Compression;
using System.Text;
using MusicWorker.Core.Analysis;
using MusicWorker.Core.Notes;
using MusicWorker.Core.Visualization;

namespace MusicWorker.Core.Tests;

public class NotationTests
{
    private const int Q = MusicalTime.TicksPerQuarter;

    private static Score One(params Note[] notes) =>
        Notation.Build("Test", [("Klavier", notes)], 4, KeySignature.CMajor, 120);

    [Theory]
    [InlineData(0, false, 0)]
    [InlineData(7, false, 1)] // G-Dur: 1 Kreuz
    [InlineData(9, true, 0)] // a-Moll
    [InlineData(4, true, 1)] // e-Moll
    [InlineData(5, false, -1)] // F-Dur: 1 B
    [InlineData(3, false, -3)] // Es-Dur
    [InlineData(2, false, 2)] // D-Dur
    public void Key_signatures(int tonic, bool minor, int fifths)
    {
        Assert.Equal(fifths, KeySignature.FromKey(tonic, minor).Fifths);
    }

    [Fact]
    public void Spelling_follows_the_key()
    {
        Assert.Equal(new SpelledPitch(3, 1, 4), Notation.Spell(66, new KeySignature(1))); // Fis4
        Assert.Equal(new SpelledPitch(4, -1, 4), Notation.Spell(66, new KeySignature(-6))); // Ges4
        Assert.Equal(new SpelledPitch(6, -1, 4), Notation.Spell(70, KeySignature.CMajor)); // B4 (Bb)
        Assert.Equal(new SpelledPitch(0, 1, 5), Notation.Spell(73, KeySignature.CMajor)); // Cis5
        Assert.Equal(new SpelledPitch(0, 0, 4), Notation.Spell(60, KeySignature.CMajor));
    }

    [Fact]
    public void Staff_positions()
    {
        Assert.Equal(0, Notation.StaffPosition(Notation.Spell(64, KeySignature.CMajor), Clef.Treble)); // E4 unterste Linie
        Assert.Equal(8, Notation.StaffPosition(Notation.Spell(77, KeySignature.CMajor), Clef.Treble)); // F5 oberste Linie
        Assert.Equal(-2, Notation.StaffPosition(Notation.Spell(60, KeySignature.CMajor), Clef.Treble)); // C4 Hilfslinie
        Assert.Equal(0, Notation.StaffPosition(Notation.Spell(43, KeySignature.CMajor), Clef.Bass)); // G2
        Assert.Equal(10, Notation.StaffPosition(Notation.Spell(60, KeySignature.CMajor), Clef.Bass)); // C4 über dem Bass-System
    }

    [Fact]
    public void Quarter_notes_fill_a_bar()
    {
        var score = One(new Note(60, 0, Q), new Note(62, Q, Q), new Note(64, 2 * Q, Q), new Note(65, 3 * Q, Q));
        var events = score.Parts[0].Staves[0].Measures[0].Events;

        Assert.Equal(1, score.MeasureCount);
        Assert.Equal(4, events.Count);
        Assert.All(events, e => Assert.Equal(4, e.Value));
        Assert.Equal(Clef.Treble, score.Parts[0].Staves[0].Clef);
    }

    [Fact]
    public void Gaps_become_rests_and_empty_bars_a_whole_rest()
    {
        var score = One(new Note(67, Q, Q), new Note(67, 8 * Q, 2 * Q));
        var first = score.Parts[0].Staves[0].Measures[0].Events;
        var second = score.Parts[0].Staves[0].Measures[1].Events;

        Assert.Equal(3, score.MeasureCount);
        Assert.True(first[0].IsRest && first[0].Value == 4);
        Assert.False(first[1].IsRest);
        Assert.True(first[2].IsRest && first[2].Value == 2); // halbe Pause
        Assert.Single(second);
        Assert.True(second[0].FullMeasure);
    }

    [Fact]
    public void Notes_across_the_bar_line_are_tied()
    {
        var score = One(new Note(60, 3 * Q, 2 * Q)); // Viertel vor und nach dem Taktstrich
        var m1 = score.Parts[0].Staves[0].Measures[0].Events.Last();
        var m2 = score.Parts[0].Staves[0].Measures[1].Events.First();

        Assert.True(m1.Notes.Single().TieToNext);
        Assert.Equal(4, m1.Value);
        Assert.False(m2.Notes.Single().TieToNext);
        Assert.True(m2.IsTieContinuation);
    }

    [Theory]
    [InlineData(0, 6, 4, true)] // punktierte Viertel
    [InlineData(0, 12, 2, true)] // punktierte Halbe
    [InlineData(0, 16, 1, false)] // Ganze
    [InlineData(2, 2, 8, false)] // Achtel auf der zweiten Achtel
    public void Durations_become_note_values(int start, int units, int value, bool dotted)
    {
        var piece = Notation.SplitDuration(start, units, 16).Single();
        Assert.Equal((value, dotted), (piece.Value, piece.Dotted));
    }

    [Fact]
    public void Syncopated_durations_are_split_on_the_beat()
    {
        var pieces = Notation.SplitDuration(2, 4, 16); // Viertel, die auf „und“ beginnt
        Assert.Equal([2, 2], pieces.Select(p => p.Units));
        Assert.Equal([8, 8], pieces.Select(p => p.Value));
    }

    [Fact]
    public void Accidentals_hold_for_the_rest_of_the_bar()
    {
        // G-Dur: F ist Fis. F-natürlich braucht ein Auflösungszeichen, beim zweiten Mal im Takt nicht mehr.
        var score = Notation.Build("Test", [("Flöte", [new Note(65, 0, Q), new Note(65, Q, Q), new Note(66, 2 * Q, Q), new Note(65, 4 * Q, Q)])], 4, new KeySignature(1), 100);
        var bar1 = score.Parts[0].Staves[0].Measures[0].Events;
        var bar2 = score.Parts[0].Staves[0].Measures[1].Events;

        Assert.Equal(0, bar1[0].Notes[0].Accidental);
        Assert.Null(bar1[1].Notes[0].Accidental);
        Assert.Equal(1, bar1[2].Notes[0].Accidental); // Fis nach F im selben Takt
        Assert.Equal(0, bar2[0].Notes[0].Accidental); // neuer Takt: wieder auflösen
    }

    [Fact]
    public void Wide_range_uses_a_grand_staff()
    {
        var score = One(new Note(72, 0, Q), new Note(48, 0, Q), new Note(60, Q, Q));
        var staves = score.Parts[0].Staves;

        Assert.Equal([Clef.Treble, Clef.Bass], staves.Select(s => s.Clef));
        Assert.Contains(staves[0].Measures[0].Events, e => e.Notes.Any(n => n.Pitch == 60));
        Assert.Contains(staves[1].Measures[0].Events, e => e.Notes.Any(n => n.Pitch == 48));
        Assert.Equal(Clef.Bass, One(new Note(40, 0, Q), new Note(50, Q, Q)).Parts[0].Staves.Single().Clef);
    }

    [Fact]
    public void Chords_are_one_event()
    {
        var score = One(Chords.Notes(60, ChordQuality.Major, 0, 4 * Q).ToArray());
        var e = score.Parts[0].Staves[0].Measures[0].Events.Single();

        Assert.Equal([60, 64, 67], e.Notes.Select(n => n.Pitch));
        Assert.Equal(1, e.Value);
    }

    [Fact]
    public void Eighths_in_a_beat_are_beamed()
    {
        var notes = Enumerable.Range(0, 8).Select(i => new Note(60 + i, i * Q / 2, Q / 2)).ToArray();
        var measure = One(notes).Parts[0].Staves[0].Measures[0];

        Assert.Equal(4, Notation.BeamGroups(measure).Count);
        Assert.All(Notation.BeamGroups(measure), g => Assert.Equal(2, g.Count));
    }

    [Fact]
    public void Layout_spreads_measures_over_systems_and_pages()
    {
        var notes = Enumerable.Range(0, 64 * 8).Select(i => new Note(60 + (i % 12), i * Q / 2, Q / 2)).ToArray();
        var score = One(notes);
        var pages = ScoreLayout.Paginate(score);
        var options = new ScoreLayoutOptions();

        Assert.Equal(64, score.MeasureCount);
        Assert.True(pages.Count >= 2, $"{pages.Count} Seiten");
        var systems = pages.SelectMany(p => p.Systems).ToArray();
        Assert.Equal(64, systems.Sum(s => s.MeasureCount));
        Assert.True(systems[0].ShowTimeSignature && !systems[1].ShowTimeSignature);
        double width = options.PageWidth - 2 * options.Margin - options.NameWidth;
        Assert.All(systems.SkipLast(1), s => Assert.Equal(width, s.HeaderWidth + s.MeasureWidths.Sum(), 3));
        Assert.All(pages, p => Assert.All(p.Systems, s => Assert.True(s.Top + ScoreLayout.SystemHeight(score, options) <= options.PageHeight - options.Margin)));
    }

    [Fact]
    public void Key_is_detected_from_notes()
    {
        var notes = Chords.Progression(Chords.Progressions[6], 9, true, 4, 0, 4 * Q); // a-Moll-Pop
        Assert.Equal("a-Moll", MusicAnalysis.DetectKey(notes)!.Name);
        Assert.Equal("C-Dur", MusicAnalysis.DetectKey(Chords.Progression(Chords.Progressions[0], 0, false, 4, 0, 4 * Q))!.Name);
        Assert.Equal("G-Dur", MusicAnalysis.DetectKey(Chords.Progression(Chords.Progressions[2], 7, false, 4, 0, 4 * Q))!.Name);
        Assert.Equal("d-Moll", MusicAnalysis.DetectKey(Chords.Progression(Chords.Progressions[7], 2, true, 4, 0, 4 * Q))!.Name);
    }

    [Fact]
    public void Pdf_has_one_image_per_page()
    {
        var pages = new[]
        {
            new PdfPageImage(40, 50, Enumerable.Range(0, 2000).Select(i => (byte)(i % 251)).ToArray()),
            new PdfPageImage(40, 50, Enumerable.Repeat((byte)255, 2000).ToArray()),
        };
        using var stream = new MemoryStream();
        PdfImageWriter.Write(stream, pages, "Lied für Übung");
        byte[] pdf = stream.ToArray();
        string text = Encoding.Latin1.GetString(pdf);

        Assert.StartsWith("%PDF-1.4", text);
        Assert.EndsWith("%%EOF\n", text);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(text, "/Type /Page /").Count);
        Assert.Contains("/Count 2", text);

        // Alle Einträge der xref-Tabelle zeigen auf ihr Objekt
        int xref = int.Parse(text[(text.LastIndexOf("startxref\n", StringComparison.Ordinal) + 10)..].Split('\n')[0]);
        var lines = text[xref..].Split('\n');
        int count = int.Parse(lines[1].Split(' ')[1]);
        for (int i = 1; i < count; i++)
        {
            int offset = int.Parse(lines[2 + i][..10]);
            Assert.StartsWith($"{i} 0 obj", text[offset..]);
        }

        // Das erste Bild lässt sich wieder auspacken
        int start = text.IndexOf("/Filter /FlateDecode", StringComparison.Ordinal);
        int data = text.IndexOf("stream\n", start, StringComparison.Ordinal) + 7;
        int length = int.Parse(System.Text.RegularExpressions.Regex.Match(text[start..], @"/Length (\d+)").Groups[1].Value);
        using var zlib = new ZLibStream(new MemoryStream(pdf, data, length), CompressionMode.Decompress);
        using var unpacked = new MemoryStream();
        zlib.CopyTo(unpacked);
        Assert.Equal(pages[0].Gray, unpacked.ToArray());
    }
}
