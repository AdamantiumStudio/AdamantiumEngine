namespace Adamantium.Core.Compression;

/// <summary>Where the next bit of a stream is in the bits handed to <see cref="PrefixCode.Decode"/>.</summary>
public enum PrefixBitOrder
{
    /// <summary>The highest bit, the rest after it: JPEG, JBIG2, MP3.</summary>
    HighFirst,

    /// <summary>The lowest bit, the rest above it: Deflate, Vorbis, PRC.</summary>
    LowFirst,
}
