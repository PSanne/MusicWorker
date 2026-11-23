using MusicWorker.Core.Editing;
using MusicWorker.Core.Effects;

namespace MusicWorker.Core.Tests;

public class AudioEditorTests
{
    private static AudioEditor EditorWith(int frames)
    {
        var editor = new AudioEditor();
        editor.Load(TestAudio.Ramp(frames));
        return editor;
    }

    [Fact]
    public void Selection_is_normalized_and_clamped()
    {
        var editor = EditorWith(10);

        editor.Selection = new SelectionRange(8, -3);

        Assert.Equal(new SelectionRange(0, 8), editor.Selection);
        editor.Selection = new SelectionRange(5, 50);
        Assert.Equal(new SelectionRange(5, 10), editor.Selection);
    }

    [Fact]
    public void Cut_removes_selection_and_fills_clipboard()
    {
        var editor = EditorWith(6);
        editor.Selection = new SelectionRange(1, 3);

        editor.Cut();

        Assert.Equal(new float[] { 0, 3, 4, 5 }, TestAudio.Frames(editor.Buffer));
        Assert.Equal(new float[] { 1, 2 }, TestAudio.Frames(editor.Clipboard!));
        Assert.Equal(SelectionRange.Caret(1), editor.Selection);
    }

    [Fact]
    public void Paste_inserts_at_cursor_and_selects_the_pasted_audio()
    {
        var editor = EditorWith(4);
        editor.Selection = new SelectionRange(0, 2);
        editor.Copy();
        editor.Selection = SelectionRange.Caret(4);

        editor.Paste();

        Assert.Equal(new float[] { 0, 1, 2, 3, 0, 1 }, TestAudio.Frames(editor.Buffer));
        Assert.Equal(new SelectionRange(4, 6), editor.Selection);
    }

    [Fact]
    public void Paste_replaces_an_existing_selection()
    {
        var editor = EditorWith(5);
        editor.Selection = new SelectionRange(4, 5);
        editor.Copy();
        editor.Selection = new SelectionRange(0, 3);

        editor.Paste();

        Assert.Equal(new float[] { 4, 3, 4 }, TestAudio.Frames(editor.Buffer));
    }

    [Fact]
    public void Trim_keeps_only_the_selection()
    {
        var editor = EditorWith(8);
        editor.Selection = new SelectionRange(2, 5);

        editor.Trim();

        Assert.Equal(new float[] { 2, 3, 4 }, TestAudio.Frames(editor.Buffer));
        Assert.Equal(new SelectionRange(0, 3), editor.Selection);
    }

    [Fact]
    public void Undo_and_redo_restore_buffer_and_selection()
    {
        var editor = EditorWith(8);
        var original = TestAudio.Frames(editor.Buffer);
        editor.Selection = new SelectionRange(2, 5);
        editor.Trim();
        var trimmed = TestAudio.Frames(editor.Buffer);

        editor.Undo();
        Assert.Equal(original, TestAudio.Frames(editor.Buffer));
        Assert.Equal(new SelectionRange(2, 5), editor.Selection);
        Assert.Equal("Zuschneiden", editor.RedoDescription);

        editor.Redo();
        Assert.Equal(trimmed, TestAudio.Frames(editor.Buffer));
        Assert.Equal(new SelectionRange(0, 3), editor.Selection);
    }

    [Fact]
    public void New_edit_clears_redo()
    {
        var editor = EditorWith(8);
        editor.Selection = new SelectionRange(0, 1);
        editor.Delete();
        editor.Undo();

        editor.Selection = new SelectionRange(0, 2);
        editor.Delete();

        Assert.False(editor.CanRedo);
    }

    [Fact]
    public void Effect_without_selection_applies_to_whole_file()
    {
        var editor = EditorWith(4);

        editor.ApplyEffect(new GainEffect(Audio.Decibels.FromGain(2)));

        Assert.Equal(new float[] { 0, 2, 4, 6 }, TestAudio.Frames(editor.Buffer), new ToleranceComparer());
        Assert.Equal("Lautstärke +6,0 dB", editor.UndoDescription!.Replace('.', ','));
    }

    [Fact]
    public void Effect_with_selection_only_changes_the_selection()
    {
        var editor = EditorWith(4);
        editor.Selection = new SelectionRange(2, 4);

        editor.ApplyEffect(new GainEffect(Audio.Decibels.FromGain(0.5)));

        Assert.Equal(new float[] { 0, 1, 1, 1.5f }, TestAudio.Frames(editor.Buffer), new ToleranceComparer());
    }

    [Fact]
    public void IsModified_tracks_save_point_across_undo_and_redo()
    {
        var editor = EditorWith(8);
        Assert.False(editor.IsModified);

        editor.Selection = new SelectionRange(0, 1);
        editor.Delete();
        Assert.True(editor.IsModified);

        editor.MarkSaved();
        Assert.False(editor.IsModified);

        editor.Undo();
        Assert.True(editor.IsModified);

        editor.Redo();
        Assert.False(editor.IsModified);
    }

    [Fact]
    public void Different_edit_with_same_depth_still_counts_as_modified()
    {
        var editor = EditorWith(8);
        editor.Selection = new SelectionRange(0, 1);
        editor.Delete();
        editor.MarkSaved();
        editor.Undo();

        editor.Selection = new SelectionRange(0, 2);
        editor.Delete();

        Assert.True(editor.IsModified);
    }

    [Fact]
    public void Commands_needing_a_selection_throw_without_one()
    {
        var editor = EditorWith(8);

        Assert.Throws<InvalidOperationException>(editor.Cut);
        Assert.Throws<InvalidOperationException>(editor.Trim);
        Assert.Throws<InvalidOperationException>(editor.Paste);
    }

    [Fact]
    public void History_capacity_drops_oldest_steps()
    {
        var editor = new AudioEditor(undoCapacity: 2);
        editor.Load(TestAudio.Ramp(10));
        for (int i = 0; i < 3; i++)
        {
            editor.Selection = new SelectionRange(0, 1);
            editor.Delete();
        }

        editor.Undo();
        editor.Undo();

        Assert.False(editor.CanUndo);
        Assert.Equal(9, editor.Buffer.FrameCount);
        Assert.True(editor.IsModified);
    }

    private sealed class ToleranceComparer : IEqualityComparer<float>
    {
        public bool Equals(float x, float y) => Math.Abs(x - y) < 1e-4f;

        public int GetHashCode(float obj) => 0;
    }
}
