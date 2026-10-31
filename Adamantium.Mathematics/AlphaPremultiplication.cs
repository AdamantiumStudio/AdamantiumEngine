using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Adamantium.Mathematics;

/// <summary>Rows of colors of four bytes with alpha last, as <see cref="Color"/> and <see cref="ColorBGRA"/> are,
/// premultiplied by their alpha with <see cref="Color.ToPremultiplied"/>: the same for both, as red, green and blue are
/// scaled alike.</summary>
internal static class AlphaPremultiplication
{
    /// <summary>Writes <paramref name="straight"/>, read as numbers with alpha in the top byte, premultiplied into
    /// <paramref name="premultiplied"/>, which is as long. Where the processor has vectors, as wide as it has them,
    /// blocks wholly clear or wholly opaque are copied as they are, so a mostly empty picture costs little.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static void Convert(ReadOnlySpan<uint> straight, Span<uint> premultiplied)
    {
        if (premultiplied.Length < straight.Length)
        {
            throw new ArgumentException("The destination is shorter than the source.", nameof(premultiplied));
        }

        var covered = Vector.IsHardwareAccelerated ? ConvertBlocks(straight, premultiplied) : 0;
        ConvertEach(straight.Slice(covered), premultiplied.Slice(covered));
    }

    private static int ConvertBlocks(ReadOnlySpan<uint> straight, Span<uint> premultiplied)
    {
        var alphaMask = new Vector<uint>(0xFF000000u);
        var blocks = straight.Length / Vector<uint>.Count * Vector<uint>.Count;
        for (var x = 0; x < blocks; x += Vector<uint>.Count)
        {
            var block = new Vector<uint>(straight.Slice(x, Vector<uint>.Count));
            var alpha = block & alphaMask;
            if (alpha == Vector<uint>.Zero)
            {
                Vector<uint>.Zero.CopyTo(premultiplied.Slice(x));
            }
            else if (alpha == alphaMask)
            {
                block.CopyTo(premultiplied.Slice(x));
            }
            else
            {
                ConvertEach(straight.Slice(x, Vector<uint>.Count), premultiplied.Slice(x));
            }
        }

        return blocks;
    }

    private static void ConvertEach(ReadOnlySpan<uint> straight, Span<uint> premultiplied)
    {
        for (var i = 0; i < straight.Length; i++)
        {
            var color = straight[i];
            premultiplied[i] = (color >> 24) switch
            {
                0 => 0,
                255 => color,
                _ => Color.FromRgba(color).ToPremultiplied().ToRgba()
            };
        }
    }
}
