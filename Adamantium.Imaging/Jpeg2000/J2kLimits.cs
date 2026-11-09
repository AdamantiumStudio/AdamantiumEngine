using System.IO;

namespace Adamantium.Imaging.Jpeg2000;

/// <summary>What one codestream may make the decoder build, in proportion to its own image: samples, tiles,
/// resolutions, subbands, precincts, code-blocks, packets and progression passes are counted, and hostile data is
/// cut off here.</summary>
internal sealed class J2kLimits
{
    /// <summary>What a resolution, subband or precinct costs, in samples.</summary>
    public const int ObjectCost = 16;

    private long _left;

    /// <summary>A budget for an image of <paramref name="pixels"/> pixels and <paramref name="components"/>
    /// components.</summary>
    public J2kLimits(long pixels, int components)
    {
        _left = pixels * (3L * components + 4) + (1 << 20);
    }

    public void Spend(long amount)
    {
        _left -= amount;
        if (_left < 0)
        {
            throw new InvalidDataException("A JPEG 2000 codestream asks for more work than its image is worth.");
        }
    }
}
