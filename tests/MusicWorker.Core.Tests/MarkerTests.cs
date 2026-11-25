using System.Text;
using MusicWorker.Core.Audio;
using MusicWorker.Core.Editing;
using MusicWorker.Core.Effects;

namespace MusicWorker.Core.Tests;

public class MarkerTests
{
    private static AudioEditor EditorWith(int frames, params long[] markers)
    {
        var editor = new AudioEditor();
        editor.Load(TestAudio.Ramp(frames), markers.Select((f, i) => new Marker($"M{i + 1}", f)).ToArray());
        return editor;
    }

    private static long[] Frames(AudioEditor editor) => editor.Markers.Select(m => m.Frame).ToArray();

    [Fact]
    public void Markers_are_sorted_and_named_automatically()
    {
        var editor = EditorWith(100);

        editor.AddMarker(60);
        int index = editor.AddMarker(20, "Strophe");

        Assert.Equal(0, index);
        Assert.Equal(["Strophe", "Marker 1"], editor.Markers.Select(m => m.Name));
        Assert.Equal("Marker 2", MarkerList.NextName(editor.Markers));
    }

    [Fact]
    public void Deleting_moves_later_markers_and_drops_markers_inside()
    {
        var editor = EditorWith(100, 10, 40, 50, 80);
        editor.Selection = new SelectionRange(30, 60);

        editor.Delete();

        Assert.Equal([10L, 50], Frames(editor)); // 40 und 50 lagen im gelöschten Bereich (50 → wird entfernt), 80 → 50
        Assert.Equal(["M1", "M4"], editor.Markers.Select(m => m.Name));
    }

    [Fact]
    public void Marker_at_the_cut_start_stays()
    {
        var editor = EditorWith(100, 30);
        editor.Selection = new SelectionRange(30, 60);

        editor.Cut();

        Assert.Equal([30L], Frames(editor));
    }

    [Fact]
    public void Pasting_shifts_markers_after_the_cursor()
    {
        var editor = EditorWith(100, 10, 50);
        editor.Selection = new SelectionRange(0, 20);
        editor.Copy();
        editor.Selection = SelectionRange.Caret(30);

        editor.Paste();

        Assert.Equal([10L, 70], Frames(editor));
    }

    [Fact]
    public void Undo_and_redo_restore_markers()
    {
        var editor = EditorWith(100, 80);
        editor.Selection = new SelectionRange(0, 50);
        editor.Delete();
        Assert.Equal([30L], Frames(editor));

        editor.Undo();
        Assert.Equal([80L], Frames(editor));

        editor.Redo();
        Assert.Equal([30L], Frames(editor));
    }

    [Fact]
    public void Trim_keeps_only_markers_in_the_kept_range()
    {
        var editor = EditorWith(100, 5, 25, 45, 95);
        editor.Selection = new SelectionRange(20, 60);

        editor.Trim();

        Assert.Equal([5L, 25], Frames(editor)); // 25 → 5, 45 → 25; 5 und 95 lagen außerhalb und fallen weg
    }

    [Fact]
    public void Stretching_a_selection_moves_markers_proportionally()
    {
        var editor = EditorWith(100, 30, 90);
        var stretched = TestAudio.Ramp(80); // 40 Frames (20–60) werden zu 80 Frames

        editor.ApplyEffectResult("Tempo", new SelectionRange(20, 60), stretched);

        Assert.Equal([40L, 130], Frames(editor));
    }

    [Fact]
    public void Replacing_the_whole_buffer_scales_markers()
    {
        var editor = EditorWith(100, 50);

        editor.ApplyBufferResult("Tempo", TestAudio.Ramp(200), default);

        Assert.Equal([100L], Frames(editor));
    }

    [Fact]
    public void Same_length_effects_keep_markers()
    {
        var editor = EditorWith(100, 30, 60);
        editor.Selection = new SelectionRange(20, 70);

        editor.ApplyEffect(new GainEffect(-6));

        Assert.Equal([30L, 60], Frames(editor));
    }

    [Fact]
    public void Rename_move_and_remove_are_undoable()
    {
        var editor = EditorWith(100, 10, 50);

        editor.RenameMarker(0, "  Intro ");
        int moved = editor.MoveMarker(0, 70);
        Assert.Equal(1, moved);
        Assert.Equal(["M2", "Intro"], editor.Markers.Select(m => m.Name));
        Assert.True(editor.IsModified);

        editor.RemoveMarker(1);
        Assert.Single(editor.Markers);

        editor.Undo();
        editor.Undo();
        editor.Undo();
        Assert.Equal(["M1", "M2"], editor.Markers.Select(m => m.Name));
        Assert.Equal([10L, 50], Frames(editor));
        Assert.False(editor.IsModified);
    }

    [Fact]
    public void Restored_documents_count_as_unsaved()
    {
        var editor = EditorWith(10);
        Assert.False(editor.IsModified);

        editor.MarkUnsaved();

        Assert.True(editor.IsModified);
        editor.MarkSaved();
        Assert.False(editor.IsModified);
    }

    [Fact]
    public void Next_and_previous_find_neighbours()
    {
        var markers = new[] { new Marker("A", 10), new Marker("B", 50) };

        Assert.Equal("B", MarkerList.Next(markers, 10)?.Name);
        Assert.Equal("A", MarkerList.Previous(markers, 50)?.Name);
        Assert.Null(MarkerList.Next(markers, 50));
        Assert.Null(MarkerList.Previous(markers, 10));
    }

    // ───────────── WAV ─────────────

    private static MemoryStream Wave(int frames, int channels = 2, int bitsPerSample = 16)
    {
        var stream = new MemoryStream();
        var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        int blockAlign = channels * bitsPerSample / 8;
        int dataSize = frames * blockAlign;
        writer.Write("RIFF"u8);
        writer.Write(4 + 8 + 16 + 8 + dataSize);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)channels);
        writer.Write(44100);
        writer.Write(44100 * blockAlign);
        writer.Write((short)blockAlign);
        writer.Write((short)bitsPerSample);
        writer.Write("data"u8);
        writer.Write(dataSize);
        writer.Write(new byte[dataSize]);
        writer.Flush();
        return stream;
    }

    [Fact]
    public void Wav_markers_round_trip_with_umlauts()
    {
        using var stream = Wave(1000);
        var markers = new[] { new Marker("Strophe", 100), new Marker("Brücke", 700), new Marker("Refrain", 400) };

        WavMarkers.Append(stream, MarkerList.Sorted(markers));
        var read = WavMarkers.Read(stream);

        Assert.Equal(["Strophe", "Refrain", "Brücke"], read.Select(m => m.Name));
        Assert.Equal([100L, 400, 700], read.Select(m => m.Frame));
        Assert.Equal(stream.Length - 8, BitConverter.ToUInt32(stream.ToArray(), 4));
    }

    [Fact]
    public void Odd_sized_data_is_padded_before_the_marker_chunks()
    {
        using var stream = Wave(3, channels: 1, bitsPerSample: 8); // 3 Bytes Daten → ungerade

        WavMarkers.Append(stream, [new Marker("A", 1)]);

        Assert.Equal(0, stream.Length % 2);
        Assert.Equal([1L], WavMarkers.Read(stream).Select(m => m.Frame));
    }

    [Fact]
    public void Files_without_markers_or_non_wav_data_give_an_empty_list()
    {
        using var wave = Wave(10);
        using var garbage = new MemoryStream(Encoding.ASCII.GetBytes("ID3 kein WAV"));

        Assert.Empty(WavMarkers.Read(wave));
        Assert.Empty(WavMarkers.Read(garbage));
    }
}
