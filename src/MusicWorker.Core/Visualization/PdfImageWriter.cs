using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace MusicWorker.Core.Visualization;

/// <summary>Eine Seite als Graustufenbild (ein Byte pro Pixel, 0 = schwarz, 255 = weiß).</summary>
public sealed record PdfPageImage(int Width, int Height, byte[] Gray);

/// <summary>
/// Schreibt eine schlichte PDF-Datei, in der jede Seite ein Bild ist (z. B. das gerenderte Notenblatt).
/// Kommt ohne Druckertreiber und ohne Zusatzbibliothek aus.
/// </summary>
public static class PdfImageWriter
{
    /// <summary>A4 in PDF-Punkten (1/72 Zoll).</summary>
    public const double A4Width = 595.28;

    public const double A4Height = 841.89;

    public static void Write(string path, IReadOnlyList<PdfPageImage> pages, string title, double pageWidth = A4Width, double pageHeight = A4Height)
    {
        using var file = File.Create(path);
        Write(file, pages, title, pageWidth, pageHeight);
    }

    public static void Write(Stream output, IReadOnlyList<PdfPageImage> pages, string title, double pageWidth = A4Width, double pageHeight = A4Height)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(pages);
        if (pages.Count == 0)
        {
            throw new ArgumentException("Mindestens eine Seite ist nötig.", nameof(pages));
        }

        var offsets = new List<long>();
        long position = 0;
        void Raw(byte[] bytes)
        {
            output.Write(bytes);
            position += bytes.Length;
        }

        void Text(string text) => Raw(Encoding.ASCII.GetBytes(text));
        void BeginObject(int number)
        {
            while (offsets.Count < number)
            {
                offsets.Add(0);
            }

            offsets[number - 1] = position;
            Text($"{number} 0 obj\n");
        }

        string w = pageWidth.ToString("0.##", CultureInfo.InvariantCulture);
        string h = pageHeight.ToString("0.##", CultureInfo.InvariantCulture);

        Text("%PDF-1.4\n");
        Raw([(byte)'%', 0xE2, 0xE3, 0xCF, 0xD3, (byte)'\n']);

        // Objekte: 1 Katalog, 2 Seitenbaum, 3 Info, danach je Seite: Seite, Inhalt, Bild
        int PageObject(int i) => 4 + (i * 3);
        BeginObject(1);
        Text("<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        BeginObject(2);
        Text($"<< /Type /Pages /Kids [{string.Join(" ", Enumerable.Range(0, pages.Count).Select(i => $"{PageObject(i)} 0 R"))}] /Count {pages.Count} >>\nendobj\n");
        BeginObject(3);
        Text($"<< /Title {PdfString(title)} /Producer (MusicWorker) >>\nendobj\n");

        for (int i = 0; i < pages.Count; i++)
        {
            var page = pages[i];
            if (page.Gray.Length != page.Width * page.Height)
            {
                throw new ArgumentException($"Seite {i + 1}: Die Bilddaten passen nicht zur Größe.", nameof(pages));
            }

            int pageObject = PageObject(i);
            BeginObject(pageObject);
            Text($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {w} {h}] /Resources << /XObject << /Im0 {pageObject + 2} 0 R >> >> /Contents {pageObject + 1} 0 R >>\nendobj\n");

            byte[] content = Encoding.ASCII.GetBytes($"q {w} 0 0 {h} 0 0 cm /Im0 Do Q\n");
            BeginObject(pageObject + 1);
            Text($"<< /Length {content.Length} >>\nstream\n");
            Raw(content);
            Text("endstream\nendobj\n");

            byte[] image = Compress(page.Gray);
            BeginObject(pageObject + 2);
            Text($"<< /Type /XObject /Subtype /Image /Width {page.Width} /Height {page.Height} /ColorSpace /DeviceGray /BitsPerComponent 8 /Filter /FlateDecode /Length {image.Length} >>\nstream\n");
            Raw(image);
            Text("\nendstream\nendobj\n");
        }

        long xref = position;
        var table = new StringBuilder();
        table.Append(CultureInfo.InvariantCulture, $"xref\n0 {offsets.Count + 1}\n0000000000 65535 f \n");
        foreach (long offset in offsets)
        {
            table.Append(CultureInfo.InvariantCulture, $"{offset:D10} 00000 n \n");
        }

        table.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {offsets.Count + 1} /Root 1 0 R /Info 3 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        Text(table.ToString());
    }

    private static byte[] Compress(byte[] data)
    {
        using var buffer = new MemoryStream();
        using (var zlib = new ZLibStream(buffer, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(data);
        }

        return buffer.ToArray();
    }

    /// <summary>Text als PDF-Zeichenkette (UTF-16 mit Kennung, damit Umlaute stimmen).</summary>
    private static string PdfString(string text)
    {
        var hex = new StringBuilder("<FEFF");
        foreach (char c in text)
        {
            hex.Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
        }

        return hex.Append('>').ToString();
    }
}
