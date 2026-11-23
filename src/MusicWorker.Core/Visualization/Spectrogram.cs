using MusicWorker.Core.Audio;
using MusicWorker.Core.Dsp;

namespace MusicWorker.Core.Visualization;

/// <summary>
/// Spektrogramm: Zeit (waagerecht) × Frequenz (senkrecht, logarithmisch) mit der Lautstärke als Wert 0 … 1
/// (-90 … 0 dB). Jede Spalte ist eine Fourier-Transformation eines kurzen Ausschnitts.
/// </summary>
public sealed class Spectrogram
{
    public const double MinDb = -90;

    private Spectrogram(int columns, int rows, float[] values, double minHz, double maxHz)
    {
        Columns = columns;
        Rows = rows;
        Values = values;
        MinHz = minHz;
        MaxHz = maxHz;
    }

    public int Columns { get; }

    public int Rows { get; }

    /// <summary>Werte zeilenweise von oben (hohe Frequenz) nach unten, 0 … 1.</summary>
    public IReadOnlyList<float> Values { get; }

    public double MinHz { get; }

    public double MaxHz { get; }

    public float this[int column, int row] => Values[row * Columns + column];

    /// <summary>Frequenz einer Zeile (0 = oben).</summary>
    public double FrequencyOfRow(double row) => MaxHz * Math.Pow(MinHz / MaxHz, row / Math.Max(1, Rows - 1));

    /// <summary>Berechnet das Spektrogramm von <paramref name="buffer"/> (Kanäle gemischt) mit der gewünschten Auflösung.</summary>
    public static Spectrogram Compute(AudioBuffer buffer, int columns, int rows, double minHz = 30, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        columns = Math.Max(1, columns);
        rows = Math.Max(2, rows);
        double maxHz = buffer.SampleRate / 2.0;
        var mono = NoiseProfile.Mono(buffer);
        var values = new float[columns * rows];
        int size = Stft.Size;
        var real = new double[size];
        var imaginary = new double[size];
        double binHz = buffer.SampleRate / (double)size;
        double reference = size / 4.0; // Vollaussteuerung eines Sinus ≈ 0 dB

        // Welche Bins gehören zu welcher Zeile (logarithmische Frequenzachse)?
        var rowBins = new (int From, int To)[rows];
        for (int r = 0; r < rows; r++)
        {
            double top = maxHz * Math.Pow(minHz / maxHz, (r - 0.5) / (rows - 1));
            double bottom = maxHz * Math.Pow(minHz / maxHz, (r + 0.5) / (rows - 1));
            int from = Math.Clamp((int)Math.Floor(bottom / binHz), 1, size / 2);
            int to = Math.Clamp((int)Math.Ceiling(top / binHz), from, size / 2);
            rowBins[r] = (from, to);
        }

        for (int c = 0; c < columns; c++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            long center = (long)((c + 0.5) / columns * mono.Length);
            long start = center - size / 2;
            for (int i = 0; i < size; i++)
            {
                long index = start + i;
                real[i] = index >= 0 && index < mono.Length ? mono[index] * Stft.Window[i] : 0;
                imaginary[i] = 0;
            }

            Fft.Transform(real, imaginary);
            for (int r = 0; r < rows; r++)
            {
                var (from, to) = rowBins[r];
                double peak = 0;
                for (int k = from; k <= to; k++)
                {
                    peak = Math.Max(peak, real[k] * real[k] + imaginary[k] * imaginary[k]);
                }

                double db = 10 * Math.Log10(peak / (reference * reference) + 1e-12);
                values[r * columns + c] = (float)Math.Clamp((db - MinDb) / -MinDb, 0, 1);
            }
        }

        return new Spectrogram(columns, rows, values, minHz, maxHz);
    }
}
