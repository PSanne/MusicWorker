using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using MusicWorker.App.Helpers;
using MusicWorker.App.ViewModels;
using MusicWorker.Core.Mixing;
using MusicWorker.Core.Visualization;

namespace MusicWorker.App.Controls;

/// <summary>
/// Spuransicht des Mischpults: Zeitleiste, eine Bahn pro Spur mit ihren Clips (als Mini-Wellenform),
/// Abspiellinie mit Positionsgriff, Takt-Raster und Loop-Bereich. Clips lassen sich mit der Maus
/// verschieben – auch auf eine andere Spur; Doppelklick öffnet einen Clip im Editor.
/// Umschalt+Ziehen legt einen Loop-Bereich fest. Strg+Mausrad zoomt, Umschalt+Mausrad scrollt waagerecht.
/// Im Hüllkurven-Modus werden statt der Clips die Hüllkurven-Punkte bearbeitet.
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

    public static readonly DependencyProperty SetLoopCommandProperty = DependencyProperty.Register(
        nameof(SetLoopCommand), typeof(ICommand), typeof(TimelineView), new PropertyMetadata(null));

    public static readonly DependencyProperty RecordingStartFrameProperty = DependencyProperty.Register(
        nameof(RecordingStartFrame), typeof(long), typeof(TimelineView),
        new FrameworkPropertyMetadata(-1L, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CreateClipCommandProperty = DependencyProperty.Register(
        nameof(CreateClipCommand), typeof(ICommand), typeof(TimelineView), new PropertyMetadata(null));

    public static readonly DependencyProperty EditAutomationCommandProperty = DependencyProperty.Register(
        nameof(EditAutomationCommand), typeof(ICommand), typeof(TimelineView), new PropertyMetadata(null));

    public static readonly DependencyProperty IsEnvelopeModeProperty = DependencyProperty.Register(
        nameof(IsEnvelopeMode), typeof(bool), typeof(TimelineView),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BpmProperty = DependencyProperty.Register(
        nameof(Bpm), typeof(double), typeof(TimelineView),
        new FrameworkPropertyMetadata(120.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BeatsPerBarProperty = DependencyProperty.Register(
        nameof(BeatsPerBar), typeof(int), typeof(TimelineView),
        new FrameworkPropertyMetadata(4, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ShowGridProperty = DependencyProperty.Register(
        nameof(ShowGrid), typeof(bool), typeof(TimelineView),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IsLoopingProperty = DependencyProperty.Register(
        nameof(IsLooping), typeof(bool), typeof(TimelineView),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LoopStartProperty = DependencyProperty.Register(
        nameof(LoopStart), typeof(long), typeof(TimelineView),
        new FrameworkPropertyMetadata(0L, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LoopEndProperty = DependencyProperty.Register(
        nameof(LoopEnd), typeof(long), typeof(TimelineView),
        new FrameworkPropertyMetadata(0L, FrameworkPropertyMetadataOptions.AffectsRender));

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
    private static readonly Pen BeatPen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromArgb(0x1C, 0xFF, 0xFF, 0xFF))), 1));
    private static readonly Pen BarPen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF))), 1));
    private static readonly Brush BarTextBrush = Frozen(new SolidColorBrush(Color.FromArgb(0x90, 0xFF, 0xFF, 0xFF)));
    private static readonly Brush LoopActiveBrush = Frozen(new SolidColorBrush(Color.FromArgb(0x90, 0x4C, 0xAF, 0x50)));
    private static readonly Brush LoopInactiveBrush = Frozen(new SolidColorBrush(Color.FromArgb(0x50, 0x9E, 0x9E, 0x9E)));
    private static readonly Brush LoopLaneBrush = Frozen(new SolidColorBrush(Color.FromArgb(0x14, 0x4C, 0xAF, 0x50)));
    private static readonly Pen LoopEdgePen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromArgb(0xC0, 0x66, 0xBB, 0x6A))), 1));
    private static readonly Brush RecordingBrush = Frozen(new SolidColorBrush(Color.FromArgb(0x55, 0xE5, 0x39, 0x35)));
    private static readonly Brush ArmedLaneBrush = Frozen(new SolidColorBrush(Color.FromArgb(0x18, 0xE5, 0x39, 0x35)));
    private static readonly Brush EnvelopeLabelBackground = Frozen(new SolidColorBrush(Color.FromArgb(0xD0, 0x15, 0x17, 0x1C)));
    private static readonly Pen CrossfadePen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromArgb(0xC8, 0xFF, 0xFF, 0xFF))), 1));
    private static readonly Typeface LabelTypeface = new("Segoe UI");
    private static readonly double[] TickSteps = [0.1, 0.2, 0.5, 1, 2, 5, 10, 15, 30, 60, 120, 300, 600];
    private static readonly TimeSpan SeekThrottle = TimeSpan.FromMilliseconds(90);

    private readonly Dictionary<ClipViewModel, (ClipKey Key, Geometry Geometry)> _waveCache = [];

    private double _viewStart;
    private double _viewLength;
    private ClipViewModel? _dragClip;
    private long _dragOffset;
    private bool _dragMoved;
    private bool _isLoopDrag;
    private TrackViewModel? _envelopeTrack;
    private int _envelopeIndex = -1;
    private bool _envelopeMoved;
    private long _loopAnchor;
    private long _loopCurrent;
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

    /// <summary>Beginn der laufenden Aufnahme (-1 = keine).</summary>
    public long RecordingStartFrame
    {
        get => (long)GetValue(RecordingStartFrameProperty);
        set => SetValue(RecordingStartFrameProperty, value);
    }

    /// <summary>Parameter: <see cref="LanePosition"/> (Doppelklick auf eine freie Stelle einer Notenspur).</summary>
    public ICommand? CreateClipCommand
    {
        get => (ICommand?)GetValue(CreateClipCommandProperty);
        set => SetValue(CreateClipCommandProperty, value);
    }

    /// <summary>Parameter: <see cref="AutomationEdit"/>.</summary>
    public ICommand? EditAutomationCommand
    {
        get => (ICommand?)GetValue(EditAutomationCommandProperty);
        set => SetValue(EditAutomationCommandProperty, value);
    }

    public bool IsEnvelopeMode
    {
        get => (bool)GetValue(IsEnvelopeModeProperty);
        set => SetValue(IsEnvelopeModeProperty, value);
    }

    /// <summary>Parameter: <see cref="LoopRange"/> (Umschalt+Ziehen).</summary>
    public ICommand? SetLoopCommand
    {
        get => (ICommand?)GetValue(SetLoopCommandProperty);
        set => SetValue(SetLoopCommandProperty, value);
    }

    public double Bpm
    {
        get => (double)GetValue(BpmProperty);
        set => SetValue(BpmProperty, value);
    }

    public int BeatsPerBar
    {
        get => (int)GetValue(BeatsPerBarProperty);
        set => SetValue(BeatsPerBarProperty, value);
    }

    public bool ShowGrid
    {
        get => (bool)GetValue(ShowGridProperty);
        set => SetValue(ShowGridProperty, value);
    }

    public bool IsLooping
    {
        get => (bool)GetValue(IsLoopingProperty);
        set => SetValue(IsLoopingProperty, value);
    }

    public long LoopStart
    {
        get => (long)GetValue(LoopStartProperty);
        set => SetValue(LoopStartProperty, value);
    }

    public long LoopEnd
    {
        get => (long)GetValue(LoopEndProperty);
        set => SetValue(LoopEndProperty, value);
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

        for (int t = 0; t < tracks.Count; t++)
        {
            double top = RulerHeight + t * LaneHeight;
            dc.DrawRectangle(LaneBrushes[t % 2], null, new Rect(0, top, width, LaneHeight));
            if (tracks[t].IsArmed)
            {
                dc.DrawRectangle(ArmedLaneBrush, null, new Rect(0, top, width, LaneHeight));
            }

            dc.DrawLine(LaneLinePen, new Point(0, top + LaneHeight - 0.5), new Point(width, top + LaneHeight - 0.5));
        }

        DrawGrid(dc, width, height, pixelsPerDip);
        DrawLoopLanes(dc, height);

        var visibleClips = new HashSet<ClipViewModel>();
        for (int t = 0; t < tracks.Count; t++)
        {
            double top = RulerHeight + t * LaneHeight;

            var color = ParseColor(tracks[t].Color);
            var fill = Frozen(new SolidColorBrush(Color.FromArgb(0x48, color.R, color.G, color.B)));
            var edge = Frozen(new Pen(Frozen(new SolidColorBrush(color)), 1));
            var wavePen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromArgb(0xD8, color.R, color.G, color.B))), 1));

            if (IsEnvelopeMode)
            {
                dc.PushOpacity(0.45);
            }

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
                if (clip.Notes is { } notes)
                {
                    DrawNoteClip(dc, clip, notes, rect, wavePen.Brush);
                }
                else
                {
                    dc.DrawGeometry(null, wavePen, GetWaveform(clip, rect, width));
                }

                var label = Text(clip.HasTakes ? $"{tracks[t].Name}  ·  {clip.Takes!.Count} Takes" : tracks[t].Name, 11, ClipTextBrush, pixelsPerDip);
                dc.DrawText(label, new Point(Math.Max(rect.Left, 0) + 6, rect.Top + 3));
                dc.Pop();
            }

            DrawCrossfades(dc, tracks[t], top, width);
            if (IsEnvelopeMode)
            {
                dc.Pop();
            }

            DrawEnvelope(dc, tracks[t], top, width, color, pixelsPerDip);

            // Laufende Aufnahme: rot wachsender Bereich auf scharfgeschalteten Spuren
            if (RecordingStartFrame >= 0 && tracks[t].IsArmed && PlayheadFrame > RecordingStartFrame)
            {
                double x0 = Math.Max(0, FrameToX(RecordingStartFrame));
                double x1 = Math.Min(width, FrameToX(PlayheadFrame));
                if (x1 > x0)
                {
                    dc.DrawRectangle(RecordingBrush, null, new Rect(x0, top + 4, x1 - x0, LaneHeight - 8));
                }
            }
        }

        // Zwischenspeicher für nicht mehr sichtbare oder gelöschte Clips aufräumen
        foreach (var stale in _waveCache.Keys.Where(c => !visibleClips.Contains(c)).ToArray())
        {
            _waveCache.Remove(stale);
        }

        DrawPlayhead(dc, width, height);
    }

    /// <summary>Notenclip als Mini-Piano-Roll: jede Note ein kleiner Balken (Höhe = Tonhöhe).</summary>
    private void DrawNoteClip(DrawingContext dc, ClipViewModel clip, IReadOnlyList<Core.Notes.Note> notes, Rect rect, Brush brush)
    {
        if (notes.Count == 0)
        {
            return;
        }

        int low = notes.Min(n => n.Pitch);
        int high = notes.Max(n => n.Pitch);
        if (high - low < 12)
        {
            low -= (12 - (high - low)) / 2;
            high = low + 12;
        }

        double framesPerTick = Core.Notes.MusicalTime.FramesPerTick(Bpm, SampleRate);
        double top = rect.Top + 16;
        double area = Math.Max(4, rect.Height - 20);
        double barHeight = Math.Clamp(area / (high - low + 1), 1.5, 5);
        foreach (var note in notes)
        {
            if (note.Start >= clip.LengthTicks)
            {
                continue;
            }

            double x0 = FrameToX(clip.StartFrame + note.Start * framesPerTick);
            double x1 = FrameToX(clip.StartFrame + Math.Min(note.End, clip.LengthTicks) * framesPerTick);
            if (x1 < rect.Left || x0 > rect.Right)
            {
                continue;
            }

            double y = top + (high - note.Pitch) / (double)(high - low) * (area - barHeight);
            dc.DrawRectangle(brush, null, new Rect(x0, y, Math.Max(1.5, x1 - x0 - 1), barHeight));
        }
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
        var (loopStart, loopEnd) = CurrentLoop();
        if (loopEnd > loopStart)
        {
            double x0 = Math.Max(-1, FrameToX(loopStart));
            double x1 = Math.Min(width + 1, FrameToX(loopEnd));
            if (x1 > x0)
            {
                dc.DrawRectangle(IsLooping || _isLoopDrag ? LoopActiveBrush : LoopInactiveBrush, null, new Rect(x0, 0, x1 - x0, RulerHeight));
            }
        }

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

    /// <summary>Takt-Raster: feine Linien pro Schlag, kräftigere pro Takt (mit Taktnummer).</summary>
    private void DrawGrid(DrawingContext dc, double width, double height, double pixelsPerDip)
    {
        if (!ShowGrid || Bpm <= 0 || SampleRate <= 0)
        {
            return;
        }

        double beatFrames = 60.0 / Bpm * SampleRate;
        double beatPixels = beatFrames / _viewLength * width;
        int beatsPerBar = Math.Max(1, BeatsPerBar);
        double barPixels = beatPixels * beatsPerBar;
        if (barPixels < 4)
        {
            return; // zu weit herausgezoomt
        }

        bool drawBeats = beatPixels >= 7;
        int barLabelEvery = barPixels >= 28 ? 1 : (int)Math.Ceiling(28 / barPixels);
        long first = (long)Math.Floor(_viewStart / beatFrames);
        long last = (long)Math.Ceiling((_viewStart + _viewLength) / beatFrames);
        for (long beat = Math.Max(0, first); beat <= last; beat++)
        {
            bool isBar = beat % beatsPerBar == 0;
            if (!isBar && !drawBeats)
            {
                continue;
            }

            double x = Math.Round(FrameToX(beat * beatFrames)) + 0.5;
            dc.DrawLine(isBar ? BarPen : BeatPen, new Point(x, RulerHeight), new Point(x, height));
            long bar = beat / beatsPerBar;
            if (isBar && bar % barLabelEvery == 0)
            {
                dc.DrawText(Text((bar + 1).ToString(CultureInfo.CurrentCulture), 9, BarTextBrush, pixelsPerDip), new Point(x + 2, RulerHeight + 1));
            }
        }
    }

    private void DrawLoopLanes(DrawingContext dc, double height)
    {
        var (loopStart, loopEnd) = CurrentLoop();
        if (!(IsLooping || _isLoopDrag) || loopEnd <= loopStart)
        {
            return;
        }

        double x0 = FrameToX(loopStart);
        double x1 = FrameToX(loopEnd);
        dc.DrawRectangle(LoopLaneBrush, null, new Rect(new Point(Math.Max(-1, x0), RulerHeight), new Point(Math.Min(ActualWidth + 1, x1), height)));
        dc.DrawLine(LoopEdgePen, new Point(x0, 0), new Point(x0, height));
        dc.DrawLine(LoopEdgePen, new Point(x1, 0), new Point(x1, height));
    }

    /// <summary>Überlappende Clips werden übergeblendet – das zeigt ein „X“ im Überlappungsbereich.</summary>
    private void DrawCrossfades(DrawingContext dc, TrackViewModel track, double top, double width)
    {
        if (track.Clips.Count < 2)
        {
            return;
        }

        var ordered = track.Clips.OrderBy(c => c.StartFrame).ToArray();
        for (int i = 0; i + 1 < ordered.Length; i++)
        {
            long overlapStart = ordered[i + 1].StartFrame;
            long overlapEnd = Math.Min(ordered[i].EndFrame, ordered[i + 1].EndFrame);
            if (overlapEnd <= overlapStart)
            {
                continue;
            }

            double x0 = FrameToX(overlapStart);
            double x1 = FrameToX(overlapEnd);
            if (x1 < 0 || x0 > width || x1 - x0 < 3)
            {
                continue;
            }

            double y0 = top + 6;
            double y1 = top + LaneHeight - 6;
            dc.DrawLine(CrossfadePen, new Point(x0, y1), new Point(x1, y0)); // Einblenden
            dc.DrawLine(CrossfadePen, new Point(x0, y0), new Point(x1, y1)); // Ausblenden
        }
    }

    /// <summary>
    /// Zeichnet die gezeigte Hüllkurve einer Spur. Im Hüllkurven-Modus mit Punkten (ohne Kurve: gestrichelt
    /// auf Höhe des Reglers), sonst nur dezent, wenn es eine Kurve gibt.
    /// </summary>
    private void DrawEnvelope(DrawingContext dc, TrackViewModel track, double top, double width, Color color, double pixelsPerDip)
    {
        var lane = track.LaneFor(track.ShownAutomation);
        if (!IsEnvelopeMode && lane.IsEmpty)
        {
            return;
        }

        var lineColor = Color.FromRgb((byte)((color.R + 255) / 2), (byte)((color.G + 255) / 2), (byte)((color.B + 255) / 2));
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(IsEnvelopeMode ? (byte)0xFF : (byte)0x90, lineColor.R, lineColor.G, lineColor.B)), IsEnvelopeMode ? 2 : 1.2);
        if (lane.IsEmpty)
        {
            pen.DashStyle = DashStyles.Dash;
            pen.Freeze();
            double y = ValueToY(track.ShownAutomation, track.KnobValue(track.ShownAutomation), top);
            dc.DrawLine(pen, new Point(0, y), new Point(width, y));
            return;
        }

        pen.Freeze();
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            var points = lane.Points;
            context.BeginFigure(new Point(Math.Min(0, FrameToX(points[0].Frame)), ValueToY(lane.Target, points[0].Value, top)), false, false);
            foreach (var point in points)
            {
                context.LineTo(new Point(FrameToX(point.Frame), ValueToY(lane.Target, point.Value, top)), true, false);
            }

            context.LineTo(new Point(Math.Max(width, FrameToX(points[^1].Frame)), ValueToY(lane.Target, points[^1].Value, top)), true, false);
        }

        geometry.Freeze();
        dc.DrawGeometry(null, pen, geometry);
        if (!IsEnvelopeMode)
        {
            return;
        }

        var fill = Frozen(new SolidColorBrush(lineColor));
        for (int i = 0; i < lane.Points.Count; i++)
        {
            var point = lane.Points[i];
            double x = FrameToX(point.Frame);
            if (x < -6 || x > width + 6)
            {
                continue;
            }

            double y = ValueToY(lane.Target, point.Value, top);
            bool dragged = ReferenceEquals(track, _envelopeTrack) && i == _envelopeIndex;
            dc.DrawEllipse(dragged ? Brushes.White : fill, Frozen(new Pen(Brushes.Black, 1)), new Point(x, y), dragged ? 5.5 : 4, dragged ? 5.5 : 4);
            if (dragged)
            {
                var label = Text(Automation.Format(lane.Target, point.Value), 11, Brushes.White, pixelsPerDip);
                double lx = Math.Min(x + 8, width - label.Width - 6);
                double ly = Math.Clamp(y - label.Height - 6, top + 2, top + LaneHeight - label.Height - 2);
                dc.DrawRoundedRectangle(EnvelopeLabelBackground, null, new Rect(lx - 3, ly - 1, label.Width + 6, label.Height + 2), 3, 3);
                dc.DrawText(label, new Point(lx, ly));
            }
        }
    }

    /// <summary>Wert → senkrechte Position in der Spurbahn (oben = Maximum).</summary>
    private static double ValueToY(AutomationTarget target, double value, double top) =>
        top + 6 + (1 - Automation.RangeOf(target).Normalize(value)) * (LaneHeight - 12);

    private static double YToValue(AutomationTarget target, double y, double top) =>
        Automation.RangeOf(target).Denormalize(1 - (y - top - 6) / (LaneHeight - 12));

    /// <summary>Index des Hüllkurven-Punkts unter der Maus (oder -1).</summary>
    private int HitEnvelopePoint(TrackViewModel track, double top, Point position)
    {
        var lane = track.LaneFor(track.ShownAutomation);
        for (int i = lane.Points.Count - 1; i >= 0; i--)
        {
            var point = lane.Points[i];
            double dx = FrameToX(point.Frame) - position.X;
            double dy = ValueToY(lane.Target, point.Value, top) - position.Y;
            if (dx * dx + dy * dy <= 49)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Spur und Oberkante der Bahn unter der Maus (nur innerhalb der Spuren).</summary>
    private (TrackViewModel Track, double Top)? LaneUnder(double y)
    {
        var tracks = Tracks;
        int lane = (int)Math.Floor((y - RulerHeight) / LaneHeight);
        return tracks is not null && y >= RulerHeight && lane >= 0 && lane < tracks.Count
            ? (tracks[lane], RulerHeight + lane * LaneHeight)
            : null;
    }

    private bool EnvelopeMouseDown(Point position, int clickCount)
    {
        if (LaneUnder(position.Y) is not var (track, top))
        {
            return false;
        }

        int hit = HitEnvelopePoint(track, top, position);
        if (hit >= 0 && clickCount == 2)
        {
            Execute(EditAutomationCommand, new AutomationEdit(track, AutomationEditKind.Remove, hit, 0, 0));
            return true;
        }

        bool added = false;
        if (hit < 0)
        {
            var edit = new AutomationEdit(track, AutomationEditKind.Add, -1, XToFrame(position.X), YToValue(track.ShownAutomation, position.Y, top));
            Execute(EditAutomationCommand, edit);
            hit = edit.ResultIndex;
            added = true;
        }

        if (hit >= 0)
        {
            _envelopeTrack = track;
            _envelopeIndex = hit;
            _envelopeMoved = added; // nach dem Hinzufügen gibt es schon einen Rückgängig-Schritt
            CaptureMouse();
            InvalidateVisual();
        }

        return true;
    }

    private (long Start, long End) CurrentLoop() => _isLoopDrag
        ? (Math.Min(_loopAnchor, _loopCurrent), Math.Max(_loopAnchor, _loopCurrent))
        : (LoopStart, LoopEnd);

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

        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
        {
            // Umschalt+Ziehen: Loop-Bereich aufziehen
            _isLoopDrag = CaptureMouse();
            _loopAnchor = _loopCurrent = frame;
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (position.Y < RulerHeight)
        {
            _isSeeking = CaptureMouse();
            _seekFrame = frame;
            SendSeek(force: true);
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (IsEnvelopeMode && EnvelopeMouseDown(position, e.ClickCount))
        {
            e.Handled = true;
            return;
        }

        var clip = HitClip(position);
        if (clip is null && e.ClickCount == 2 && LaneUnder(position.Y) is var (laneTrack, _) && laneTrack.IsNoteTrack)
        {
            Execute(CreateClipCommand, new LanePosition(laneTrack, frame));
            e.Handled = true;
            return;
        }

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
        _dragMoved = false;
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

        if (_isLoopDrag)
        {
            AutoScroll(position.X);
            _loopCurrent = XToFrame(Math.Clamp(position.X, 0, ActualWidth));
            InvalidateVisual();
            return;
        }

        if (_envelopeTrack is { } envelopeTrack && Tracks is { } list && list.IndexOf(envelopeTrack) is int laneIndex and >= 0)
        {
            AutoScroll(position.X);
            double top = RulerHeight + laneIndex * LaneHeight;
            double y = Math.Clamp(position.Y, top + 6, top + LaneHeight - 6);
            var edit = new AutomationEdit(envelopeTrack, AutomationEditKind.Move, _envelopeIndex,
                XToFrame(Math.Clamp(position.X, 0, ActualWidth)), YToValue(envelopeTrack.ShownAutomation, y, top), dragStarted: !_envelopeMoved);
            Execute(EditAutomationCommand, edit);
            _envelopeMoved = true;
            return;
        }

        if (IsEnvelopeMode && LaneUnder(position.Y) is var (hoverTrack, hoverTop))
        {
            Cursor = HitEnvelopePoint(hoverTrack, hoverTop, position) >= 0 ? Cursors.Hand : Cursors.Cross;
            return;
        }

        if (_dragClip is { } clip)
        {
            AutoScroll(position.X);
            long newStart = Math.Max(0, XToFrame(Math.Clamp(position.X, 0, ActualWidth)) - _dragOffset);
            var target = LaneAt(position.Y);
            bool changeTrack = target is not null && !ReferenceEquals(target, clip.Track);
            if (newStart != clip.StartFrame || changeTrack)
            {
                Execute(MoveClipCommand, new ClipMove(clip, newStart, changeTrack ? target : null, DragStarted: !_dragMoved));
                _dragMoved = true;
            }

            return;
        }

        Cursor = (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? Cursors.IBeam
            : position.Y < RulerHeight ? Cursors.Hand
            : HitClip(position) is not null ? Cursors.SizeAll : Cursors.Arrow;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_isSeeking)
        {
            SendSeek(force: true);
        }

        if (_isLoopDrag)
        {
            var (start, end) = CurrentLoop();
            Execute(SetLoopCommand, new LoopRange(start, end));
        }

        EndDrag();
        e.Handled = true;
    }

    /// <summary>Rechtsklick auf einen Hüllkurven-Punkt löscht ihn.</summary>
    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonDown(e);
        var position = e.GetPosition(this);
        if (IsEnvelopeMode && LaneUnder(position.Y) is var (track, top) && HitEnvelopePoint(track, top, position) is int hit and >= 0)
        {
            Execute(EditAutomationCommand, new AutomationEdit(track, AutomationEditKind.Remove, hit, 0, 0));
            e.Handled = true;
        }
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        _isSeeking = false;
        _isLoopDrag = false;
        _envelopeTrack = null;
        _envelopeIndex = -1;
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

    /// <summary>Die Spur unter der Mausposition (oberhalb/unterhalb wird auf die erste/letzte begrenzt).</summary>
    private TrackViewModel? LaneAt(double y)
    {
        var tracks = Tracks;
        if (tracks is null || tracks.Count == 0)
        {
            return null;
        }

        int lane = (int)Math.Floor((y - RulerHeight) / LaneHeight);
        return tracks[Math.Clamp(lane, 0, tracks.Count - 1)];
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
        _isLoopDrag = false;
        _envelopeTrack = null;
        _envelopeIndex = -1;
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
