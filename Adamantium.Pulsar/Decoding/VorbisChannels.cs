namespace Adamantium.Pulsar.Decoding;

/// <summary>Vorbis's order of channels (Vorbis I, 4.3.9), which Opus in Ogg's mapping family 1 has too, against the
/// order of the speakers' bits that Pulsar's samples are in.</summary>
internal static class VorbisChannels
{
    private static readonly int[][] Orders =
    [
        [], [0], [0, 1], [0, 2, 1], [0, 1, 2, 3], [0, 2, 1, 3, 4], [0, 2, 1, 4, 5, 3], [0, 2, 1, 5, 6, 4, 3],
        [0, 2, 1, 6, 7, 4, 5, 3],
    ];

    /// <summary>By Vorbis's channel of <paramref name="channels"/>, 1 to 8, where it is in the speakers' order.
    /// </summary>
    public static int[] SpeakerIndices(int channels) => Orders[channels];
}
