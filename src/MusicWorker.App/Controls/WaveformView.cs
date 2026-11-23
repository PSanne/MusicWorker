using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using MusicWorker.App.Helpers;
using MusicWorker.Core.Audio;
using MusicWorker.Core.Editing;
using MusicWorker.Core.Visualization;

namespace MusicWorker.App.Controls;

/// <summary>
/// Zeichnet die Wellenform mit Zeitleiste, Auswahl und Abspielposition.
/// Maus: klicken = Cursor setzen, ziehen = markieren, Umschalt+Klick = Auswahl erweitern,
/// Mausrad = scrollen, Strg+Mausrad = zoomen. Tasten: Pos1/Ende springen an Anfang/Ende.
/// </summary>
public sealed class WaveformView : FrameworkElement
{
    public static readonly DependencyProperty BufferProperty = DependencyProperty.Register(
        nameof(Buffer), typeof(AudioBuffer), typeof(WaveformView),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SelectionProperty = DependencyProperty.Register(
        nameof(Selection), typeof(SelectionRange), typeof(WaveformView),
        new FrameworkPropertyMetadata(
            default(SelectionRange),
            FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty PlayheadFrameProperty = DependencyProperty.Register(
        nameof(PlayheadFrame), typeof(int), typeof(WaveformView),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender, OnPlayheadFrameChanged));

    public static readonly DependencyProperty FollowPlayheadProperty = DependencyProperty.Register(
        nameof(FollowPlayhead), typeof(bool), typeof(WaveformView), new PropertyMetadata(false));

    private const double RulerHeight = 22;
    private const double MaxPixelsPerFrame = 8;

    private static readonly Brush BackgroundBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x1B, 0x1D, 0x23)));
    private static readonly Brush RulerBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x25, 0x28, 0x30)));
    private static readonly Brush RulerTextBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x9A, 0xA0, 0xAA)));
    private static readonly Brush HintBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x7A, 0x80, 0x8A)));
    private static readonly Brush SelectionBrush = Frozen(new SolidColorBrush(Color.FromArgb(0x50, 0x4F, 0xC3, 0xF7)));
    private static readonly Pen WavePen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromRgb(0x4F, 0xC3, 0xF7))), 1));
    private static readonly Pen CenterLinePen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF))), 1));
    private static readonly Pen RulerTickPen = Frozen(new Pen(RulerTextBrush, 1));
    private static readonly Pen SelectionEdgePen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromRgb(0x81, 0xD4, 0xFA))), 1));
    private static readonly Pen PlayheadPen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromRgb(0xFF, 0x52, 0x52))), 1.5));
    private static readonly Typeface LabelTypeface = new("Segoe UI");

    private static readonly double[] TickSteps =
        [0.001, 0.002, 0.005, 0.01, 0.02, 0.05, 0.1, 0.2, 0.5, 1, 2, 5, 10, 15, 30, 60, 120, 300, 600, 1800];

    // Sichtbarer Bereich in Frames (double, damit Zoomen stufenlos funktioniert).
    private double _viewStart;
    private double _viewLength;

    // Zwischenspeicher, damit beim Bewegen der Abspielposition nicht alles neu berechnet wird.
    private ViewKey _cachedKey;
    private Geometry? _cachedWaveform;

    private bool _isSelecting;
    private int _anchorFrame;

    public WaveformView()
    {
        Focusable = true;
        ClipToBounds = true;
        Cursor = Cursors.IBeam;
        FocusVisualStyle = null;
    }

    public AudioBuffer? Buffer
    {
        get => (AudioBuffer?)GetValue(BufferProperty);
        set => SetValue(BufferProperty, value);
    }

    public SelectionRange Selection
    {
        get => (SelectionRange)GetValue(SelectionProperty);
        set => SetValue(SelectionProperty, value);
    }

    public int PlayheadFrame
    {
        get => (int)GetValue(PlayheadFrameProperty);
        set => SetValue(PlayheadFrameProperty, value);
    }

    /// <summary>Wenn true, scrollt die Ansicht mit, sobald die Abspielposition den sichtbaren Bereich verlässt.</summary>
    public bool FollowPlayhead
    {
        get => (bool)GetValue(FollowPlayheadProperty);
        set => SetValue(FollowPlayheadProperty, value);
    }

    private bool HasAudio => Buffer is { IsEmpty: false } && ActualWidth > 0;

    // ───────────── Zoom ─────────────

    public void ZoomToFit()
    {
        _viewStart = 0;
        _viewLength = 0;
        InvalidateVisual();
    }

    public void ZoomIn() => ZoomAround(0.5, ZoomAnchorX());

    public void ZoomOut() => ZoomAround(2.0, ZoomAnchorX());

    public void ZoomToSelection()
    {
        var selection = Selection;
        if (!HasAudio || selection.IsEmpty)
        {
            ZoomToFit();
            return;
        }

        _viewStart = selection.Start;
        _viewLength = selection.Length;
        EnsureView();
        InvalidateVisual();
    }

    private void ZoomAround(double factor, double anchorX)
    {
        if (!HasAudio)
        {
            return;
        }

        EnsureView();
        double anchorFrame = _viewStart + anchorX / ActualWidth * _viewLength;
        _viewLength *= factor;
        EnsureView();
        _viewStart = anchorFrame - anchorX / ActualWidth * _viewLength;
        EnsureView();
        InvalidateVisual();
    }

    private void ScrollBy(double fractionOfView)
    {
        if (!HasAudio)
        {
            return;
        }

        EnsureView();
        _viewStart += fractionOfView * _viewLength;
        EnsureView();
        InvalidateVisual();
    }

    private double ZoomAnchorX()
    {
        double x = FrameToX(PlayheadFrame);
        return x >= 0 && x <= ActualWidth ? x : ActualWidth / 2;
    }

    /// <summary>Hält den sichtbaren Bereich innerhalb der Datei und innerhalb der Zoom-Grenzen.</summary>
    private void EnsureView()
    {
        if (!HasAudio)
        {
            return;
        }

        double frames = Buffer!.FrameCount;
        double minLength = Math.Min(frames, Math.Max(1, ActualWidth / MaxPixelsPerFrame));
        if (_viewLength <= 0 || _viewLength > frames)
        {
            _viewLength = frames;
        }
        else if (_viewLength < minLength)
        {
            _viewLength = minLength;
        }

        _viewStart = Math.Clamp(_viewStart, 0, frames - _viewLength);
    }

    private double FrameToX(double frame) =>
        _viewLength <= 0 ? 0 : (frame - _viewStart) / _viewLength * ActualWidth;

    private int XToFrame(double x) =>
        (int)Math.Clamp(Math.Round(_viewStart + x / ActualWidth * _viewLength), 0, Buffer!.FrameCount);

    // ───────────── Zeichnen ─────────────

    protected override void OnRender(DrawingContext dc)
    {
        var size = RenderSize;
        dc.DrawRectangle(BackgroundBrush, null, new Rect(size));

        var buffer = Buffer;
        if (buffer is null || buffer.IsEmpty || size.Width < 1 || size.Height <= RulerHeight)
        {
            DrawHint(dc, size);
            return;
        }

        EnsureView();
        var waveArea = new Rect(0, RulerHeight, size.Width, size.Height - RulerHeight);

        DrawRuler(dc, buffer, size.Width);
        DrawSelection(dc, size);
        dc.DrawLine(CenterLinePen, new Point(0, waveArea.Top + waveArea.Height / 2),
            new Point(size.Width, waveArea.Top + waveArea.Height / 2));
        dc.DrawGeometry(null, WavePen, GetWaveformGeometry(buffer, waveArea));
        DrawPlayhead(dc, size);
    }

    private Geometry GetWaveformGeometry(AudioBuffer buffer, Rect area)
    {
        var key = new ViewKey(buffer, _viewStart, _viewLength, area.Width, area.Height);
        if (_cachedWaveform is not null && key == _cachedKey)
        {
            return _cachedWaveform;
        }

        double middle = area.Top + area.Height / 2;
        double halfHeight = area.Height / 2 * 0.95;
        double Y(float sample) => Math.Clamp(middle - sample * halfHeight, area.Top, area.Bottom);

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            if (_viewLength < area.Width)
            {
                // Stark hineingezoomt: einzelne Samples als verbundene Linie (Mittelwert der Kanäle).
                int first = Math.Max(0, (int)Math.Floor(_viewStart));
                int last = Math.Min(buffer.FrameCount - 1, (int)Math.Ceiling(_viewStart + _viewLength));
                for (int frame = first; frame <= last; frame++)
                {
                    float sum = 0;
                    for (int channel = 0; channel < buffer.Channels; channel++)
                    {
                        sum += buffer.GetSample(frame, channel);
                    }

                    var point = new Point(FrameToX(frame), Y(sum / buffer.Channels));
                    if (frame == first)
                    {
                        context.BeginFigure(point, false, false);
                    }
                    else
                    {
                        context.LineTo(point, true, false);
                    }
                }
            }
            else
            {
                // Übersicht: pro Pixelspalte ein senkrechter Strich von Minimum bis Maximum.
                int columns = (int)Math.Ceiling(area.Width);
                var peaks = WaveformPeaks.Compute(
                    buffer, (int)Math.Floor(_viewStart), (int)Math.Ceiling(_viewLength), columns);
                for (int column = 0; column < peaks.Length; column++)
                {
                    double top = Y(peaks[column].Max);
                    double bottom = Y(peaks[column].Min);
                    if (bottom - top < 1)
                    {
                        top -= 0.5;
                        bottom += 0.5;
                    }

                    double x = column + 0.5;
                    context.BeginFigure(new Point(x, top), false, false);
                    context.LineTo(new Point(x, bottom), true, false);
                }
            }
        }

        geometry.Freeze();
        _cachedKey = key;
        _cachedWaveform = geometry;
        return geometry;
    }

    private void DrawRuler(DrawingContext dc, AudioBuffer buffer, double width)
    {
        dc.DrawRectangle(RulerBrush, null, new Rect(0, 0, width, RulerHeight));

        double secondsPerPixel = _viewLength / buffer.SampleRate / width;
        double step = TickSteps.FirstOrDefault(s => s / secondsPerPixel >= 90, TickSteps[^1]);
        double startSeconds = _viewStart / buffer.SampleRate;
        double endSeconds = (_viewStart + _viewLength) / buffer.SampleRate;
        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        for (long i = (long)Math.Ceiling(startSeconds / step); i * step <= endSeconds; i++)
        {
            double seconds = i * step;
            double x = FrameToX(seconds * buffer.SampleRate);
            dc.DrawLine(RulerTickPen, new Point(x, RulerHeight - 6), new Point(x, RulerHeight));

            var label = new FormattedText(
                TimeFormatter.Format(TimeSpan.FromSeconds(seconds), withMilliseconds: step < 1),
                CultureInfo.CurrentCulture, FlowDirection.LeftToRight, LabelTypeface, 11,
                RulerTextBrush, pixelsPerDip);
            dc.DrawText(label, new Point(x + 3, 3));
        }
    }

    private void DrawSelection(DrawingContext dc, Size size)
    {
        var selection = Selection;
        if (selection.IsEmpty)
        {
            return;
        }

        double left = Math.Clamp(FrameToX(selection.Start), -1, size.Width + 1);
        double right = Math.Clamp(FrameToX(selection.End), -1, size.Width + 1);
        if (right - left < 1)
        {
            right = left + 1;
        }

        dc.DrawRectangle(SelectionBrush, null, new Rect(left, 0, right - left, size.Height));
        dc.DrawLine(SelectionEdgePen, new Point(left, 0), new Point(left, size.Height));
        dc.DrawLine(SelectionEdgePen, new Point(right, 0), new Point(right, size.Height));
    }

    private void DrawPlayhead(DrawingContext dc, Size size)
    {
        double x = FrameToX(PlayheadFrame);
        if (x >= 0 && x <= size.Width)
        {
            dc.DrawLine(PlayheadPen, new Point(x, 0), new Point(x, size.Height));
        }
    }

    private void DrawHint(DrawingContext dc, Size size)
    {
        var text = new FormattedText(
            "Audiodatei öffnen (Strg+O) oder hierher ziehen",
            CultureInfo.CurrentCulture, FlowDirection.LeftToRight, LabelTypeface, 16,
            HintBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(text, new Point((size.Width - text.Width) / 2, (size.Height - text.Height) / 2));
    }

    // ───────────── Maus & Tastatur ─────────────

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        if (!HasAudio)
        {
            return;
        }

        EnsureView();
        int frame = XToFrame(e.GetPosition(this).X);
        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
        {
            // Auswahl erweitern: das weiter entfernte Ende bleibt fest.
            var current = Selection;
            _anchorFrame = Math.Abs(frame - current.Start) < Math.Abs(frame - current.End)
                ? current.End
                : current.Start;
        }
        else
        {
            _anchorFrame = frame;
        }

        SetCurrentValue(SelectionProperty, new SelectionRange(_anchorFrame, frame));
        _isSelecting = CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_isSelecting || !HasAudio)
        {
            return;
        }

        double x = e.GetPosition(this).X;
        if (x < 0)
        {
            ScrollBy(-0.02);
        }
        else if (x > ActualWidth)
        {
            ScrollBy(0.02);
        }

        int frame = XToFrame(Math.Clamp(x, 0, ActualWidth));
        SetCurrentValue(SelectionProperty, new SelectionRange(_anchorFrame, frame));
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_isSelecting)
        {
            _isSelecting = false;
            ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        _isSelecting = false;
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (!HasAudio)
        {
            return;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            ZoomAround(e.Delta > 0 ? 0.8 : 1.25, e.GetPosition(this).X);
        }
        else
        {
            ScrollBy(-e.Delta / 120.0 * 0.15);
        }

        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!HasAudio)
        {
            return;
        }

        int? target = e.Key switch
        {
            Key.Home => 0,
            Key.End => Buffer!.FrameCount,
            _ => null,
        };

        if (target is { } frame)
        {
            SetCurrentValue(SelectionProperty, SelectionRange.Caret(frame));
            ScrollIntoView(frame);
            e.Handled = true;
        }
    }

    private static void OnPlayheadFrameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (WaveformView)d;
        if (view.FollowPlayhead)
        {
            view.ScrollIntoView((int)e.NewValue);
        }
    }

    private void ScrollIntoView(int frame)
    {
        if (!HasAudio)
        {
            return;
        }

        EnsureView();
        if (frame < _viewStart || frame > _viewStart + _viewLength)
        {
            _viewStart = frame - _viewLength * 0.05;
            EnsureView();
            InvalidateVisual();
        }
    }

    private static T Frozen<T>(T freezable)
        where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }

    private readonly record struct ViewKey(
        AudioBuffer? Buffer, double Start, double Length, double Width, double Height);
}
