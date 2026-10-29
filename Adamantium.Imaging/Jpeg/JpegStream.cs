using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Adamantium.Imaging.Jpeg;

/// <summary>Decodes a JPEG stream (ITU-T T.81) into its components' samples: sequential DCT frames of 8 or 12 bits
/// (baseline and extended, SOF0/SOF1) and progressive ones (SOF2), Huffman-coded, and both arithmetic-coded
/// (SOF9/SOF10, <see cref="JpegArithmeticScan"/>); restart intervals; the number of lines given after the first scan
/// (DNL). JFIF (T.871) and Adobe's APP14 say how the components are to be colored. Lossless and hierarchical frames
/// throw <see cref="NotSupportedException"/>; a broken stream
/// <see cref="InvalidDataException"/>, while data cut short decodes as far as it goes.</summary>
internal sealed class JpegStream
{
    private const int MaxComponents = 4;

    // Pictures from this size are turned into samples on a thread of their own, a row of units behind the reading: one
    // thread, so the rows' coefficients pass between two cores only. A tile of a TIFF is smaller, and its reader
    // already decodes tiles side by side.
    private const long PipelinedPixels = 1 << 18;

    // Rows of units read ahead of those turned into samples, at most.
    private const int PipelinedRows = 4;

    // ZigZag[k]: the natural position of the k-th coefficient in zig-zag order (A.3.6).
    internal static readonly byte[] ZigZag = BuildZigZag();

    private readonly ushort[][] _quantization = new ushort[4][];
    private readonly JpegHuffman[] _dcTables = new JpegHuffman[4];
    private readonly JpegHuffman[] _acTables = new JpegHuffman[4];
    private readonly byte[] _dcBounds = [0x10, 0x10, 0x10, 0x10];
    private readonly byte[] _acThresholds = [5, 5, 5, 5];
    private readonly bool _preview;
    private bool _rgba;
    private JpegComponent[] _components;
    private int _restartInterval;
    private int _mcusAcross;
    private int _mcusDown;
    private int _maxHorizontal;
    private int _maxVertical;
    private int _eobRun;

    private JpegStream(bool preview, JpegStream tables)
    {
        _preview = preview;
        if (tables != null)
        {
            Array.Copy(tables._quantization, _quantization, 4);
            Array.Copy(tables._dcTables, _dcTables, 4);
            Array.Copy(tables._acTables, _acTables, 4);
        }
    }

    public int Width { get; private set; }

    public int Height { get; private set; }

    /// <summary>Bits of each sample: 8 or 12.</summary>
    public int Precision { get; private set; }

    public bool Progressive { get; private set; }

    /// <summary>Whether the frame is arithmetic-coded (SOF9, SOF10) rather than Huffman-coded.</summary>
    public bool Arithmetic { get; private set; }

    /// <summary>MCUs across the frame: what an interleaved scan's units are counted in.</summary>
    public int McusAcross => _mcusAcross;

    /// <summary>MCUs, or a lone component's blocks, a restart interval holds; 0 for none.</summary>
    public int RestartInterval => _restartInterval;

    public bool Jfif { get; private set; }

    /// <summary>Adobe's APP14 color transform - 0 none, 1 YCbCr, 2 YCCK; -1 without the segment.</summary>
    public int AdobeTransform { get; private set; } = -1;

    public JpegComponent[] Components => _components;

    /// <summary>Whether the samples are one a block - the eighth-scale picture.</summary>
    public bool Preview => _preview;

    /// <summary>The frame of <paramref name="data"/>, decoded; <paramref name="preview"/> keeps one sample a block,
    /// the block's DC term, and skips the inverse DCT and the AC-only scans of a progressive frame.</summary>
    public static JpegStream Decode(ReadOnlySpan<byte> data, bool preview) => Decode(data, preview, null, true);

    /// <summary>As <see cref="Decode(ReadOnlySpan{byte}, bool)"/>, the stream's tables starting as
    /// <paramref name="tables"/>'s - an abbreviated stream of tables only (B.5) - as TIFF shares them. With
    /// <paramref name="rgba"/>, a frame whose RGBA picture no array holds is refused as soon as its size is known.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static JpegStream Decode(ReadOnlySpan<byte> data, bool preview, JpegStream tables, bool rgba)
    {
        var stream = new JpegStream(preview, tables) { _rgba = rgba };
        try
        {
            stream.Read(data, false);
        }
        catch
        {
            stream.Release();
            throw;
        }

        return stream;
    }

    /// <summary>The frame's size and bits from <paramref name="data"/>'s headers, decoding nothing; a height of 0 for a
    /// frame that gives its lines after the first scan (DNL), all 0 without a frame.</summary>
    public static (int Width, int Height, int Precision) FrameSize(ReadOnlySpan<byte> data)
    {
        // Markers found as Read finds them: bytes between segments and markers that stand alone passed over.
        var at = 2;
        while (at + 4 <= data.Length)
        {
            if (data[at] != 0xFF)
            {
                at++;
                continue;
            }

            var marker = data[at + 1];
            if (marker == 0xFF)
            {
                at++;
                continue;
            }

            if (marker is 0x00 or 0x01 or 0xD8 or >= 0xD0 and <= 0xD7)
            {
                at += 2;
                continue;
            }

            var length = BinaryPrimitives.ReadUInt16BigEndian(data[(at + 2)..]);
            if (marker is >= 0xC0 and <= 0xCF and not (0xC4 or 0xC8 or 0xCC) && at + 9 <= data.Length)
            {
                return (BinaryPrimitives.ReadUInt16BigEndian(data[(at + 7)..]),
                    BinaryPrimitives.ReadUInt16BigEndian(data[(at + 5)..]), data[at + 4]);
            }

            if (marker is 0xDA or 0xD9 || length < 2)
            {
                break;
            }

            at += 2 + length;
        }

        return (0, 0, 0);
    }

    /// <summary>The quantization and Huffman tables of an abbreviated stream that holds only them (B.5), for
    /// <see cref="Decode(ReadOnlySpan{byte}, bool, JpegStream)"/>.</summary>
    public static JpegStream Tables(ReadOnlySpan<byte> data)
    {
        var stream = new JpegStream(false, null);
        stream.Read(data, true);
        return stream;
    }

    /// <summary>Gives the samples back to the pool they came from; the stream is not to be read after.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public void Release()
    {
        foreach (var component in _components ?? [])
        {
            if (component.Samples != null)
            {
                ArrayPool<byte>.Shared.Return(component.Samples);
            }

            if (component.Wide != null)
            {
                ArrayPool<ushort>.Shared.Return(component.Wide);
            }

            (component.Samples, component.Wide) = (null, null);
        }
    }

    private static byte[] BuildZigZag()
    {
        var order = new byte[64];
        var (x, y) = (0, 0);
        for (var k = 0; k < 64; k++)
        {
            order[k] = (byte)(y * 8 + x);
            if ((x + y) % 2 == 0)
            {
                if (x == 7)
                {
                    y++;
                }
                else if (y == 0)
                {
                    x++;
                }
                else
                {
                    x++;
                    y--;
                }
            }
            else if (y == 7)
            {
                x++;
            }
            else if (x == 0)
            {
                y++;
            }
            else
            {
                x--;
                y++;
            }
        }

        return order;
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private void Read(ReadOnlySpan<byte> data, bool tablesOnly)
    {
        if (data.Length < 4 || data[0] != 0xFF || data[1] != 0xD8)
        {
            throw new InvalidDataException("The data is not JPEG: no start-of-image marker.");
        }

        var at = 2;
        var scans = 0;
        while (true)
        {
            while (at < data.Length && data[at] != 0xFF)
            {
                at++;
            }

            while (at + 1 < data.Length && data[at + 1] == 0xFF)
            {
                at++;
            }

            if (at + 1 >= data.Length)
            {
                break;
            }

            var marker = data[at + 1];
            at += 2;
            if (marker == 0xD9)
            {
                break;
            }

            if (marker is 0x00 or 0x01 or 0xD8 or >= 0xD0 and <= 0xD7)
            {
                continue;
            }

            if (at + 2 > data.Length)
            {
                break;
            }

            var length = BinaryPrimitives.ReadUInt16BigEndian(data[at..]);
            if (length < 2)
            {
                throw new InvalidDataException($"A JPEG segment {marker:X2} of {length} bytes.");
            }

            var segment = data.Slice(at + 2, Math.Min(length - 2, data.Length - at - 2));
            at += length;
            switch (marker)
            {
                case 0xC4:
                    ReadHuffman(segment);
                    break;
                case 0xDB:
                    ReadQuantization(segment);
                    break;
                case 0xDD:
                    _restartInterval = segment.Length >= 2 ? BinaryPrimitives.ReadUInt16BigEndian(segment) : 0;
                    break;
                case 0xC0 or 0xC1 or 0xC2 or 0xC9 or 0xCA:
                    if (_components != null)
                    {
                        throw new NotSupportedException("A JPEG stream of more than one frame - hierarchical - is not "
                                                        + "read.");
                    }

                    Arithmetic = marker >= 0xC9;
                    ReadFrame(segment, marker is 0xC2 or 0xCA, data, Math.Min(at, data.Length));
                    break;
                case 0xCC:
                    ReadConditioning(segment);
                    break;
                case >= 0xC3 and <= 0xCF when marker is not (0xC4 or 0xC8):
                    throw new NotSupportedException($"JPEG of {Kind(marker)} is not read.");
                case 0xDA:
                    if (_components == null)
                    {
                        throw new InvalidDataException("A JPEG scan before its frame.");
                    }

                    at = Math.Min(at, data.Length);
                    at = ReadScan(segment, data, at, scans++ == 0);
                    break;
                case 0xE0:
                    Jfif |= segment.StartsWith("JFIF\0"u8);
                    break;
                case 0xEE:
                    if (segment.Length >= 12 && segment.StartsWith("Adobe"u8))
                    {
                        AdobeTransform = segment[11];
                    }

                    break;
            }

            if (at > data.Length)
            {
                break;
            }
        }

        if (tablesOnly)
        {
            return;
        }

        if (_components == null)
        {
            throw new InvalidDataException("The JPEG stream has no frame.");
        }

        if (Progressive)
        {
            FinishProgressive();
        }
    }

    private static string Kind(byte marker) => marker switch
    {
        0xC3 => "lossless coding",
        0xC5 or 0xC6 or 0xC7 => "hierarchical coding",
        0xCB => "lossless arithmetic coding",
        _ => "hierarchical arithmetic coding"
    };

    private void ReadConditioning(ReadOnlySpan<byte> segment)
    {
        for (var at = 0; at + 1 < segment.Length; at += 2)
        {
            int table = segment[at] & 15, value = segment[at + 1];
            if (table > 3)
            {
                throw new InvalidDataException($"A JPEG arithmetic conditioning table {table}.");
            }

            if (segment[at] >> 4 == 0)
            {
                if ((value & 15) > value >> 4)
                {
                    throw new InvalidDataException("A JPEG DC conditioning with L above U.");
                }

                _dcBounds[table] = (byte)value;
            }
            else
            {
                if (value is 0 or > 63)
                {
                    throw new InvalidDataException($"A JPEG AC conditioning Kx of {value}.");
                }

                _acThresholds[table] = (byte)value;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private void ReadQuantization(ReadOnlySpan<byte> segment)
    {
        while (!segment.IsEmpty)
        {
            var precision = segment[0] >> 4;
            var index = segment[0] & 15;
            var size = precision == 0 ? 64 : 128;
            if (index > 3 || precision > 1 || segment.Length < 1 + size)
            {
                throw new InvalidDataException("A broken JPEG quantization table.");
            }

            var table = new ushort[64];
            for (var k = 0; k < 64; k++)
            {
                table[ZigZag[k]] = precision == 0
                    ? segment[1 + k]
                    : BinaryPrimitives.ReadUInt16BigEndian(segment[(1 + 2 * k)..]);
            }

            _quantization[index] = table;
            segment = segment[(1 + size)..];
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private void ReadHuffman(ReadOnlySpan<byte> segment)
    {
        while (!segment.IsEmpty)
        {
            if (segment.Length < 17)
            {
                throw new InvalidDataException("A broken JPEG Huffman table.");
            }

            var kind = segment[0] >> 4;
            var index = segment[0] & 15;
            var counts = segment.Slice(1, 16);
            var total = 0;
            foreach (var count in counts)
            {
                total += count;
            }

            if (kind > 1 || index > 3 || total > 256 || segment.Length < 17 + total)
            {
                throw new InvalidDataException("A broken JPEG Huffman table.");
            }

            var table = new JpegHuffman(counts, segment.Slice(17, total));
            (kind == 0 ? _dcTables : _acTables)[index] = table;
            segment = segment[(17 + total)..];
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private void ReadFrame(ReadOnlySpan<byte> segment, bool progressive, ReadOnlySpan<byte> data, int after)
    {
        if (segment.Length < 6)
        {
            throw new InvalidDataException("A broken JPEG frame header.");
        }

        Precision = segment[0];
        Height = BinaryPrimitives.ReadUInt16BigEndian(segment[1..]);
        Width = BinaryPrimitives.ReadUInt16BigEndian(segment[3..]);
        var count = segment[5];
        Progressive = progressive;
        if (Precision is not (8 or 12))
        {
            throw new NotSupportedException($"JPEG samples of {Precision} bits are not read.");
        }

        if (count is 0 or > MaxComponents || segment.Length < 6 + 3 * count)
        {
            throw new NotSupportedException($"JPEG of {count} components is not read.");
        }

        if (Height == 0)
        {
            Height = LinesAfterTheFirstScan(data, after);
        }

        if (Width == 0 || Height == 0)
        {
            throw new InvalidDataException("A JPEG frame of no size.");
        }

        var eighth = _preview ? 8 : 1;
        if (_rgba && (long)((Width + eighth - 1) / eighth) * ((Height + eighth - 1) / eighth) * (Precision > 8 ? 8 : 4)
            > Array.MaxLength)
        {
            throw new NotSupportedException($"A JPEG of {Width}×{Height} is more than one array holds.");
        }

        _components = new JpegComponent[count];
        for (var i = 0; i < count; i++)
        {
            var entry = segment.Slice(6 + 3 * i, 3);
            int horizontal = entry[1] >> 4, vertical = entry[1] & 15;
            if (horizontal is < 1 or > 4 || vertical is < 1 or > 4 || entry[2] > 3)
            {
                throw new InvalidDataException("A broken JPEG component.");
            }

            _components[i] = new JpegComponent(entry[0], horizontal, vertical, entry[2]);
            _maxHorizontal = Math.Max(_maxHorizontal, horizontal);
            _maxVertical = Math.Max(_maxVertical, vertical);
        }

        foreach (var component in _components)
        {
            if (_maxHorizontal % component.Horizontal != 0 || _maxVertical % component.Vertical != 0)
            {
                throw new NotSupportedException("JPEG components sampled at factors that do not divide the largest.");
            }
        }

        _mcusAcross = (Width + 8 * _maxHorizontal - 1) / (8 * _maxHorizontal);
        _mcusDown = (Height + 8 * _maxVertical - 1) / (8 * _maxVertical);
        long samples = 0;
        foreach (var component in _components)
        {
            component.Width = (Width * component.Horizontal + _maxHorizontal - 1) / _maxHorizontal;
            component.Height = (Height * component.Vertical + _maxVertical - 1) / _maxVertical;
            component.BlocksAcross = (component.Width + 7) / 8;
            component.BlocksDown = (component.Height + 7) / 8;
            component.StoredAcross = _mcusAcross * component.Horizontal;
            component.StoredDown = _mcusDown * component.Vertical;
            samples += (long)component.StoredAcross * component.StoredDown * 64;
        }

        // Every block takes at least a bit of its first scan: a frame of far more blocks than the data could begin to
        // code is refused before anything is allocated for it - a few bytes claiming a gigapixel.
        if (samples / 64 > 64L * data.Length)
        {
            throw new InvalidDataException($"A JPEG of {Width}×{Height} in {data.Length} bytes.");
        }

        if (samples * (progressive && !_preview ? 2 : 1) > Array.MaxLength)
        {
            throw new NotSupportedException($"A JPEG of {Width}×{Height} is more than this decoder holds.");
        }

        foreach (var component in _components)
        {
            // From the pool, cleared: a component whose scan the data lacks reads as zeros.
            var side = _preview ? 1 : 8;
            component.Stride = component.StoredAcross * side;
            var length = component.Stride * component.StoredDown * side;
            if (Precision == 8)
            {
                component.Samples = ArrayPool<byte>.Shared.Rent(length);
                component.Samples.AsSpan(0, length).Clear();
            }
            else
            {
                component.Wide = ArrayPool<ushort>.Shared.Rent(length);
                component.Wide.AsSpan(0, length).Clear();
            }

            if (progressive)
            {
                component.Coefficients = new short[component.StoredAcross * component.StoredDown * Kept];
            }
        }
    }

    // DNL (B.2.5): a frame of 0 lines gets them from the segment after its first scan. Found by walking the segments
    // and the entropy-coded data, where 0xFF is only ever a stuffed byte or a restart.
    private static int LinesAfterTheFirstScan(ReadOnlySpan<byte> data, int at)
    {
        while (at + 3 < data.Length)
        {
            if (data[at] != 0xFF || data[at + 1] is 0x00 or 0xFF or >= 0xD0 and <= 0xD7)
            {
                at++;
                continue;
            }

            var marker = data[at + 1];
            if (marker == 0xDC)
            {
                return at + 5 < data.Length ? BinaryPrimitives.ReadUInt16BigEndian(data[(at + 4)..]) : 0;
            }

            if (marker == 0xD9)
            {
                return 0;
            }

            at += 2 + BinaryPrimitives.ReadUInt16BigEndian(data[(at + 2)..]);
        }

        return 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private int ReadScan(ReadOnlySpan<byte> header, ReadOnlySpan<byte> data, int at, bool first)
    {
        if (header.Length < 1 || header.Length < 1 + 2 * header[0] + 3 || header[0] is 0 or > MaxComponents)
        {
            throw new InvalidDataException("A broken JPEG scan header.");
        }

        var count = header[0];
        var scan = new JpegComponent[count];
        for (var i = 0; i < count; i++)
        {
            var id = header[1 + 2 * i];
            var tables = header[2 + 2 * i];
            scan[i] = Array.Find(_components, component => component.Id == id)
                      ?? throw new InvalidDataException($"A JPEG scan of component {id}, which the frame lacks.");
            // A table the stream does not define is K.3's - the luminance one as table 0, chrominance as the others.
            int dc = tables >> 4, ac = tables & 15;
            if (Arithmetic && (dc > 3 || ac > 3))
            {
                throw new InvalidDataException("A JPEG scan names an arithmetic conditioning table past the fourth.");
            }

            (scan[i].DcSelector, scan[i].AcSelector) = (dc, ac);
            scan[i].Dc = dc < 4 ? _dcTables[dc] ?? (dc == 0 ? JpegStandardTables.DcLuminance
                : JpegStandardTables.DcChrominance) : null;
            scan[i].Ac = ac < 4 ? _acTables[ac] ?? (ac == 0 ? JpegStandardTables.AcLuminance
                : JpegStandardTables.AcChrominance) : null;
        }

        var spectral = header.Slice(1 + 2 * count, 3);
        int start = spectral[0], end = spectral[1], high = spectral[2] >> 4, low = spectral[2] & 15;
        if (!Progressive)
        {
            (start, end, high, low) = (0, 63, 0, 0);
        }
        else if (start > end || end > 63 || start == 0 && end != 0 || start > 0 && count != 1)
        {
            throw new InvalidDataException("A broken JPEG progressive scan.");
        }

        if (_preview && Progressive && start > 0)
        {
            return SkipEntropyData(data, at);
        }

        foreach (var component in scan)
        {
            if (_quantization[component.Table] == null)
            {
                throw new InvalidDataException("A JPEG component without its quantization table.");
            }

            // The table in force at a component's first scan is the one it is dequantized by (B.2.4.1); a sequential
            // frame's scans are dequantized as they are decoded.
            component.Quantization = Progressive
                ? component.Quantization ?? _quantization[component.Table]
                : _quantization[component.Table];

            if (!Arithmetic && (start == 0 && high == 0 && component.Dc == null || start > 0 && component.Ac == null
                                || !Progressive && component.Ac == null))
            {
                throw new InvalidDataException("A JPEG scan without its Huffman tables.");
            }
        }

        _eobRun = 0;
        var units = count == 1 ? scan[0].BlocksAcross * scan[0].BlocksDown : _mcusAcross * _mcusDown;
        if (Arithmetic)
        {
            var after = new JpegArithmeticScan(this, scan, start, end, high, low, _dcBounds, _acThresholds)
                .Decode(data, at, units);
            return SkipEntropyData(data, after);
        }

        if (!Progressive && _restartInterval > 0 && Intervals(data, at, units) is { } starts)
        {
            return InParallel(scan, data, starts, units);
        }

        if (!Progressive && !_preview && Environment.ProcessorCount > 1 && (long)Width * Height >= PipelinedPixels)
        {
            return Pipelined(scan, data, at, units);
        }

        var reader = new JpegBitReader(data, at);
        Units(ref reader, scan, new int[count], 0, units, true, start, end, high, low);
        return SkipEntropyData(data, reader.Position);
    }

    // Where each restart interval's data starts, when the scan has enough of them to decode side by side and every
    // one is marked; null otherwise.
    private int[] Intervals(ReadOnlySpan<byte> data, int at, int units)
    {
        var count = (units + _restartInterval - 1) / _restartInterval;
        if (count < 4)
        {
            return null;
        }

        var starts = new int[count];
        starts[0] = at;
        var found = 1;
        for (var position = at; position + 1 < data.Length && found < count; position++)
        {
            if (data[position] != 0xFF)
            {
                continue;
            }

            var marker = data[position + 1];
            if (marker is >= 0xD0 and <= 0xD7)
            {
                starts[found++] = position + 2;
                position++;
            }
            else if (marker is not (0x00 or 0xFF))
            {
                break;
            }
        }

        return found == count ? starts : null;
    }

    // A sequential scan's restart intervals decoded at once, each from its own start with predictors of its own:
    // they share nothing but the samples, and those of different units.
    private unsafe int InParallel(JpegComponent[] scan, ReadOnlySpan<byte> data, int[] starts, int units)
    {
        fixed (byte* pointer = data)
        {
            var address = (nint)pointer;
            var length = data.Length;
            Parallel.For(0, starts.Length, interval =>
            {
                var reader = new JpegBitReader(new ReadOnlySpan<byte>((byte*)address, length), starts[interval]);
                Units(ref reader, scan, new int[scan.Length], interval * _restartInterval,
                    Math.Min(units, (interval + 1) * _restartInterval), false, 0, 63, 0, 0);
            });
        }

        return SkipEntropyData(data, starts[^1]);
    }

    private int Pipelined(JpegComponent[] scan, ReadOnlySpan<byte> data, int at, int units)
    {
        var across = scan.Length == 1 ? scan[0].BlocksAcross : _mcusAcross;
        var blocks = 0;
        foreach (var component in scan)
        {
            blocks += scan.Length == 1 ? 1 : component.Horizontal * component.Vertical;
        }

        using var free = new BlockingCollection<(short[] Coefficients, int[] Masks)>();
        using var read = new BlockingCollection<(int First, int Last, short[] Coefficients, int[] Masks)>();
        for (var i = 0; i < PipelinedRows; i++)
        {
            free.Add((new short[across * blocks * 64], new int[across * blocks]));
        }

        var storing = Task.Factory.StartNew(() =>
        {
            try
            {
                foreach (var (first, last, coefficients, masks) in read.GetConsumingEnumerable())
                {
                    StoreUnits(scan, first, last, coefficients, masks);
                    free.Add((coefficients, masks));
                }
            }
            finally
            {
                free.CompleteAdding();
            }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

        var reader = new JpegBitReader(data, at);
        var predictors = new int[scan.Length];
        try
        {
            for (var first = 0; first < units; first += across)
            {
                if (!free.TryTake(out var row, Timeout.Infinite))
                {
                    break;
                }

                var last = Math.Min(units, first + across);
                DecodeUnits(ref reader, scan, predictors, first, last, row.Coefficients, row.Masks);
                read.Add((first, last, row.Coefficients, row.Masks));
            }
        }
        finally
        {
            read.CompleteAdding();
            try
            {
                storing.Wait();
            }
            catch (AggregateException failed)
            {
                ExceptionDispatchInfo.Capture(failed.InnerExceptions[0]).Throw();
            }
        }

        return SkipEntropyData(data, reader.Position);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private void DecodeUnits(ref JpegBitReader reader, JpegComponent[] scan, int[] predictors, int first, int last,
        short[] coefficients, int[] masks)
    {
        var block = 0;
        for (var unit = first; unit < last; unit++)
        {
            if (_restartInterval > 0 && unit > 0 && unit % _restartInterval == 0)
            {
                reader.Restart();
                Array.Clear(predictors);
            }

            for (var c = 0; c < scan.Length; c++)
            {
                var component = scan[c];
                var count = scan.Length == 1 ? 1 : component.Horizontal * component.Vertical;
                for (var i = 0; i < count; i++, block++)
                {
                    masks[block] = DecodeSequential(ref reader, component, ref predictors[c],
                        coefficients.AsSpan(block * 64, 64));
                }
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private void StoreUnits(JpegComponent[] scan, int first, int last, short[] coefficients, int[] masks)
    {
        var block = 0;
        for (var unit = first; unit < last; unit++)
        {
            if (scan.Length == 1)
            {
                var component = scan[0];
                Store(component, coefficients.AsSpan(block * 64, 64), masks[block], unit % component.BlocksAcross,
                    unit / component.BlocksAcross);
                block++;
                continue;
            }

            var (across, down) = (unit % _mcusAcross, unit / _mcusAcross);
            foreach (var component in scan)
            {
                for (var v = 0; v < component.Vertical; v++)
                {
                    for (var h = 0; h < component.Horizontal; h++, block++)
                    {
                        Store(component, coefficients.AsSpan(block * 64, 64), masks[block],
                            across * component.Horizontal + h, down * component.Vertical + v);
                    }
                }
            }
        }
    }

    // Units first to last of a scan: blocks of its one component, or MCUs of several; restart markers passed when
    // restarts says so.
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private void Units(ref JpegBitReader reader, JpegComponent[] scan, int[] predictors, int first, int last,
        bool restarts, int start, int end, int high, int low)
    {
        for (var unit = first; unit < last; unit++)
        {
            if (restarts && _restartInterval > 0 && unit > first && unit % _restartInterval == 0)
            {
                reader.Restart();
                Array.Clear(predictors);
                _eobRun = 0;
            }

            if (scan.Length == 1)
            {
                var component = scan[0];
                Block(ref reader, component, ref predictors[0], unit % component.BlocksAcross,
                    unit / component.BlocksAcross, start, end, high, low);
                continue;
            }

            var (across, down) = (unit % _mcusAcross, unit / _mcusAcross);
            for (var c = 0; c < scan.Length; c++)
            {
                var component = scan[c];
                for (var v = 0; v < component.Vertical; v++)
                {
                    for (var h = 0; h < component.Horizontal; h++)
                    {
                        Block(ref reader, component, ref predictors[c], across * component.Horizontal + h,
                            down * component.Vertical + v, start, end, high, low);
                    }
                }
            }
        }
    }

    // To the next marker that is not a restart: the data after a scan's units - the end of a cut-short one, or the
    // whole of a scan the preview passes over.
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static int SkipEntropyData(ReadOnlySpan<byte> data, int at)
    {
        while (at + 1 < data.Length)
        {
            if (data[at] == 0xFF && data[at + 1] is not (0x00 or 0xFF) and not (>= 0xD0 and <= 0xD7))
            {
                return at;
            }

            at++;
        }

        return data.Length;
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private void Block(ref JpegBitReader reader, JpegComponent component, ref int predictor, int across, int down,
        int start, int end, int high, int low)
    {
        if (!Progressive)
        {
            Sequential(ref reader, component, ref predictor, across, down);
            return;
        }

        var coefficients = component.Coefficients.AsSpan((down * component.StoredAcross + across) * Kept, Kept);
        if (start == 0)
        {
            if (high == 0)
            {
                reader.DecodeValue(component.Dc, out var difference);
                predictor += difference;
                coefficients[0] = (short)(predictor << low);
            }
            else if (reader.Bit())
            {
                coefficients[0] |= (short)(1 << low);
            }

            return;
        }

        if (high == 0)
        {
            FirstAc(ref reader, component.Ac, coefficients, start, end, low);
        }
        else
        {
            RefineAc(ref reader, component.Ac, coefficients, start, end, low);
        }
    }

    // G.1.2.2: the first pass over a band of AC coefficients, with runs of blocks that end at once (EOBRUN).
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private void FirstAc(ref JpegBitReader reader, JpegHuffman table, Span<short> coefficients, int start, int end,
        int low)
    {
        if (_eobRun > 0)
        {
            _eobRun--;
            return;
        }

        for (var k = start; k <= end; k++)
        {
            var symbol = reader.Decode(table);
            if (symbol < 0)
            {
                return;
            }

            int run = symbol >> 4, size = symbol & 15;
            if (size == 0)
            {
                if (run < 15)
                {
                    _eobRun = (1 << run) - 1 + reader.Bits(run);
                    return;
                }

                k += 15;
                continue;
            }

            k += run;
            if (k > end)
            {
                return;
            }

            coefficients[k] = (short)(reader.Receive(size) * (1 << low));
        }
    }

    // G.1.2.3: one more bit of each coefficient of the band - a coefficient already nonzero gains a correction bit,
    // a new one comes in as ±1 at this bit, after its run of still-zero coefficients.
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private void RefineAc(ref JpegBitReader reader, JpegHuffman table, Span<short> coefficients, int start, int end,
        int low)
    {
        var plus = 1 << low;
        var minus = -1 << low;
        var k = start;
        if (_eobRun == 0)
        {
            for (; k <= end; k++)
            {
                var symbol = reader.Decode(table);
                if (symbol < 0)
                {
                    return;
                }

                int run = symbol >> 4, size = symbol & 15;
                var value = 0;
                if (size == 0)
                {
                    if (run < 15)
                    {
                        _eobRun = (1 << run) + reader.Bits(run);
                        break;
                    }
                }
                else
                {
                    value = reader.Bit() ? plus : minus;
                }

                for (; k <= end; k++)
                {
                    if (coefficients[k] != 0)
                    {
                        Correct(ref reader, ref coefficients[k], plus, minus);
                    }
                    else
                    {
                        if (run == 0)
                        {
                            if (value != 0)
                            {
                                coefficients[k] = (short)value;
                            }

                            break;
                        }

                        run--;
                    }
                }
            }
        }

        if (_eobRun > 0)
        {
            if (k <= end)
            {
                var band = NonzeroBits(coefficients) >> k;
                band &= end - k == 63 ? ulong.MaxValue : (1UL << (end - k + 1)) - 1;
                while (band != 0)
                {
                    var at = k + BitOperations.TrailingZeroCount(band);
                    Correct(ref reader, ref coefficients[at], plus, minus);
                    band &= band - 1;
                }
            }

            _eobRun--;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong NonzeroBits(ReadOnlySpan<short> coefficients)
    {
        ref var first = ref MemoryMarshal.GetReference(coefficients);
        ulong bits = 0;
        for (var i = 0; i < 64; i += 8)
        {
            var zero = Vector128.Equals(Vector128.LoadUnsafe(ref first, (nuint)i), Vector128<short>.Zero);
            bits |= (ulong)(~zero.ExtractMostSignificantBits() & 0xFF) << i;
        }

        return bits;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Correct(ref JpegBitReader reader, ref short coefficient, int plus, int minus)
    {
        if (reader.Bit() && (coefficient & plus) == 0)
        {
            coefficient += (short)(coefficient >= 0 ? plus : minus);
        }
    }

    // A block of a sequential scan, decoded and turned into samples at once.
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private void Sequential(ref JpegBitReader reader, JpegComponent component, ref int predictor, int across,
        int down)
    {
        Span<short> coefficients = stackalloc short[64];
        var rows = DecodeSequential(ref reader, component, ref predictor, coefficients);
        Store(component, coefficients, rows, across, down);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int DecodeSequential(ref JpegBitReader reader, JpegComponent component, ref int predictor,
        scoped Span<short> coefficients)
    {
        coefficients.Clear();
        reader.DecodeValue(component.Dc, out var difference);
        predictor += difference;
        coefficients[0] = (short)Math.Clamp(predictor, short.MinValue, short.MaxValue);
        var rows = 0;
        for (var k = 1; k < 64; k++)
        {
            var symbol = reader.DecodeValue(component.Ac, out var value);
            if (symbol <= 0)
            {
                break;
            }

            var run = symbol >> 4;
            if ((symbol & 15) == 0)
            {
                if (run != 15)
                {
                    break;
                }

                k += 15;
                continue;
            }

            k += run;
            if (k > 63)
            {
                break;
            }

            var at = ZigZag[k];
            coefficients[at] = (short)value;
            rows |= JpegIdct.Mask(at);
        }

        return rows;
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    /// <summary>A block's <paramref name="coefficients"/> (natural order; <paramref name="rows"/> the
    /// <see cref="JpegIdct.Mask"/> of its AC terms) into <paramref name="component"/>'s samples, its block
    /// <paramref name="across"/> and <paramref name="down"/>.</summary>
    internal void Store(JpegComponent component, ReadOnlySpan<short> coefficients, int rows, int across, int down)
    {
        var quantization = component.Quantization;
        if (_preview)
        {
            var value = JpegIdct.Average(coefficients[0], quantization[0], Precision);
            var at = down * component.Stride + across;
            if (component.Samples != null)
            {
                component.Samples[at] = (byte)value;
            }
            else
            {
                component.Wide[at] = (ushort)value;
            }

            return;
        }

        var offset = down * 8 * component.Stride + across * 8;
        if (component.Samples != null)
        {
            JpegIdct.Transform(coefficients, quantization, rows, Precision, component.Samples, offset,
                component.Stride);
        }
        else
        {
            JpegIdct.Transform(coefficients, quantization, rows, Precision, component.Wide, offset, component.Stride);
        }
    }

    /// <summary>Coefficients a progressive frame keeps of each block: its DC term alone for the eighth-scale picture.
    /// </summary>
    internal int Kept => _preview ? 1 : 64;

    // The coefficients of a progressive frame into samples, once every scan is in - rows of blocks at once.
    private void FinishProgressive()
    {
        foreach (var component in _components)
        {
            if ((component.Quantization ?? _quantization[component.Table]) == null)
            {
                throw new InvalidDataException("A JPEG component without its quantization table.");
            }

            Parallel.For(0, component.StoredDown, down => StoreRow(component, down));
            component.Coefficients = null;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private void StoreRow(JpegComponent component, int down)
    {
        Span<short> natural = stackalloc short[64];
        for (var across = 0; across < component.StoredAcross; across++)
        {
            var coefficients = component.Coefficients.AsSpan((down * component.StoredAcross + across) * Kept, Kept);
            if (Kept == 1)
            {
                Store(component, coefficients, 0, across, down);
                continue;
            }

            natural.Clear();
            natural[0] = coefficients[0];
            var rows = 0;
            var nonzero = NonzeroBits(coefficients) & ~1UL;
            while (nonzero != 0)
            {
                var k = BitOperations.TrailingZeroCount(nonzero);
                var at = ZigZag[k];
                natural[at] = coefficients[k];
                rows |= JpegIdct.Mask(at);
                nonzero &= nonzero - 1;
            }

            Store(component, natural, rows, across, down);
        }
    }
}
