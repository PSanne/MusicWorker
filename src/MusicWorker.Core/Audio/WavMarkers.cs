using System.Text;
using MusicWorker.Core.Editing;

namespace MusicWorker.Core.Audio;

/// <summary>
/// Liest und schreibt Marker in WAV-Dateien – als „cue “-Chunk mit Namen im „LIST/adtl/labl“-Chunk.
/// Das ist der übliche Weg, den auch andere Audioprogramme verstehen.
/// </summary>
public static class WavMarkers
{
    /// <summary>Hängt die Marker an eine fertig geschriebene WAV-Datei an (vorhandene Chunks bleiben unverändert).</summary>
    public static void Append(string path, IReadOnlyList<Marker> markers)
    {
        ArgumentNullException.ThrowIfNull(markers);
        if (markers.Count == 0)
        {
            return;
        }

        using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite);
        Append(stream, markers);
    }

    public static void Append(Stream stream, IReadOnlyList<Marker> markers)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (markers.Count == 0)
        {
            return;
        }

        RequireWave(stream);
        stream.Seek(0, SeekOrigin.End);
        if (stream.Length % 2 == 1)
        {
            stream.WriteByte(0); // Chunks beginnen immer an geraden Positionen.
        }

        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);

        // cue-Chunk: je Marker 24 Bytes.
        writer.Write("cue "u8);
        writer.Write(4 + 24 * markers.Count);
        writer.Write(markers.Count);
        for (int i = 0; i < markers.Count; i++)
        {
            uint position = (uint)Math.Clamp(markers[i].Frame, 0, uint.MaxValue);
            writer.Write(i + 1);          // ID
            writer.Write(position);       // Position
            writer.Write("data"u8);       // Chunk, auf den sich der Marker bezieht
            writer.Write(0);              // Chunk-Anfang
            writer.Write(0);              // Block-Anfang
            writer.Write(position);       // Sample-Offset
        }

        // LIST/adtl mit einem labl-Eintrag je Marker (Name, nullterminiert).
        var labels = markers.Select((m, i) => (Id: i + 1, Text: Latin1(m.Name))).ToArray();
        int listSize = 4 + labels.Sum(l => 8 + Padded(4 + l.Text.Length + 1));
        writer.Write("LIST"u8);
        writer.Write(listSize);
        writer.Write("adtl"u8);
        foreach (var (id, text) in labels)
        {
            int size = 4 + text.Length + 1;
            writer.Write("labl"u8);
            writer.Write(size);
            writer.Write(id);
            writer.Write(text);
            writer.Write((byte)0);
            if (size % 2 == 1)
            {
                writer.Write((byte)0);
            }
        }

        writer.Flush();
        long riffSize = stream.Length - 8;
        stream.Seek(4, SeekOrigin.Begin);
        writer.Write((uint)Math.Min(riffSize, uint.MaxValue));
        writer.Flush();
    }

    /// <summary>Liest die Marker einer WAV-Datei (leer, wenn es keine gibt oder es keine WAV-Datei ist).</summary>
    public static IReadOnlyList<Marker> Read(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return Read(stream);
        }
        catch (IOException)
        {
            return MarkerList.Empty;
        }
    }

    public static IReadOnlyList<Marker> Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!IsWave(stream))
        {
            return MarkerList.Empty;
        }

        var positions = new List<(int Id, long Frame)>();
        var names = new Dictionary<int, string>();
        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
        stream.Seek(12, SeekOrigin.Begin);
        while (stream.Length - stream.Position >= 8)
        {
            string id = Encoding.ASCII.GetString(reader.ReadBytes(4));
            long size = reader.ReadUInt32();
            long start = stream.Position;
            long end = Math.Min(stream.Length, start + size);
            if (id == "cue " && size >= 4)
            {
                int count = reader.ReadInt32();
                for (int i = 0; i < count && stream.Position + 24 <= end; i++)
                {
                    int cueId = reader.ReadInt32();
                    uint position = reader.ReadUInt32();
                    reader.ReadBytes(12);
                    uint sampleOffset = reader.ReadUInt32();
                    positions.Add((cueId, sampleOffset != 0 || position == 0 ? sampleOffset : position));
                }
            }
            else if (id == "LIST" && size >= 4 && Encoding.ASCII.GetString(reader.ReadBytes(4)) == "adtl")
            {
                while (end - stream.Position >= 8)
                {
                    string subId = Encoding.ASCII.GetString(reader.ReadBytes(4));
                    long subSize = reader.ReadUInt32();
                    long subStart = stream.Position;
                    if ((subId == "labl" || subId == "note") && subSize >= 4 && subStart + subSize <= end)
                    {
                        int cueId = reader.ReadInt32();
                        var bytes = reader.ReadBytes((int)subSize - 4);
                        string text = Decode(bytes);
                        if (subId == "labl" || !names.ContainsKey(cueId))
                        {
                            names[cueId] = text;
                        }
                    }

                    stream.Seek(subStart + subSize + (subSize % 2), SeekOrigin.Begin);
                }
            }

            if (start + size + (size % 2) > stream.Length)
            {
                break;
            }

            stream.Seek(start + size + (size % 2), SeekOrigin.Begin);
        }

        var markers = positions.Select((p, i) => new Marker(
            names.TryGetValue(p.Id, out var name) && !string.IsNullOrWhiteSpace(name) ? name : $"Marker {i + 1}",
            p.Frame));
        return MarkerList.Sorted(markers);
    }

    private static int Padded(int size) => size + (size % 2);

    private static byte[] Latin1(string text) =>
        Encoding.Latin1.GetBytes(text.Replace('\0', ' '));

    /// <summary>UTF-8, wenn gültig – sonst Latin-1 (so schreiben es die meisten Windows-Programme).</summary>
    private static string Decode(byte[] bytes)
    {
        int length = Array.IndexOf(bytes, (byte)0);
        var span = bytes.AsSpan(0, length < 0 ? bytes.Length : length);
        try
        {
            return new UTF8Encoding(false, true).GetString(span).Trim();
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(span).Trim();
        }
    }

    private static bool IsWave(Stream stream)
    {
        if (stream.Length < 12)
        {
            return false;
        }

        stream.Seek(0, SeekOrigin.Begin);
        Span<byte> header = stackalloc byte[12];
        stream.ReadExactly(header);
        return header[..4].SequenceEqual("RIFF"u8) && header[8..12].SequenceEqual("WAVE"u8);
    }

    private static void RequireWave(Stream stream)
    {
        if (!IsWave(stream))
        {
            throw new InvalidDataException("Das ist keine WAV-Datei.");
        }
    }
}
