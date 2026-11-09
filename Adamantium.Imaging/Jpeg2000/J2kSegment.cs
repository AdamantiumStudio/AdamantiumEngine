using System.IO;

namespace Adamantium.Imaging.Jpeg2000;

/// <summary>A codeword segment of a code-block: coding passes coded together, with their bytes gathered from every
/// layer that brought some of them.</summary>
internal sealed class J2kSegment
{
    private readonly MemoryStream _data = new();

    public J2kSegment(int firstPass)
    {
        FirstPass = firstPass;
    }

    public int FirstPass { get; }

    public int Passes { get; set; }

    /// <summary>The passes of the layers being decoded, the first ones of the segment.</summary>
    public int KeptPasses { get; set; }

    public int Length => (int)_data.Length;

    public void Add(byte[] data, int start, int length) => _data.Write(data, start, length);

    public byte[] Data() => _data.ToArray();
}
