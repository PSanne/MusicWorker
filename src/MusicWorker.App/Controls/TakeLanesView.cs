using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using MusicWorker.Core.Audio;
using MusicWorker.Core.Recording;
using MusicWorker.Core.Visualization;

namespace MusicWorker.App.Controls;

/// <summary>
/// Comping-Ansicht: jede Aufnahme (Take) eine Bahn. Hell hinterlegt ist, was in der Zusammenstellung klingt.
/// Ziehen über einen Take übernimmt diesen Abschnitt in die Zusammenstellung („Swipe-Comping“); ein Klick
/// wählt den Take und setzt die Abspielposition.
/// </summary>
public sealed class TakeLanesView : FrameworkElement
{
    private const double HeaderWidth = 76;
    private const double RulerHeight = 20;

    private static readonly Brush Background = Frozen(new SolidColorBrush(Color.FromRgb(0x1B, 0x1D, 0x23)));
    private static readonly Brush LaneBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x22, 0x25, 0x2C)));
    private static readonly Brush ActiveBrush = Frozen(new SolidColorBrush(Color.FromArgb(0x50, 0x4F, 0xC3, 0xF7)));
    private static readonly Brush SelectionBrush = Frozen(new SolidColorBrush(Color.FromArgb(0x50, 0xFF, 0xB7, 0x4D)));
    private static readonly Brush HeaderBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x2A, 0x2D, 0x35)));
    private static readonly Brush SelectedHeaderBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x2E, 0x4A, 0x5C)));
    private static readonly Brush TextBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xC8, 0xCC, 0xD4)));
    private static readonly Pen ActiveWave = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromRgb(0x81, 0xD4, 0xFA))), 1));
    private static readonly Pen InactiveWave = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromRgb(0x5A, 0x60, 0x6B))), 1));
    private static readonly Pen LinePen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromRgb(0x30, 0x34, 0x3D))), 1));
    private static readonly Pen PlayheadPen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromRgb(0xFF, 0x52, 0x52))), 1.5));
    private static readonly Typeface LabelTypeface = new("Segoe UI");

    private IReadOnlyList<AudioBuffer> _takes = [];
    private IReadOnlyList<CompSegment> _comp = [];
    private int _dragTake = -1;
    private double _dragFromX;
    private double _dragToX;

    public TakeLanesView()
    {
        ClipToBounds = true;
    }

    /// <summary>Die Zusammenstellung wurde geändert.</summary>
    public event EventHandler? CompChanged;

    /// <summary>Klick an eine Stelle (Frame) – zum Anhören ab dort.</summary>
    public event EventHandler<long>? PositionClicked;

    public IReadOnlyList<AudioBuffer> Takes
    {
        get => _takes;
        set
        {
            _takes = value;
            SelectedTake = Math.Clamp(SelectedTake, 0, Math.Max(0, value.Count - 1));
            InvalidateVisual();
        }
    }

    public IReadOnlyList<CompSegment> Comp
    {
        get => _comp;
        set
        {
            _comp = value;
            InvalidateVisual();
        }
    }

    public int SelectedTake { get; set; }

    /// <summary>Abspielposition (Frame) oder -1.</summary>
    public long PlayheadFrame { get; set; } = -1;

    private long Length => TakeTools.LengthOf(_takes);

    private double LaneHeight => _takes.Count == 0 ? 0 : (ActualHeight - RulerHeight) / _takes.Count;

    private double FrameToX(double frame) => HeaderWidth + frame / Math.Max(1, Length) * (ActualWidth - HeaderWidth);

    private long XToFrame(double x) => (long)Math.Clamp((x - HeaderWidth) / Math.Max(1, ActualWidth - HeaderWidth) * Length, 0, Length);

    protected override void OnRender(DrawingContext dc)
    {
        double width = ActualWidth;
        dc.DrawRectangle(Background, null, new Rect(0, 0, width, ActualHeight));
        if (_takes.Count == 0 || width <= HeaderWidth + 10)
        {
            return;
        }

        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        DrawRuler(dc, width, pixelsPerDip);
        double lane = LaneHeight;
        int columns = Math.Max(1, (int)(width - HeaderWidth));
        for (int t = 0; t < _takes.Count; t++)
        {
            double top = RulerHeight + t * lane;
            dc.DrawRectangle(LaneBrush, LinePen, new Rect(HeaderWidth, top, width - HeaderWidth, lane));
            foreach (var segment in _comp.Where(s => s.Take == t))
            {
                dc.DrawRectangle(ActiveBrush, null, new Rect(new Point(FrameToX(segment.Start), top), new Point(FrameToX(segment.End), top + lane)));
            }

            if (_dragTake == t)
            {
                dc.DrawRectangle(SelectionBrush, null, new Rect(new Point(Math.Min(_dragFromX, _dragToX), top), new Point(Math.Max(_dragFromX, _dragToX), top + lane)));
            }

            DrawWave(dc, _takes[t], t, top, lane, columns);
            dc.DrawRectangle(t == SelectedTake ? SelectedHeaderBrush : HeaderBrush, LinePen, new Rect(0, top, HeaderWidth, lane));
            var label = new FormattedText($"Take {t + 1}", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, LabelTypeface, 12, TextBrush, pixelsPerDip);
            dc.DrawText(label, new Point(8, top + (lane - label.Height) / 2));
        }

        if (PlayheadFrame >= 0)
        {
            double x = FrameToX(PlayheadFrame);
            dc.DrawLine(PlayheadPen, new Point(x, 0), new Point(x, ActualHeight));
        }
    }

    private void DrawRuler(DrawingContext dc, double width, double pixelsPerDip)
    {
        double seconds = Length / (double)Math.Max(1, _takes[0].SampleRate);
        double step = new[] { 0.5, 1, 2, 5, 10, 15, 30, 60 }.FirstOrDefault(s => s / seconds * (width - HeaderWidth) >= 60, 60);
        for (double s = 0; s <= seconds; s += step)
        {
            double x = FrameToX(s * _takes[0].SampleRate);
            dc.DrawLine(LinePen, new Point(x, RulerHeight - 5), new Point(x, RulerHeight));
            var text = new FormattedText(TimeSpan.FromSeconds(s).ToString(@"m\:ss", CultureInfo.InvariantCulture), CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, LabelTypeface, 10, TextBrush, pixelsPerDip);
            dc.DrawText(text, new Point(x + 2, 2));
        }
    }

    private void DrawWave(DrawingContext dc, AudioBuffer take, int index, double top, double lane, int columns)
    {
        var peaks = WaveformPeaks.Compute(take, 0, take.FrameCount, Math.Min(columns, Math.Max(1, take.FrameCount)));
        double middle = top + lane / 2;
        double half = lane / 2 - 4;
        double columnWidth = (ActualWidth - HeaderWidth) * take.FrameCount / Math.Max(1, Length) / Math.Max(1, peaks.Length);
        var active = new StreamGeometry();
        var inactive = new StreamGeometry();
        using (var a = active.Open())
        using (var n = inactive.Open())
        {
            for (int i = 0; i < peaks.Length; i++)
            {
                double x = HeaderWidth + (i + 0.5) * columnWidth;
                long frame = (long)((i + 0.5) / peaks.Length * take.FrameCount);
                bool on = _comp.Any(s => s.Take == index && frame >= s.Start && frame < s.End);
                var context = on ? a : n;
                context.BeginFigure(new Point(x, middle - Math.Clamp(peaks[i].Max, -1, 1) * half - 0.5), false, false);
                context.LineTo(new Point(x, middle - Math.Clamp(peaks[i].Min, -1, 1) * half + 0.5), true, false);
            }
        }

        active.Freeze();
        inactive.Freeze();
        dc.DrawGeometry(null, InactiveWave, inactive);
        dc.DrawGeometry(null, ActiveWave, active);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        var p = e.GetPosition(this);
        int take = TakeAt(p.Y);
        if (take < 0)
        {
            return;
        }

        SelectedTake = take;
        if (p.X < HeaderWidth)
        {
            InvalidateVisual();
            return;
        }

        _dragTake = take;
        _dragFromX = _dragToX = p.X;
        CaptureMouse();
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragTake >= 0)
        {
            _dragToX = Math.Clamp(e.GetPosition(this).X, HeaderWidth, ActualWidth);
            InvalidateVisual();
        }

        Cursor = e.GetPosition(this).X < HeaderWidth ? Cursors.Hand : Cursors.IBeam;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_dragTake < 0)
        {
            return;
        }

        int take = _dragTake;
        _dragTake = -1;
        ReleaseMouseCapture();
        if (Math.Abs(_dragToX - _dragFromX) < 4)
        {
            PositionClicked?.Invoke(this, XToFrame(_dragFromX)); // Klick: nur Position setzen
        }
        else
        {
            long from = XToFrame(Math.Min(_dragFromX, _dragToX));
            long to = XToFrame(Math.Max(_dragFromX, _dragToX));
            _comp = TakeTools.Assign(_comp, from, to, take, Length);
            CompChanged?.Invoke(this, EventArgs.Empty);
        }

        InvalidateVisual();
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        _dragTake = -1;
        InvalidateVisual();
    }

    private int TakeAt(double y)
    {
        if (y < RulerHeight || LaneHeight <= 0)
        {
            return -1;
        }

        int index = (int)((y - RulerHeight) / LaneHeight);
        return index < _takes.Count ? index : -1;
    }

    private static T Frozen<T>(T freezable)
        where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
