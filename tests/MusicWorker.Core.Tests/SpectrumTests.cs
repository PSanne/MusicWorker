using MusicWorker.Core.Dsp;

namespace MusicWorker.Core.Tests;

public class SpectrumTests
{
    private const int Bands = 60;

    private static int BandOf(double frequency) => (int)(SpectrumScale.ToPosition(frequency) * Bands);

    private static float[] Analyze(double frequency, float amplitude, int sampleRate = 44100)
    {
        var analyzer = new SpectrumAnalyzer(sampleRate);
        var signal = Signals.Sine(frequency, 0.2, amplitude, sampleRate, channels: 2);
        analyzer.Write(signal.Samples, 2);
        return analyzer.GetBands(Bands);
    }

    [Fact]
    public void Fft_finds_a_sine_in_the_right_bin()
    {
        int n = 64;
        var real = Enumerable.Range(0, n).Select(i => Math.Cos(2 * Math.PI * 5 * i / n)).ToArray();
        var imaginary = new double[n];

        Fft.Transform(real, imaginary);

        double Magnitude(int k) => Math.Sqrt(real[k] * real[k] + imaginary[k] * imaginary[k]);
        Assert.Equal(n / 2.0, Magnitude(5), 1e-9);
        Assert.All(Enumerable.Range(0, n / 2).Where(k => k != 5), k => Assert.True(Magnitude(k) < 1e-9));
    }

    [Theory]
    [InlineData(1000)]
    [InlineData(100)]
    [InlineData(40)]
    [InlineData(8000)]
    public void Sine_shows_up_in_its_band_with_correct_level(double frequency)
    {
        var bands = Analyze(frequency, amplitude: 0.5f);

        int band = BandOf(frequency);
        Assert.Equal(-6.02, bands[band], 2.0); // 0,5 = -6 dBFS (je nach Lage im Band bis ~1,7 dB Abweichung)
        Assert.InRange(Array.IndexOf(bands, bands.Max()), band - 1, band + 1);
    }

    [Fact]
    public void Distant_bands_stay_quiet()
    {
        var bands = Analyze(1000, amplitude: 0.5f);

        Assert.True(bands[BandOf(100)] < -60, $"100 Hz: {bands[BandOf(100)]} dB");
        Assert.True(bands[BandOf(10000)] < -60, $"10 kHz: {bands[BandOf(10000)]} dB");
    }

    [Fact]
    public void Silence_reads_as_floor()
    {
        var bands = new SpectrumAnalyzer(44100).GetBands(Bands);

        Assert.All(bands, b => Assert.Equal(SpectrumAnalyzer.FloorDb, b, 1e-6));
    }

    [Fact]
    public void Scale_puts_equalizer_bands_in_the_middle_of_their_columns()
    {
        var centers = SpectrumScale.CenterFrequencies(15);

        for (int i = 0; i < 15; i++)
        {
            // Die genormten Bandfrequenzen sind gerundet (z. B. „2,5 kHz“ = 2,54 kHz) – höchstens ~1,6 % Abweichung.
            Assert.Equal(EqualizerSettings.BandFrequencies[i], centers[i], EqualizerSettings.BandFrequencies[i] * 0.02);
        }
    }

    [Fact]
    public void Response_of_neutral_equalizer_is_flat()
    {
        var response = EqualizerSettings.Neutral.ResponseDb(44100, SpectrumScale.CenterFrequencies(100));

        Assert.All(response, db => Assert.Equal(0, db, 1e-9));
    }

    [Fact]
    public void Response_matches_band_gain_and_preamp()
    {
        var gains = new double[15];
        gains[8] = 12;  // 1 kHz
        gains[13] = -6; // 10 kHz
        var settings = new EqualizerSettings(gains, preampDb: -2);

        var response = settings.ResponseDb(44100, [1000, 10000, 100]);

        Assert.Equal(10, response[0], 0.3);
        Assert.Equal(-8, response[1], 0.3);
        Assert.Equal(-2, response[2], 0.3);
    }

    [Fact]
    public void Response_matches_what_the_processor_really_does()
    {
        var gains = new double[15];
        gains[10] = 9; // 2,5 kHz
        var settings = new EqualizerSettings(gains);
        var input = Signals.Sine(2000, 1, amplitude: 0.1f);
        var output = new Effects.EqualizerEffect(settings, preventClipping: false).Apply(input);

        double measuredDb = 20 * Math.Log10(Signals.Rms(output) / Signals.Rms(input));

        Assert.Equal(settings.ResponseDb(44100, [2000])[0], measuredDb, 0.2);
    }
}
