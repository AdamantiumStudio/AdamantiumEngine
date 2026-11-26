namespace Adamantium.Pulsar.Decoding;

/// <summary>Where the scalefactor bands of a sample rate begin: 23 of long blocks' lines, the last 576, and 14 of short
/// blocks' lines of one window, the last 192.</summary>
internal sealed class Mp3Bands(int[] longs, int[] shorts)
{
    public int[] Long { get; } = longs;

    public int[] Short { get; } = shorts;
}
