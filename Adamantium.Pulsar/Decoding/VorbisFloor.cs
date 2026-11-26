namespace Adamantium.Pulsar.Decoding;

/// <summary>A Vorbis floor: a channel's spectral envelope, read from a packet, then multiplied into its spectrum.
/// </summary>
internal abstract class VorbisFloor
{
    /// <summary>Reads a channel's floor; false where it is unused this frame, the packet's end within it included.
    /// </summary>
    public abstract bool Read(ref VorbisBits bits, VorbisCodebook[] books, int channel);

    /// <summary>Multiplies the <paramref name="spectrum"/> of a channel whose floor was read by its curve.</summary>
    public abstract void Apply(int channel, Span<float> spectrum);

    protected static InvalidDataException Damaged(string how) => new($"The Vorbis stream is damaged: {how}.");

    protected static int CheckBook(int book, VorbisCodebook[] books) =>
        book < books.Length ? book : throw Damaged($"a floor names codebook {book}, which there is not");
}
