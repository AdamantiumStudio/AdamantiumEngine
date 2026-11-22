using System;

namespace Adamantium.Mathematics;

public static partial class MathHelper
{
    /// <summary>How the sweep of an arc is read, in degrees - the one rule an ellipse's mesh and a drawn arc both follow.
    /// Angles between directions, in radians, are <see cref="Angle"/>'s.</summary>
    public static class Arc
    {
        /// <summary>A whole turn, in degrees.</summary>
        public const double TurnInDegrees = 360;

        /// <summary>The degrees past whole turns a sweep may go and still be whole turns: rounding, not an arc anybody
        /// meant - a ring summed from its shares in double precision comes out a little past 360. A single-precision
        /// sum is not covered: its step at 360 is 3e-5.</summary>
        public const double SweepRounding = 1e-5;

        /// <summary>What a sweep of <paramref name="degrees"/> covers, in [-360, 360], its sign its way round: within a
        /// turn as it is; past one what is left over whole turns, 485 as 125; whole turns - and up to
        /// <see cref="SweepRounding"/> past them, and a sweep with no end - a turn. No number is no sweep.</summary>
        public static double ReduceSweep(double degrees)
        {
            if (double.IsNaN(degrees)) return 0;
            if (Math.Abs(degrees) <= TurnInDegrees) return degrees;
            if (double.IsInfinity(degrees)) return Math.CopySign(TurnInDegrees, degrees);
            var left = degrees % TurnInDegrees;
            return Math.Abs(left) <= SweepRounding ? Math.CopySign(TurnInDegrees, degrees) : left;
        }
    }
}
