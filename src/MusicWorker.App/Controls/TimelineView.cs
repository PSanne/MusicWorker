using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using MusicWorker.App.Helpers;
using MusicWorker.App.ViewModels;
using MusicWorker.Core.Visualization;

namespace MusicWorker.App.Controls;

/// <summary>
/// Spuransicht des Mischpults: Zeitleiste, eine Bahn pro Spur mit ihren Clips (als Mini-Wellenform),
/// Abspiellinie mit Positionsgriff. Clips lassen sich mit der Maus verschieben; Doppelklick öffnet
/// einen Clip im Editor. Strg+Mausrad zoomt, Umschalt+Mausrad scrollt waagerecht.
/// </summary>
public sealed class TimelineView : FrameworkElement
{
    public const double RulerHeight = 22;
    public const double LaneHeight = 76;

    public static readonly DependencyProperty TracksProperty = DependencyProperty.Register(
        nameof(Tracks), typeof(IList<TrackViewModel>), typeof(TimelineView),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty VersionProperty = DependencyProperty.Register(
        nameof(Version), typeof(int), typeof(TimelineView),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty PlayheadFrameProperty = DependencyProperty.Register(
        nameof(PlayheadFrame), typeof(long), typeof(TimelineView),
        new FrameworkPropertyMetadata(0L, FrameworkPropertyMetadataOptions.AffectsRender, OnPlayheadFrameChanged));

    public static readonly DependencyProperty SampleRateProperty = DependencyProperty.Register(
        nameof(SampleRate), typeof(int), typeof(TimelineView),
        new FrameworkPropertyMetadata(44100, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SelectedClipProperty = DependencyProperty.Register(
        nameof(SelectedClip), typeof(ClipViewModel), typeof(TimelineView),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FollowPlayheadProperty = DependencyProperty.Register(
        nameof(FollowPlayhead), typeof(bool), typeof(TimelineView), new PropertyMetadata(false));

    public static readonly DependencyProperty SeekCommandProperty = DependencyProperty.Register(
        nameof(SeekCommand), typeof(ICommand), typeof(TimelineView), new PropertyMetadata(null));

    public static readonly DependencyProperty MoveClipCommandProperty = DependencyProperty.Register(
        nameof(MoveClipCommand), typeof(ICommand), typeof(TimelineView), new PropertyMetadata(null));

    public static readonly DependencyProperty SelectClipCommandProperty = DependencyProperty.Register(
        nameof(SelectClipCommand), typeof(ICommand), typeof(TimelineView), new PropertyMetadata(null));

    public static readonly DependencyProperty OpenClipCommandProperty = DependencyProperty.Register(
        nameof(OpenClipCommand), typeof(ICommand), typeof(TimelineView), new PropertyMetadata(null));

    private static readonly Brush BackgroundBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x1B, 0x1D, 0x23)));
    private static readonly Brush[] LaneBrushes =
    [
        Frozen(new SolidColorBrush(Color.FromRgb(0x20, 0x23, 0x2A))),
        Frozen(new SolidColorBrush(Color.FromRgb(0x24, 0x27, 0x2F))),
    ];
    private static readonly Brush RulerBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x25, 0x28, 0x30)));
    private static readonly Brush TextBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x9A, 0xA0, 0xAA)));
    private static readonly Brush ClipTextBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xF2, 0xF4, 0xF8)));
    private static readonly Pen TickPen = Frozen(new Pen(TextBrush, 1));
    private static readonly Pen LaneLinePen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromRgb(0x30, 0x34, 0x3D))), 1));
    private static readonly Pen SelectedPen = Frozen(new Pen(Brushes.White, 2));
    private static readonly Brush PlayheadBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xFF, 0x52, 0x52)));
    private static readonly Pen PlayheadPen = Frozen(new Pen(PlayheadBrush, 1.5));
    private static readonly Pen HandleEdgePen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromRgb(0x8E, 0x1B, 0x1B))), 1));
    private static readonly Typeface LabelTypeface = new("Segoe UI");
    private static readonly double[] TickSteps = [0.1, 0.2, 0.5, 1, 2, 5, 10, 15, 30, 60, 120, 300, 600];
    private static readonly TimeSpan SeekThrottle = TimeSpan.FromMilliseconds(90);

    private readonly Dictionary<ClipViewModel, (ClipKey Key, Geometry Geometry)> _waveCache = [];

    private double _viewStart;
    private double _viewLength;
    private ClipViewModel? _dragClip;
    private long _dragOffset;
    private bool _isSeeking;
    private long _seekFrame;
    private DateTime _lastSeek;

    public TimelineView()
    {
        Focusable = true;
        ClipToBounds = true;
        FocusVisualStyle = null;
    }

    public IList<TrackViewModel>? Tracks
    {
        get => (IList<TrackViewModel>?)GetValue(TracksProperty);
        set => SetValue(TracksProperty, value);
    }

    /// <summary>Bei jeder Änderung an Spuren/Clips erhöhen – dann wird neu gezeichnet.</summary>
    public int Version
    {
        get => (int)GetValue(VersionProperty);
        set => SetValue(VersionProperty, value);
    }

    public long PlayheadFrame
    {
        get => (long)GetValue(PlayheadFrameProperty);
        set => SetValue(PlayheadFrameProperty, value);
    }

    public int SampleRate
    {
        get => (int)GetValue(SampleRateProperty);
        set => SetValue(SampleRateProperty, value);
    }

    public ClipViewModel? SelectedClip
    {
        get => (ClipViewModel?)GetValue(SelectedClipProperty);
        set => SetValue(SelectedClipProperty, value);
    }

    public bool FollowPlayhead
    {
        get => (bool)GetValue(FollowPlayheadProperty);
        set => SetValue(FollowPlayheadProperty, value);
    }

    /// <summary>Parameter: Ziel-Frame (long).</summary>
    public ICommand? SeekCommand
    {
        get => (ICommand?)GetValue(SeekCommandProperty);
        set => SetValue(SeekCommandProperty, value);
    }

    /// <summary>Parameter: <see cref="ClipMove"/>.</summary>
    public ICommand? MoveClipCommand
    {
        get => (ICommand?)GetValue(MoveClipCommandProperty);
        set => SetValue(MoveClipCommandProperty, value);
    }

    /// <summary>Parameter: <see cref="ClipViewModel"/> oder null.</summary>
    public ICommand? SelectClipCommand
    {
        get => (ICommand?)GetValue(SelectClipCommandProperty);
        set => SetValue(SelectClipCommandProperty, value);
    }

    /// <summary>Parameter: <see cref="ClipViewModel"/> (Doppelklick).</summary>
    public ICommand? OpenClipCommand
    {
        get => (ICommand?)GetValue(OpenClipCommandProperty);
        set => SetValue(OpenClipCommandProperty, value);
    }

    private int TrackCount => Tracks?.Count ?? 0;

    private long ProjectFrames => Tracks?.SelectMany(t => t.Clips).Select(c => c.EndFrame).DefaultIfEmpty(0).Max() ?? 0;

    // ───────────── Zoom ─────────────

    public void ZoomToFit()
    {
        _viewStart = 0;
        _viewLength = 0;
        InvalidateVisual();
    }

    public void ZoomIn() => ZoomAround(0.5, ActualWidth / 2);

    public void ZoomOut() => ZoomAround(2, ActualWidth / 2);

    private void ZoomAround(double factor, double anchorX)
    {
        if (ActualWidth <= 0)
        {
            return;
        }

        EnsureView();
        double anchor = _viewStart + anchorX / ActualWidth * _viewLength;
        _viewLength *= factor;
        EnsureView();
        _viewStart = anchor - anchorX / ActualWidth * _viewLength;
        EnsureView();
        InvalidateVisual();
    }

    private void ScrollBy(double fraction)
    {
        EnsureView();
        _viewStart += fraction * _viewLength;
        EnsureView();
        InvalidateVisual();
    }

    private void EnsureView()
    {
        double rate = Math.Max(1, SampleRate);
        double content = Math.Max(ProjectFrames * 1.05, 30 * rate); // mindestens 30 s sichtbar
        if (_viewLength <= 0)
        {
            _viewLength = content;
        }

        _viewLength = Math.Clamp(_viewLength, 0.05 * rate, Math.Max(content, 0.05 * rate) * 2);
        double scrollable = Math.Max(ProjectFrames + 10 * rate, _viewLength);
        _viewStart = Math.Clamp(_viewStart, 0, Math.Max(0, scrollable - _viewLength));
    }

    private double FrameToX(double frame) => (frame - _viewStart) / _viewLength * ActualWidth;

    private long XToFrame(double x) => Math.Max(0, (long)Math.Round(_viewStart + x / ActualWidth * _viewLength));

    // ───────────── Größe & Zeichnen ─────────────

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = double.IsInfinity(availableSize.Width) ? 800 : availableSize.Width;
        return new Size(width, RulerHeight + Math.Max(1, TrackCount) * LaneHeight);
    }

    protected override void OnRender(DrawingContext dc)
    {
        double width = ActualWidth;
        double height = ActualHeight;
        dc.DrawRectangle(BackgroundBrush, null, new Rect(0, 0, width, height));
        if (width < 10)
        {
            return;
        }

        EnsureView();
        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        DrawRuler(dc, width, pixelsPerDip);

        var tracks = Tracks;
        if (tracks is null || tracks.Count == 0)
        {
            var hint = Text("Noch keine Spuren – Audiodateien hierher ziehen oder „Spur aus Datei …“ wählen.", 14, TextBrush, pixelsPerDip);
            dc.DrawText(hint, new Point((width - hint.Width) / 2, RulerHeight + (LaneHeight - hint.Height) / 2));
            DrawPlayhead(dc, width, height);
            return;
        }

        var visibleClips = new HashSet<ClipViewModel>();
        for (int t = 0; t < tracks.Count; t++)
        {
            double top = RulerHeight + t * LaneHeight;
            dc.DrawRectangle(LaneBrushes[t % 2], null, new Rect(0, top, width, LaneHeight));
            dc.DrawLine(LaneLinePen, new Point(0, top + LaneHeight - 0.5), new Point(width, top + LaneHeight - 0.5));

            var color = ParseColor(tracks[t].Color);
            var fill = Frozen(new SolidColorBrush(Color.FromArgb(0x48, color.R, color.G, color.B)));
            var edge = Frozen(new Pen(Frozen(new SolidColorBrush(color)), 1));
            var wavePen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromArgb(0xD8, color.R, color.G, color.B))), 1));

            foreach (var clip in tracks[t].Clips)
            {
                double x0 = FrameToX(clip.StartFrame);
                double x1 = FrameToX(clip.EndFrame);
                if (x1 < 0 || x0 > width)
                {
                    continue;
                }

                visibleClips.Add(clip);
                var rect = new Rect(Math.Max(x0, -2), top + 4, Math.Max(2, Math.Min(x1, width + 2) - Math.Max(x0, -2)), LaneHeight - 8);
                dc.DrawRoundedRectangle(fill, ReferenceEquals(clip, SelectedClip) ? SelectedPen : edge, rect, 4, 4);

                dc.PushClip(new RectangleGeometry(rect));
                dc.DrawGeometry(null, wavePen, GetWaveform(clip, rect, width));
                var label = Text(tracks[t].Name, 11, ClipTextBrush, pixelsPerDip);
                dc.DrawText(label, new Point(Math.Max(rect.Left, 0) + 6, rect.Top + 3));
                dc.Pop();
            }
        }

        // Zwischenspeicher für nicht mehr sichtbare oder gelöschte Clips aufräumen
        foreach (var stale in _waveCache.Keys.Where(c => !visibleClips.Contains(c)).ToArray())
        {
            _waveCache.Remove(stale);
        }

        DrawPlayhead(dc, width, height);
    }

    private Geometry GetWaveform(ClipViewModel clip, Rect rect, double width)
    {
        var key = new ClipKey(clip.Audio, clip.StartFrame, _viewStart, _viewLength, width, rect.Height);
        if (_waveCache.TryGetValue(clip, out var cached) && cached.Key == key)
        {
            return cached.Geometry;
        }

        // Nur den sichtbaren Teil des Clips berechnen.
        long visibleFrom = Math.Max(clip.StartFrame, (long)_viewStart);
        long visibleTo = Math.Min(clip.EndFrame, (long)(_viewStart + _viewLength) + 1);
        var geometry = new StreamGeometry();
        if (visibleTo > visibleFrom)
        {
            double left = FrameToX(visibleFrom);
            double right = FrameToX(visibleTo);
            int columns = Math.Max(1, (int)Math.Ceiling(right - left));
            var peaks = WaveformPeaks.Compute(clip.Audio, (int)(visibleFrom - clip.StartFrame), (int)(visibleTo - visibleFrom), columns);
            double middle = rect.Top + rect.Height / 2 + 6;
            double half = rect.Height / 2 - 10;

            using var context = geometry.Open();
            for (int i = 0; i < peaks.Length; i++)
            {
                double x = left + i + 0.5;
                double y0 = middle - Math.Clamp(peaks[i].Max, -1, 1) * half;
                double y1 = middle - Math.Clamp(peaks[i].Min, -1, 1) * half;
                if (y1 - y0 < 1)
                {
                    y0 -= 0.5;
                    y1 += 0.5;
                }

                context.BeginFigure(new Point(x, y0), false, false);
                context.LineTo(new Point(x, y1), true, false);
            }
        }

        geometry.Freeze();
        _waveCache[clip] = (key, geometry);
        return geometry;
    }

    private void DrawRuler(DrawingContext dc, double width, double pixelsPerDip)
    {
        dc.DrawRectangle(RulerBrush, null, new Rect(0, 0, width, RulerHeight));
        double rate = Math.Max(1, SampleRate);
        double secondsPerPixel = _viewLength / rate / width;
        double step = TickSteps.FirstOrDefault(s => s / secondsPerPixel >= 90, TickSteps[^1]);
        double start = _viewStart / rate;
        double end = (_viewStart + _viewLength) / rate;

        for (long i = (long)Math.Ceiling(start / step); i * step <= end; i++)
        {
            double seconds = i * step;
            double x = FrameToX(seconds * rate);
            dc.DrawLine(TickPen, new Point(x, RulerHeight - 6), new Point(x, RulerHeight));
            dc.DrawText(Text(TimeFormatter.Format(TimeSpan.FromSeconds(seconds), step < 1), 11, TextBrush, pixelsPerDip),
                new Point(x + 3, 3));
        }
    }

    private void DrawPlayhead(DrawingContext dc, double width, double height)
    {
        double x = FrameToX(_isSeeking ? _seekFrame : PlayheadFrame);
        if (x < -8 || x > width + 8)
        {
            return;
        }

        dc.DrawLine(PlayheadPen, new Point(x, 0), new Point(x, height));
        var handle = new StreamGeometry();
        using (var context = handle.Open())
        {
            context.BeginFigure(new Point(x - 7, 1), true, true);
            context.LineTo(new Point(x + 7, 1), true, false);
            context.LineTo(new Point(x + 7, RulerHeight - 9), true, false);
            context.LineTo(new Point(x, RulerHeight - 1), true, false);
            context.LineTo(new Point(x - 7, RulerHeight - 9), true, false);
        }

        handle.Freeze();
        dc.DrawGeometry(PlayheadBrush, HandleEdgePen, handle);
    }

    // ───────────── Maus ─────────────

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        EnsureView();
        var position = e.GetPosition(this);
        long frame = XToFrame(position.X);

        if (position.Y < RulerHeight)
        {
            _isSeeking = CaptureMouse();
            _seekFrame = frame;
            SendSeek(force: true);
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        var clip = HitClip(position);
        if (clip is null)
        {
            Execute(SelectClipCommand, null);
            _seekFrame = frame;
            SendSeek(force: true); // Klick ins Leere setzt die Abspielposition
            e.Handled = true;
            return;
        }

        if (e.ClickCount == 2)
        {
            Execute(OpenClipCommand, clip);
            e.Handled = true;
            return;
        }

        Execute(SelectClipCommand, clip);
        _dragClip = clip;
        _dragOffset = frame - clip.StartFrame;
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var position = e.GetPosition(this);

        if (_isSeeking)
        {
            AutoScroll(position.X);
            _seekFrame = XToFrame(Math.Clamp(position.X, 0, ActualWidth));
            SendSeek(force: false);
            InvalidateVisual();
            return;
        }

        if (_dragClip is { } clip)
        {
            AutoScroll(position.X);
            long newStart = Math.Max(0, XToFrame(Math.Clamp(position.X, 0, ActualWidth)) - _dragOffset);
            if (newStart != clip.StartFrame)
            {
                Execute(MoveClipCommand, new ClipMove(clip, newStart));
            }

            return;
        }

        Cursor = position.Y < RulerHeight ? Cursors.Hand : HitClip(position) is not null ? Cursors.SizeWE : Cursors.Arrow;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_isSeeking)
        {
            SendSeek(force: true);
        }

        EndDrag();
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        _isSeeking = false;
        _dragClip = null;
        InvalidateVisual();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            ZoomAround(e.Delta > 0 ? 0.8 : 1.25, e.GetPosition(this).X);
            e.Handled = true;
        }
        else if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
        {
            ScrollBy(-e.Delta / 120.0 * 0.15);
            e.Handled = true;
        }

        // Ohne Taste: senkrecht scrollen übernimmt die umgebende Bildlaufleiste.
    }

    private ClipViewModel? HitClip(Point position)
    {
        var tracks = Tracks;
        if (tracks is null || position.Y < RulerHeight)
        {
            return null;
        }

        int lane = (int)((position.Y - RulerHeight) / LaneHeight);
        if (lane < 0 || lane >= tracks.Count)
        {
            return null;
        }

        long frame = XToFrame(position.X);
        return tracks[lane].Clips.LastOrDefault(c => frame >= c.StartFrame && frame < c.EndFrame);
    }

    private void AutoScroll(double x)
    {
        if (x < 0)
        {
            ScrollBy(-0.02);
        }
        else if (x > ActualWidth)
        {
            ScrollBy(0.02);
        }
    }

    private void EndDrag()
    {
        _isSeeking = false;
        _dragClip = null;
        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }

        InvalidateVisual();
    }

    private void SendSeek(bool force)
    {
        var now = DateTime.UtcNow;
        if (!force && now - _lastSeek < SeekThrottle)
        {
            return;
        }

        _lastSeek = now;
        Execute(SeekCommand, _seekFrame);
    }

    private static void Execute(ICommand? command, object? parameter)
    {
        if (command is not null && command.CanExecute(parameter))
        {
            command.Execute(parameter);
        }
    }

    private static void OnPlayheadFrameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (TimelineView)d;
        if (!view.FollowPlayhead || view._isSeeking || view._viewLength <= 0)
        {
            return;
        }

        long frame = (long)e.NewValue;
        if (frame < view._viewStart || frame > view._viewStart + view._viewLength)
        {
            view._viewStart = frame - view._viewLength * 0.05;
            view.EnsureView();
        }
    }

    private FormattedText Text(string text, double size, Brush brush, double pixelsPerDip) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, LabelTypeface, size, brush, pixelsPerDip);

    private static Color ParseColor(string color)
    {
        try
        {
            return (Color)ColorConverter.ConvertFromString(color);
        }
        catch (FormatException)
        {
            return Colors.SkyBlue;
        }
    }

    private static T Frozen<T>(T freezable)
        where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }

    private readonly record struct ClipKey(
        Core.Audio.AudioBuffer Audio, long Start, double ViewStart, double ViewLength, double Width, double Height);
}
