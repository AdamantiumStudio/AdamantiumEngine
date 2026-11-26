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
            if (!ogg.TryReadPacket(out var first, out _, out _))
            {
                throw new InvalidDataException("The Ogg file's first page is damaged.");
            }

            if (OggFlacStream.IsFlac(first))
            {
                return new FlacDecoder(new OggFlacStream(ogg));
            }

            ogg.SeekToPage(0);
            return new VorbisDecoder(ogg);
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
