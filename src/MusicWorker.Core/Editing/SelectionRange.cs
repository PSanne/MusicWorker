namespace MusicWorker.Core.Editing;

/// <summary>
/// Ein markierter Bereich in Frames, halboffen: [Start, End).
/// Ist der Bereich leer (Start == End), dient Start als Cursor-/Einfügeposition.
/// </summary>
public readonly record struct SelectionRange
{
    public SelectionRange(int start, int end)
    {
        Start = Math.Min(start, end);
        End = Math.Max(start, end);
    }

    public int Start { get; }

    public int End { get; }

    public int Length => End - Start;

    public bool IsEmpty => Length == 0;

    /// <summary>Leere Auswahl an einer Position (Cursor).</summary>
    public static SelectionRange Caret(int position) => new(position, position);

    /// <summary>Begrenzt die Auswahl auf einen Puffer mit <paramref name="frameCount"/> Frames.</summary>
    public SelectionRange Clamp(int frameCount) =>
        new(Math.Clamp(Start, 0, frameCount), Math.Clamp(End, 0, frameCount));

    public override string ToString() => IsEmpty ? $"[{Start}]" : $"[{Start}, {End})";
}
