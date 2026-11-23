using MusicWorker.Core.Audio;

namespace MusicWorker.Core.Visualization;

/// <summary>Minimum und Maximum aller Kanäle innerhalb einer Bildschirmspalte.</summary>
public readonly record struct PeakColumn(float Min, float Max);

/// <summary>Berechnet die Daten für die Wellenform-Darstellung.</summary>
public static class WaveformPeaks
{
    /// <summary>
    /// Teilt den Bereich [<paramref name="startFrame"/>, +<paramref name="frameCount"/>) in
    /// <paramref name="columns"/> Spalten und liefert je Spalte Minimum und Maximum.
    /// Ist man so weit hineingezoomt, dass eine Spalte weniger als ein Frame umfasst,
    /// wird das nächstliegende Frame verwendet.
    /// </summary>
    public static PeakColumn[] Compute(AudioBuffer buffer, int startFrame, int frameCount, int columns)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (columns <= 0)
        {
            return [];
        }

        var result = new PeakColumn[columns];
        int totalFrames = buffer.FrameCount;
        if (frameCount <= 0 || totalFrames == 0)
        {
            return result;
        }

        var samples = buffer.Samples;
        int channels = buffer.Channels;
        double framesPerColumn = (double)frameCount / columns;

        for (int column = 0; column < columns; column++)
        {
            int first = startFrame + (int)Math.Floor(column * framesPerColumn);
            int last = startFrame + (int)Math.Floor((column + 1) * framesPerColumn);
            if (last <= first)
            {
                last = first + 1;
            }

            first = Math.Clamp(first, 0, totalFrames);
            last = Math.Clamp(last, 0, totalFrames);
            if (first >= last)
            {
                continue;
            }

            float min = float.MaxValue;
            float max = float.MinValue;
            foreach (float sample in samples[(first * channels)..(last * channels)])
            {
                if (sample < min)
                {
                    min = sample;
                }

                if (sample > max)
                {
                    max = sample;
                }
            }

            result[column] = new PeakColumn(min, max);
        }

        return result;
    }
}
