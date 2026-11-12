using System;

namespace Adamantium.Mathematics;

public static partial class MathHelper
{
    /// <summary>The one step geometry coordinates are rounded to, so points equal up to floating-point noise compare
    /// equal: meshes, contours, outlines and the points they are cut at.</summary>
    public static class Coordinates
    {
        /// <summary>The decimals a coordinate keeps; a coordinate halfway between two steps rounds away from zero.</summary>
        public const int Decimals = 4;

        /// <summary>The step a coordinate is rounded to, ten to the minus <see cref="Decimals"/>.</summary>
        public const double Step = 1e-4;

        /// <summary><paramref name="value"/> rounded to <see cref="Decimals"/>.</summary>
        public static double Round(double value) => Math.Round(value, Decimals, MidpointRounding.AwayFromZero);

        /// <summary><paramref name="point"/> rounded to <see cref="Decimals"/>.</summary>
        public static Vector2<double> Round(Vector2<double> point) => Vector2<double>.Round(point, Decimals);

        /// <summary><paramref name="point"/> rounded to <see cref="Decimals"/>.</summary>
        public static Vector3<double> Round(Vector3<double> point) => Vector3<double>.Round(point, Decimals);
    }
}
