using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace MusicWorker.App.Controls;

/// <summary>
/// Drehregler wie ein Potentiometer am Mischpult (270° Drehbereich).
/// Bedienung: mit gedrückter Maustaste hoch/runter ziehen (mit Umschalt feiner), Mausrad,
/// Pfeiltasten; Doppelklick setzt auf <see cref="DefaultValue"/> zurück.
/// </summary>
public sealed class Knob : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(Knob),
        new FrameworkPropertyMetadata(
            0.0,
            FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            null,
            CoerceValue));

    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
        nameof(Minimum), typeof(double), typeof(Knob),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(double), typeof(Knob),
        new FrameworkPropertyMetadata(100.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty DefaultValueProperty = DependencyProperty.Register(
        nameof(DefaultValue), typeof(double), typeof(Knob), new PropertyMetadata(0.0));

    public static readonly DependencyProperty AccentProperty = DependencyProperty.Register(
        nameof(Accent), typeof(Brush), typeof(Knob),
        new FrameworkPropertyMetadata(Frozen(new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD4))), FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Farbe der Skala (kommt aus dem Design, siehe Themes/*.xaml).</summary>
    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
        nameof(TrackBrush), typeof(Brush), typeof(Knob),
        new FrameworkPropertyMetadata(Frozen(new SolidColorBrush(Color.FromRgb(0xD5, 0xD7, 0xDD))), FrameworkPropertyMetadataOptions.AffectsRender));

    private const double StartAngle = -135;
    private const double SweepAngle = 270;
    private const double DragPixelsForFullRange = 200;

    private static readonly Brush BodyBrush = Frozen(new RadialGradientBrush(Color.FromRgb(0x5A, 0x5F, 0x6A), Color.FromRgb(0x2B, 0x2E, 0x35))
    {
        GradientOrigin = new Point(0.35, 0.3),
    });
    private static readonly Pen BodyEdgePen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromRgb(0x1A, 0x1C, 0x21))), 1));
    private static readonly Pen PointerPen = Frozen(new Pen(Brushes.White, 2.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });
    private static readonly Pen FocusPen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromArgb(0x60, 0x00, 0x78, 0xD4))), 1) { DashStyle = DashStyles.Dot });

    private Point _dragStart;
    private double _dragStartValue;
    private bool _dragging;

    public Knob()
    {
        Focusable = true;
        Cursor = Cursors.SizeNS;
        FocusVisualStyle = null;
        SetResourceReference(TrackBrushProperty, "Theme.KnobTrack");
    }

    public Brush TrackBrush
    {
        get => (Brush)GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public double Minimum
    {
        get => (double)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    /// <summary>Wert nach einem Doppelklick.</summary>
    public double DefaultValue
    {
        get => (double)GetValue(DefaultValueProperty);
        set => SetValue(DefaultValueProperty, value);
    }

    /// <summary>Farbe des Wertebogens.</summary>
    public Brush Accent
    {
        get => (Brush)GetValue(AccentProperty);
        set => SetValue(AccentProperty, value);
    }

    private double Range => Math.Max(1e-9, Maximum - Minimum);

    protected override Size MeasureOverride(Size availableSize)
    {
        double size = double.IsInfinity(availableSize.Width) ? 56 : Math.Min(availableSize.Width, 56);
        return new Size(size, size);
    }

    protected override void OnRender(DrawingContext dc)
    {
        double size = Math.Min(ActualWidth, ActualHeight);
        if (size < 12)
        {
            return;
        }

        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        double trackRadius = size / 2 - 3;
        double bodyRadius = trackRadius - 6;
        double fraction = Math.Clamp((Value - Minimum) / Range, 0, 1);
        double valueAngle = StartAngle + SweepAngle * fraction;

        // Hintergrund ist transparent, damit die ganze Fläche auf die Maus reagiert.
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));

        // Skala: grauer Bogen über den ganzen Bereich, farbiger Bogen bis zum Wert
        dc.DrawGeometry(null, new Pen(TrackBrush, 4) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round },
            Arc(center, trackRadius, StartAngle, StartAngle + SweepAngle));
        if (fraction > 0.001)
        {
            dc.DrawGeometry(null, new Pen(Accent, 4) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round },
                Arc(center, trackRadius, StartAngle, valueAngle));
        }

        // Drehknopf mit Zeiger
        dc.DrawEllipse(BodyBrush, BodyEdgePen, center, bodyRadius, bodyRadius);
        dc.DrawLine(PointerPen, PointAt(center, bodyRadius * 0.25, valueAngle), PointAt(center, bodyRadius * 0.85, valueAngle));

        if (IsKeyboardFocused)
        {
            dc.DrawEllipse(null, FocusPen, center, trackRadius + 2, trackRadius + 2);
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        if (e.ClickCount == 2)
        {
            SetKnobValue(DefaultValue);
            e.Handled = true;
            return;
        }

        _dragStart = e.GetPosition(this);
        _dragStartValue = Value;
        _dragging = CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging)
        {
            return;
        }

        // Nach oben ziehen = aufdrehen. Mit Umschalt fünfmal feiner.
        double pixels = _dragStart.Y - e.GetPosition(this).Y;
        double sensitivity = (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? 5 : 1;
        SetKnobValue(_dragStartValue + pixels / (DragPixelsForFullRange * sensitivity) * Range);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_dragging)
        {
            _dragging = false;
            ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        _dragging = false;
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        SetKnobValue(Value + Math.Sign(e.Delta) * Range / 50);
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            return; // Strg+Pfeil gehört dem Hauptfenster (5 Sekunden vor/zurück)
        }

        double? step = e.Key switch
        {
            Key.Up or Key.Right => Range / 100,
            Key.Down or Key.Left => -Range / 100,
            Key.PageUp => Range / 10,
            Key.PageDown => -Range / 10,
            _ => null,
        };

        if (step is { } delta)
        {
            SetKnobValue(Value + delta);
            e.Handled = true;
        }
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        InvalidateVisual();
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        InvalidateVisual();
    }

    private static object CoerceValue(DependencyObject d, object baseValue)
    {
        var knob = (Knob)d;
        return Math.Clamp((double)baseValue, knob.Minimum, knob.Maximum);
    }

    /// <summary>Setzt den Wert, ohne eine bestehende Datenbindung zu entfernen.</summary>
    private void SetKnobValue(double value) =>
        SetCurrentValue(ValueProperty, Math.Round(Math.Clamp(value, Minimum, Maximum), 1));

    private static Point PointAt(Point center, double radius, double angleDegrees)
    {
        double radians = angleDegrees * Math.PI / 180;
        return new Point(center.X + radius * Math.Sin(radians), center.Y - radius * Math.Cos(radians));
    }

    private static Geometry Arc(Point center, double radius, double fromAngle, double toAngle)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(PointAt(center, radius, fromAngle), isFilled: false, isClosed: false);
            context.ArcTo(PointAt(center, radius, toAngle), new Size(radius, radius), 0,
                isLargeArc: toAngle - fromAngle > 180, SweepDirection.Clockwise, isStroked: true, isSmoothJoin: false);
        }

        geometry.Freeze();
        return geometry;
    }

    private static T Frozen<T>(T freezable)
        where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
