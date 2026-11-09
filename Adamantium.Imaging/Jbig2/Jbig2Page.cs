using System;
using System.IO;

namespace Adamantium.Imaging.Jbig2;

/// <summary>The page a JBIG2 stream draws its regions on (ITU-T T.88, 7.4.8). A page of unknown height grows as its
/// stripes and regions reach down.</summary>
internal sealed class Jbig2Page
{
    private const uint UnknownHeight = 0xFFFFFFFF;

    private readonly Jbig2Limits _limits;
    private readonly bool _growing;
    private Jbig2Bitmap _bitmap;
    private int _height;

    private Jbig2Page(int width, int height, int defaultPixel, bool growing, Jbig2Limits limits)
    {
        _limits = limits;
        _growing = growing;
        _height = height;
        DefaultPixel = defaultPixel;
        _bitmap = New(width, height);
    }

    public int DefaultPixel { get; }

    /// <summary>The page as drawn so far.</summary>
    public Jbig2Bitmap Bitmap => _bitmap.Height == _height ? _bitmap : _bitmap.Crop(0, 0, _bitmap.Width, _height);

    /// <summary>Reads a page information segment.</summary>
    public static Jbig2Page Read(BigEndianReader bytes, Jbig2Limits limits)
    {
        var width = bytes.ReadUInt32();
        var height = bytes.ReadUInt32();
        bytes.Skip(8);
        var flags = bytes.ReadByte();
        if (width > int.MaxValue)
        {
            throw new InvalidDataException("A JBIG2 page is too wide.");
        }

        var growing = height == UnknownHeight;
        return new Jbig2Page((int)width, growing ? 0 : (int)Math.Min(height, int.MaxValue), (flags >> 2) & 1,
            growing, limits);
    }

    /// <summary>The part of the page under a region, for a refinement with nothing else to refine.</summary>
    public Jbig2Bitmap Under(int x, int y, int width, int height)
    {
        var part = _limits.Bitmap(width, height);
        _bitmap.CopyTo(part, x, y);
        return part;
    }

    /// <summary>Draws a region's bitmap at its place.</summary>
    public void Draw(Jbig2Bitmap region, int x, int y, Jbig2Combination combination)
    {
        Reach((long)y + region.Height);
        _bitmap.Combine(region, x, y, combination);
    }

    /// <summary>The end of a stripe: a page of unknown height is at least this tall.</summary>
    public void EndStripe(uint lastRow) => Reach(lastRow + 1L);

    private void Reach(long height)
    {
        if (!_growing || height <= _height)
        {
            return;
        }

        if (height > _bitmap.Height)
        {
            var most = _bitmap.Width == 0 ? height : _limits.MaxPixels / _bitmap.Width;
            var grown = New(_bitmap.Width, (int)Math.Min(Math.Max(height, 2L * _bitmap.Height), Math.Max(most, height)));
            Array.Copy(_bitmap.Pixels, grown.Pixels, _bitmap.Pixels.Length);
            _bitmap = grown;
        }

        _height = (int)height;
    }

    private Jbig2Bitmap New(int width, long height)
    {
        var bitmap = _limits.Bitmap(width, height);
        if (DefaultPixel != 0)
        {
            bitmap.Fill(1);
        }

        return bitmap;
    }
}
