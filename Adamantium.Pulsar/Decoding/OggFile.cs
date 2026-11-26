namespace Adamantium.Pulsar.Decoding;

/// <summary>An Ogg file's decoder, by the codec of its first stream of a sound: Vorbis, Opus, or FLAC.</summary>
internal static class OggFile
{
    /// <exception cref="InvalidDataException">It has no stream of a codec read; the stream is disposed.</exception>
    public static ISoundDecoder Open(Stream stream)
    {
        // An Opus stream of what is not read - ambisonics, a later version - is passed by as any of a codec not known.
        var ogg = OggStream.Open(stream, packet => VorbisDecoder.IsVorbis(packet) || OggOpusDecoder.IsReadable(packet) ||
            OggFlacStream.IsFlac(packet));
        try
        {
            // By the first page's body, as the stream was chosen by it: whole or not, Vorbis and Opus read their
            // packets checked.
            Span<byte> first = stackalloc byte[64];
            first = first[..ogg.ReadBody(0, 0, first)];
            if (OggFlacStream.IsFlac(first))
            {
                return new FlacDecoder(new OggFlacStream(ogg));
            }

            return OggOpusDecoder.IsOpus(first) ? new OggOpusDecoder(ogg) : new VorbisDecoder(ogg);
        }
        catch
        {
            ogg.Dispose();
            throw;
        }
    }
}
