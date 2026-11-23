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

    public static readonly DependencyProperty ActiveChannelsProperty = DependencyProperty.Register(
        nameof(ActiveChannels), typeof(bool[]), typeof(WaveformView),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MutedChannelsProperty = DependencyProperty.Register(
        nameof(MutedChannels), typeof(bool[]), typeof(WaveformView),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SeekCommandProperty = DependencyProperty.Register(
        nameof(SeekCommand), typeof(ICommand), typeof(WaveformView), new PropertyMetadata(null));

    public static readonly DependencyProperty FollowPlayheadProperty = DependencyProperty.Register(
        nameof(FollowPlayhead), typeof(bool), typeof(WaveformView), new PropertyMetadata(false));

    private const double RulerHeight = 22;
    private const double MaxPixelsPerFrame = 8;

    private static readonly Brush BackgroundBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x1B, 0x1D, 0x23)));
    private static readonly Brush RulerBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x25, 0x28, 0x30)));
    private static readonly Brush RulerTextBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x9A, 0xA0, 0xAA)));
    private static readonly Brush HintBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x7A, 0x80, 0x8A)));
    private static readonly Brush SelectionBrush = Frozen(new SolidColorBrush(Color.FromArgb(0x50, 0x4F, 0xC3, 0xF7)));
    // Farben je Kanal – passend zur Spektrumanzeige: links blau, rechts rosa.
    private static readonly Pen[] ChannelPens =
    [
        Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromRgb(0x4F, 0xC3, 0xF7))), 1)),
        Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromRgb(0xF0, 0x62, 0x92))), 1)),
    ];
    private static readonly Pen MutedPen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromRgb(0x55, 0x5A, 0x64))), 1));
    private static readonly Pen LaneSeparatorPen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF))), 1));
    private static readonly Brush LaneLabelBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xC8, 0xCC, 0xD4)));
    private static readonly Pen CenterLinePen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF))), 1));
    private static readonly Pen RulerTickPen = Frozen(new Pen(RulerTextBrush, 1));
    private static readonly Pen SelectionEdgePen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromRgb(0x81, 0xD4, 0xFA))), 1));
    private static readonly Brush PlayheadBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xFF, 0x52, 0x52)));
    private static readonly Pen PlayheadPen = Frozen(new Pen(PlayheadBrush, 1.5));
    private static readonly Pen PlayheadHandleEdgePen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromRgb(0x8E, 0x1B, 0x1B))), 1));
    private static readonly Brush SeekTimeBackground = Frozen(new SolidColorBrush(Color.FromArgb(0xE0, 0x12, 0x14, 0x18)));

    /// <summary>Beim Ziehen des Positionsgriffs höchstens so oft wirklich springen (flüssig, ohne Stottern).</summary>
    private static readonly TimeSpan SeekThrottle = TimeSpan.FromMilliseconds(90);
    private static readonly Typeface LabelTypeface = new("Segoe UI");

    private static readonly double[] TickSteps =
        [0.001, 0.002, 0.005, 0.01, 0.02, 0.05, 0.1, 0.2, 0.5, 1, 2, 5, 10, 15, 30, 60, 120, 300, 600, 1800];

    // Sichtbarer Bereich in Frames (double, damit Zoomen stufenlos funktioniert).
    private double _viewStart;
    private double _viewLength;

    // Zwischenspeicher, damit beim Bewegen der Abspielposition nicht alles neu berechnet wird.
    private ViewKey _cachedKey;
    private Geometry[]? _cachedWaveforms;

    private bool _isSelecting;
    private bool _isSeeking;
    private int _seekFrame;
    private DateTime _lastSeekSent;
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
    /// <summary>Kanäle, auf die Effekte wirken (null = alle). Die Auswahl wird nur dort markiert.</summary>
    public bool[]? ActiveChannels
    {
        get => (bool[]?)GetValue(ActiveChannelsProperty);
        set => SetValue(ActiveChannelsProperty, value);
    }

    /// <summary>Kanäle, die beim Abhören stumm sind (null = keiner). Sie werden grau gezeichnet.</summary>
    public bool[]? MutedChannels
    {
        get => (bool[]?)GetValue(MutedChannelsProperty);
        set => SetValue(MutedChannelsProperty, value);
    }

    /// <summary>
    /// Wird mit der Ziel-Frame-Nummer (int) ausgeführt, wenn der Positionsgriff in der Zeitleiste
    /// gezogen oder in die Zeitleiste geklickt wird.
    /// </summary>
    public ICommand? SeekCommand
    {
        get => (ICommand?)GetValue(SeekCommandProperty);
        set => SetValue(SeekCommandProperty, value);
    }

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
        if (buffer is null || buffer.IsEmpty || size.Width < 1 || size.Height <= RulerHeight + 10)
        {
            DrawHint(dc, size);
            return;
        }

        EnsureView();
        DrawRuler(dc, buffer, size.Width);
        DrawSelectionBackground(dc, size);

        // Jeder Kanal bekommt eine eigene Spur (Stereo: oben links, unten rechts).
        var waveforms = GetWaveformGeometries(buffer, size);
        var muted = MutedChannels;
        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        for (int channel = 0; channel < buffer.Channels; channel++)
        {
            var lane = LaneRect(channel, buffer.Channels, size);
            bool isMuted = muted is not null && channel < muted.Length && muted[channel];

            DrawSelectionInLane(dc, lane, channel);
            dc.DrawLine(CenterLinePen, new Point(0, lane.Top + lane.Height / 2), new Point(size.Width, lane.Top + lane.Height / 2));
            dc.DrawGeometry(null, isMuted ? MutedPen : ChannelPens[Math.Min(channel, ChannelPens.Length - 1)], waveforms[channel]);
            if (channel > 0)
            {
                dc.DrawLine(LaneSeparatorPen, new Point(0, lane.Top), new Point(size.Width, lane.Top));
            }

            if (buffer.Channels > 1)
            {
                string label = buffer.Channels == 2 ? (channel == 0 ? "L" : "R") : $"{channel + 1}";
                if (isMuted)
                {
                    label += "  (stumm)";
                }

                var text = new FormattedText(label, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                    LabelTypeface, 12, LaneLabelBrush, pixelsPerDip);
                dc.DrawText(text, new Point(6, lane.Top + 4));
            }
        }

        DrawPlayhead(dc, size);
    }

    private static Rect LaneRect(int channel, int channels, Size size)
    {
        double laneHeight = (size.Height - RulerHeight) / channels;
        return new Rect(0, RulerHeight + channel * laneHeight, size.Width, laneHeight);
    }

    private Geometry[] GetWaveformGeometries(AudioBuffer buffer, Size size)
    {
        var key = new ViewKey(buffer, _viewStart, _viewLength, size.Width, size.Height);
        if (_cachedWaveforms is not null && key == _cachedKey)
        {
            return _cachedWaveforms;
        }

        var geometries = new Geometry[buffer.Channels];
        for (int channel = 0; channel < buffer.Channels; channel++)
        {
            geometries[channel] = CreateWaveformGeometry(buffer, channel, LaneRect(channel, buffer.Channels, size));
        }

        _cachedKey = key;
        _cachedWaveforms = geometries;
        return geometries;
    }

    private Geometry CreateWaveformGeometry(AudioBuffer buffer, int channel, Rect area)
    {
        double middle = area.Top + area.Height / 2;
        double halfHeight = area.Height / 2 * 0.92;
        double Y(float sample) => Math.Clamp(middle - sample * halfHeight, area.Top, area.Bottom);

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            if (_viewLength < area.Width)
            {
                // Stark hineingezoomt: einzelne Samples als verbundene Linie.
                int first = Math.Max(0, (int)Math.Floor(_viewStart));
                int last = Math.Min(buffer.FrameCount - 1, (int)Math.Ceiling(_viewStart + _viewLength));
                for (int frame = first; frame <= last; frame++)
                {
                    var point = new Point(FrameToX(frame), Y(buffer.GetSample(frame, channel)));
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
                    buffer, (int)Math.Floor(_viewStart), (int)Math.Ceiling(_viewLength), columns, channel);
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

    private bool TryGetSelectionX(Size size, out double left, out double right)
    {
        left = right = 0;
        var selection = Selection;
        if (selection.IsEmpty)
        {
            return false;
        }

        left = Math.Clamp(FrameToX(selection.Start), -1, size.Width + 1);
        right = Math.Clamp(FrameToX(selection.End), -1, size.Width + 1);
        if (right - left < 1)
        {
            right = left + 1;
        }

        return true;
    }

    /// <summary>Auswahlränder über die ganze Höhe (auch in der Zeitleiste).</summary>
    private void DrawSelectionBackground(DrawingContext dc, Size size)
    {
        if (!TryGetSelectionX(size, out double left, out double right))
        {
            return;
        }

        dc.DrawRectangle(SelectionBrush, null, new Rect(left, 0, right - left, RulerHeight));
        dc.DrawLine(SelectionEdgePen, new Point(left, 0), new Point(left, size.Height));
        dc.DrawLine(SelectionEdgePen, new Point(right, 0), new Point(right, size.Height));
    }

    /// <summary>Auswahlfläche nur in den Spuren der Kanäle, auf die Effekte wirken.</summary>
    private void DrawSelectionInLane(DrawingContext dc, Rect lane, int channel)
    {
        var active = ActiveChannels;
        if (active is not null && channel < active.Length && !active[channel])
        {
            return;
        }

        if (TryGetSelectionX(RenderSize, out double left, out double right))
        {
            dc.DrawRectangle(SelectionBrush, null, new Rect(left, lane.Top, right - left, lane.Height));
        }
    }

    private void DrawPlayhead(DrawingContext dc, Size size)
    {
        // Beim Ziehen folgt die Linie direkt der Maus (nicht erst der Wiedergabe).
        int frame = _isSeeking ? _seekFrame : PlayheadFrame;
        double x = FrameToX(frame);
        if (x < -8 || x > size.Width + 8)
        {
            return;
        }

        dc.DrawLine(PlayheadPen, new Point(x, 0), new Point(x, size.Height));

        // Griff in der Zeitleiste zum Anfassen: ein nach unten zeigendes Dreieck.
        var handle = new StreamGeometry();
        using (var context = handle.Open())
        {
            context.BeginFigure(new Point(x - 7, 1), isFilled: true, isClosed: true);
            context.LineTo(new Point(x + 7, 1), isStroked: true, isSmoothJoin: false);
            context.LineTo(new Point(x + 7, RulerHeight - 9), isStroked: true, isSmoothJoin: false);
            context.LineTo(new Point(x, RulerHeight - 1), isStroked: true, isSmoothJoin: false);
            context.LineTo(new Point(x - 7, RulerHeight - 9), isStroked: true, isSmoothJoin: false);
        }

        handle.Freeze();
        dc.DrawGeometry(PlayheadBrush, PlayheadHandleEdgePen, handle);

        if (_isSeeking && Buffer is { } buffer)
        {
            // Beim Ziehen die Zielzeit groß anzeigen.
            var text = new FormattedText(
                TimeFormatter.Format(buffer.FrameToTime(frame)),
                CultureInfo.CurrentCulture, FlowDirection.LeftToRight, LabelTypeface, 13,
                Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            double left = Math.Clamp(x + 10, 2, Math.Max(2, size.Width - text.Width - 10));
            dc.DrawRoundedRectangle(SeekTimeBackground, null,
                new Rect(left - 4, RulerHeight + 4, text.Width + 8, text.Height + 4), 3, 3);
            dc.DrawText(text, new Point(left, RulerHeight + 6));
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
        var position = e.GetPosition(this);
        int frame = XToFrame(position.X);

        // Klick in die Zeitleiste (oder auf den roten Griff): Position verschieben statt markieren.
        if (position.Y < RulerHeight)
        {
            _isSeeking = CaptureMouse();
            _seekFrame = frame;
            SendSeek(force: true);
            InvalidateVisual();
            e.Handled = true;
            return;
        }

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
        var position = e.GetPosition(this);
        if (!_isSelecting && !_isSeeking)
        {
            // Hand über der Zeitleiste zeigt: hier kann man die Position anfassen.
            Cursor = HasAudio && position.Y < RulerHeight ? Cursors.Hand : Cursors.IBeam;
        }

        if (_isSeeking && HasAudio)
        {
            if (position.X < 0)
            {
                ScrollBy(-0.02);
            }
            else if (position.X > ActualWidth)
            {
                ScrollBy(0.02);
            }

            _seekFrame = XToFrame(Math.Clamp(position.X, 0, ActualWidth));
            SendSeek(force: false);
            InvalidateVisual();
            return;
        }

        if (!_isSelecting || !HasAudio)
        {
            return;
        }

        double x = position.X;
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
        if (_isSeeking)
        {
            SendSeek(force: true);   // genau an der Loslass-Stelle weiterspielen
            _isSeeking = false;
            ReleaseMouseCapture();
            InvalidateVisual();
            e.Handled = true;
            return;
        }

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
        if (_isSeeking)
        {
            _isSeeking = false;
            InvalidateVisual();
        }
    }

    /// <summary>Führt den Sprung aus – beim Ziehen gedrosselt, beim Klicken/Loslassen sofort.</summary>
    private void SendSeek(bool force)
    {
        var now = DateTime.UtcNow;
        if (!force && now - _lastSeekSent < SeekThrottle)
        {
            return;
        }

        _lastSeekSent = now;
        if (SeekCommand is { } command && command.CanExecute(_seekFrame))
        {
            command.Execute(_seekFrame);
        }
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
        if (view.FollowPlayhead && !view._isSeeking)
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
