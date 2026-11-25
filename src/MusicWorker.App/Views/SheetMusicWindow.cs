using System.IO;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using MusicWorker.App.Controls;
using MusicWorker.App.Services;
using MusicWorker.Core.Analysis;
using MusicWorker.Core.Notes;
using MusicWorker.Core.Visualization;

namespace MusicWorker.App.Views;

/// <summary>
/// Notenblatt: zeigt die Notenspuren als Noten (Vorschau mit Seiten), lässt sich drucken und als PDF speichern.
/// </summary>
public sealed class SheetMusicWindow : Window
{
    private const double PdfDpi = 300;

    private readonly SheetMusicRequest _request;
    private readonly ScoreLayoutOptions _options = new();
    private readonly ComboBox _parts = new() { MinWidth = 170, Margin = new Thickness(4, 0, 12, 0) };
    private readonly ComboBox _key = new() { Width = 100, Margin = new Thickness(4, 0, 12, 0) };
    private readonly TextBox _title = new() { Width = 200, Margin = new Thickness(4, 0, 12, 0), VerticalContentAlignment = VerticalAlignment.Center };
    private readonly Slider _zoom = new() { Minimum = 0.5, Maximum = 2, Value = 1, Width = 110, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) };
    private readonly StackPanel _pagesPanel = new() { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 12) };
    private readonly TextBlock _info = new() { VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.DimGray, Margin = new Thickness(12, 0, 0, 0) };

    private Score? _score;
    private IReadOnlyList<LayoutPage> _pages = [];

    public SheetMusicWindow(SheetMusicRequest request)
    {
        _request = request ?? throw new ArgumentNullException(nameof(request));
        Title = "Notenblatt – MusicWorker";
        Width = 1000;
        Height = 820;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        _parts.Items.Add("Alle Notenspuren");
        foreach (var part in request.Parts)
        {
            _parts.Items.Add(part.Name);
        }

        _parts.SelectedIndex = 0;
        for (int tonic = 0; tonic < 12; tonic++)
        {
            _key.Items.Add(new KeyResult(tonic, false, 1).Name);
            _key.Items.Add(new KeyResult(tonic, true, 1).Name);
        }

        _key.SelectedIndex = (Math.Clamp(request.KeyTonic, 0, 11) * 2) + (request.KeyMinor ? 1 : 0);
        _title.Text = request.Title;

        var bar = new WrapPanel { Margin = new Thickness(10, 8, 10, 8) };
        bar.Children.Add(Label("Stimmen:"));
        bar.Children.Add(_parts);
        bar.Children.Add(Label("Titel:"));
        bar.Children.Add(_title);
        bar.Children.Add(Label("Tonart:"));
        bar.Children.Add(_key);
        bar.Children.Add(Label("Größe:"));
        bar.Children.Add(_zoom);
        var print = Button("Drucken …", (_, _) => Print());
        print.Margin = new Thickness(16, 0, 0, 0);
        bar.Children.Add(print);
        bar.Children.Add(Button("Als PDF speichern …", (_, _) => SavePdf()));
        bar.Children.Add(_info);

        var scroll = new ScrollViewer
        {
            Content = _pagesPanel,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = new SolidColorBrush(Color.FromRgb(0x8A, 0x8F, 0x98)),
        };
        _pagesPanel.LayoutTransform = new ScaleTransform(1, 1);

        var root = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top);
        root.Children.Add(bar);
        root.Children.Add(scroll);
        Content = root;

        _parts.SelectionChanged += (_, _) => Rebuild();
        _key.SelectionChanged += (_, _) => Rebuild();
        _title.LostFocus += (_, _) => Rebuild();
        _title.KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                Rebuild();
            }
        };
        _zoom.ValueChanged += (_, _) => _pagesPanel.LayoutTransform = new ScaleTransform(_zoom.Value, _zoom.Value);
        Rebuild();
    }

    private static TextBlock Label(string text) => new() { Text = text, VerticalAlignment = VerticalAlignment.Center };

    private static Button Button(string text, RoutedEventHandler click)
    {
        var button = new Button { Content = text, Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 8, 0) };
        button.Click += click;
        return button;
    }

    private void Rebuild()
    {
        var parts = _parts.SelectedIndex <= 0
            ? _request.Parts
            : [_request.Parts[_parts.SelectedIndex - 1]];
        int index = Math.Max(0, _key.SelectedIndex);
        var key = KeySignature.FromKey(index / 2, index % 2 == 1);
        string title = string.IsNullOrWhiteSpace(_title.Text) ? "Notenblatt" : _title.Text.Trim();
        _score = Notation.Build(title, parts.Select(p => (p.Name, p.Notes)), _request.BeatsPerBar, key, _request.Bpm);
        _pages = ScoreLayout.Paginate(_score, _options);
        var renderer = new ScoreRenderer(_score, _options, _pages.Count);
        _pagesPanel.Children.Clear();
        foreach (var page in _pages)
        {
            _pagesPanel.Children.Add(new Border
            {
                Child = new ScorePageView(renderer, page, _options),
                Margin = new Thickness(0, 0, 0, 16),
                Effect = new DropShadowEffect { BlurRadius = 10, ShadowDepth = 2, Opacity = 0.35 },
            });
        }

        _info.Text = $"{_score.MeasureCount} Takte · {_pages.Count} Seite(n)";
    }

    private DrawingVisual PageVisual(ScoreRenderer renderer, LayoutPage page, double scale, double pixelsPerDip)
    {
        var visual = new DrawingVisual();
        using var dc = visual.RenderOpen();
        dc.PushTransform(new ScaleTransform(scale, scale));
        dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, _options.PageWidth, _options.PageHeight));
        renderer.RenderPage(page, new WpfScoreCanvas(dc, pixelsPerDip));
        dc.Pop();
        return visual;
    }

    private void Print()
    {
        if (_score is null)
        {
            return;
        }

        var dialog = new PrintDialog();
        dialog.PrintTicket.PageOrientation = PageOrientation.Portrait;
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var renderer = new ScoreRenderer(_score, _options, _pages.Count);
            double scale = Math.Min(dialog.PrintableAreaWidth / _options.PageWidth, dialog.PrintableAreaHeight / _options.PageHeight);
            var document = new FixedDocument();
            document.DocumentPaginator.PageSize = new Size(dialog.PrintableAreaWidth, dialog.PrintableAreaHeight);
            foreach (var page in _pages)
            {
                var fixedPage = new FixedPage { Width = dialog.PrintableAreaWidth, Height = dialog.PrintableAreaHeight };
                fixedPage.Children.Add(new VisualHost(PageVisual(renderer, page, scale, 1)));
                var content = new PageContent();
                ((System.Windows.Markup.IAddChild)content).AddChild(fixedPage);
                document.Pages.Add(content);
            }

            dialog.PrintDocument(document.DocumentPaginator, _score.Title);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Das Notenblatt konnte nicht gedruckt werden.\n\n{ex.Message}", "Drucken", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SavePdf()
    {
        if (_score is null)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Notenblatt als PDF speichern",
            Filter = "PDF-Dokument (*.pdf)|*.pdf",
            FileName = string.Concat(_score.Title.Split(Path.GetInvalidFileNameChars())) + ".pdf",
            DefaultExt = ".pdf",
            AddExtension = true,
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var renderer = new ScoreRenderer(_score, _options, _pages.Count);
            int width = (int)Math.Round(_options.PageWidth * PdfDpi / 96);
            int height = (int)Math.Round(_options.PageHeight * PdfDpi / 96);
            var images = new List<PdfPageImage>();
            foreach (var page in _pages)
            {
                var bitmap = new RenderTargetBitmap(width, height, PdfDpi, PdfDpi, PixelFormats.Pbgra32);
                bitmap.Render(PageVisual(renderer, page, 1, PdfDpi / 96));
                var gray = new FormatConvertedBitmap(bitmap, PixelFormats.Gray8, null, 0);
                var pixels = new byte[width * height];
                gray.CopyPixels(pixels, width, 0);
                images.Add(new PdfPageImage(width, height, pixels));
            }

            PdfImageWriter.Write(dialog.FileName, images, _score.Title);
            _info.Text = $"Gespeichert: {Path.GetFileName(dialog.FileName)}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Das PDF konnte nicht gespeichert werden.\n\n{ex.Message}", "Als PDF speichern", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>Hält ein <see cref="Visual"/> in einer Druckseite.</summary>
    private sealed class VisualHost : FrameworkElement
    {
        private readonly Visual _visual;

        public VisualHost(Visual visual)
        {
            _visual = visual;
            AddVisualChild(visual);
        }

        protected override int VisualChildrenCount => 1;

        protected override Visual GetVisualChild(int index) => _visual;
    }
}
