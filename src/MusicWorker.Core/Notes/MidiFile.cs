using System.Text;

namespace MusicWorker.Core.Notes;

/// <summary>Eine Notenspur aus/für eine MIDI-Datei.</summary>
/// <param name="Name">Spurname.</param>
/// <param name="Channel">MIDI-Kanal 0 … 15 (9 = Schlagzeug).</param>
/// <param name="Program">General-MIDI-Klang 0 … 127.</param>
/// <param name="Notes">Noten in <see cref="MusicalTime.TicksPerQuarter"/>-Auflösung.</param>
public sealed record MidiTrackData(string Name, int Channel, int Program, IReadOnlyList<Note> Notes)
{
    public bool IsDrums => Channel == 9;
}

/// <summary>Inhalt einer MIDI-Datei: Tempo, Taktart, Notenspuren und (optional) Tempowechsel nach dem Anfang.</summary>
public sealed record MidiSong(double Bpm, int BeatsPerBar, IReadOnlyList<MidiTrackData> Tracks, IReadOnlyList<TempoChange>? TempoChanges = null);

/// <summary>
/// Liest und schreibt Standard-MIDI-Dateien (.mid, Format 0 und 1). Beim Lesen werden Spuren mit mehreren
/// Kanälen nach Kanal aufgeteilt und alle Zeiten auf <see cref="MusicalTime.TicksPerQuarter"/> umgerechnet.
/// </summary>
public static class MidiFile
{
    public static MidiSong Read(string path) => Read(File.ReadAllBytes(path));

    public static MidiSong Read(byte[] data)
    {
        var reader = new ByteReader(data);
        if (reader.ReadAscii(4) != "MThd")
        {
            throw new InvalidDataException("Keine MIDI-Datei (Kopfteil „MThd“ fehlt).");
        }

        int headerLength = reader.ReadInt32();
        int format = reader.ReadInt16();
        int trackCount = reader.ReadInt16();
        int division = reader.ReadInt16();
        reader.Skip(headerLength - 6);
        if ((division & 0x8000) != 0)
        {
            throw new NotSupportedException("MIDI-Dateien mit SMPTE-Zeitbasis werden nicht unterstützt.");
        }

        if (format > 1)
        {
            throw new NotSupportedException("MIDI-Format 2 (unabhängige Muster) wird nicht unterstützt.");
        }

        double scale = MusicalTime.TicksPerQuarter / (double)Math.Max(1, division);
        var tempos = new SortedDictionary<long, double>();
        int beatsPerBar = 4;
        var tracks = new List<MidiTrackData>();

        for (int t = 0; t < trackCount && reader.Remaining >= 8; t++)
        {
            string id = reader.ReadAscii(4);
            int length = reader.ReadInt32();
            if (id != "MTrk")
            {
                reader.Skip(length);
                t--;
                continue;
            }

            int end = reader.Position + length;
            string name = string.Empty;
            long tick = 0;
            int status = 0;
            var programs = new Dictionary<int, int>();
            var open = new Dictionary<(int Channel, int Pitch), Stack<(long Start, int Velocity)>>();
            var notes = new Dictionary<int, List<Note>>();

            while (reader.Position < end)
            {
                tick += reader.ReadVariableLength();
                int b = reader.ReadByte();
                if (b < 0x80)
                {
                    reader.Position--; // laufender Status: Datenbyte gehört zum vorigen Status
                    b = status;
                }
                else if (b < 0xF0)
                {
                    status = b;
                }

                int type = b & 0xF0;
                int channel = b & 0x0F;
                switch (type)
                {
                    case 0x80:
                    case 0x90:
                    {
                        int pitch = reader.ReadByte();
                        int velocity = reader.ReadByte();
                        var key = (channel, pitch);
                        if (type == 0x90 && velocity > 0)
                        {
                            if (!open.TryGetValue(key, out var stack))
                            {
                                open[key] = stack = new Stack<(long, int)>();
                            }

                            stack.Push((tick, velocity));
                        }
                        else if (open.TryGetValue(key, out var stack) && stack.Count > 0)
                        {
                            var (start, vel) = stack.Pop();
                            AddNote(notes, channel, pitch, start, tick, vel, scale);
                        }

                        break;
                    }

                    case 0xA0:
                    case 0xB0:
                    case 0xE0:
                        reader.Skip(2);
                        break;
                    case 0xC0:
                        programs[channel] = reader.ReadByte();
                        break;
                    case 0xD0:
                        reader.Skip(1);
                        break;
                    default:
                        if (b == 0xFF)
                        {
                            int meta = reader.ReadByte();
                            int metaLength = (int)reader.ReadVariableLength();
                            int metaStart = reader.Position;
                            if (meta == 0x03 && metaLength > 0)
                            {
                                name = Encoding.Latin1.GetString(data, metaStart, metaLength).Trim();
                            }
                            else if (meta == 0x51 && metaLength == 3)
                            {
                                int microsPerQuarter = (data[metaStart] << 16) | (data[metaStart + 1] << 8) | data[metaStart + 2];
                                tempos.TryAdd((long)Math.Round(tick * scale), Math.Round(60_000_000.0 / Math.Max(1, microsPerQuarter), 2));
                            }
                            else if (meta == 0x58 && metaLength >= 1)
                            {
                                beatsPerBar = Math.Clamp((int)data[metaStart], 1, 16);
                            }

                            reader.Position = metaStart + metaLength;
                        }
                        else if (b is 0xF0 or 0xF7)
                        {
                            reader.Skip((int)reader.ReadVariableLength());
                        }

                        break;
                }
            }

            // Nicht beendete Noten am Spurende schließen
            foreach (var ((channel, pitch), stack) in open)
            {
                while (stack.Count > 0)
                {
                    var (start, vel) = stack.Pop();
                    AddNote(notes, channel, pitch, start, Math.Max(tick, start + 1), vel, scale);
                }
            }

            reader.Position = end;
            foreach (var (channel, list) in notes.OrderBy(p => p.Key))
            {
                string trackName = string.IsNullOrWhiteSpace(name) ? $"Spur {tracks.Count + 1}" : name;
                if (notes.Count > 1)
                {
                    trackName += channel == 9 ? " (Schlagzeug)" : $" (Kanal {channel + 1})";
                }

                tracks.Add(new MidiTrackData(trackName, channel, programs.GetValueOrDefault(channel), NoteTools.Sorted(list)));
            }
        }

        double bpm = tempos.Count > 0 && tempos.First().Key == 0 ? tempos.First().Value : 120;
        var changes = tempos.Where(t => t.Key > 0).Select(t => new TempoChange(t.Key, t.Value)).ToArray();
        return new MidiSong(bpm, beatsPerBar, tracks, changes.Length > 0 ? changes : null);
    }

    /// <summary>Schreibt eine MIDI-Datei (Format 1, <see cref="MusicalTime.TicksPerQuarter"/> Ticks pro Viertel).</summary>
    public static void Write(string path, MidiSong song) => File.WriteAllBytes(path, Write(song));

    public static byte[] Write(MidiSong song)
    {
        using var output = new MemoryStream();
        WriteAscii(output, "MThd");
        WriteInt32(output, 6);
        WriteInt16(output, 1);
        WriteInt16(output, song.Tracks.Count + 1);
        WriteInt16(output, MusicalTime.TicksPerQuarter);

        // Spur 0: Tempo und Taktart
        var conductor = new MemoryStream();
        var tempo = new TempoMap(song.Bpm, song.TempoChanges);
        WriteVariableLength(conductor, 0);
        conductor.Write([0xFF, 0x58, 0x04, (byte)Math.Clamp(song.BeatsPerBar, 1, 16), 0x02, 0x18, 0x08]);
        long previousTick = 0;
        foreach (var change in tempo.Changes)
        {
            int micros = (int)Math.Round(60_000_000 / change.Bpm);
            WriteVariableLength(conductor, change.Tick - previousTick);
            conductor.Write([0xFF, 0x51, 0x03, (byte)(micros >> 16), (byte)(micros >> 8), (byte)micros]);
            previousTick = change.Tick;
        }

        WriteVariableLength(conductor, 0);
        conductor.Write([0xFF, 0x2F, 0x00]);
        WriteChunk(output, conductor.ToArray());

        foreach (var track in song.Tracks)
        {
            var body = new MemoryStream();
            byte[] name = Encoding.Latin1.GetBytes(track.Name);
            WriteVariableLength(body, 0);
            body.Write([0xFF, 0x03]);
            WriteVariableLength(body, name.Length);
            body.Write(name);
            int channel = Math.Clamp(track.Channel, 0, 15);
            WriteVariableLength(body, 0);
            body.Write([(byte)(0xC0 | channel), (byte)Math.Clamp(track.Program, 0, 127)]);

            // Ereignisse: Note-Aus vor Note-An bei gleicher Zeit
            var events = track.Notes
                .SelectMany(n => new[]
                {
                    (Tick: n.Start, Order: 1, Status: 0x90 | channel, Pitch: n.Pitch, Velocity: Math.Clamp(n.Velocity, 1, 127)),
                    (Tick: n.End, Order: 0, Status: 0x80 | channel, Pitch: n.Pitch, Velocity: 64),
                })
                .OrderBy(e => e.Tick)
                .ThenBy(e => e.Order);
            long last = 0;
            foreach (var e in events)
            {
                WriteVariableLength(body, e.Tick - last);
                last = e.Tick;
                body.Write([(byte)e.Status, (byte)Math.Clamp(e.Pitch, 0, 127), (byte)e.Velocity]);
            }

            WriteVariableLength(body, 0);
            body.Write([0xFF, 0x2F, 0x00]);
            WriteChunk(output, body.ToArray());
        }

        return output.ToArray();
    }

    private static void AddNote(Dictionary<int, List<Note>> notes, int channel, int pitch, long start, long end, int velocity, double scale)
    {
        if (!notes.TryGetValue(channel, out var list))
        {
            notes[channel] = list = [];
        }

        long s = (long)Math.Round(start * scale);
        long e = (long)Math.Round(end * scale);
        list.Add(new Note(pitch, s, Math.Max(1, e - s), Math.Clamp(velocity, 1, 127)));
    }

    private static void WriteChunk(Stream output, byte[] body)
    {
        WriteAscii(output, "MTrk");
        WriteInt32(output, body.Length);
        output.Write(body);
    }

    private static void WriteAscii(Stream output, string text) => output.Write(Encoding.ASCII.GetBytes(text));

    private static void WriteInt32(Stream output, int value) =>
        output.Write([(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value]);

    private static void WriteInt16(Stream output, int value) => output.Write([(byte)(value >> 8), (byte)value]);

    private static void WriteVariableLength(Stream output, long value)
    {
        value = Math.Max(0, value);
        var bytes = new Stack<byte>();
        bytes.Push((byte)(value & 0x7F));
        while ((value >>= 7) > 0)
        {
            bytes.Push((byte)((value & 0x7F) | 0x80));
        }

        while (bytes.Count > 0)
        {
            output.WriteByte(bytes.Pop());
        }
    }

    private sealed class ByteReader(byte[] data)
    {
        public int Position { get; set; }

        public int Remaining => data.Length - Position;

        public int ReadByte() => Position < data.Length
            ? data[Position++]
            : throw new InvalidDataException("Die MIDI-Datei ist unvollständig.");

        public int ReadInt16() => (ReadByte() << 8) | ReadByte();

        public int ReadInt32() => (ReadByte() << 24) | (ReadByte() << 16) | (ReadByte() << 8) | ReadByte();

        public string ReadAscii(int count)
        {
            var text = new StringBuilder(count);
            for (int i = 0; i < count; i++)
            {
                text.Append((char)ReadByte());
            }

            return text.ToString();
        }

        public long ReadVariableLength()
        {
            long value = 0;
            for (int i = 0; i < 4; i++)
            {
                int b = ReadByte();
                value = (value << 7) | (uint)(b & 0x7F);
                if ((b & 0x80) == 0)
                {
                    break;
                }
            }

            return value;
        }

        public void Skip(int count) => Position = Math.Min(data.Length, Position + Math.Max(0, count));
    }
}
