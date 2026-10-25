using System.Collections.Generic;
using System.IO;

namespace Adamantium.Imaging.Tiff;

/// <summary>One image file directory (IFD) of a TIFF file: its fields by tag - those of <see cref="TiffTag"/>; others
/// are passed over.</summary>
internal sealed class TiffDirectory
{
    private readonly Dictionary<ushort, TiffField> _fields;

    internal TiffDirectory(long offset, long size, Dictionary<ushort, TiffField> fields, long nextOffset)
    {
        Offset = offset;
        Size = size;
        _fields = fields;
        NextOffset = nextOffset;
    }

    /// <summary>Where the directory starts in the file.</summary>
    public long Offset { get; }

    /// <summary>The bytes of its table of entries.</summary>
    public long Size { get; }

    /// <summary>Where the next directory of the chain starts; 0 at the last.</summary>
    public long NextOffset { get; }

    /// <summary>The fields, by tag.</summary>
    public IReadOnlyDictionary<ushort, TiffField> Fields => _fields;

    /// <summary>The field of <paramref name="tag"/>; null when the directory has none.</summary>
    public TiffField this[TiffTag tag] => _fields.GetValueOrDefault((ushort)tag);

    /// <summary>The first value of <paramref name="tag"/> as an unsigned integer, or <paramref name="fallback"/> when
    /// the field is missing.</summary>
    public ulong GetUnsigned(TiffTag tag, ulong fallback) =>
        this[tag] is { Count: > 0 } field ? field.GetUnsigned(0) : fallback;

    /// <summary>The first value of <paramref name="tag"/>; a missing field throws <see cref="InvalidDataException"/>.
    /// </summary>
    public ulong GetRequired(TiffTag tag) =>
        this[tag] is { Count: > 0 } field
            ? field.GetUnsigned(0)
            : throw new InvalidDataException($"The TIFF directory at {Offset} has no {tag}.");

    /// <summary>Whether this is a page: neither a reduced-resolution copy nor a transparency mask (NewSubfileType bits 0
    /// and 2). A value that cannot be read counts as neither.</summary>
    public bool IsPage => SubfileType is { } type && (type & 0b101) == 0;

    /// <summary>Whether this is a reduced-resolution copy of a page, not a mask.</summary>
    public bool IsReducedCopy => SubfileType is { } type && (type & 0b101) == 1;

    private ulong? SubfileType => TiffTry.Read(() => GetUnsigned(TiffTag.NewSubfileType, 0), (ulong?)null);

    /// <summary>All values of <paramref name="tag"/> as unsigned integers; null when the field is missing.</summary>
    public ulong[] GetUnsignedValues(TiffTag tag) => this[tag]?.GetUnsignedValues();

    /// <summary>All values of <paramref name="tag"/> as numbers; null when the field is missing.</summary>
    public double[] GetDoubleValues(TiffTag tag) => this[tag]?.GetDoubleValues();
}
