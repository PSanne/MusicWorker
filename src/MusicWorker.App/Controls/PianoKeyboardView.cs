using System.Collections.Immutable;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using MusicWorker.Core.Notes;

namespace MusicWorker.App.Controls;

/// <summary>
/// Bildschirmtastatur: drei Oktaven ab <see cref="LowestKey"/>, mit der Maus spielbar (auch gleitend).
/// Gedrückte Tasten (auch von Computertastatur oder MIDI-Keyboard) werden hervorgehoben; auf den Tasten
/// steht, welche Computertaste sie spielt.
/// </summary>
public sealed class PianoKeyboardView : FrameworkElement
{
    public const int Octaves = 3;

    public static readonly DependencyProperty LowestKeyProperty = DependencyProperty.Register(
        nameof(LowestKey), typeof(int), typeof(PianoKeyboardView), new FrameworkPropertyMetadata(48, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty PressedKeysProperty = DependencyProperty.Register(
        nameof(PressedKeys), typeof(ImmutableHashSet<int>), typeof(PianoKeyboardView), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty PlayCommandProperty = DependencyProperty.Register(
        nameof(PlayCommand), typeof(ICommand), typeof(PianoKeyboardView), new PropertyMetadata(null));

    public static readonly DependencyProperty ReleaseCommandProperty = DependencyProperty.Register(
        nameof(ReleaseCommand), typeof(ICommand), typeof(PianoKeyboardView), new PropertyMetadata(null));

    private static readonly string[] ComputerKeys =
    [
        "Y", "S", "X", "D", "C", "V", "G", "B", "H", "N", "J", "M",
        "Q", "2", "W", "3", "E", "R", "5", "T", "6", "Z", "7", "U",
        "I", "9", "O", "0", "P",
    ];

    private static readonly Brush WhiteBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xF2, 0xF3, 0xF5)));
    private static readonly Brush BlackBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x22, 0x24, 0x2A)));
    private static readonly Brush PressedBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x4F, 0xC3, 0xF7)));
    private static readonly Brush LabelBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x70, 0x74, 0x7C)));
    private static readonly Brush HintBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xA0, 0xA4, 0xAC)));
    private static readonly Pen EdgePen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromRgb(0x50, 0x54, 0x5C))), 1));
    private static readonly Typeface LabelTypeface = new("Segoe UI");

    private int _mousePitch = -1;

    public PianoKeyboardView()
    {
        ClipToBounds = true;
    }

    public int LowestKey { get => (int)GetValue(LowestKeyProperty); set => SetValue(LowestKeyProperty, value); }

    public ImmutableHashSet<int>? PressedKeys { get => (ImmutableHashSet<int>?)GetValue(PressedKeysProperty); set => SetValue(PressedKeysProperty, value); }

    /// <summary>Parameter: Tonhöhe (int).</summary>
    public ICommand? PlayCommand { get => (ICommand?)GetValue(PlayCommandProperty); set => SetValue(PlayCommandProperty, value); }

    /// <summary>Parameter: Tonhöhe (int).</summary>
    public ICommand? ReleaseCommand { get => (ICommand?)GetValue(ReleaseCommandProperty); set => SetValue(ReleaseCommandProperty, value); }

    private static bool IsBlack(int pitch) => (pitch % 12) is 1 or 3 or 6 or 8 or 10;

    private int WhiteCount => Octaves * 7 + 1;

    protected override void OnRender(DrawingContext dc)
    {
        double whiteWidth = ActualWidth / WhiteCount;
        double height = ActualHeight;
        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var pressed = PressedKeys ?? [];

        foreach (var (pitch, rect) in Keys(whiteWidth, height).Where(k => !IsBlack(k.Pitch)))
        {
            dc.DrawRectangle(pressed.Contains(pitch) ? PressedBrush : WhiteBrush, EdgePen, rect);
            if (pitch % 12 == 0)
            {
                var label = Text(Note.PitchName(pitch), 10, LabelBrush, pixelsPerDip);
                dc.DrawText(label, new Point(rect.Left + (rect.Width - label.Width) / 2, rect.Bottom - label.Height - 3));
            }

            DrawHint(dc, pitch, rect, height * 0.62, pixelsPerDip, LabelBrush);
        }

        foreach (var (pitch, rect) in Keys(whiteWidth, height).Where(k => IsBlack(k.Pitch)))
        {
            dc.DrawRoundedRectangle(pressed.Contains(pitch) ? PressedBrush : BlackBrush, EdgePen, rect, 2, 2);
            DrawHint(dc, pitch, rect, rect.Bottom - 16, pixelsPerDip, HintBrush);
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        CaptureMouse();
        PlayAt(e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (IsMouseCaptured)
        {
            PlayAt(e.GetPosition(this)); // gleitend über die Tasten
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        ReleaseMouseCapture();
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        if (_mousePitch >= 0)
        {
            Execute(ReleaseCommand, _mousePitch);
            _mousePitch = -1;
        }
    }

    private void PlayAt(Point p)
    {
        int pitch = HitKey(p);
        if (pitch == _mousePitch)
        {
            return;
        }

        if (_mousePitch >= 0)
        {
            Execute(ReleaseCommand, _mousePitch);
        }

        _mousePitch = pitch;
        if (pitch >= 0)
        {
            Execute(PlayCommand, pitch);
        }
    }

    private int HitKey(Point p)
    {
        double whiteWidth = ActualWidth / WhiteCount;
        var keys = Keys(whiteWidth, ActualHeight).ToArray();
        foreach (var (pitch, rect) in keys.Where(k => IsBlack(k.Pitch)))
        {
            if (rect.Contains(p))
            {
                return pitch;
            }
        }

        foreach (var (pitch, rect) in keys.Where(k => !IsBlack(k.Pitch)))
        {
            if (rect.Contains(p))
            {
                return pitch;
            }
        }

        return -1;
    }

    /// <summary>Alle Tasten mit ihrem Rechteck.</summary>
    private IEnumerable<(int Pitch, Rect Rect)> Keys(double whiteWidth, double height)
    {
        int white = 0;
        int lowest = LowestKey;
        for (int pitch = lowest; pitch <= Math.Min(127, lowest + Octaves * 12); pitch++)
        {
            if (IsBlack(pitch))
            {
                double x = white * whiteWidth - whiteWidth * 0.32;
                yield return (pitch, new Rect(x, 0, whiteWidth * 0.64, height * 0.6));
            }
            else
            {
                yield return (pitch, new Rect(white * whiteWidth, 0, whiteWidth, height));
                white++;
            }
        }
    }

    private void DrawHint(DrawingContext dc, int pitch, Rect rect, double y, double pixelsPerDip, Brush brush)
    {
        int offset = pitch - LowestKey;
        if (offset < 0 || offset >= ComputerKeys.Length || rect.Width < 14)
        {
            return;
        }

        var hint = Text(ComputerKeys[offset], 9, brush, pixelsPerDip);
        dc.DrawText(hint, new Point(rect.Left + (rect.Width - hint.Width) / 2, y));
    }

    private static void Execute(ICommand? command, int pitch)
    {
        if (command is not null && command.CanExecute(pitch))
        {
            command.Execute(pitch);
        }
    }

    private FormattedText Text(string text, double size, Brush brush, double pixelsPerDip) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, LabelTypeface, size, brush, pixelsPerDip);

    private static T Frozen<T>(T freezable)
        where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
