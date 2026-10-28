using System.Numerics;

namespace Adamantium.Mathematics.Typed;

public static partial class MathHelper
{
    /// <summary>Angles between directions, in radians, from the arctangent of the sine and cosine together - exact near
    /// 0 and π, where the arccosine of the cosine alone loses half the digits. Degrees convert with
    /// <c>T.DegreesToRadians</c> and <c>T.RadiansToDegrees</c>.</summary>
    public static class Angle
    {
        /// <summary>The angle from <paramref name="from"/> to <paramref name="to"/>, in (-π, π]: positive the way X
        /// turns to Y.</summary>
        public static T Between<T>(Vector2<T> from, Vector2<T> to) where T : unmanaged, IFloatingPointIeee754<T>
        {
            var angle = T.Atan2(Vector2<T>.Determinant(from, to), Vector2<T>.Dot(from, to));
            return angle == -T.Pi ? T.Pi : angle;
        }

        /// <summary>The angle from <paramref name="from"/> to <paramref name="to"/> the way X turns to Y, in [0, 2π):
        /// a quarter turn back is 3π/2.</summary>
        public static T Turn<T>(Vector2<T> from, Vector2<T> to) where T : unmanaged, IFloatingPointIeee754<T>
        {
            var angle = Between(from, to);
            if (angle >= T.Zero)
            {
                return angle;
            }

            angle += T.Tau;
            return angle < T.Tau || T.IsNaN(angle) ? angle : T.Zero;
        }

        /// <summary>The angle between <paramref name="first"/> and <paramref name="second"/>, in [0, π].</summary>
        public static T Between<T>(Vector3<T> first, Vector3<T> second) where T : unmanaged, IFloatingPointIeee754<T> =>
            T.Atan2(Vector3<T>.Cross(first, second).Length(), Vector3<T>.Dot(first, second));

        /// <summary>The angle from <paramref name="from"/> to <paramref name="to"/>, both at right angles to
        /// <paramref name="normal"/>, around it, in (-π, π]: positive counterclockwise as seen from where
        /// <paramref name="normal"/> points.</summary>
        public static T Between<T>(Vector3<T> from, Vector3<T> to, Vector3<T> normal) where T : unmanaged, IFloatingPointIeee754<T> =>
            T.Atan2(Vector3<T>.Dot(Vector3<T>.Cross(from, to), Vector3<T>.Normalize(normal)), Vector3<T>.Dot(from, to));
    }
}
