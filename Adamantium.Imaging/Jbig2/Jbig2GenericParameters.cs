namespace Adamantium.Imaging.Jbig2;

/// <summary>The parameters of generic region decoding (ITU-T T.88, table 2).</summary>
internal sealed class Jbig2GenericParameters
{
    public bool Mmr { get; init; }

    public int Width { get; init; }

    public int Height { get; init; }

    public int Template { get; init; }

    public bool TypicalPrediction { get; init; }

    /// <summary>The adaptive template pixels, x and y of each in turn.</summary>
    public int[] At { get; init; }

    /// <summary>The pixels left 0 and not decoded, or null.</summary>
    public Jbig2Bitmap Skip { get; init; }

    /// <summary>The adaptive pixels a template has by default (ITU-T T.88, 6.2.5.4 and 6.7.5).</summary>
    public static int[] DefaultAt(int template) => template == 0
        ? [3, -1, -3, -1, 2, -2, -2, -2]
        : template == 1 ? [3, -1] : [2, -1];
}
