using System;
using System.Collections.Generic;
using System.IO;
using static Adamantium.Imaging.Tiff.TiffBytes;

namespace Adamantium.Imaging.Tiff;

/// <summary>The structure of a TIFF or BigTIFF file: its byte order and its chain of directories. Pixels are not read
/// here.</summary>
internal sealed class TiffFile
{
    private const int MaxDirectories = 65536;
    private const int MaxFieldsPerDirectory = 65535;

    private TiffFile(ITiffSource source, bool littleEndian, bool isBigTiff, List<TiffDirectory> directories)
    {
        Source = source;
        LittleEndian = littleEndian;
        IsBigTiff = isBigTiff;
        Directories = directories;
    }

    /// <summary>Where the bytes come from.</summary>
    public ITiffSource Source { get; }

    /// <summary>Whether multi-byte values are least significant byte first ("II").</summary>
    public bool LittleEndian { get; }

    /// <summary>Whether this is BigTIFF: 64-bit offsets and counts.</summary>
    public bool IsBigTiff { get; }

    /// <summary>The directories of the main chain, in file order.</summary>
    public IReadOnlyList<TiffDirectory> Directories { get; }

    /// <summary>Whether <paramref name="header"/> starts a TIFF or BigTIFF file.</summary>
    public static bool IsTiff(ReadOnlySpan<byte> header) => header is
        [0x49, 0x49, 0x2A, 0x00, ..] or [0x4D, 0x4D, 0x00, 0x2A, ..]
        or [0x49, 0x49, 0x2B, 0x00, ..] or [0x4D, 0x4D, 0x00, 0x2B, ..];

    /// <summary>Reads the header and the chain of directories. A file that is not TIFF, or whose first directory is
    /// broken, throws <see cref="InvalidDataException"/>; a broken link further on ends the chain there.</summary>
    public static TiffFile Open(ITiffSource source)
    {
        Span<byte> header = stackalloc byte[16];
        if (source.Length < 8)
        {
            throw new InvalidDataException("The file is too short to be TIFF.");
        }

        source.Read(0, header[..8]);
        if (!IsTiff(header))
        {
            throw new InvalidDataException("The file is not TIFF: its header is neither II nor MM with 42 or 43.");
        }

        var littleEndian = header[0] == 0x49;
        var isBigTiff = Read16(header[2..], littleEndian) == 43;
        long first;
        if (isBigTiff)
        {
            source.Read(0, header);
            if (Read16(header[4..], littleEndian) != 8 || Read16(header[6..], littleEndian) != 0)
            {
                throw new InvalidDataException("The BigTIFF header does not give 8-byte offsets.");
            }

            first = ToOffset(Read64(header[8..], littleEndian), source);
        }
        else
        {
            first = Read32(header[4..], littleEndian);
        }

        List<TiffDirectory> directories = [];
        var file = new TiffFile(source, littleEndian, isBigTiff, directories);
        directories.Add(file.ReadDirectory(first));
        var visited = new HashSet<long> { first };
        var tables = directories[0].Size;
        while (directories.Count < MaxDirectories && directories[^1].NextOffset is var next and not 0
               && visited.Add(next) && tables <= source.Length)
        {
            try
            {
                directories.Add(file.ReadDirectory(next));
                tables += directories[^1].Size;
            }
            catch (InvalidDataException)
            {
                break;
            }
        }

        return file;
    }

    /// <summary>Reads the directory at <paramref name="offset"/> - one of the chain, or a child one such as a
    /// reduced-resolution copy. Values that do not fit in their entries are read when first asked for. A field whose
    /// values would run past the file's end throws <see cref="InvalidDataException"/> - unless it only describes the
    /// image (resolution, color profile, geotags, GDAL's), then it is left out, as if the file did not have it.
    /// </summary>
    public TiffDirectory ReadDirectory(long offset)
    {
        var countSize = IsBigTiff ? 8 : 2;
        var entrySize = IsBigTiff ? 20 : 12;
        var offsetSize = IsBigTiff ? 8 : 4;
        Span<byte> count = stackalloc byte[8];
        Source.Read(offset, count[..countSize]);
        var entries = IsBigTiff ? Read64(count, LittleEndian) : Read16(count, LittleEndian);
        if (entries > MaxFieldsPerDirectory)
        {
            throw new InvalidDataException($"The TIFF directory at {offset} claims {entries} fields.");
        }

        var table = new byte[(int)entries * entrySize + offsetSize];
        Source.Read(offset + countSize, table);
        var fields = new Dictionary<ushort, TiffField>();
        for (var i = 0; i < (int)entries; i++)
        {
            var entry = table.AsSpan(i * entrySize, entrySize);
            var tag = Read16(entry, LittleEndian);
            var type = (TiffFieldType)Read16(entry[2..], LittleEndian);
            var valueCount = IsBigTiff ? Read64(entry[4..], LittleEndian) : Read32(entry[4..], LittleEndian);
            var size = TiffField.SizeOf(type);
            if (size == 0 || !Enum.IsDefined((TiffTag)tag) || fields.ContainsKey(tag))
            {
                continue;
            }

            if (valueCount > (ulong)Source.Length / (ulong)size || valueCount > (ulong)(Array.MaxLength / size))
            {
                if (Describes((TiffTag)tag))
                {
                    continue;
                }

                throw new InvalidDataException($"TIFF tag {tag} claims {valueCount} values, more than the file holds.");
            }

            var bytes = (long)valueCount * size;
            var value = entry[(IsBigTiff ? 12 : 8)..];
            if (bytes <= offsetSize)
            {
                fields[tag] = new TiffField(tag, type, valueCount, value[..(int)bytes].ToArray(), LittleEndian);
                continue;
            }

            var at = IsBigTiff ? Read64(value, LittleEndian) : Read32(value, LittleEndian);
            if (at > (ulong)(Source.Length - bytes))
            {
                if (Describes((TiffTag)tag))
                {
                    continue;
                }

                throw new InvalidDataException($"The values of TIFF tag {tag} run past the file's end.");
            }

            fields[tag] = new TiffField(tag, type, valueCount, Source, (long)at, LittleEndian);
        }

        var next = table.AsSpan((int)entries * entrySize);
        var nextOffset = IsBigTiff ? Read64(next, LittleEndian) : Read32(next, LittleEndian);
        return new TiffDirectory(offset, countSize + table.Length, fields,
            nextOffset < (ulong)Source.Length ? (long)nextOffset : 0);
    }

    private static bool Describes(TiffTag tag) => tag is TiffTag.XResolution or TiffTag.YResolution
        or TiffTag.ResolutionUnit or TiffTag.PageNumber or TiffTag.IccProfile
        or TiffTag.ModelPixelScale or TiffTag.ModelTiepoint or TiffTag.ModelTransformation or TiffTag.GeoKeyDirectory
        or TiffTag.GeoDoubleParams or TiffTag.GeoAsciiParams or TiffTag.GdalMetadata or TiffTag.GdalNoData;

    private static long ToOffset(ulong offset, ITiffSource source) =>
        offset <= (ulong)source.Length
            ? (long)offset
            : throw new InvalidDataException($"A TIFF offset {offset} points past the file's end at {source.Length}.");
}
