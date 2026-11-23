namespace MusicWorker.Core.Dsp;

/// <summary>Schnelle Fourier-Transformation (iterativer Radix-2-Cooley-Tukey), an Ort und Stelle.</summary>
public static class Fft
{
    /// <summary>Transformiert <paramref name="real"/>/<paramref name="imaginary"/> (Länge = Zweierpotenz).</summary>
    public static void Transform(Span<double> real, Span<double> imaginary)
    {
        int n = real.Length;
        if (n != imaginary.Length || n == 0 || (n & (n - 1)) != 0)
        {
            throw new ArgumentException("Die Länge muss eine Zweierpotenz sein und für beide Teile gleich.");
        }

        // Bit-Umkehr-Permutation
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1)
            {
                j ^= bit;
            }

            j ^= bit;
            if (i < j)
            {
                (real[i], real[j]) = (real[j], real[i]);
                (imaginary[i], imaginary[j]) = (imaginary[j], imaginary[i]);
            }
        }

        // Schmetterlings-Stufen
        for (int length = 2; length <= n; length <<= 1)
        {
            double angle = -2 * Math.PI / length;
            double wReal = Math.Cos(angle);
            double wImaginary = Math.Sin(angle);
            int half = length / 2;

            for (int start = 0; start < n; start += length)
            {
                double curReal = 1;
                double curImaginary = 0;
                for (int k = 0; k < half; k++)
                {
                    int a = start + k;
                    int b = a + half;
                    double tReal = real[b] * curReal - imaginary[b] * curImaginary;
                    double tImaginary = real[b] * curImaginary + imaginary[b] * curReal;
                    real[b] = real[a] - tReal;
                    imaginary[b] = imaginary[a] - tImaginary;
                    real[a] += tReal;
                    imaginary[a] += tImaginary;

                    double nextReal = curReal * wReal - curImaginary * wImaginary;
                    curImaginary = curReal * wImaginary + curImaginary * wReal;
                    curReal = nextReal;
                }
            }
        }
    }
}
