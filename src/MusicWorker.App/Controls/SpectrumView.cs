using System.Globalization;
using System.Windows;
using System.Windows.Media;
using MusicWorker.Core.Dsp;

namespace MusicWorker.App.Controls;

/// <summary>
/// Live-Spektrum (Balken, dBFS) mit darübergelegter Equalizer-Kurve (±15 dB).
/// Die Frequenzachse folgt <see cref="SpectrumScale"/>, damit die EQ-Regler darunter genau
/// unter ihrer Frequenz stehen.
/// </summary>
public sealed class SpectrumView : FrameworkElement
{
    public static readonly DependencyProperty SpectraProperty = DependencyProperty.Register(
        nameof(Spectra), typeof(float[][]), typeof(SpectrumView),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty EqualizerCurveProperty = DependencyProperty.Register(
        nameof(EqualizerCurve), typeof(double[]), typeof(SpectrumView),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    private const double CurveRangeDb = 15;
    private const double TopDb = 0;

    private static readonly Brush BackgroundBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x1B, 0x1D, 0x23)));
    private static readonly Brush LabelBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x8A, 0x90, 0x9A)));
    private static readonly Pen GridPen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF))), 1));
    private static readonly Pen ZeroPen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF))), 1));
    private static readonly Pen CurvePen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromRgb(0xFF, 0x9F, 0x1C))), 2)
    {
        LineJoin = PenLineJoin.Round,
    });
    private static readonly Brush CurveFill = Frozen(new SolidColorBrush(Color.FromArgb(0x38, 0xFF, 0x9F, 0x1C)));
    // Farben je Kanal – passend zur Wellenform: links blau, rechts rosa.
    private static readonly Brush[] BarBrushes =
    [
        Frozen(new LinearGradientBrush(Color.FromRgb(0x81, 0xD4, 0xFA), Color.FromRgb(0x15, 0x65, 0xC0), new Point(0, 0), new Point(0, 1))),
        Frozen(new LinearGradientBrush(Color.FromRgb(0xF8, 0xBB, 0xD0), Color.FromRgb(0xC2, 0x18, 0x5B), new Point(0, 0), new Point(0, 1))),
    ];
    private static readonly Typeface LabelTypeface = new("Segoe UI");

    private static readonly (double Frequency, string Label)[] FrequencyGrid =
    [
        (50, "50"), (100, "100"), (200, "200"), (500, "500"),
        (1000, "1k"), (2000, "2k"), (5000, "5k"), (10000, "10k"),
    ];

    public SpectrumView()
    {
        ClipToBounds = true;
        SnapsToDevicePixels = true;
    }

    /// <summary>
    /// Pegel je Spalte in dBFS – ein Array pro Kanal (eins = Summe, zwei = links/rechts);
    /// null = keine Wiedergabe.
    /// </summary>
    public float[][]? Spectra
    {
        get => (float[][]?)GetValue(SpectraProperty);
        set => SetValue(SpectraProperty, value);
    }

    /// <summary>EQ-Frequenzgang in dB, gleichmäßig über die Frequenzachse verteilt.</summary>
    public double[]? EqualizerCurve
    {
        get => (double[]?)GetValue(EqualizerCurveProperty);
        set => SetValue(EqualizerCurveProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        double width = ActualWidth;
        double height = ActualHeight;
        if (width < 10 || height < 10)
        {
            return;
        }

        dc.DrawRectangle(BackgroundBrush, null, new Rect(0, 0, width, height));
        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        double curveMiddle = height / 2;
        double curveScale = (height / 2 - 6) / CurveRangeDb;

        // Raster: Frequenzen (senkrecht) und EQ-Skala ±12/±6 dB (waagerecht)
        foreach (var (frequency, label) in FrequencyGrid)
        {
            double x = SpectrumScale.ToPosition(frequency) * width;
            dc.DrawLine(GridPen, new Point(x, 0), new Point(x, height));
            DrawLabel(dc, label, new Point(x + 2, height - 14), pixelsPerDip);
        }

        foreach (double db in new[] { 12.0, 6.0, -6.0, -12.0 })
        {
            double y = curveMiddle - db * curveScale;
            dc.DrawLine(GridPen, new Point(0, y), new Point(width, y));
        }

        DrawLabel(dc, "+12", new Point(2, curveMiddle - 12 * curveScale - 7), pixelsPerDip);
        DrawLabel(dc, "-12", new Point(2, curveMiddle + 12 * curveScale - 7), pixelsPerDip);

        // Spektrum-Balken: bei Stereo pro Spalte zwei schmale Balken nebeneinander (L | R).
        var spectra = Spectra;
        if (spectra is { Length: > 0 } && spectra[0].Length > 0)
        {
            int columns = spectra[0].Length;
            double columnWidth = width / columns;
            double barWidth = Math.Max(1, (columnWidth - 1.5) / spectra.Length);
            for (int c = 0; c < spectra.Length; c++)
            {
                var brush = BarBrushes[Math.Min(c, BarBrushes.Length - 1)];
                for (int i = 0; i < columns && i < spectra[c].Length; i++)
                {
                    double level = (spectra[c][i] - SpectrumAnalyzer.FloorDb) / (TopDb - SpectrumAnalyzer.FloorDb);
                    double barHeight = Math.Clamp(level, 0, 1) * height;
                    if (barHeight >= 1)
                    {
                        dc.DrawRectangle(brush, null,
                            new Rect(i * columnWidth + 0.5 + c * barWidth, height - barHeight, barWidth, barHeight));
                    }
                }
            }
        }
        else
        {
            DrawLabel(dc, "Spektrum erscheint beim Abspielen", new Point(30, 4), pixelsPerDip);
        }

        // EQ-Kurve mit Füllung zur 0-dB-Linie
        dc.DrawLine(ZeroPen, new Point(0, curveMiddle), new Point(width, curveMiddle));
        if (EqualizerCurve is { Length: > 1 } curve)
        {
            var line = new StreamGeometry();
            var area = new StreamGeometry();
            using (var lineContext = line.Open())
            using (var areaContext = area.Open())
            {
                areaContext.BeginFigure(new Point(0, curveMiddle), isFilled: true, isClosed: true);
                for (int i = 0; i < curve.Length; i++)
                {
                    double x = (i + 0.5) / curve.Length * width;
                    double y = Math.Clamp(curveMiddle - curve[i] * curveScale, 0, height);
                    var point = new Point(x, y);
                    if (i == 0)
                    {
                        lineContext.BeginFigure(point, isFilled: false, isClosed: false);
                    }
                    else
                    {
                        lineContext.LineTo(point, isStroked: true, isSmoothJoin: true);
                    }

                    areaContext.LineTo(point, isStroked: false, isSmoothJoin: false);
                }

                areaContext.LineTo(new Point(width, curveMiddle), isStroked: false, isSmoothJoin: false);
            }

            line.Freeze();
            area.Freeze();
            dc.DrawGeometry(CurveFill, null, area);
            dc.DrawGeometry(null, CurvePen, line);
        }

        // Legende
        bool stereo = spectra is { Length: 2 };
        var legend = new FormattedText(stereo ? "▮ L   ▮ R   ━ EQ-Kurve" : "▮ Spektrum   ━ EQ-Kurve",
            CultureInfo.CurrentCulture, FlowDirection.LeftToRight, LabelTypeface, 10, LabelBrush, pixelsPerDip);
        if (stereo)
        {
            // „L“ und „R“ in ihren Kanalfarben einfärben
            legend.SetForegroundBrush(BarBrushes[0], 0, 1);
            legend.SetForegroundBrush(BarBrushes[1], 6, 1);
        }

        dc.DrawText(legend, new Point(width - legend.Width - 6, 3));
    }

    private static void DrawLabel(DrawingContext dc, string text, Point position, double pixelsPerDip)
    {
        var label = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            LabelTypeface, 10, LabelBrush, pixelsPerDip);
        dc.DrawText(label, position);
    }

    private static T Frozen<T>(T freezable)
        where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
