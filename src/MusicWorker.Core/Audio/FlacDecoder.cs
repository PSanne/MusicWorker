namespace MusicWorker.Core.Audio;

/// <summary>Dekodiert FLAC-Dateien (CONSTANT, VERBATIM, FIXED, LPC; Rice/Rice2; alle Kanalaufteilungen).</summary>
internal static class FlacDecoder
{
    private static readonly int[] SampleRates = [0, 88200, 176400, 192000, 8000, 16000, 22050, 24000, 32000, 44100, 48000, 96000];
    private static readonly int[] SampleSizes = [0, 8, 12, 0, 16, 20, 24, 32];

    public static AudioBuffer Decode(byte[] data)
    {
        if (data.Length < 8 || data[0] != 'f' || data[1] != 'L' || data[2] != 'a' || data[3] != 'C')
        {
            throw new InvalidDataException("Das ist keine FLAC-Datei.");
        }

        // Metadaten: nur STREAMINFO wird gebraucht.
        int position = 4;
        int sampleRate = 0, channels = 0, bits = 0;
        long totalSamples = 0;
        bool last = false;
        while (!last)
        {
            if (position + 4 > data.Length)
            {
                throw new InvalidDataException("Die FLAC-Datei ist unvollständig.");
            }

            last = (data[position] & 0x80) != 0;
            int type = data[position] & 0x7F;
            int length = (data[position + 1] << 16) | (data[position + 2] << 8) | data[position + 3];
            position += 4;
            if (type == 0)
            {
                var info = new FlacBitReader(data, position);
                info.Read(16);
                info.Read(16);
                info.Read(24);
                info.Read(24);
                sampleRate = (int)info.Read(20);
                channels = (int)info.Read(3) + 1;
                bits = (int)info.Read(5) + 1;
                totalSamples = ((long)info.Read(4) << 32) | info.Read(32);
            }

            position += length;
        }

        if (channels == 0)
        {
            throw new InvalidDataException("In der FLAC-Datei fehlen die Formatangaben.");
        }

        var output = new List<float>(totalSamples > 0 ? (int)Math.Min(totalSamples * channels, int.MaxValue / 2) : 1 << 20);
        var reader = new FlacBitReader(data, position);
        double scale = 1.0 / (1L << (bits - 1));
        var block = Array.Empty<int[]>();
        while (reader.BytePosition + 2 < data.Length)
        {
            // Sync suchen (0xFFF8 / 0xFFF9)
            if (data[reader.BytePosition] != 0xFF || (data[reader.BytePosition + 1] & 0xFE) != 0xF8)
            {
                reader.SkipToByte(reader.BytePosition + 1);
                continue;
            }

            int frameStart = reader.BytePosition;
            try
            {
                int frameChannels = DecodeFrame(reader, ref block, channels, bits, ref sampleRate, out int n);
                for (int f = 0; f < n; f++)
                {
                    for (int c = 0; c < frameChannels; c++)
                    {
                        output.Add((float)(block[c][f] * scale));
                    }
                }

                if (totalSamples > 0 && output.Count >= totalSamples * channels)
                {
                    break;
                }
            }
            catch (InvalidDataException) when (output.Count > 0)
            {
                reader.SkipToByte(frameStart + 1); // beschädigter Frame – weitersuchen
            }
        }

        if (totalSamples > 0 && output.Count > totalSamples * channels)
        {
            output.RemoveRange((int)(totalSamples * channels), output.Count - (int)(totalSamples * channels));
        }

        return new AudioBuffer(output.ToArray(), sampleRate, channels);
    }

    private static int DecodeFrame(FlacBitReader r, ref int[][] block, int channels, int streamBits, ref int sampleRate, out int n)
    {
        r.Read(15);                     // Sync + reserviert
        r.Read(1);                      // Blocktyp (fest/variabel) – für das Dekodieren egal
        int sizeCode = (int)r.Read(4);
        int rateCode = (int)r.Read(4);
        int assignment = (int)r.Read(4);
        int sizeBits = (int)r.Read(3);
        r.Read(1);
        ReadUtf8(r);
        n = sizeCode switch
        {
            1 => 192,
            >= 2 and <= 5 => 576 << (sizeCode - 2),
            6 => (int)r.Read(8) + 1,
            7 => (int)r.Read(16) + 1,
            >= 8 => 256 << (sizeCode - 8),
            _ => throw new InvalidDataException("Ungültige Blockgröße."),
        };

        switch (rateCode)
        {
            case >= 1 and <= 11:
                sampleRate = SampleRates[rateCode];
                break;
            case 12:
                sampleRate = (int)r.Read(8) * 1000;
                break;
            case 13:
                sampleRate = (int)r.Read(16);
                break;
            case 14:
                sampleRate = (int)r.Read(16) * 10;
                break;
            case 15:
                throw new InvalidDataException("Ungültige Abtastrate.");
        }

        int bits = sizeBits == 0 ? streamBits : SampleSizes[sizeBits];
        if (bits == 0)
        {
            throw new InvalidDataException("Ungültige Bittiefe.");
        }

        r.Read(8); // CRC-8
        int frameChannels = assignment < 8 ? assignment + 1 : 2;
        if (assignment > 10 || frameChannels != channels)
        {
            throw new InvalidDataException("Ungültige Kanalaufteilung.");
        }

        if (block.Length != channels || block[0].Length < n)
        {
            int size = Math.Max(n, 4608);
            block = new int[channels][];
            for (int c = 0; c < channels; c++)
            {
                block[c] = new int[size];
            }
        }

        for (int c = 0; c < channels; c++)
        {
            int channelBits = bits + ((assignment == 8 && c == 1) || (assignment == 9 && c == 0) || (assignment == 10 && c == 1) ? 1 : 0);
            DecodeSubframe(r, block[c], n, channelBits);
        }

        r.AlignToByte();
        r.Read(16); // CRC-16

        var a = block[0];
        var b = channels > 1 ? block[1] : a;
        switch (assignment)
        {
            case 8: // links/Seite
                for (int i = 0; i < n; i++)
                {
                    b[i] = a[i] - b[i];
                }

                break;
            case 9: // Seite/rechts
                for (int i = 0; i < n; i++)
                {
                    a[i] += b[i];
                }

                break;
            case 10: // Mitte/Seite
                for (int i = 0; i < n; i++)
                {
                    long mid = ((long)a[i] << 1) | (uint)(b[i] & 1);
                    int side = b[i];
                    a[i] = (int)((mid + side) >> 1);
                    b[i] = (int)((mid - side) >> 1);
                }

                break;
        }

        return channels;
    }

    private static void DecodeSubframe(FlacBitReader r, int[] output, int n, int bits)
    {
        r.Read(1);
        int type = (int)r.Read(6);
        int wasted = 0;
        if (r.Read(1) == 1)
        {
            wasted = r.ReadUnary() + 1;
            bits -= wasted;
        }

        if (type == 0)
        {
            int value = r.ReadSigned(bits);
            Array.Fill(output, value, 0, n);
        }
        else if (type == 1)
        {
            for (int i = 0; i < n; i++)
            {
                output[i] = r.ReadSigned(bits);
            }
        }
        else if (type is >= 8 and <= 12)
        {
            int order = type - 8;
            for (int i = 0; i < order; i++)
            {
                output[i] = r.ReadSigned(bits);
            }

            ReadResidual(r, output, n, order);
            for (int i = order; i < n; i++)
            {
                long prediction = order switch
                {
                    0 => 0,
                    1 => output[i - 1],
                    2 => 2L * output[i - 1] - output[i - 2],
                    3 => 3L * output[i - 1] - 3L * output[i - 2] + output[i - 3],
                    _ => 4L * output[i - 1] - 6L * output[i - 2] + 4L * output[i - 3] - output[i - 4],
                };
                output[i] = (int)(output[i] + prediction);
            }
        }
        else if (type >= 32)
        {
            int order = (type & 31) + 1;
            for (int i = 0; i < order; i++)
            {
                output[i] = r.ReadSigned(bits);
            }

            int precision = (int)r.Read(4) + 1;
            int shift = r.ReadSigned(5);
            var coefficients = new int[order];
            for (int i = 0; i < order; i++)
            {
                coefficients[i] = r.ReadSigned(precision);
            }

            ReadResidual(r, output, n, order);
            for (int i = order; i < n; i++)
            {
                long sum = 0;
                for (int j = 0; j < order; j++)
                {
                    sum += (long)coefficients[j] * output[i - 1 - j];
                }

                output[i] += (int)(sum >> shift);
            }
        }
        else
        {
            throw new InvalidDataException("Unbekannter Subframe-Typ.");
        }

        if (wasted > 0)
        {
            for (int i = 0; i < n; i++)
            {
                output[i] <<= wasted;
            }
        }
    }

    private static void ReadResidual(FlacBitReader r, int[] output, int n, int order)
    {
        int method = (int)r.Read(2);
        if (method > 1)
        {
            throw new InvalidDataException("Unbekannte Rest-Kodierung.");
        }

        int parameterBits = method == 0 ? 4 : 5;
        int escape = method == 0 ? 15 : 31;
        int partitionOrder = (int)r.Read(4);
        int partitions = 1 << partitionOrder;
        int perPartition = n >> partitionOrder;
        int index = order;
        for (int p = 0; p < partitions; p++)
        {
            int k = (int)r.Read(parameterBits);
            int end = (p + 1) * perPartition;
            if (k == escape)
            {
                int raw = (int)r.Read(5);
                for (; index < end; index++)
                {
                    output[index] = raw == 0 ? 0 : r.ReadSigned(raw);
                }
            }
            else
            {
                for (; index < end; index++)
                {
                    uint u = ((uint)r.ReadUnary() << k) | r.Read(k);
                    output[index] = (int)(u >> 1) ^ -(int)(u & 1);
                }
            }
        }
    }

    private static void ReadUtf8(FlacBitReader r)
    {
        uint first = r.Read(8);
        int extra = first < 0x80 ? 0 : first < 0xE0 ? 1 : first < 0xF0 ? 2 : first < 0xF8 ? 3 : first < 0xFC ? 4 : first < 0xFE ? 5 : 6;
        for (int i = 0; i < extra; i++)
        {
            r.Read(8);
        }
    }
}

/// <summary>Bitweises Lesen, höchstwertiges Bit zuerst.</summary>
internal sealed class FlacBitReader(byte[] data, int start)
{
    private long _bit = start * 8L;

    public int BytePosition => (int)((_bit + 7) / 8);

    public uint Read(int count)
    {
        uint value = 0;
        for (int i = 0; i < count; i++)
        {
            long index = _bit >> 3;
            if (index >= data.Length)
            {
                throw new InvalidDataException("Die FLAC-Datei endet unerwartet.");
            }

            value = (value << 1) | (uint)((data[index] >> (7 - (int)(_bit & 7))) & 1);
            _bit++;
        }

        return value;
    }

    public int ReadSigned(int count)
    {
        if (count == 0)
        {
            return 0;
        }

        uint value = Read(count);
        int shift = 32 - count;
        return (int)(value << shift) >> shift;
    }

    public int ReadUnary()
    {
        int count = 0;
        while (true)
        {
            long index = _bit >> 3;
            if (index >= data.Length)
            {
                throw new InvalidDataException("Die FLAC-Datei endet unerwartet.");
            }

            // Ganze Null-Bytes am Stück überspringen.
            if ((_bit & 7) == 0 && data[index] == 0)
            {
                count += 8;
                _bit += 8;
                continue;
            }

            int bit = (data[index] >> (7 - (int)(_bit & 7))) & 1;
            _bit++;
            if (bit == 1)
            {
                return count;
            }

            count++;
        }
    }

    public void AlignToByte() => _bit = (_bit + 7) & ~7L;

    public void SkipToByte(int position) => _bit = position * 8L;
}
