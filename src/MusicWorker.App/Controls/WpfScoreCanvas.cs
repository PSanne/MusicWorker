using System.Globalization;
using System.Windows;
using System.Windows.Media;
using MusicWorker.Core.Notes;

namespace MusicWorker.App.Controls;

/// <summary>Zeichnet das Notenblatt mit WPF (Bildschirm, Drucker und PDF-Bild).</summary>
internal sealed class WpfScoreCanvas(DrawingContext dc, double pixelsPerDip) : IScoreCanvas
{
    private static readonly Typeface Regular = new(new FontFamily("Georgia, Times New Roman"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private static readonly Typeface Bold = new(new FontFamily("Georgia, Times New Roman"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
    private static readonly Typeface Italic = new(new FontFamily("Georgia, Times New Roman"), FontStyles.Italic, FontWeights.Normal, FontStretches.Normal);

    public void Line(double x1, double y1, double x2, double y2, double thickness) =>
        dc.DrawLine(Pen(thickness, PenLineCap.Flat), new Point(x1, y1), new Point(x2, y2));

    public void Rect(double x, double y, double width, double height) =>
        dc.DrawRectangle(Brushes.Black, null, new Rect(x, y, Math.Max(0, width), Math.Max(0, height)));

    public void Ellipse(double cx, double cy, double rx, double ry, double rotationDegrees, bool filled, double strokeThickness)
    {
        var center = new Point(cx, cy);
        bool rotated = Math.Abs(rotationDegrees) > 0.01;
        if (rotated)
        {
            dc.PushTransform(new RotateTransform(rotationDegrees, cx, cy));
        }

        dc.DrawEllipse(filled ? Brushes.Black : null, filled ? null : Pen(strokeThickness, PenLineCap.Flat), center, rx, ry);
        if (rotated)
        {
            dc.Pop();
        }
    }

    public void Path(string data, bool filled, double strokeThickness)
    {
        var geometry = Geometry.Parse(data);
        dc.DrawGeometry(filled ? Brushes.Black : null, strokeThickness > 0 ? Pen(strokeThickness, PenLineCap.Round) : null, geometry);
    }

    public void Text(string text, double x, double baseline, double size, bool bold, bool italic, TextAnchor anchor)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            bold ? Bold : italic ? Italic : Regular, size, Brushes.Black, pixelsPerDip);
        double left = anchor switch
        {
            TextAnchor.Center => x - (formatted.WidthIncludingTrailingWhitespace / 2),
            TextAnchor.Right => x - formatted.WidthIncludingTrailingWhitespace,
            _ => x,
        };
        dc.DrawText(formatted, new Point(left, baseline - formatted.Baseline));
    }

    private static Pen Pen(double thickness, PenLineCap cap)
    {
        var pen = new Pen(Brushes.Black, thickness) { StartLineCap = cap, EndLineCap = cap, LineJoin = PenLineJoin.Round };
        pen.Freeze();
        return pen;
    }
}

/// <summary>Eine Seite des Notenblatts als Steuerelement (für die Vorschau).</summary>
internal sealed class ScorePageView : FrameworkElement
{
    private readonly ScoreRenderer _renderer;
    private readonly LayoutPage _page;
    private readonly ScoreLayoutOptions _options;

    public ScorePageView(ScoreRenderer renderer, LayoutPage page, ScoreLayoutOptions options)
    {
        _renderer = renderer;
        _page = page;
        _options = options;
        Width = options.PageWidth;
        Height = options.PageHeight;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        drawingContext.DrawRectangle(Brushes.White, null, new Rect(0, 0, _options.PageWidth, _options.PageHeight));
        _renderer.RenderPage(_page, new WpfScoreCanvas(drawingContext, VisualTreeHelper.GetDpi(this).PixelsPerDip));
    }
}
