using System.IO;

namespace Adamantium.Imaging.Jbig2;

/// <summary>What one JBIG2 stream may make the decoder do: hostile data is cut off here, not left to run.</summary>
internal sealed class Jbig2Limits
{
    /// <summary>The longest symbol number - 2^20 symbols in one region.</summary>
    public const int MaxSymbolCodeLength = 20;

    private long _work;

    public Jbig2Limits(long maxPixels)
    {
        MaxPixels = maxPixels;
        _work = maxPixels * 4 + (1 << 20);
    }

    /// <summary>The most pixels any one bitmap may have.</summary>
    public long MaxPixels { get; }

    /// <summary>Takes <paramref name="amount"/> from the stream's budget of pixels decoded and symbols placed.
    /// </summary>
    public void Spend(long amount)
    {
        _work -= amount;
        if (_work < 0)
        {
            throw new InvalidDataException("A JBIG2 stream asks for more work than its page is worth.");
        }
    }

    /// <summary>A new bitmap within the bound on its size.</summary>
    public Jbig2Bitmap Bitmap(long width, long height)
    {
        if (width < 0 || height < 0 || width * height > MaxPixels)
        {
            throw new InvalidDataException("A JBIG2 bitmap of " + width + " by " + height + " is too big.");
        }

        return new Jbig2Bitmap((int)width, (int)height);
    }
}
