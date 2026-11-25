namespace MusicWorker.Core.Dsp;

/// <summary>
/// Wandelt die Abtastrate eines laufenden Datenstroms (in Stücken) um – mit gefensterter Sinc-Interpolation,
/// damit keine hörbaren Verzerrungen entstehen. Für Aufnahmegeräte, die mit einer anderen Rate laufen als das Projekt.
/// </summary>
public sealed class StreamingResampler
{
    private const int HalfTaps = 16;

    private readonly int _channels;
    private readonly double _step; // Eingangs-Samples pro Ausgangs-Sample
    private readonly double _cutoff;
    private readonly List<float> _history = []; // verschachtelt
    private double _position; // nächster Ausgangszeitpunkt in Eingangs-Frames, relativ zum Anfang von _history

    public StreamingResampler(int inputRate, int outputRate, int channels)
    {
        if (inputRate <= 0 || outputRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(inputRate));
        }

        InputRate = inputRate;
        OutputRate = outputRate;
        _channels = Math.Max(1, channels);
        _step = inputRate / (double)outputRate;
        _cutoff = Math.Min(1.0, outputRate / (double)inputRate) * 0.95;
        _position = HalfTaps; // erst nach genug Vorlauf rechnen (Anfangsverzögerung von HalfTaps Frames)
        _history.AddRange(new float[HalfTaps * _channels]);
    }

    public int InputRate { get; }

    public int OutputRate { get; }

    /// <summary>Nimmt neue Eingangs-Samples (verschachtelt) und liefert alle Ausgangs-Samples, die schon berechnet werden können.</summary>
    public float[] Process(ReadOnlySpan<float> input)
    {
        if (InputRate == OutputRate)
        {
            return input.ToArray();
        }

        foreach (float v in input)
        {
            _history.Add(v);
        }

        int frames = _history.Count / _channels;
        var output = new List<float>();
        while (_position + HalfTaps < frames)
        {
            int center = (int)Math.Floor(_position);
            double frac = _position - center;
            for (int c = 0; c < _channels; c++)
            {
                double sum = 0;
                double weights = 0;
                for (int k = -HalfTaps + 1; k <= HalfTaps; k++)
                {
                    double x = k - frac;
                    double w = Kernel(x);
                    sum += _history[((center + k) * _channels) + c] * w;
                    weights += w;
                }

                output.Add((float)(weights != 0 ? sum / weights : 0));
            }

            _position += _step;
        }

        // Nicht mehr benötigte alte Samples verwerfen
        int drop = Math.Max(0, (int)Math.Floor(_position) - HalfTaps);
        if (drop > 0)
        {
            _history.RemoveRange(0, drop * _channels);
            _position -= drop;
        }

        return [.. output];
    }

    private double Kernel(double x)
    {
        double a = x * _cutoff;
        double sinc = Math.Abs(a) < 1e-9 ? 1 : Math.Sin(Math.PI * a) / (Math.PI * a);
        double t = (x / HalfTaps) + 1; // Blackman-Fenster über [-HalfTaps, HalfTaps]
        double window = (t is < 0 or > 2) ? 0 : 0.42 - (0.5 * Math.Cos(Math.PI * t)) + (0.08 * Math.Cos(2 * Math.PI * t));
        return sinc * window;
    }
}
