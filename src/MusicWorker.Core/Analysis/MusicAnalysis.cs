using MusicWorker.Core.Audio;
using MusicWorker.Core.Dsp;
using MusicWorker.Core.Effects;

namespace MusicWorker.Core.Analysis;

/// <summary>Erkanntes Tempo: Schläge pro Minute und Lage des ersten Schlags.</summary>
public sealed record TempoResult(double Bpm, double FirstBeatSeconds, double Confidence);

/// <summary>Erkannte Tonart, z. B. a-Moll.</summary>
/// <param name="Tonic">Grundton 0 = C … 11 = H.</param>
/// <param name="Minor">Moll (sonst Dur).</param>
/// <param name="Confidence">Wie eindeutig die Tonart ist (0 … 1).</param>
public sealed record KeyResult(int Tonic, bool Minor, double Confidence)
{
    public ScaleKind Scale => Minor ? ScaleKind.Minor : ScaleKind.Major;

    /// <summary>„a-Moll“, „Es-Dur“ – deutsche Tonnamen (Moll klein geschrieben).</summary>
    public string Name => Minor ? $"{GermanNames[Tonic].ToLowerInvariant()}-Moll" : $"{GermanNames[Tonic]}-Dur";

    private static readonly string[] GermanNames = ["C", "Cis", "D", "Es", "E", "F", "Fis", "G", "As", "A", "B", "H"];
}

/// <summary>Erkennt Tempo (BPM) und Tonart einer Aufnahme – z. B. um das Raster oder Autotune passend einzustellen.</summary>
public static class MusicAnalysis
{
    private const double FramesPerSecond = 200; // Auflösung der Einsatz-Kurve

    // Krumhansl-Kessler-Profile: wie stark jede Tonstufe in Dur bzw. Moll vorkommt.
    private static readonly double[] MajorProfile = [6.35, 2.23, 3.48, 2.33, 4.38, 4.09, 2.52, 5.19, 2.39, 3.66, 2.29, 2.88];
    private static readonly double[] MinorProfile = [6.33, 2.68, 3.52, 5.38, 2.60, 3.53, 2.54, 4.75, 3.98, 2.69, 3.34, 3.17];

    /// <summary>Tempo zwischen 60 und 180 BPM (null bei zu kurzem oder rhythmuslosem Audio).</summary>
    public static TempoResult? DetectTempo(AudioBuffer buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        var onset = OnsetStrength(buffer, out double fps);
        if (onset.Length < fps * 3)
        {
            return null;
        }

        // 1. Grob (Schritte von 0,2 %): Kammfilter über die Einsatzkurve, leicht bevorzugt um 110 BPM (wie Menschen Takt wahrnehmen).
        double bestScore = 0;
        double bestBpm = 0;
        var scores = new List<double>();
        for (double bpm = 60; bpm <= 180; bpm *= 1.002)
        {
            double score = CombScore(onset, 60 * fps / bpm, out _);
            double prior = Math.Exp(-0.5 * Math.Pow(Math.Log2(bpm / 110) / 1.0, 2));
            score *= prior;
            scores.Add(score);
            if (score > bestScore)
            {
                bestScore = score;
                bestBpm = bpm;
            }
        }

        if (bestScore <= 0)
        {
            return null;
        }

        // 2. Fein: ±0,5 % in 0,02er-Schritten.
        double fineBpm = Refine(onset, fps, bestBpm, CombScore, out double phase);

        // 3. Doppelt oder halb so schnell? Wir beginnen beim langsamsten Kandidaten im Bereich und verdoppeln,
        //    solange zwischen den Schlägen fast genauso viel passiert wie auf ihnen – dann ist das schnellere
        //    Raster der eigentliche Grundschlag (z. B. Bass-Trommel auf 1 und 3, Snare auf 2 und 4).
        double tempo = fineBpm / 2 >= 60 ? Refine(onset, fps, fineBpm / 2, CombScore, out phase) : fineBpm;
        while (tempo * 2 <= 180 && OffbeatRatio(onset, 60 * fps / tempo) >= 0.93)
        {
            tempo = Refine(onset, fps, tempo * 2, CombScore, out phase);
        }

        fineBpm = tempo;
        double mean = scores.Average();
        double confidence = mean > 0 ? Math.Clamp((bestScore / mean - 1) / 2, 0, 1) : 0;
        return new TempoResult(Math.Round(fineBpm, 1), phase / fps, confidence);
    }

    private delegate double TempoScore(double[] onset, double period, out double phase);

    private static double Refine(double[] onset, double fps, double around, TempoScore scoring, out double phase)
    {
        double fineBpm = around;
        double fineScore = double.MinValue;
        phase = 0;
        for (double bpm = around * 0.995; bpm <= around * 1.005; bpm += 0.02)
        {
            double score = scoring(onset, 60 * fps / bpm, out double p);
            if (score > fineScore)
            {
                fineScore = score;
                fineBpm = bpm;
                phase = p;
            }
        }

        return fineBpm;
    }

    /// <summary>Tonart nach dem Krumhansl-Schmuckler-Verfahren (Tonhöhenverteilung gegen Dur-/Moll-Profile).</summary>
    public static KeyResult? DetectKey(AudioBuffer buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        var mono = NoiseProfile.Mono(buffer);
        int size = Stft.Size * 2; // 4096 für feine Auflösung im Bass
        if (mono.Length < size)
        {
            return null;
        }

        var chroma = new double[12];
        var re = new double[size];
        var im = new double[size];
        var window = Enumerable.Range(0, size).Select(i => 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / size)).ToArray();
        double binHz = buffer.SampleRate / (double)size;
        int firstBin = (int)Math.Ceiling(55 / binHz);
        int lastBin = Math.Min(size / 2, (int)(4200 / binHz));
        for (int start = 0; start + size <= mono.Length; start += size / 2)
        {
            for (int i = 0; i < size; i++)
            {
                re[i] = mono[start + i] * window[i];
                im[i] = 0;
            }

            Fft.Transform(re, im);
            for (int k = firstBin; k <= lastBin; k++)
            {
                double pitch = 69 + 12 * Math.Log2(k * binHz / 440);
                int pitchClass = ((int)Math.Round(pitch) % 12 + 12) % 12;
                chroma[pitchClass] += Math.Sqrt(Math.Sqrt(re[k] * re[k] + im[k] * im[k])); // gedämpft: laute Töne dominieren nicht
            }
        }

        if (chroma.Sum() <= 1e-9)
        {
            return null;
        }

        var results = new List<(int Tonic, bool Minor, double R)>();
        for (int tonic = 0; tonic < 12; tonic++)
        {
            var rotated = Enumerable.Range(0, 12).Select(i => chroma[(tonic + i) % 12]).ToArray();
            results.Add((tonic, false, Correlation(rotated, MajorProfile)));
            results.Add((tonic, true, Correlation(rotated, MinorProfile)));
        }

        var ordered = results.OrderByDescending(r => r.R).ToArray();
        double confidence = Math.Clamp((ordered[0].R - ordered[1].R) * 5 + ordered[0].R * 0.5, 0, 1);
        return new KeyResult(ordered[0].Tonic, ordered[0].Minor, confidence);
    }

    /// <summary>Einsatzkurve: wie stark das Spektrum von Moment zu Moment zunimmt (Anschläge, Schläge).</summary>
    private static double[] OnsetStrength(AudioBuffer buffer, out double framesPerSecond)
    {
        var mono = NoiseProfile.Mono(buffer);
        int hop = Math.Max(1, (int)(buffer.SampleRate / FramesPerSecond));
        framesPerSecond = (double)buffer.SampleRate / hop; // exakt, da hop gerundet ist
        const int size = 1024;
        var window = Enumerable.Range(0, size).Select(i => 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / size)).ToArray();
        var re = new double[size];
        var im = new double[size];
        var previous = new double[size / 2];
        var flux = new List<double>();
        var lowFlux = new List<double>();
        int lowBins = Math.Max(2, (int)(200.0 * size / buffer.SampleRate)); // Bassbereich bis 200 Hz (Bass-Trommel, Bass)
        for (int start = 0; start + size <= mono.Length; start += hop)
        {
            for (int i = 0; i < size; i++)
            {
                re[i] = mono[start + i] * window[i];
                im[i] = 0;
            }

            Fft.Transform(re, im);
            double sum = 0;
            double low = 0;
            for (int k = 1; k < size / 2; k++)
            {
                double magnitude = Math.Log(1 + 100 * Math.Sqrt(re[k] * re[k] + im[k] * im[k]));
                double increase = Math.Max(0, magnitude - previous[k]);
                sum += increase;
                if (k <= lowBins)
                {
                    low += increase;
                }

                previous[k] = magnitude;
            }

            flux.Add(sum);
            lowFlux.Add(low);
        }

        // Der Bassbereich zählt doppelt: Schläge der Bass-Trommel bestimmen, was wir als Grundschlag hören –
        // so wird nicht versehentlich das Achtel-Hi-Hat zum Tempo.
        double fullMean = Math.Max(1e-9, flux.Average());
        double lowMean = Math.Max(1e-9, lowFlux.Average());
        for (int i = 0; i < flux.Count; i++)
        {
            flux[i] = flux[i] / fullMean + 2 * lowFlux[i] / lowMean;
        }

        // Gleitenden Mittelwert (0,5 s) abziehen, nur Zunahmen behalten.
        var result = new double[flux.Count];
        int half = (int)(FramesPerSecond / 4);
        for (int i = 0; i < flux.Count; i++)
        {
            int from = Math.Max(0, i - half);
            int to = Math.Min(flux.Count - 1, i + half);
            double mean = 0;
            for (int j = from; j <= to; j++)
            {
                mean += flux[j];
            }

            result[i] = Math.Max(0, flux[i] - mean / (to - from + 1));
        }

        // Leicht glätten (±10 ms), damit kleine Abweichungen der Schlagzeit nicht ins Gewicht fallen.
        double[] kernel = [0.25, 0.5, 1, 0.5, 0.25];
        var smoothed = new double[result.Length];
        for (int i = 0; i < result.Length; i++)
        {
            double sum = 0;
            for (int k = 0; k < kernel.Length; k++)
            {
                int j = i + k - kernel.Length / 2;
                if (j >= 0 && j < result.Length)
                {
                    sum += result[j] * kernel[k];
                }
            }

            smoothed[i] = sum / 2.5;
        }

        return smoothed;
    }

    /// <summary>Wie gut ein gleichmäßiger Schlag mit <paramref name="period"/> (Einsatz-Frames) auf die Kurve passt.</summary>
    private static double CombScore(double[] onset, double period, out double bestPhase)
    {
        bestPhase = 0;
        double best = 0;
        int steps = (int)Math.Ceiling(period);
        for (int p = 0; p < steps; p++)
        {
            // Halbe Schläge zählen etwas mit (Achtel), damit das Grundtempo gewinnt.
            double score = CombMean(onset, p, period) + 0.25 * CombMean(onset, p + period / 2, period);
            if (score > best)
            {
                best = score;
                bestPhase = p;
            }
        }

        return best;
    }

    /// <summary>Verhältnis der Stärke zwischen den Schlägen zu der auf den Schlägen (0 = nur Schläge, 1 = gleich stark).</summary>
    private static double OffbeatRatio(double[] onset, double period)
    {
        double bestOn = 0;
        double between = 0;
        int steps = (int)Math.Ceiling(period);
        for (int p = 0; p < steps; p++)
        {
            double on = PeakMean(onset, p, period);
            if (on > bestOn)
            {
                bestOn = on;
                between = PeakMean(onset, p + period / 2, period);
            }
        }

        return bestOn > 0 ? between / bestOn : 1;
    }

    // Wie CombMean, nimmt aber den Höchstwert in ±15 ms – unempfindlich gegen kleine Abweichungen der Schlagzeit.
    private static double PeakMean(double[] onset, double start, double period)
    {
        double sum = 0;
        int count = 0;
        for (double t = start; t < onset.Length; t += period)
        {
            int center = (int)Math.Round(t);
            double peak = 0;
            for (int i = Math.Max(0, center - 3); i <= Math.Min(onset.Length - 1, center + 3); i++)
            {
                peak = Math.Max(peak, onset[i]);
            }

            sum += peak;
            count++;
        }

        return count > 0 ? sum / count : 0;
    }

    private static double CombMean(double[] onset, double start, double period)
    {
        double sum = 0;
        int count = 0;
        for (double t = start; t < onset.Length - 1; t += period)
        {
            int i = (int)t;
            double frac = t - i;
            sum += onset[i] * (1 - frac) + onset[i + 1] * frac;
            count++;
        }

        return count > 0 ? sum / count : 0;
    }

    private static double Correlation(double[] a, double[] b)
    {
        double meanA = a.Average();
        double meanB = b.Average();
        double ab = 0, aa = 0, bb = 0;
        for (int i = 0; i < a.Length; i++)
        {
            ab += (a[i] - meanA) * (b[i] - meanB);
            aa += (a[i] - meanA) * (a[i] - meanA);
            bb += (b[i] - meanB) * (b[i] - meanB);
        }

        return aa <= 0 || bb <= 0 ? 0 : ab / Math.Sqrt(aa * bb);
    }
}
