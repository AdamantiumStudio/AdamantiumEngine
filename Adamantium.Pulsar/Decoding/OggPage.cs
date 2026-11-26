namespace Adamantium.Pulsar.Decoding;

/// <summary>Where a page of a logical stream is (RFC 3533): its header and body, the last packet's end it says, and its
/// flags.</summary>
/// <param name="Granule">Of the last packet that ends on it; -1 where none does.</param>
internal readonly record struct OggPage(long At, int HeaderLength, int BodyLength, long Granule, byte Flags)
{
    public const byte Continued = 1;
    public const byte First = 2;
    public const byte Last = 4;

    public bool IsContinued => (Flags & Continued) != 0;

    public bool IsFirst => (Flags & First) != 0;

    public bool IsLast => (Flags & Last) != 0;

    public long End => At + HeaderLength + BodyLength;
}
