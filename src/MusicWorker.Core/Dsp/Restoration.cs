using MusicWorker.Core.Audio;
using MusicWorker.Core.Effects;

namespace MusicWorker.Core.Dsp;

/// <summary>
/// Entfernt Knackser und Knistern (Schallplatte, Kassette, Schnittfehler): Ein Vorhersagemodell (lineare Prädiktion)
/// sagt jedes Sample aus seinen Vorgängern voraus. Wo die Abweichung plötzlich weit über dem Üblichen liegt, sitzt ein
/// Knackser. Diese kurzen Stellen werden aus den Samples davor und danach neu berechnet.
/// </summary>
/// <param name="sensitivity">1 (nur deutliche Knackser) … 10 (auch leises Knistern).</param>
/// <param name="maxClickMs">Längste Störung, die noch als Knackser gilt.</param>
public sealed class DeclickEffect(double sensitivity = 5, double maxClickMs = 2) : IAudioEffect
{
    private const int Order = 24;
    private const int Block = 4096;

    public double Sensitivity { get; } = Math.Clamp(sensitivity, 1, 10);

    public double MaxClickMs { get; } = Math.Clamp(maxClickMs, 0.2, 10);

    /// <summary>Anzahl repariert Stellen beim letzten Durchlauf.</summary>
    public int RepairedClicks { get; private set; }

    public string Name => "Knackser entfernen";

    public AudioBuffer Apply(AudioBuffer input)
    {
        ArgumentNullException.ThrowIfNull(input);
        int channels = input.Channels;
        var output = input.Samples.ToArray();
        int maxLength = Math.Max(1, (int)(MaxClickMs / 1000 * input.SampleRate));
        double threshold = 12 - Sensitivity; // in robusten Standardabweichungen: 11 (streng) … 2 (empfindlich)
        int repaired = 0;
        for (int c = 0; c < channels; c++)
        {
            var x = new double[input.FrameCount];
            for (int f = 0; f < x.Length; f++)
            {
                x[f] = output[f * channels + c];
            }

            repaired += ProcessChannel(x, maxLength, threshold);
            for (int f = 0; f < x.Length; f++)
            {
                output[f * channels + c] = (float)x[f];
            }
        }

        RepairedClicks = repaired;
        return new AudioBuffer(output, input.SampleRate, channels);
    }

    private static int ProcessChannel(double[] x, int maxLength, double threshold)
    {
        int repaired = 0;
        int n = x.Length;
        for (int start = 0; start < n; start += Block)
        {
            int length = Math.Min(Block, n - start);
            if (length <= Order * 4)
            {
                continue;
            }

            var coefficients = Lpc(x, start, length, Order);
            var error = new double[length];
            for (int i = Order; i < length; i++)
            {
                error[i] = x[start + i] - Predict(x, start + i, coefficients);
            }

            // Robuste Streuung der Vorhersagefehler (Median der Beträge): einzelne Knackser verfälschen sie nicht.
            var magnitudes = error.Skip(Order).Select(Math.Abs).OrderBy(v => v).ToArray();
            double sigma = Math.Max(1e-6, magnitudes[magnitudes.Length / 2] * 1.4826);
            double limit = threshold * sigma;

            int i2 = Order;
            while (i2 < length)
            {
                if (Math.Abs(error[i2]) <= limit)
                {
                    i2++;
                    continue;
                }

                // Zusammenhängende Störung finden (kurze Lücken dazwischen gehören dazu).
                int from = i2;
                int to = i2;
                int gap = 0;
                while (to + 1 < length && gap < 4)
                {
                    to++;
                    gap = Math.Abs(error[to]) > limit * 0.5 ? 0 : gap + 1;
                }

                to -= gap;
                int runStart = Math.Max(Order, from - 2);
                int runEnd = Math.Min(length - 1, to + Order / 2); // der Fehler „klingt“ im Modell nach
                if (runEnd - runStart + 1 <= maxLength * 2 && start + runStart > Order && start + runEnd + Order < n)
                {
                    Interpolate(x, start + runStart, start + runEnd, coefficients);
                    repaired++;
                }

                i2 = runEnd + 1;
            }
        }

        return repaired;
    }

    /// <summary>Füllt [from, to] aus einer Vorwärts- und einer Rückwärtsvorhersage, sanft überblendet.</summary>
    private static void Interpolate(double[] x, int from, int to, double[] a)
    {
        int length = to - from + 1;
        var forward = new double[length];
        var backward = new double[length];
        var work = (double[])x.Clone();
        for (int i = 0; i < length; i++)
        {
            work[from + i] = Predict(work, from + i, a);
            forward[i] = work[from + i];
        }

        // Rückwärts: dasselbe Modell auf das zeitlich umgekehrte Signal (für stationären Klang gleichwertig).
        for (int i = length - 1; i >= 0; i--)
        {
            int position = from + i;
            double sum = 0;
            for (int k = 0; k < a.Length; k++)
            {
                int index = position + 1 + k;
                sum += a[k] * (index <= to ? backward[index - from] : x[index]);
            }

            backward[i] = sum;
        }

        for (int i = 0; i < length; i++)
        {
            double w = (i + 1.0) / (length + 1);
            x[from + i] = forward[i] * (1 - w) + backward[i] * w;
        }
    }

    private static double Predict(double[] x, int position, double[] a)
    {
        double sum = 0;
        for (int k = 0; k < a.Length; k++)
        {
            sum += a[k] * x[position - 1 - k];
        }

        return sum;
    }

    /// <summary>Koeffizienten der linearen Prädiktion (Autokorrelation + Levinson-Durbin).</summary>
    private static double[] Lpc(double[] x, int start, int length, int order)
    {
        var r = new double[order + 1];
        for (int lag = 0; lag <= order; lag++)
        {
            double sum = 0;
            for (int i = lag; i < length; i++)
            {
                double window = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (length - 1));
                double windowLag = 0.5 - 0.5 * Math.Cos(2 * Math.PI * (i - lag) / (length - 1));
                sum += x[start + i] * window * x[start + i - lag] * windowLag;
            }

            r[lag] = sum;
        }

        r[0] *= 1.0001; // leichte Regularisierung gegen Instabilität
        var a = new double[order];
        if (r[0] <= 1e-12)
        {
            return a;
        }

        var previous = new double[order];
        double error = r[0];
        for (int i = 0; i < order; i++)
        {
            double acc = r[i + 1];
            for (int j = 0; j < i; j++)
            {
                acc -= previous[j] * r[i - j];
            }

            double k = acc / error;
            a[i] = k;
            for (int j = 0; j < i; j++)
            {
                a[j] = previous[j] - k * previous[i - 1 - j];
            }

            error *= 1 - k * k;
            if (error <= 1e-12)
            {
                break;
            }

            Array.Copy(a, previous, order);
        }

        return a;
    }
}

/// <summary>Wie ein markiertes Störgeräusch im Spektrum behandelt wird.</summary>
public enum SpectralRepairMode
{
    /// <summary>Mit dem Klang davor und danach ersetzen (für Husten, Klingeln, Stuhlknarren …).</summary>
    Replace,

    /// <summary>Nur um einen festen Betrag leiser machen.</summary>
    Attenuate,
}

/// <summary>
/// Spektrale Reparatur: Ein Zeit- und Frequenzbereich (z. B. ein Telefonklingeln bei 2–4 kHz) wird im Spektrum
/// gezielt leiser gemacht oder durch den Klang der Umgebung ersetzt. Alles außerhalb bleibt unverändert.
/// </summary>
/// <param name="startFrame">Beginn der Störung im übergebenen Audio.</param>
/// <param name="endFrame">Ende der Störung.</param>
/// <param name="lowHz">Untere Frequenz (0 = ganz unten).</param>
/// <param name="highHz">Obere Frequenz (≥ halbe Abtastrate = ganz oben).</param>
/// <param name="mode">Ersetzen durch die Umgebung oder nur abschwächen.</param>
/// <param name="reductionDb">Abschwächung in dB (nur beim Abschwächen).</param>
public sealed class SpectralRepairEffect(
    long startFrame, long endFrame, double lowHz, double highHz, SpectralRepairMode mode = SpectralRepairMode.Replace, double reductionDb = 24) : IAudioEffect
{
    /// <summary>So viel Umgebung (Sekunden) wird vor und nach der Stelle zum Vergleich herangezogen.</summary>
    public const double ContextSeconds = 0.3;

    public string Name => mode == SpectralRepairMode.Replace ? "Störgeräusch ersetzen" : "Störgeräusch abschwächen";

    public AudioBuffer Apply(AudioBuffer input)
    {
        ArgumentNullException.ThrowIfNull(input);
        int rate = input.SampleRate;
        int channels = input.Channels;
        int size = Stft.Size;
        int hop = Stft.Hop;
        double binHz = rate / (double)size;
        int lowBin = Math.Clamp((int)Math.Floor(lowHz / binHz), 0, size / 2);
        int highBin = Math.Clamp((int)Math.Ceiling(highHz / binHz), lowBin, size / 2);
        double floor = Decibels.ToGain(-Math.Clamp(reductionDb, 0, 60));
        var output = new float[input.Samples.Length];
        for (int c = 0; c < channels; c++)
        {
            var mono = new float[input.FrameCount];
            for (int f = 0; f < mono.Length; f++)
            {
                mono[f] = input.GetSample(f, c);
            }

            // Erste Analyse: Beträge aller Fenster, um die Umgebung vor und nach der Störung zu kennen.
            var magnitudes = new List<double[]>();
            Stft.Process(mono, (_, re, im) =>
            {
                var m = new double[size / 2 + 1];
                for (int k = 0; k <= size / 2; k++)
                {
                    m[k] = Math.Sqrt(re[k] * re[k] + im[k] * im[k]);
                }

                magnitudes.Add(m);
            });

            // Fenster-Index ↔ Zeit: Fenster i deckt (i·Hop − Size) … (i·Hop) ab (Stft legt vorne Size Nullen an).
            int FrameIndexOf(long frame) => (int)Math.Floor((frame + size / 2.0) / hop);
            int first = Math.Clamp(FrameIndexOf(startFrame), 0, magnitudes.Count - 1);
            int last = Math.Clamp(FrameIndexOf(endFrame), first, magnitudes.Count - 1);
            int context = Math.Max(2, (int)(ContextSeconds * rate / hop));
            var before = Average(magnitudes, Math.Max(0, first - context - 2), Math.Max(0, first - 2));
            var after = Average(magnitudes, Math.Min(magnitudes.Count - 1, last + 2), Math.Min(magnitudes.Count, last + 2 + context));

            var processed = Stft.Process(mono, (index, re, im) =>
            {
                if (index < first - 2 || index > last + 2)
                {
                    return;
                }

                // Weiche Ränder in der Zeit (2 Fenster) und in der Frequenz (3 Bins).
                double timeWeight = index < first ? (index - (first - 3)) / 3.0 : index > last ? ((last + 3) - index) / 3.0 : 1;
                double t = last > first ? Math.Clamp((index - first) / (double)(last - first), 0, 1) : 0.5;
                for (int k = Math.Max(0, lowBin - 3); k <= Math.Min(size / 2, highBin + 3); k++)
                {
                    double freqWeight = k < lowBin ? 1 - (lowBin - k) / 4.0 : k > highBin ? 1 - (k - highBin) / 4.0 : 1;
                    double weight = Math.Clamp(timeWeight * freqWeight, 0, 1);
                    double magnitude = Math.Sqrt(re[k] * re[k] + im[k] * im[k]) + 1e-12;
                    double gain;
                    if (mode == SpectralRepairMode.Replace)
                    {
                        double target = (before?[k] ?? after?[k] ?? 0) * (1 - t) + (after?[k] ?? before?[k] ?? 0) * t;
                        gain = Math.Max(floor, Math.Min(1, target / magnitude)); // nie lauter machen
                    }
                    else
                    {
                        gain = floor;
                    }

                    double g = 1 - weight * (1 - gain);
                    re[k] *= g;
                    im[k] *= g;
                    if (k > 0 && k < size / 2)
                    {
                        re[size - k] *= g;
                        im[size - k] *= g;
                    }
                }
            });

            for (int f = 0; f < processed.Length; f++)
            {
                output[f * channels + c] = processed[f];
            }
        }

        return new AudioBuffer(output, rate, channels);
    }

    private static double[]? Average(List<double[]> frames, int from, int to)
    {
        if (to <= from)
        {
            return null;
        }

        var result = new double[frames[0].Length];
        for (int i = from; i < to; i++)
        {
            for (int k = 0; k < result.Length; k++)
            {
                result[k] += frames[i][k] / (to - from);
            }
        }

        return result;
    }
}
