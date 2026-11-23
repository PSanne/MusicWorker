using MusicWorker.Core.Audio;

namespace MusicWorker.Core.Dsp;

/// <summary>
/// Ändert die Länge (das Tempo) von Audio, ohne die Tonhöhe zu verändern – nach dem
/// WSOLA-Verfahren (Waveform Similarity Overlap-Add): Das Signal wird in kurze, überlappende
/// Stücke zerlegt; jedes Stück wird leicht verschoben so gewählt, dass es möglichst nahtlos an
/// das vorherige passt, und dann mit einem Fenster überblendet.
/// </summary>
public static class TimeStretcher
{
    private const double FrameSeconds = 0.05;      // 50 ms Fensterlänge
    private const double ToleranceSeconds = 0.0125; // Suchbereich ±12,5 ms
    private const int CoarseStep = 4;

    /// <summary>Streckt das Audio um <paramref name="factor"/> (2 = doppelt so lang, 0,5 = halb so lang).</summary>
    public static AudioBuffer Stretch(AudioBuffer input, double factor)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(factor, 0);

        int inputFrames = input.FrameCount;
        int channels = input.Channels;
        int outputFrames = (int)Math.Round(inputFrames * factor);
        if (Math.Abs(factor - 1) < 1e-6 || inputFrames == 0)
        {
            return input;
        }

        int frameLength = Math.Max(16, (int)(input.SampleRate * FrameSeconds) & ~1);
        int hop = frameLength / 2;
        int tolerance = Math.Max(1, (int)(input.SampleRate * ToleranceSeconds));
        double inputHop = hop / factor;

        var source = input.Samples;
        var mono = MixToMono(input);
        var window = CreateWindow(frameLength);
        var output = new float[(outputFrames + frameLength) * channels];

        int previousStart = 0;
        for (int k = 0; ; k++)
        {
            int outputPosition = k * hop;
            if (outputPosition >= outputFrames)
            {
                break;
            }

            int start = k == 0
                ? 0
                : FindBestStart(mono, previousStart + hop, (int)Math.Round(k * inputHop), tolerance, hop);

            for (int i = 0; i < frameLength; i++)
            {
                int sourceFrame = start + i;
                if (sourceFrame >= inputFrames)
                {
                    break;
                }

                // Das erste Stück beginnt ohne Einblenden, damit der Anfang nicht leiser wird.
                float w = k == 0 && i < hop ? 1f : window[i];
                int o = (outputPosition + i) * channels;
                int s = sourceFrame * channels;
                for (int c = 0; c < channels; c++)
                {
                    output[o + c] += source[s + c] * w;
                }
            }

            previousStart = start;
        }

        Array.Resize(ref output, outputFrames * channels);
        return new AudioBuffer(output, input.SampleRate, channels);
    }

    /// <summary>
    /// Sucht um <paramref name="nominal"/> herum den Startpunkt, dessen Anfang am besten zur
    /// natürlichen Fortsetzung des vorherigen Stücks (<paramref name="natural"/>) passt.
    /// Erst grob (jede 4. Position), dann fein um den besten Treffer.
    /// </summary>
    private static int FindBestStart(float[] mono, int natural, int nominal, int tolerance, int overlap)
    {
        int maxStart = mono.Length - overlap;
        if (maxStart <= 0)
        {
            return 0;
        }

        int low = Math.Clamp(nominal - tolerance, 0, maxStart);
        int high = Math.Clamp(nominal + tolerance, 0, maxStart);
        if (natural < 0 || natural > maxStart)
        {
            return Math.Clamp(nominal, low, high);
        }

        int best = Math.Clamp(nominal, low, high);
        double bestScore = double.NegativeInfinity;
        for (int candidate = low; candidate <= high; candidate += CoarseStep)
        {
            double score = Similarity(mono, natural, candidate, overlap, stride: 4);
            if (score > bestScore)
            {
                (bestScore, best) = (score, candidate);
            }
        }

        int fineLow = Math.Max(low, best - CoarseStep + 1);
        int fineHigh = Math.Min(high, best + CoarseStep - 1);
        bestScore = double.NegativeInfinity;
        for (int candidate = fineLow; candidate <= fineHigh; candidate++)
        {
            double score = Similarity(mono, natural, candidate, overlap, stride: 1);
            if (score > bestScore)
            {
                (bestScore, best) = (score, candidate);
            }
        }

        return best;
    }

    /// <summary>Normierte Kreuzkorrelation zweier Abschnitte (1 = identische Form).</summary>
    private static double Similarity(float[] mono, int a, int b, int length, int stride)
    {
        double dot = 0;
        double energy = 1e-9;
        for (int i = 0; i < length; i += stride)
        {
            float y = mono[b + i];
            dot += mono[a + i] * y;
            energy += y * y;
        }

        return dot / Math.Sqrt(energy);
    }

    private static float[] MixToMono(AudioBuffer input)
    {
        var mono = new float[input.FrameCount];
        var samples = input.Samples;
        int channels = input.Channels;
        for (int frame = 0; frame < mono.Length; frame++)
        {
            float sum = 0;
            for (int c = 0; c < channels; c++)
            {
                sum += samples[frame * channels + c];
            }

            mono[frame] = sum / channels;
        }

        return mono;
    }

    /// <summary>Hann-Fenster; bei 50 % Überlappung ergänzen sich die Fenster exakt zu 1.</summary>
    private static float[] CreateWindow(int length)
    {
        var window = new float[length];
        for (int i = 0; i < length; i++)
        {
            window[i] = (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * i / length));
        }

        return window;
    }
}
