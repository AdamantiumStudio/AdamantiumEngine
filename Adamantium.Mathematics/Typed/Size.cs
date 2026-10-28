using System;
using System.Numerics;

namespace Adamantium.Mathematics.Typed;

/// <summary>Makes <see cref="Size{T}"/>s and gives those of fractional numbers an infinite size and those of doubles
/// room taken away or added by a <see cref="Thickness"/>.</summary>
public static class Size
{
    /// <summary>A size of <paramref name="width"/> by <paramref name="height"/>.</summary>
    public static Size<T> Create<T>(T width, T height) where T : unmanaged, INumber<T> => new(width, height);

    /// <summary>A square of <paramref name="value"/> each way.</summary>
    public static Size<T> Create<T>(T value) where T : unmanaged, INumber<T> => new(value);

    extension<T>(Size<T>) where T : unmanaged, IFloatingPointIeee754<T>
    {
        /// <summary>Infinite each way: no limit, as a layout measures against.</summary>
        public static Size<T> Infinity => new(T.PositiveInfinity, T.PositiveInfinity);
    }

    extension(Size<double> size)
    {
        /// <summary>The size less <paramref name="thickness"/> on each side, at least zero each way.</summary>
        public Size<double> Deflate(Thickness thickness) =>
            new(Math.Max(0, size.Width - thickness.Left - thickness.Right), Math.Max(0, size.Height - thickness.Top - thickness.Bottom));

        /// <summary>The size with <paramref name="thickness"/> added on each side, at least zero each way.</summary>
        public Size<double> Inflate(Thickness thickness) =>
            new(Math.Max(0, size.Width + thickness.Left + thickness.Right), Math.Max(0, size.Height + thickness.Top + thickness.Bottom));
    }
}
