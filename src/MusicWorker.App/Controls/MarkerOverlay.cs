using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MusicWorker.App.ViewModels;
using MusicWorker.Core.Editing;

namespace MusicWorker.App.Controls;

/// <summary>
/// Zeichnet Marker als gelbe Fähnchen mit Linie und kümmert sich um die Maus:
/// Klick = hinspringen, Ziehen = verschieben, Doppelklick = umbenennen, Rechtsklick = Menü.
/// Wird von der Wellenform (Editor) und der Spuransicht (Mischpult) gemeinsam genutzt.
/// </summary>
internal sealed class MarkerOverlay
{
    private const double FlagHeight = 17;

    private static readonly Brush FlagBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xFF, 0xB3, 0x00)));
    private static readonly Brush FlagDragBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xFF, 0xD5, 0x4F)));
    private static readonly Brush FlagTextBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x1B, 0x1D, 0x23)));
    private static readonly Pen LinePen = CreateLinePen();
    private static readonly Typeface LabelTypeface = new("Segoe UI");

    private readonly List<(Rect Rect, int Index)> _hits = [];
    private int _dragIndex = -1;
    private long _dragFrame;
    private bool _dragMoved;
    private Point _dragStart;

    public bool IsDragging => _dragIndex >= 0;

    public void Draw(DrawingContext dc, IReadOnlyList<Marker>? markers, Func<double, double> frameToX,
        double top, double bottom, double width, double pixelsPerDip)
    {
        _hits.Clear();
        if (markers is null)
        {
            return;
        }

        for (int i = 0; i < markers.Count; i++)
        {
            long frame = i == _dragIndex ? _dragFrame : markers[i].Frame;
            double x = Math.Round(frameToX(frame)) + 0.5;
            if (x < -300 || x > width + 1)
            {
                continue;
            }

            dc.DrawLine(LinePen, new Point(x, top), new Point(x, bottom));
            var text = new FormattedText(markers[i].Name, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                LabelTypeface, 11, FlagTextBrush, pixelsPerDip);
            var rect = new Rect(x - 0.5, top + 1, Math.Min(text.Width, 220) + 9, FlagHeight);
            dc.DrawRoundedRectangle(i == _dragIndex ? FlagDragBrush : FlagBrush, null, rect, 2, 2);
            text.MaxTextWidth = 220;
            text.MaxLineCount = 1;
            text.Trimming = TextTrimming.CharacterEllipsis;
            dc.DrawText(text, new Point(x + 4, top + 2));
            _hits.Add((rect, i));
        }
    }

    /// <summary>Index des Markers, dessen Fähnchen unter <paramref name="position"/> liegt (-1 = keiner).</summary>
    public int HitTest(Point position)
    {
        for (int k = _hits.Count - 1; k >= 0; k--)
        {
            if (_hits[k].Rect.Contains(position))
            {
                return _hits[k].Index;
            }
        }

        return -1;
    }

    public bool MouseDown(Point position, int clickCount, IReadOnlyList<Marker>? markers, ICommand? command, UIElement owner)
    {
        int index = HitTest(position);
        if (index < 0 || markers is null || index >= markers.Count)
        {
            return false;
        }

        if (clickCount == 2)
        {
            Execute(command, new MarkerAction(MarkerActionKind.Rename, index, markers[index].Frame));
            return true;
        }

        _dragIndex = index;
        _dragFrame = markers[index].Frame;
        _dragMoved = false;
        _dragStart = position;
        owner.CaptureMouse();
        return true;
    }

    /// <summary>Während des Ziehens: neue Position übernehmen. Liefert true, solange ein Marker gezogen wird.</summary>
    public bool MouseMove(Point position, Func<double, long> xToFrame)
    {
        if (_dragIndex < 0)
        {
            return false;
        }

        if (!_dragMoved && Math.Abs(position.X - _dragStart.X) < 3)
        {
            return true;
        }

        _dragMoved = true;
        _dragFrame = xToFrame(position.X);
        return true;
    }

    public bool MouseUp(ICommand? command, UIElement owner)
    {
        if (_dragIndex < 0)
        {
            return false;
        }

        var action = new MarkerAction(_dragMoved ? MarkerActionKind.Move : MarkerActionKind.Jump, _dragIndex, _dragFrame);
        _dragIndex = -1;
        owner.ReleaseMouseCapture();
        Execute(command, action);
        return true;
    }

    public void Cancel() => _dragIndex = -1;

    /// <summary>Rechtsklick auf ein Fähnchen: kleines Menü anzeigen. Liefert false, wenn dort kein Marker ist.</summary>
    public bool ShowMenu(Point position, IReadOnlyList<Marker>? markers, ICommand? command, FrameworkElement owner)
    {
        int index = HitTest(position);
        if (index < 0 || markers is null || index >= markers.Count)
        {
            return false;
        }

        long frame = markers[index].Frame;
        var menu = new ContextMenu { PlacementTarget = owner };
        menu.Items.Add(Item("Hierhin _springen", MarkerActionKind.Jump));
        menu.Items.Add(Item("_Umbenennen …", MarkerActionKind.Rename));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Marker _löschen", MarkerActionKind.Delete));
        menu.IsOpen = true;
        return true;

        MenuItem Item(string header, MarkerActionKind kind)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => Execute(command, new MarkerAction(kind, index, frame));
            return item;
        }
    }

    private static void Execute(ICommand? command, MarkerAction action)
    {
        if (command?.CanExecute(action) == true)
        {
            command.Execute(action);
        }
    }

    private static Pen CreateLinePen()
    {
        var pen = new Pen(Frozen(new SolidColorBrush(Color.FromArgb(0xC0, 0xFF, 0xB3, 0x00))), 1)
        {
            DashStyle = new DashStyle([4, 3], 0),
        };
        pen.Freeze();
        return pen;
    }

    private static T Frozen<T>(T freezable)
        where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
