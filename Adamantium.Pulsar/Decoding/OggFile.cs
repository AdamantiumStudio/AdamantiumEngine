namespace Adamantium.Pulsar.Decoding;

/// <summary>An Ogg file's decoder, by the codec of its first stream of a sound: Vorbis, or FLAC.</summary>
internal static class OggFile
{
    /// <exception cref="InvalidDataException">It has no stream of a codec read; the stream is disposed.</exception>
    public static ISoundDecoder Open(Stream stream)
    {
        var ogg = OggStream.Open(stream, packet => VorbisDecoder.IsVorbis(packet) || OggFlacStream.IsFlac(packet) ||
            IsOpus(packet));
        try
        {
            if (!ogg.TryReadPacket(out var first, out _, out _))
            {
                throw new InvalidDataException("The Ogg file's first page is damaged.");
            }

            if (VorbisDecoder.IsVorbis(first))
            {
                ogg.SeekToPage(0);
                return new VorbisDecoder(ogg);
            }

            if (OggFlacStream.IsFlac(first))
            {
                return new FlacDecoder(new OggFlacStream(ogg));
            }

            throw new InvalidDataException("The Ogg file is of Opus, which is not read yet.");
        }
        catch
        {
            ogg.Dispose();
            throw;
        }
    }

    private static bool IsOpus(ReadOnlySpan<byte> packet) => packet.StartsWith("OpusHead"u8);
}
