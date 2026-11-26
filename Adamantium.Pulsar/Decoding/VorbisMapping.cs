namespace Adamantium.Pulsar.Decoding;

/// <summary>A Vorbis mapping of type 0 (Vorbis I, 4.2.4): the channels coupled as magnitude and angle, and which
/// submap - which floor and residue - each channel is coded by.</summary>
internal sealed class VorbisMapping
{
    public required int[] Magnitudes { get; init; }

    public required int[] Angles { get; init; }

    /// <summary>By channel, its submap.</summary>
    public required int[] Mux { get; init; }

    public required int[] SubmapFloors { get; init; }

    public required int[] SubmapResidues { get; init; }

    /// <summary>By submap, its channels in order, and their vectors as the residue decodes them.</summary>
    public required int[][] SubmapChannels { get; init; }

    public required float[][][] SubmapVectors { get; init; }

    public required bool[][] SubmapSkipped { get; init; }
}
