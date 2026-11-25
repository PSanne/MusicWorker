using System.Security.Cryptography;

namespace MusicWorker.Core.Audio;

/// <summary>
/// FLAC – verlustfrei komprimiertes Audio (meist 40–60 % der WAV-Größe). Eigener Kodierer (feste Prädiktoren,
/// Rice-Kodierung, Stereo-Dekorrelation) und Dekodierer (alle gängigen FLAC-Dateien, auch mit LPC).
/// </summary>
public static class FlacFile
{
    public const int BlockSize = 4096;

    public static void Write(AudioBuffer buffer, string path, int bitsPerSample = 16)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite);
        Write(buffer, stream, bitsPerSample);
    }

    /// <summary>Schreibt FLAC mit 16 oder 24 Bit; der Stream muss zum Nachtragen der Kopfdaten zurückspulbar sein.</summary>
    public static void Write(AudioBuffer buffer, Stream stream, int bitsPerSample = 16)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (bitsPerSample is not (16 or 24))
        {
            throw new ArgumentOutOfRangeException(nameof(bitsPerSample), "FLAC wird mit 16 oder 24 Bit geschrieben.");
        }

        if (buffer.Channels is < 1 or > 8)
        {
            throw new NotSupportedException("FLAC unterstützt 1 bis 8 Kanäle.");
        }

        int channels = buffer.Channels;
        int frames = buffer.FrameCount;
        long start = stream.Position;
        stream.Write("fLaC"u8);
        long streamInfoPosition = stream.Position;
        stream.Write(new byte[4 + 34]); // Platzhalter, wird am Ende geschrieben

        using var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
        var pcm = new int[channels][];
        for (int c = 0; c < channels; c++)
        {
            pcm[c] = new int[BlockSize];
        }

        int minFrame = int.MaxValue, maxFrame = 0;
        long frameNumber = 0;
        var md5Bytes = new byte[BlockSize * channels * (bitsPerSample / 8)];
        for (int offset = 0; offset < frames; offset += BlockSize, frameNumber++)
        {
            int n = Math.Min(BlockSize, frames - offset);
            Quantize(buffer, offset, n, bitsPerSample, pcm, md5Bytes, md5);
            var frame = FlacEncoder.EncodeFrame(pcm, n, channels, bitsPerSample, frameNumber);
            stream.Write(frame);
            minFrame = Math.Min(minFrame, frame.Length);
            maxFrame = Math.Max(maxFrame, frame.Length);
        }

        long end = stream.Position;
        stream.Position = streamInfoPosition;
        stream.Write(StreamInfo(buffer.SampleRate, channels, bitsPerSample, frames, frames == 0 ? 0 : minFrame, maxFrame, md5.GetHashAndReset()));
        stream.Position = end;
        _ = start;
    }

    public static AudioBuffer Read(string path) => Read(File.ReadAllBytes(path));

    public static AudioBuffer Read(Stream stream)
    {
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return Read(memory.ToArray());
    }

    public static AudioBuffer Read(byte[] data) => FlacDecoder.Decode(data);

    private static void Quantize(AudioBuffer buffer, int offset, int n, int bits, int[][] pcm, byte[] md5Bytes, IncrementalHash md5)
    {
        int channels = buffer.Channels;
        double scale = 1 << (bits - 1);
        int max = (1 << (bits - 1)) - 1;
        int min = -(1 << (bits - 1));
        int bytes = bits / 8;
        var samples = buffer.Samples;
        int k = 0;
        for (int f = 0; f < n; f++)
        {
            for (int c = 0; c < channels; c++)
            {
                int v = (int)Math.Clamp(Math.Round(samples[(offset + f) * channels + c] * scale), min, max);
                pcm[c][f] = v;
                for (int b = 0; b < bytes; b++)
                {
                    md5Bytes[k++] = (byte)(v >> (8 * b));
                }
            }
        }

        md5.AppendData(md5Bytes, 0, k);
    }

    private static byte[] StreamInfo(int sampleRate, int channels, int bits, long totalSamples, int minFrame, int maxFrame, byte[] md5)
    {
        var writer = new FlacBitWriter();
        writer.Write(1, 1);          // letzter Metadatenblock
        writer.Write(0, 7);          // STREAMINFO
        writer.Write(34, 24);
        writer.Write(BlockSize, 16); // kleinste Blockgröße (der letzte Block zählt nicht)
        writer.Write(BlockSize, 16);
        writer.Write((uint)minFrame, 24);
        writer.Write((uint)maxFrame, 24);
        writer.Write((uint)sampleRate, 20);
        writer.Write((uint)(channels - 1), 3);
        writer.Write((uint)(bits - 1), 5);
        writer.Write((uint)(totalSamples >> 32), 4);
        writer.Write((uint)totalSamples, 32);
        foreach (byte b in md5)
        {
            writer.Write(b, 8);
        }

        return writer.ToArray();
    }
}

/// <summary>Bitweises Schreiben, höchstwertiges Bit zuerst.</summary>
internal sealed class FlacBitWriter
{
    private readonly List<byte> _bytes = new(16384);
    private ulong _accumulator;
    private int _bits;

    public int ByteCount => _bytes.Count;

    public void Write(uint value, int bits)
    {
        if (bits == 0)
        {
            return;
        }

        _accumulator = (_accumulator << bits) | (value & (bits == 32 ? uint.MaxValue : (1u << bits) - 1));
        _bits += bits;
        while (_bits >= 8)
        {
            _bits -= 8;
            _bytes.Add((byte)(_accumulator >> _bits));
        }
    }

    public void WriteSigned(int value, int bits) => Write((uint)value, bits);

    public void WriteRice(int value, int k)
    {
        uint u = (uint)((value << 1) ^ (value >> 31));
        uint quotient = u >> k;
        while (quotient >= 32)
        {
            Write(0, 32);
            quotient -= 32;
        }

        Write(1, (int)quotient + 1); // „quotient“ Nullen, dann eine Eins
        Write(u, k);
    }

    public void AlignToByte()
    {
        if (_bits > 0)
        {
            Write(0, 8 - _bits);
        }
    }

    public void WriteBytes(ReadOnlySpan<byte> bytes)
    {
        foreach (byte b in bytes)
        {
            Write(b, 8);
        }
    }

    public byte[] ToArray() => _bytes.ToArray();

    public List<byte> Bytes => _bytes;
}

internal static class FlacCrc
{
    private static readonly byte[] Crc8Table = CreateCrc8();
    private static readonly ushort[] Crc16Table = CreateCrc16();

    public static byte Crc8(IReadOnlyList<byte> data, int count)
    {
        byte crc = 0;
        for (int i = 0; i < count; i++)
        {
            crc = Crc8Table[crc ^ data[i]];
        }

        return crc;
    }

    public static ushort Crc16(IReadOnlyList<byte> data, int count)
    {
        ushort crc = 0;
        for (int i = 0; i < count; i++)
        {
            crc = (ushort)((crc << 8) ^ Crc16Table[(crc >> 8) ^ data[i]]);
        }

        return crc;
    }

    private static byte[] CreateCrc8()
    {
        var table = new byte[256];
        for (int i = 0; i < 256; i++)
        {
            int crc = i;
            for (int b = 0; b < 8; b++)
            {
                crc = (crc & 0x80) != 0 ? (crc << 1) ^ 0x07 : crc << 1;
            }

            table[i] = (byte)crc;
        }

        return table;
    }

    private static ushort[] CreateCrc16()
    {
        var table = new ushort[256];
        for (int i = 0; i < 256; i++)
        {
            int crc = i << 8;
            for (int b = 0; b < 8; b++)
            {
                crc = (crc & 0x8000) != 0 ? (crc << 1) ^ 0x8005 : crc << 1;
            }

            table[i] = (ushort)crc;
        }

        return table;
    }
}

/// <summary>Kodiert einen FLAC-Frame.</summary>
internal static class FlacEncoder
{
    private const int MaxPartitionOrder = 8;

    public static byte[] EncodeFrame(int[][] pcm, int n, int channels, int bits, long frameNumber)
    {
        // Kanalaufteilung wählen: bei Stereo die günstigste von L/R, L/S, S/R und M/S.
        int assignment = channels - 1;
        int[][] signals = pcm;
        int[] signalBits = Enumerable.Repeat(bits, channels).ToArray();
        if (channels == 2)
        {
            var left = pcm[0];
            var right = pcm[1];
            var side = new int[n];
            var mid = new int[n];
            for (int i = 0; i < n; i++)
            {
                side[i] = left[i] - right[i];
                mid[i] = (left[i] + right[i]) >> 1;
            }

            long costL = EstimateCost(left, n, bits);
            long costR = EstimateCost(right, n, bits);
            long costS = EstimateCost(side, n, bits + 1);
            long costM = EstimateCost(mid, n, bits);
            long[] costs = [costL + costR, costL + costS, costS + costR, costM + costS];
            int best = Array.IndexOf(costs, costs.Min());
            (assignment, signals, signalBits) = best switch
            {
                1 => (8, new[] { left, side }, new[] { bits, bits + 1 }),
                2 => (9, new[] { side, right }, new[] { bits + 1, bits }),
                3 => (10, new[] { mid, side }, new[] { bits, bits + 1 }),
                _ => (1, new[] { left, right }, new[] { bits, bits }),
            };
        }

        var writer = new FlacBitWriter();
        writer.Write(0xFFF8, 16); // Sync, fester Blocktyp
        int sizeCode = n == FlacFile.BlockSize ? 12 : n <= 256 ? 6 : 7;
        writer.Write((uint)sizeCode, 4);
        writer.Write(0, 4);                       // Abtastrate aus STREAMINFO
        writer.Write((uint)assignment, 4);
        writer.Write(0, 3);                       // Bittiefe aus STREAMINFO
        writer.Write(0, 1);
        WriteUtf8(writer, frameNumber);
        if (sizeCode == 6)
        {
            writer.Write((uint)(n - 1), 8);
        }
        else if (sizeCode == 7)
        {
            writer.Write((uint)(n - 1), 16);
        }

        writer.Write(FlacCrc.Crc8(writer.Bytes, writer.ByteCount), 8);

        for (int c = 0; c < signals.Length; c++)
        {
            WriteSubframe(writer, signals[c], n, signalBits[c]);
        }

        writer.AlignToByte();
        writer.Write(FlacCrc.Crc16(writer.Bytes, writer.ByteCount), 16);
        return writer.ToArray();
    }

    private static void WriteUtf8(FlacBitWriter writer, long value)
    {
        if (value < 0x80)
        {
            writer.Write((uint)value, 8);
            return;
        }

        int bytes = value < 0x800 ? 2 : value < 0x10000 ? 3 : value < 0x200000 ? 4 : value < 0x4000000 ? 5 : value < 0x80000000 ? 6 : 7;
        int firstBits = 7 - bytes;
        uint lead = (uint)((0xFF00 >> bytes) & 0xFF);
        writer.Write(lead | (uint)(value >> (6 * (bytes - 1))) & ((1u << firstBits) - 1), 8);
        for (int i = bytes - 2; i >= 0; i--)
        {
            writer.Write(0x80u | (uint)((value >> (6 * i)) & 0x3F), 8);
        }
    }

    /// <summary>Grobe Größe in Bits (für die Wahl der Stereo-Aufteilung).</summary>
    private static long EstimateCost(int[] signal, int n, int bits)
    {
        var (order, _) = ChooseOrder(signal, n);
        if (order < 0)
        {
            return bits;
        }

        var residual = Residual(signal, n, order);
        return (long)order * bits + ResidualCost(residual, n, order, out _, out _, out _);
    }

    private static void WriteSubframe(FlacBitWriter writer, int[] signal, int n, int bits)
    {
        var (order, _) = ChooseOrder(signal, n);
        if (order < 0)
        {
            writer.Write(0, 8);                    // CONSTANT
            writer.WriteSigned(signal[0], bits);
            return;
        }

        var residual = Residual(signal, n, order);
        long cost = (long)order * bits + ResidualCost(residual, n, order, out int partitionOrder, out int[] parameters, out bool rice2);
        if (cost >= (long)n * bits)
        {
            writer.Write(0b00000010, 8);           // VERBATIM
            for (int i = 0; i < n; i++)
            {
                writer.WriteSigned(signal[i], bits);
            }

            return;
        }

        writer.Write((uint)((0b001000 | order) << 1), 8); // FIXED, Ordnung 0–4, keine „wasted bits“
        for (int i = 0; i < order; i++)
        {
            writer.WriteSigned(signal[i], bits);
        }

        writer.Write(rice2 ? 1u : 0u, 2);
        writer.Write((uint)partitionOrder, 4);
        int partitions = 1 << partitionOrder;
        int perPartition = n >> partitionOrder;
        int index = order;
        for (int p = 0; p < partitions; p++)
        {
            int k = parameters[p];
            writer.Write((uint)k, rice2 ? 5 : 4);
            int end = (p + 1) * perPartition;
            for (; index < end; index++)
            {
                writer.WriteRice(residual[index], k);
            }
        }
    }

    /// <summary>Ordnung des festen Prädiktors mit der kleinsten Restsumme (-1 = konstantes Signal).</summary>
    private static (int Order, long Sum) ChooseOrder(int[] s, int n)
    {
        bool constant = true;
        for (int i = 1; i < n && constant; i++)
        {
            constant = s[i] == s[0];
        }

        if (constant)
        {
            return (-1, 0);
        }

        var sums = new long[5];
        for (int i = 4; i < n; i++)
        {
            long e0 = s[i];
            long e1 = e0 - s[i - 1];
            long e2 = e1 - (s[i - 1] - (long)s[i - 2]);
            long e3 = e2 - ((s[i - 1] - (long)s[i - 2]) - (s[i - 2] - (long)s[i - 3]));
            long e4 = e3 - (((s[i - 1] - (long)s[i - 2]) - (s[i - 2] - (long)s[i - 3])) - ((s[i - 2] - (long)s[i - 3]) - (s[i - 3] - (long)s[i - 4])));
            sums[0] += Math.Abs(e0);
            sums[1] += Math.Abs(e1);
            sums[2] += Math.Abs(e2);
            sums[3] += Math.Abs(e3);
            sums[4] += Math.Abs(e4);
        }

        int maxOrder = Math.Min(4, n - 1);
        int best = 0;
        for (int o = 1; o <= maxOrder; o++)
        {
            if (sums[o] < sums[best])
            {
                best = o;
            }
        }

        return (n <= 4 ? 0 : best, sums[best]);
    }

    private static int[] Residual(int[] s, int n, int order)
    {
        var r = new int[n];
        for (int i = order; i < n; i++)
        {
            r[i] = order switch
            {
                0 => s[i],
                1 => s[i] - s[i - 1],
                2 => s[i] - 2 * s[i - 1] + s[i - 2],
                3 => s[i] - 3 * s[i - 1] + 3 * s[i - 2] - s[i - 3],
                _ => s[i] - 4 * s[i - 1] + 6 * s[i - 2] - 4 * s[i - 3] + s[i - 4],
            };
        }

        return r;
    }

    /// <summary>Kleinste Größe der Rice-kodierten Reste über alle Partitionsordnungen.</summary>
    private static long ResidualCost(int[] residual, int n, int order, out int bestOrder, out int[] bestParameters, out bool rice2)
    {
        bestOrder = 0;
        bestParameters = [0];
        long bestCost = long.MaxValue;
        rice2 = false;
        for (int po = 0; po <= MaxPartitionOrder; po++)
        {
            if (n % (1 << po) != 0 || (n >> po) <= order)
            {
                break;
            }

            int partitions = 1 << po;
            int perPartition = n >> po;
            var parameters = new int[partitions];
            long cost = 2 + 4;
            bool needsRice2 = false;
            for (int p = 0; p < partitions; p++)
            {
                int from = p == 0 ? order : p * perPartition;
                int to = (p + 1) * perPartition;
                long sum = 0;
                for (int i = from; i < to; i++)
                {
                    int v = residual[i];
                    sum += (uint)((v << 1) ^ (v >> 31));
                }

                int count = to - from;
                int k = count == 0 || sum <= count ? 0 : Math.Min(30, (int)Math.Floor(Math.Log2((double)sum / count)));
                long kCost = PartitionCost(residual, from, to, k);
                for (int candidate = Math.Max(0, k - 1); candidate <= Math.Min(30, k + 1); candidate++)
                {
                    if (candidate != k)
                    {
                        long c = PartitionCost(residual, from, to, candidate);
                        if (c < kCost)
                        {
                            kCost = c;
                            k = candidate;
                        }
                    }
                }

                parameters[p] = k;
                needsRice2 |= k > 14;
                cost += kCost;
            }

            cost += partitions * (needsRice2 ? 5 : 4);
            if (cost < bestCost)
            {
                bestCost = cost;
                bestOrder = po;
                bestParameters = parameters;
                rice2 = needsRice2;
            }
        }

        return bestCost;
    }

    private static long PartitionCost(int[] residual, int from, int to, int k)
    {
        long cost = (long)(to - from) * (k + 1);
        for (int i = from; i < to; i++)
        {
            int v = residual[i];
            cost += (uint)((v << 1) ^ (v >> 31)) >> k;
        }

        return cost;
    }
}
