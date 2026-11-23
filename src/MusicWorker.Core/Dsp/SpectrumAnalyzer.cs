namespace MusicWorker.Core.Dsp;

/// <summary>
/// Sammelt die zuletzt gespielten Samples (aus dem Audio-Thread) und berechnet daraus auf Abruf
/// (im UI-Thread) das Spektrum in logarithmisch verteilten Bändern.
/// </summary>
public sealed class SpectrumAnalyzer
{
    public const int FftSize = 4096;

    /// <summary>Untergrenze der Anzeige in dBFS.</summary>
    public const double FloorDb = -90;

    private readonly object _lock = new();
    private readonly float[] _ring = new float[FftSize];
    private readonly double[] _window = new double[FftSize];
    private readonly double[] _windowed = new double[FftSize];
    private readonly double[] _real = new double[FftSize];
    private readonly double[] _imaginary = new double[FftSize];
    private readonly double[] _magnitudes = new double[FftSize / 2 + 1];
    private int _writePosition;

    public SpectrumAnalyzer(int sampleRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        SampleRate = sampleRate;
        for (int i = 0; i < FftSize; i++)
        {
            _window[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / FftSize); // Hann
        }
    }

    public int SampleRate { get; }

    /// <summary>Nimmt verschachtelte Samples auf (Mittelwert der Kanäle). Thread-sicher.</summary>
    public void Write(ReadOnlySpan<float> samples, int channels)
    {
        lock (_lock)
        {
            for (int i = 0; i + channels <= samples.Length; i += channels)
            {
                float sum = 0;
                for (int c = 0; c < channels; c++)
                {
                    sum += samples[i + c];
                }

                _ring[_writePosition] = sum / channels;
                _writePosition = (_writePosition + 1) % FftSize;
            }
        }
    }

    /// <summary>Nimmt nur einen Kanal aus verschachtelten Samples auf (für getrennte L/R-Anzeige). Thread-sicher.</summary>
    public void WriteChannel(ReadOnlySpan<float> samples, int channels, int channel)
    {
        lock (_lock)
        {
            for (int i = channel; i < samples.Length; i += channels)
            {
                _ring[_writePosition] = samples[i];
                _writePosition = (_writePosition + 1) % FftSize;
            }
        }
    }

    /// <summary>
    /// Liefert den Pegel (dBFS) für <paramref name="bandCount"/> gleich breite Spalten der
    /// <see cref="SpectrumScale"/>. Ein Vollaussteuerungs-Sinus ergibt 0 dB.
    /// </summary>
    public float[] GetBands(int bandCount)
    {
        lock (_lock)
        {
            for (int i = 0; i < FftSize; i++)
            {
                _windowed[i] = _ring[(_writePosition + i) % FftSize] * _window[i];
            }
        }

        _windowed.CopyTo(_real, 0);
        Array.Clear(_imaginary);

        Fft.Transform(_real, _imaginary);

        // Hann-Fenster (Faktor 0,5) und einseitiges Spektrum (Faktor 2) → Skalierung 4/N.
        for (int k = 0; k < _magnitudes.Length; k++)
        {
            _magnitudes[k] = Math.Sqrt(_real[k] * _real[k] + _imaginary[k] * _imaginary[k]) * 4 / FftSize;
        }

        var bands = new float[bandCount];
        double binsPerHz = FftSize / (double)SampleRate;
        int lastBin = _magnitudes.Length - 1;

        for (int b = 0; b < bandCount; b++)
        {
            double lowBin = SpectrumScale.FromPosition(b / (double)bandCount) * binsPerHz;
            double highBin = SpectrumScale.FromPosition((b + 1) / (double)bandCount) * binsPerHz;
            if (lowBin >= lastBin)
            {
                bands[b] = (float)FloorDb; // oberhalb der halben Abtastrate
                continue;
            }

            double magnitude;
            if (highBin - lowBin < 2)
            {
                // Schmales Band (Bass): Die FFT-Bins liegen hier weiter auseinander als die Bänder.
                // Deshalb den Pegel direkt bei der Mittenfrequenz des Bandes berechnen.
                magnitude = MagnitudeAt((lowBin + highBin) / 2);
            }
            else
            {
                magnitude = 0;
                int first = (int)Math.Ceiling(lowBin);
                int last = Math.Min((int)Math.Floor(highBin), lastBin);
                for (int k = first; k <= last; k++)
                {
                    magnitude = Math.Max(magnitude, _magnitudes[k]);
                }
            }

            bands[b] = (float)Math.Max(FloorDb, 20 * Math.Log10(Math.Max(magnitude, 1e-12)));
        }

        return bands;
    }

    /// <summary>
    /// Betrag des Spektrums bei einer beliebigen (auch gebrochenen) Bin-Position –
    /// eine einzelne DFT-Summe, berechnet mit einem rotierenden Zeiger statt sin/cos je Sample.
    /// </summary>
    private double MagnitudeAt(double bin)
    {
        double omega = 2 * Math.PI * bin / FftSize;
        double stepReal = Math.Cos(omega);
        double stepImaginary = -Math.Sin(omega);
        double phasorReal = 1;
        double phasorImaginary = 0;
        double sumReal = 0;
        double sumImaginary = 0;

        for (int n = 0; n < FftSize; n++)
        {
            sumReal += _windowed[n] * phasorReal;
            sumImaginary += _windowed[n] * phasorImaginary;
            double next = phasorReal * stepReal - phasorImaginary * stepImaginary;
            phasorImaginary = phasorReal * stepImaginary + phasorImaginary * stepReal;
            phasorReal = next;
        }

        return Math.Sqrt(sumReal * sumReal + sumImaginary * sumImaginary) * 4 / FftSize;
    }
}
