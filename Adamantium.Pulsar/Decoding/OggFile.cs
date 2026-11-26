namespace Adamantium.Pulsar.Decoding;

/// <summary>An Ogg file's decoder, by the codec of its first stream of a sound read: Vorbis, or FLAC.</summary>
internal static class OggFile
{
    /// <exception cref="InvalidDataException">It has no stream of a codec read; the stream is disposed.</exception>
    public static ISoundDecoder Open(Stream stream)
    {
        var start = stream.Position;
        OggStream ogg;
        try
        {
            ogg = OggStream.Open(stream, packet => VorbisDecoder.IsVorbis(packet) || OggFlacStream.IsFlac(packet));
        }
        catch (InvalidDataException) when (HasOpus(stream, start))
        {
            throw new InvalidDataException("The Ogg file is of Opus, which is not read yet.");
        }

        try
        {
            // By the first page's body, as the stream was chosen by it: whole or not, Vorbis reads its packets checked.
            Span<byte> first = stackalloc byte[64];
            return OggFlacStream.IsFlac(first[..ogg.ReadBody(0, 0, first)])
                ? new FlacDecoder(new OggFlacStream(ogg))
                : new VorbisDecoder(ogg);
        }
        catch
        {
            ogg.Dispose();
            throw;
        }
    }

    private static bool HasOpus(Stream stream, long start)
    {
        stream.Position = start;
        return OggStream.Has(stream, packet => packet.StartsWith("OpusHead"u8));
    }
}
