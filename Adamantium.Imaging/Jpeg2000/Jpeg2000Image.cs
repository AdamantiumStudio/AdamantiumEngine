namespace Adamantium.Imaging.Jpeg2000;

/// <summary>A decoded JPEG 2000 image: its channels at 8 bits, interleaved, row after row from the top, the color
/// channels first in their order and an opacity channel, when there is one, last.</summary>
public sealed class Jpeg2000Image
{
    internal Jpeg2000Image(int width, int height, int colorChannels, bool hasAlpha, byte[] samples,
        Jpeg2000ColorSpace colorSpace, byte[] iccProfile, bool alphaIsPremultiplied, int[] channelBits)
    {
        ChannelBits = channelBits;
        Width = width;
        Height = height;
        ColorChannels = colorChannels;
        HasAlpha = hasAlpha;
        Samples = samples;
        ColorSpace = colorSpace;
        IccProfile = iccProfile;
        AlphaIsPremultiplied = alphaIsPremultiplied;
    }

    public int Width { get; }

    public int Height { get; }

    public int ColorChannels { get; }

    public bool HasAlpha { get; }

    /// <summary>The channels of a pixel: the color channels and the opacity.</summary>
    public int Channels => ColorChannels + (HasAlpha ? 1 : 0);

    public byte[] Samples { get; }

    public Jpeg2000ColorSpace ColorSpace { get; }

    /// <summary>The ICC profile of an <see cref="Jpeg2000ColorSpace.Icc"/> image, or null.</summary>
    public byte[] IccProfile { get; }

    /// <summary>The depth each channel had before it was scaled to 8 bits, in the order of <see cref="Samples"/>.
    /// </summary>
    public int[] ChannelBits { get; }

    /// <summary>Whether the color channels are already multiplied by the opacity.</summary>
    public bool AlphaIsPremultiplied { get; }
}
