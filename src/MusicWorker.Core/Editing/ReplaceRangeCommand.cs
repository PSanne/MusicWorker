using MusicWorker.Core.Audio;

namespace MusicWorker.Core.Editing;

/// <summary>
/// Ersetzt einen Bereich durch neuen Inhalt. Damit lassen sich Löschen (Ersatz ist leer),
/// Einfügen (Bereich ist leer) und Effekte (Ersatz ist der bearbeitete Ausschnitt) abbilden.
/// Gespeichert wird nur der betroffene Ausschnitt – nicht die ganze Datei.
/// </summary>
public sealed class ReplaceRangeCommand : IEditCommand
{
    private readonly int _startFrame;
    private readonly int _frameCount;
    private readonly AudioBuffer _replacement;
    private AudioBuffer? _removed;

    public ReplaceRangeCommand(string description, int startFrame, int frameCount, AudioBuffer replacement)
    {
        Description = description;
        _startFrame = startFrame;
        _frameCount = frameCount;
        _replacement = replacement ?? throw new ArgumentNullException(nameof(replacement));
    }

    public string Description { get; }

    public AudioBuffer Apply(AudioBuffer buffer)
    {
        _removed = buffer.Slice(_startFrame, _frameCount);
        return buffer.ReplaceRange(_startFrame, _frameCount, _replacement);
    }

    public long LengthAfter(long lengthBefore) => lengthBefore - _frameCount + _replacement.FrameCount;

    public IReadOnlyList<Marker> MapMarkers(IReadOnlyList<Marker> markers, long lengthBefore) =>
        MarkerList.Replace(markers, _startFrame, _frameCount, _replacement.FrameCount);

    public AudioBuffer Revert(AudioBuffer buffer)
    {
        if (_removed is null)
        {
            throw new InvalidOperationException("Der Befehl wurde noch nicht ausgeführt.");
        }

        return buffer.ReplaceRange(_startFrame, _replacement.FrameCount, _removed);
    }
}
