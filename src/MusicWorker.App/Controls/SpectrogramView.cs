using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MusicWorker.Core.Audio;
using MusicWorker.Core.Editing;
using MusicWorker.Core.Visualization;

namespace MusicWorker.App.Controls;

/// <summary>
/// Spektrogramm: Zeit waagerecht (derselbe Ausschnitt wie die Wellenform darüber), Frequenz senkrecht
/// (logarithmisch, 30 Hz … Nyquist), Lautstärke als Farbe (dunkel = leise, hell = laut). Ideal, um Brummen,
/// Rauschen, Zischlaute oder Obertöne zu sehen. Klick setzt die Abspielposition, Ziehen markiert einen Bereich.
/// </summary>
public sealed class SpectrogramView : FrameworkElement
{
    private const double AxisWidth = 48;

    public static readonly DependencyProperty BufferProperty = DependencyProperty.Register(
        nameof(Buffer), typeof(AudioBuffer), typeof(SpectrogramView),
        new FrameworkPropertyMetadata(null, (d, _) => ((SpectrogramView)d).Recompute()));

    public static readonly DependencyProperty SelectionProperty = DependencyProperty.Register(
        nameof(Selection), typeof(SelectionRange), typeof(SpectrogramView),
        new FrameworkPropertyMetadata(default(SelectionRange), FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty PlayheadFrameProperty = DependencyProperty.Register(
        nameof(PlayheadFrame), typeof(int), typeof(SpectrogramView), new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SeekCommandProperty = DependencyProperty.Register(
        nameof(SeekCommand), typeof(ICommand), typeof(SpectrogramView), new PropertyMetadata(null));

    private static readonly Brush Background = Frozen(new SolidColorBrush(Color.FromRgb(0x0B, 0x0B, 0x12)));
    private static readonly Brush AxisBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x22, 0x25, 0x2C)));
    private static readonly Brush TextBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x9A, 0xA0, 0xAA)));
    private static readonly Brush SelectionBrush = Frozen(new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)));
    private static readonly Pen GridPen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF))), 1));
    private static readonly Pen PlayheadPen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromRgb(0xFF, 0x52, 0x52))), 1.5));
    private static readonly Typeface LabelTypeface = new("Segoe UI");
    private static readonly uint[] Palette = BuildPalette();

    private CancellationTokenSource? _computing;
    private WriteableBitmap? _bitmap;
    private Spectrogram? _spectrogram;
    private double _viewStart;
    private double _viewLength;
    private bool _dragging;
    private int _dragAnchor;

    public SpectrogramView()
    {
        ClipToBounds = true;
        SizeChanged += (_, _) => Recompute();
    }

    public AudioBuffer? Buffer { get => (AudioBuffer?)GetValue(BufferProperty); set => SetValue(BufferProperty, value); }

    public SelectionRange Selection { get => (SelectionRange)GetValue(SelectionProperty); set => SetValue(SelectionProperty, value); }

    public int PlayheadFrame { get => (int)GetValue(PlayheadFrameProperty); set => SetValue(PlayheadFrameProperty, value); }

    /// <summary>Parameter: Ziel-Frame (int).</summary>
    public ICommand? SeekCommand { get => (ICommand?)GetValue(SeekCommandProperty); set => SetValue(SeekCommandProperty, value); }

    /// <summary>Denselben Ausschnitt wie die Wellenform zeigen.</summary>
    public void SetView(double start, double length)
    {
        if (Math.Abs(start - _viewStart) < 0.5 && Math.Abs(length - _viewLength) < 0.5)
        {
            return;
        }

        _viewStart = start;
        _viewLength = length;
        Recompute();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double width = ActualWidth;
        double height = ActualHeight;
        dc.DrawRectangle(Background, null, new Rect(0, 0, width, height));
        if (width <= AxisWidth + 4 || Buffer is not { IsEmpty: false } buffer)
        {
            return;
        }

        var area = new Rect(AxisWidth, 0, width - AxisWidth, height);
        if (_bitmap is not null)
        {
            dc.DrawImage(_bitmap, area);
        }

        DrawAxis(dc, height, buffer.SampleRate);

        var selection = Selection;
        if (!selection.IsEmpty)
        {
            double x0 = Math.Max(AxisWidth, FrameToX(selection.Start));
            double x1 = Math.Min(width, FrameToX(selection.End));
            if (x1 > x0)
            {
                dc.DrawRectangle(SelectionBrush, null, new Rect(x0, 0, x1 - x0, height));
            }
        }

        double playhead = FrameToX(PlayheadFrame);
        if (playhead >= AxisWidth && playhead <= width)
        {
            dc.DrawLine(PlayheadPen, new Point(playhead, 0), new Point(playhead, height));
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        int frame = XToFrame(e.GetPosition(this).X);
        _dragAnchor = frame;
        _dragging = CaptureMouse();
        Selection = default;
        if (SeekCommand is { } seek && seek.CanExecute(frame))
        {
            seek.Execute(frame);
        }

        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging)
        {
            int frame = XToFrame(e.GetPosition(this).X);
            Selection = new SelectionRange(Math.Min(frame, _dragAnchor), Math.Max(frame, _dragAnchor));
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        _dragging = false;
        ReleaseMouseCapture();
    }

    private double FrameToX(double frame) => AxisWidth + (frame - _viewStart) / Math.Max(1, _viewLength) * (ActualWidth - AxisWidth);

    private int XToFrame(double x)
    {
        int frames = Buffer?.FrameCount ?? 0;
        return (int)Math.Clamp(_viewStart + (x - AxisWidth) / Math.Max(1, ActualWidth - AxisWidth) * _viewLength, 0, frames);
    }

    private void DrawAxis(DrawingContext dc, double height, int sampleRate)
    {
        dc.DrawRectangle(AxisBrush, null, new Rect(0, 0, AxisWidth, height));
        if (_spectrogram is not { } spectrogram)
        {
            return;
        }

        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        foreach (double hz in new double[] { 50, 100, 200, 500, 1000, 2000, 5000, 10000, 20000 })
        {
            if (hz >= sampleRate / 2.0)
            {
                continue;
            }

            double row = Math.Log(hz / spectrogram.MaxHz) / Math.Log(spectrogram.MinHz / spectrogram.MaxHz);
            double y = row * height;
            dc.DrawLine(GridPen, new Point(AxisWidth - 4, y), new Point(ActualWidth, y));
            string label = hz >= 1000 ? $"{hz / 1000:0.#} kHz" : $"{hz:0} Hz";
            var text = new FormattedText(label, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, LabelTypeface, 9, TextBrush, pixelsPerDip);
            dc.DrawText(text, new Point(3, Math.Clamp(y - text.Height / 2, 0, height - text.Height)));
        }
    }

    /// <summary>Spektrogramm im Hintergrund neu berechnen (verzögert, damit Zoomen flüssig bleibt).</summary>
    private async void Recompute()
    {
        _computing?.Cancel();
        if (Buffer is not { IsEmpty: false } buffer || ActualWidth <= AxisWidth + 4 || ActualHeight < 10)
        {
            _bitmap = null;
            InvalidateVisual();
            return;
        }

        if (_viewLength <= 0)
        {
            _viewStart = 0;
            _viewLength = buffer.FrameCount;
        }

        using var cts = new CancellationTokenSource();
        _computing = cts;
        int columns = Math.Clamp((int)(ActualWidth - AxisWidth), 1, 2400);
        int rows = Math.Clamp((int)ActualHeight, 2, 800);
        int start = (int)Math.Clamp(_viewStart, 0, buffer.FrameCount - 1);
        int length = (int)Math.Clamp(_viewLength, 1, buffer.FrameCount - start);
        try
        {
            await Task.Delay(120, cts.Token);
            var spectrogram = await Task.Run(() => Spectrogram.Compute(buffer.Slice(start, length), columns, rows, cancellationToken: cts.Token), cts.Token);
            if (cts.IsCancellationRequested)
            {
                return;
            }

            var bitmap = new WriteableBitmap(spectrogram.Columns, spectrogram.Rows, 96, 96, PixelFormats.Bgra32, null);
            var pixels = new uint[spectrogram.Columns * spectrogram.Rows];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = Palette[(int)(spectrogram.Values[i] * 255)];
            }

            bitmap.WritePixels(new Int32Rect(0, 0, spectrogram.Columns, spectrogram.Rows), pixels, spectrogram.Columns * 4, 0);
            bitmap.Freeze();
            _bitmap = bitmap;
            _spectrogram = spectrogram;
            InvalidateVisual();
        }
        catch (OperationCanceledException)
        {
            // neuer Auftrag unterwegs
        }
        finally
        {
            if (ReferenceEquals(_computing, cts))
            {
                _computing = null;
            }
        }
    }

    /// <summary>Farbskala wie „Magma“: schwarz → violett → rot → orange → hellgelb.</summary>
    private static uint[] BuildPalette()
    {
        (double T, byte R, byte G, byte B)[] stops =
        [
            (0.0, 0, 0, 4), (0.25, 59, 15, 112), (0.5, 183, 55, 121), (0.75, 252, 137, 97), (1.0, 252, 253, 191),
        ];
        var palette = new uint[256];
        for (int i = 0; i < 256; i++)
        {
            double t = i / 255.0;
            int s = 0;
            while (s < stops.Length - 2 && t > stops[s + 1].T)
            {
                s++;
            }

            var (t0, r0, g0, b0) = stops[s];
            var (t1, r1, g1, b1) = stops[s + 1];
            double f = (t - t0) / (t1 - t0);
            byte r = (byte)(r0 + (r1 - r0) * f);
            byte g = (byte)(g0 + (g1 - g0) * f);
            byte b = (byte)(b0 + (b1 - b0) * f);
            palette[i] = 0xFF000000u | ((uint)r << 16) | ((uint)g << 8) | b;
        }

        return palette;
    }

    private static T Frozen<T>(T freezable)
        where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
