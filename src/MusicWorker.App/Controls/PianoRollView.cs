using System.Collections.Immutable;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using MusicWorker.App.ViewModels;
using MusicWorker.Core.Notes;

namespace MusicWorker.App.Controls;

/// <summary>
/// Piano-Roll: links die Klaviatur (Klick = vorhören), oben das Taktlineal (Klick = Cursor setzen),
/// in der Mitte die Noten, unten das Anschlag-Feld. Klick in eine freie Stelle setzt eine Note
/// (Ziehen bestimmt die Länge), Ziehen einer Note verschiebt die Auswahl, Ziehen am rechten Rand
/// ändert die Länge, Doppel- oder Rechtsklick löscht, Umschalt+Ziehen wählt mit einem Rahmen aus,
/// Strg+Klick ergänzt die Auswahl. Mausrad scrollt, Umschalt+Mausrad waagerecht, Strg+Mausrad zoomt.
/// </summary>
public sealed class PianoRollView : FrameworkElement
{
    public const double KeyWidth = 64;
    public const double RulerHeight = 22;
    public const double VelocityHeight = 72;
    public const double RowHeight = 12;

    public static readonly DependencyProperty NotesProperty = Register(nameof(Notes), typeof(IReadOnlyList<Note>), null);
    public static readonly DependencyProperty SelectionProperty = Register(nameof(Selection), typeof(ImmutableHashSet<Note>), null);
    public static readonly DependencyProperty LengthTicksProperty = Register(nameof(LengthTicks), typeof(long), (long)MusicalTime.TicksPerQuarter * 16);
    public static readonly DependencyProperty GridTicksProperty = Register(nameof(GridTicks), typeof(long), (long)MusicalTime.TicksPerQuarter / 4);
    public static readonly DependencyProperty BeatsPerBarProperty = Register(nameof(BeatsPerBar), typeof(int), 4);
    public static readonly DependencyProperty PlayheadTickProperty = Register(nameof(PlayheadTick), typeof(long), 0L);
    public static readonly DependencyProperty StepCursorTickProperty = Register(nameof(StepCursorTick), typeof(long), 0L);
    public static readonly DependencyProperty ShowStepCursorProperty = Register(nameof(ShowStepCursor), typeof(bool), false);
    public static readonly DependencyProperty IsDrumsProperty = Register(nameof(IsDrums), typeof(bool), false);
    public static readonly DependencyProperty VersionProperty = Register(nameof(Version), typeof(int), 0);

    public static readonly DependencyProperty DocumentProperty = DependencyProperty.Register(
        nameof(Document), typeof(object), typeof(PianoRollView),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((PianoRollView)d).ResetView()));

    public static readonly DependencyProperty EditCommandProperty = DependencyProperty.Register(
        nameof(EditCommand), typeof(ICommand), typeof(PianoRollView), new PropertyMetadata(null));

    private static readonly Brush Background = Frozen(new SolidColorBrush(Color.FromRgb(0x1B, 0x1D, 0x23)));
    private static readonly Brush BlackRow = Frozen(new SolidColorBrush(Color.FromRgb(0x17, 0x19, 0x1E)));
    private static readonly Brush OutsideBrush = Frozen(new SolidColorBrush(Color.FromArgb(0x90, 0x0E, 0x0F, 0x12)));
    private static readonly Brush RulerBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x25, 0x28, 0x30)));
    private static readonly Brush VelocityBackground = Frozen(new SolidColorBrush(Color.FromRgb(0x20, 0x22, 0x29)));
    private static readonly Brush TextBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x9A, 0xA0, 0xAA)));
    private static readonly Brush WhiteKey = Frozen(new SolidColorBrush(Color.FromRgb(0xE8, 0xEA, 0xEE)));
    private static readonly Brush BlackKey = Frozen(new SolidColorBrush(Color.FromRgb(0x2A, 0x2C, 0x33)));
    private static readonly Brush KeyText = Frozen(new SolidColorBrush(Color.FromRgb(0x40, 0x44, 0x4C)));
    private static readonly Brush BandBrush = Frozen(new SolidColorBrush(Color.FromArgb(0x30, 0x90, 0xCA, 0xF9)));
    private static readonly Pen RowPen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromRgb(0x24, 0x27, 0x2E))), 1));
    private static readonly Pen OctavePen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromRgb(0x38, 0x3C, 0x46))), 1));
    private static readonly Pen GridPen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF))), 1));
    private static readonly Pen BeatPen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF))), 1));
    private static readonly Pen BarPen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF))), 1));
    private static readonly Pen EndPen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromRgb(0xFF, 0xB7, 0x4D))), 1.5));
    private static readonly Pen PlayheadPen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromRgb(0xFF, 0x52, 0x52))), 1.5));
    private static readonly Pen CursorPen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromRgb(0x66, 0xBB, 0x6A))), 2));
    private static readonly Pen SelectedPen = Frozen(new Pen(Brushes.White, 1.5));
    private static readonly Pen NoteEdgePen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromArgb(0xA0, 0, 0, 0))), 1));
    private static readonly Pen BandPen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromRgb(0x90, 0xCA, 0xF9))), 1));
    private static readonly Color[] VoiceColors =
    [
        Color.FromRgb(0x4F, 0xC3, 0xF7), Color.FromRgb(0xFF, 0xB7, 0x4D), Color.FromRgb(0x81, 0xC7, 0x84),
        Color.FromRgb(0xF0, 0x62, 0x92), Color.FromRgb(0xBA, 0x68, 0xC8), Color.FromRgb(0xFF, 0xF1, 0x76),
    ];

    private static readonly Typeface LabelTypeface = new("Segoe UI");

    private double _viewStart;
    private double _ticksPerPixel = 4;
    private double _topPitch = 84;
    private Mode _mode;
    private Point _down;
    private long _downTick;
    private int _downPitch;
    private Note _anchor;
    private Rect _band;

    public PianoRollView()
    {
        Focusable = true;
        ClipToBounds = true;
        FocusVisualStyle = null;
    }

    private enum Mode
    {
        None,
        Seek,
        Preview,
        Move,
        Resize,
        Band,
        Velocity,
    }

    public IReadOnlyList<Note>? Notes { get => (IReadOnlyList<Note>?)GetValue(NotesProperty); set => SetValue(NotesProperty, value); }

    public ImmutableHashSet<Note>? Selection { get => (ImmutableHashSet<Note>?)GetValue(SelectionProperty); set => SetValue(SelectionProperty, value); }

    public long LengthTicks { get => (long)GetValue(LengthTicksProperty); set => SetValue(LengthTicksProperty, value); }

    public long GridTicks { get => (long)GetValue(GridTicksProperty); set => SetValue(GridTicksProperty, value); }

    public int BeatsPerBar { get => (int)GetValue(BeatsPerBarProperty); set => SetValue(BeatsPerBarProperty, value); }

    public long PlayheadTick { get => (long)GetValue(PlayheadTickProperty); set => SetValue(PlayheadTickProperty, value); }

    public long StepCursorTick { get => (long)GetValue(StepCursorTickProperty); set => SetValue(StepCursorTickProperty, value); }

    public bool ShowStepCursor { get => (bool)GetValue(ShowStepCursorProperty); set => SetValue(ShowStepCursorProperty, value); }

    public bool IsDrums { get => (bool)GetValue(IsDrumsProperty); set => SetValue(IsDrumsProperty, value); }

    public int Version { get => (int)GetValue(VersionProperty); set => SetValue(VersionProperty, value); }

    /// <summary>Der bearbeitete Clip – wechselt er, wird die Ansicht neu ausgerichtet.</summary>
    public object? Document { get => GetValue(DocumentProperty); set => SetValue(DocumentProperty, value); }

    /// <summary>Parameter: <see cref="PianoRollEdit"/>.</summary>
    public ICommand? EditCommand { get => (ICommand?)GetValue(EditCommandProperty); set => SetValue(EditCommandProperty, value); }

    private IReadOnlyList<Note> NoteList => Notes ?? [];

    private double GridBottom => Math.Max(RulerHeight, ActualHeight - VelocityHeight);

    // ───────────── Ansicht ─────────────

    public void ZoomToFit()
    {
        double width = Math.Max(100, ActualWidth - KeyWidth);
        long length = Math.Max(LengthTicks, NoteTools.EndOf(NoteList));
        _ticksPerPixel = Math.Max(0.5, length * 1.05 / width);
        _viewStart = 0;
        InvalidateVisual();
    }

    public void ZoomIn() => Zoom(0.7, KeyWidth + (ActualWidth - KeyWidth) / 2);

    public void ZoomOut() => Zoom(1 / 0.7, KeyWidth + (ActualWidth - KeyWidth) / 2);

    private void ResetView()
    {
        var notes = NoteList;
        double center = notes.Count > 0 ? notes.Average(n => n.Pitch) : 60;
        double rows = Math.Max(12, (GridBottom - RulerHeight) / RowHeight);
        _topPitch = Math.Clamp(Math.Round(center + rows / 2), rows - 1, 127);
        ZoomToFit();
    }

    private void Zoom(double factor, double anchorX)
    {
        double anchor = XToTick(anchorX);
        _ticksPerPixel = Math.Clamp(_ticksPerPixel * factor, 0.25, 200);
        _viewStart = Math.Max(0, anchor - (anchorX - KeyWidth) * _ticksPerPixel);
        InvalidateVisual();
    }

    private double TickToX(double tick) => KeyWidth + (tick - _viewStart) / _ticksPerPixel;

    private double XToTick(double x) => _viewStart + (x - KeyWidth) * _ticksPerPixel;

    private double PitchToY(int pitch) => RulerHeight + (_topPitch - pitch) * RowHeight;

    private int YToPitch(double y) => (int)Math.Clamp(_topPitch - Math.Floor((y - RulerHeight) / RowHeight), 0, 127);

    private static bool IsBlack(int pitch) => (pitch % 12) is 1 or 3 or 6 or 8 or 10;

    // ───────────── Zeichnen ─────────────

    protected override void OnRender(DrawingContext dc)
    {
        double width = ActualWidth;
        double height = ActualHeight;
        dc.DrawRectangle(Background, null, new Rect(0, 0, width, height));
        if (width <= KeyWidth + 10 || height <= RulerHeight + VelocityHeight + 10)
        {
            return;
        }

        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        double bottom = GridBottom;
        var gridArea = new Rect(KeyWidth, RulerHeight, width - KeyWidth, bottom - RulerHeight);

        dc.PushClip(new RectangleGeometry(gridArea));
        DrawRows(dc, width, bottom);
        DrawTimeLines(dc, RulerHeight, bottom, width);
        double end = TickToX(LengthTicks);
        if (end < width)
        {
            dc.DrawRectangle(OutsideBrush, null, new Rect(Math.Max(KeyWidth, end), RulerHeight, width - Math.Max(KeyWidth, end), bottom - RulerHeight));
        }

        DrawNotes(dc, width, pixelsPerDip);
        if (_mode == Mode.Band)
        {
            dc.DrawRectangle(BandBrush, BandPen, _band);
        }

        dc.Pop();

        DrawRuler(dc, width, pixelsPerDip);
        DrawKeys(dc, bottom, pixelsPerDip);
        DrawVelocities(dc, width, height, bottom, pixelsPerDip);

        // Clip-Ende, Abspielposition und Eingabe-Cursor über alles
        dc.PushClip(new RectangleGeometry(new Rect(KeyWidth, 0, width - KeyWidth, height)));
        dc.DrawLine(EndPen, new Point(end, 0), new Point(end, height));
        double playhead = TickToX(PlayheadTick);
        dc.DrawLine(PlayheadPen, new Point(playhead, 0), new Point(playhead, height));
        if (ShowStepCursor)
        {
            double cursor = TickToX(StepCursorTick);
            dc.DrawLine(CursorPen, new Point(cursor, RulerHeight), new Point(cursor, bottom));
        }

        dc.Pop();
    }

    private void DrawRows(DrawingContext dc, double width, double bottom)
    {
        for (int pitch = (int)_topPitch; pitch >= 0; pitch--)
        {
            double y = PitchToY(pitch);
            if (y > bottom)
            {
                break;
            }

            if (IsBlack(pitch))
            {
                dc.DrawRectangle(BlackRow, null, new Rect(KeyWidth, y, width - KeyWidth, RowHeight));
            }

            dc.DrawLine(pitch % 12 == 0 ? OctavePen : RowPen, new Point(KeyWidth, y + RowHeight), new Point(width, y + RowHeight));
        }
    }

    private void DrawTimeLines(DrawingContext dc, double top, double bottom, double width)
    {
        long grid = Math.Max(1, GridTicks);
        long beat = MusicalTime.TicksPerQuarter;
        long bar = MusicalTime.TicksPerBar(BeatsPerBar);
        long step = grid / _ticksPerPixel >= 6 ? grid : beat / _ticksPerPixel >= 6 ? beat : bar;
        long first = (long)Math.Floor(_viewStart / step) * step;
        for (long tick = Math.Max(0, first); TickToX(tick) <= width; tick += step)
        {
            double x = Math.Round(TickToX(tick)) + 0.5;
            var pen = tick % bar == 0 ? BarPen : tick % beat == 0 ? BeatPen : GridPen;
            dc.DrawLine(pen, new Point(x, top), new Point(x, bottom));
        }
    }

    private void DrawNotes(DrawingContext dc, double width, double pixelsPerDip)
    {
        var selection = Selection ?? [];
        foreach (var note in NoteList)
        {
            double x0 = TickToX(note.Start);
            double x1 = TickToX(note.End);
            double y = PitchToY(note.Pitch);
            if (x1 < KeyWidth || x0 > width || y < RulerHeight - RowHeight || y > GridBottom)
            {
                continue;
            }

            var color = VoiceColors[Math.Abs(note.Voice) % VoiceColors.Length];
            byte alpha = (byte)(110 + 145 * note.Velocity / 127);
            var fill = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
            fill.Freeze();
            var rect = new Rect(x0, y + 1, Math.Max(3, x1 - x0 - 1), RowHeight - 2);
            dc.DrawRoundedRectangle(fill, selection.Contains(note) ? SelectedPen : NoteEdgePen, rect, 2, 2);
            if (rect.Width > 34)
            {
                var label = Text(IsDrums ? DrumName(note.Pitch) : Note.PitchName(note.Pitch), 9, Brushes.Black, pixelsPerDip);
                dc.PushClip(new RectangleGeometry(rect));
                dc.DrawText(label, new Point(rect.Left + 3, rect.Top + (rect.Height - label.Height) / 2));
                dc.Pop();
            }
        }
    }

    private void DrawRuler(DrawingContext dc, double width, double pixelsPerDip)
    {
        dc.DrawRectangle(RulerBrush, null, new Rect(KeyWidth, 0, width - KeyWidth, RulerHeight));
        long bar = MusicalTime.TicksPerBar(BeatsPerBar);
        int labelEvery = Math.Max(1, (int)Math.Ceiling(34 / (bar / _ticksPerPixel)));
        long first = (long)Math.Floor(_viewStart / bar);
        dc.PushClip(new RectangleGeometry(new Rect(KeyWidth, 0, width - KeyWidth, RulerHeight)));
        for (long b = Math.Max(0, first); TickToX(b * bar) <= width; b++)
        {
            double x = TickToX(b * bar);
            dc.DrawLine(BarPen, new Point(x, RulerHeight - 8), new Point(x, RulerHeight));
            if (b % labelEvery == 0)
            {
                dc.DrawText(Text((b + 1).ToString(CultureInfo.CurrentCulture), 11, TextBrush, pixelsPerDip), new Point(x + 3, 3));
            }
        }

        dc.Pop();
    }

    private void DrawKeys(DrawingContext dc, double bottom, double pixelsPerDip)
    {
        dc.PushClip(new RectangleGeometry(new Rect(0, RulerHeight, KeyWidth, bottom - RulerHeight)));
        for (int pitch = (int)_topPitch; pitch >= 0; pitch--)
        {
            double y = PitchToY(pitch);
            if (y > bottom)
            {
                break;
            }

            bool black = IsBlack(pitch);
            dc.DrawRectangle(black ? BlackKey : WhiteKey, RowPen, new Rect(0, y, KeyWidth - 1, RowHeight));
            if (IsDrums ? DrumName(pitch).Length > 0 : pitch % 12 == 0)
            {
                string label = IsDrums ? DrumName(pitch) : Note.PitchName(pitch);
                dc.DrawText(Text(label, 9, black ? WhiteKey : KeyText, pixelsPerDip), new Point(3, y));
            }
        }

        dc.Pop();
        dc.DrawRectangle(RulerBrush, null, new Rect(0, 0, KeyWidth, RulerHeight));
    }

    private void DrawVelocities(DrawingContext dc, double width, double height, double bottom, double pixelsPerDip)
    {
        dc.DrawRectangle(VelocityBackground, OctavePen, new Rect(KeyWidth, bottom, width - KeyWidth, height - bottom));
        dc.DrawRectangle(RulerBrush, null, new Rect(0, bottom, KeyWidth, height - bottom));
        dc.DrawText(Text("Anschlag", 10, TextBrush, pixelsPerDip), new Point(6, bottom + 6));
        var selection = Selection ?? [];
        dc.PushClip(new RectangleGeometry(new Rect(KeyWidth, bottom, width - KeyWidth, height - bottom)));
        foreach (var note in NoteList)
        {
            double x = TickToX(note.Start);
            if (x < KeyWidth - 4 || x > width)
            {
                continue;
            }

            double barHeight = (height - bottom - 8) * note.Velocity / 127.0;
            var color = selection.Contains(note) ? Colors.White : VoiceColors[Math.Abs(note.Voice) % VoiceColors.Length];
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            dc.DrawRectangle(brush, null, new Rect(x, height - 4 - barHeight, 3, barHeight));
            dc.DrawEllipse(brush, null, new Point(x + 1.5, height - 4 - barHeight), 3, 3);
        }

        dc.Pop();
    }

    // ───────────── Maus ─────────────

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        var p = e.GetPosition(this);
        _down = p;
        _downTick = (long)Math.Round(XToTick(p.X));
        _downPitch = YToPitch(p.Y);
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

        if (p.X < KeyWidth && p.Y > RulerHeight && p.Y < GridBottom)
        {
            _mode = Mode.Preview;
            Execute(new PianoRollEdit(PianoRollAction.Preview, new Note(_downPitch, 0, 1)));
        }
        else if (p.Y < RulerHeight)
        {
            _mode = Mode.Seek;
            Execute(new PianoRollEdit(PianoRollAction.Seek, Tick: Math.Max(0, _downTick)));
        }
        else if (p.Y >= GridBottom)
        {
            if (NearestVelocityNote(p.X) is { } note)
            {
                _mode = Mode.Velocity;
                _anchor = note;
                Execute(new PianoRollEdit(PianoRollAction.Velocity, note, Value: VelocityAt(p.Y)));
            }
        }
        else if (HitNote(p) is { } note)
        {
            if (e.ClickCount == 2)
            {
                Execute(new PianoRollEdit(PianoRollAction.Delete, note));
                _mode = Mode.None;
                e.Handled = true;
                return;
            }

            if (ctrl)
            {
                Execute(new PianoRollEdit(PianoRollAction.Select, Notes: [note], Additive: true));
                e.Handled = true;
                return;
            }

            _anchor = note;
            _mode = p.X > TickToX(note.End) - 6 ? Mode.Resize : Mode.Move;
            Execute(new PianoRollEdit(PianoRollAction.BeginDrag, note));
        }
        else if (shift)
        {
            _mode = Mode.Band;
            _band = new Rect(p, p);
        }
        else
        {
            _mode = Mode.Resize; // neue Note: Ziehen bestimmt die Länge
            Execute(new PianoRollEdit(PianoRollAction.Add, new Note(_downPitch, Math.Max(0, _downTick), 1)));
        }

        if (_mode != Mode.None)
        {
            CaptureMouse();
        }

        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var p = e.GetPosition(this);
        long tick = (long)Math.Round(XToTick(p.X));
        switch (_mode)
        {
            case Mode.Seek:
                Execute(new PianoRollEdit(PianoRollAction.Seek, Tick: Math.Max(0, tick)));
                return;
            case Mode.Preview when YToPitch(p.Y) != _downPitch:
                _downPitch = YToPitch(p.Y);
                Execute(new PianoRollEdit(PianoRollAction.Preview, new Note(_downPitch, 0, 1)));
                return;
            case Mode.Move:
                Execute(new PianoRollEdit(PianoRollAction.Move, DeltaTicks: tick - _downTick, DeltaPitch: YToPitch(p.Y) - _downPitch));
                return;
            case Mode.Resize:
                Execute(new PianoRollEdit(PianoRollAction.Resize, DeltaTicks: tick - _downTick));
                return;
            case Mode.Velocity:
                Execute(new PianoRollEdit(PianoRollAction.Velocity, _anchor, Value: VelocityAt(p.Y)));
                return;
            case Mode.Band:
                _band = new Rect(_down, p);
                InvalidateVisual();
                return;
        }

        Cursor = p.X < KeyWidth ? Cursors.Hand
            : p.Y < RulerHeight ? Cursors.Hand
            : p.Y >= GridBottom ? Cursors.SizeNS
            : HitNote(p) is { } hover ? (p.X > TickToX(hover.End) - 6 ? Cursors.SizeWE : Cursors.SizeAll)
            : Cursors.Pen;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        Finish();
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        if (_mode != Mode.None)
        {
            Finish();
        }
    }

    /// <summary>Rechtsklick auf eine Note löscht sie.</summary>
    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonDown(e);
        if (HitNote(e.GetPosition(this)) is { } note)
        {
            Execute(new PianoRollEdit(PianoRollAction.Delete, note));
            e.Handled = true;
        }
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            Zoom(e.Delta > 0 ? 0.8 : 1.25, Math.Max(KeyWidth, e.GetPosition(this).X));
        }
        else if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
        {
            _viewStart = Math.Max(0, _viewStart - e.Delta / 120.0 * 60 * _ticksPerPixel);
            InvalidateVisual();
        }
        else
        {
            double rows = (GridBottom - RulerHeight) / RowHeight;
            _topPitch = Math.Clamp(_topPitch + e.Delta / 120.0 * 3, Math.Max(0, rows - 1), 127);
            InvalidateVisual();
        }

        e.Handled = true;
    }

    private void Finish()
    {
        var mode = _mode;
        _mode = Mode.None;
        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }

        switch (mode)
        {
            case Mode.Move or Mode.Resize or Mode.Velocity:
                Execute(new PianoRollEdit(PianoRollAction.EndDrag));
                break;
            case Mode.Preview:
                Execute(new PianoRollEdit(PianoRollAction.PreviewEnd));
                break;
            case Mode.Band:
                var band = _band;
                var inside = NoteList.Where(n => band.IntersectsWith(new Rect(TickToX(n.Start), PitchToY(n.Pitch), Math.Max(2, TickToX(n.End) - TickToX(n.Start)), RowHeight))).ToArray();
                Execute(new PianoRollEdit(PianoRollAction.Select, Notes: inside, Additive: (Keyboard.Modifiers & ModifierKeys.Control) != 0));
                break;
        }

        InvalidateVisual();
    }

    private Note? HitNote(Point p)
    {
        if (p.X < KeyWidth || p.Y < RulerHeight || p.Y >= GridBottom)
        {
            return null;
        }

        int pitch = YToPitch(p.Y);
        double tick = XToTick(p.X);
        var notes = NoteList;
        for (int i = notes.Count - 1; i >= 0; i--)
        {
            var n = notes[i];
            if (n.Pitch == pitch && tick >= n.Start && tick <= n.End + 3 * _ticksPerPixel)
            {
                return n;
            }
        }

        return null;
    }

    private Note? NearestVelocityNote(double x)
    {
        var selection = Selection ?? [];
        return NoteList
            .Select(n => (Note: n, Distance: Math.Abs(TickToX(n.Start) + 1.5 - x)))
            .Where(c => c.Distance <= 6)
            .OrderBy(c => c.Distance)
            .ThenByDescending(c => selection.Contains(c.Note))
            .Select(c => (Note?)c.Note)
            .FirstOrDefault();
    }

    private int VelocityAt(double y) =>
        (int)Math.Clamp(Math.Round((ActualHeight - 4 - y) / (ActualHeight - GridBottom - 8) * 127), 1, 127);

    private void Execute(PianoRollEdit edit)
    {
        if (EditCommand is { } command && command.CanExecute(edit))
        {
            command.Execute(edit);
        }
    }

    private static string DrumName(int pitch) => pitch switch
    {
        35 => "Bassdrum 2",
        36 => "Bassdrum",
        37 => "Sidestick",
        38 => "Snare",
        39 => "Klatschen",
        40 => "Snare 2",
        41 => "Tom tief",
        42 => "Hi-Hat zu",
        43 => "Tom tief 2",
        44 => "Hi-Hat Pedal",
        45 => "Tom mittel",
        46 => "Hi-Hat offen",
        47 => "Tom mittel 2",
        48 => "Tom hoch",
        49 => "Crash",
        50 => "Tom hoch 2",
        51 => "Ride",
        54 => "Tamburin",
        56 => "Kuhglocke",
        57 => "Crash 2",
        59 => "Ride 2",
        _ => string.Empty,
    };

    private FormattedText Text(string text, double size, Brush brush, double pixelsPerDip) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, LabelTypeface, size, brush, pixelsPerDip);

    private static DependencyProperty Register(string name, Type type, object? defaultValue) =>
        DependencyProperty.Register(name, type, typeof(PianoRollView),
            new FrameworkPropertyMetadata(defaultValue, FrameworkPropertyMetadataOptions.AffectsRender));

    private static T Frozen<T>(T freezable)
        where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
