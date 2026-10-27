using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Enumerable = System.Linq.Enumerable;

namespace Adamantium.Mathematics
{
    public static class MathHelper
    {
        /// <summary>
        /// The value for which all absolute numbers smaller than are considered equal to zero.
        /// </summary>
        public const float ZeroToleranceF = 1e-6f;

        /// <summary>
        /// The value for which all absolute numbers smaller than are considered equal to zero.
        /// </summary>
        public static double ZeroToleranceD = 1e-9;

        /// <summary>
        /// A value specifying the approximation of π which is 180 degrees.
        /// </summary>
        public const float Pi = (float)Math.PI;

        /// <summary>
        /// A value specifying the approximation of 2π which is 360 degrees.
        /// </summary>
        public const float TwoPi = (float)(2 * Math.PI);

        /// <summary>
        /// A value specifying the approximation of π/2 which is 90 degrees.
        /// </summary>
        public const float PiOverTwo = (float)(Math.PI / 2);

        /// <summary>
        /// A value specifying the approximation of π/4 which is 45 degrees.
        /// </summary>
        public const float PiOverFour = (float)(Math.PI / 4);

        public const float FloatNormal = (1 << 23) * Single.Epsilon;

        public const double DoubleNormal = (1L << 52) * Double.Epsilon;

        /// <summary>
        /// Checks if a and b are almost equals, taking into account the magnitude of floating point numbers (unlike <see cref="WithinEpsilon"/> method). See Remarks.
        /// See remarks.
        /// </summary>
        /// <param name="a">The left value to compare.</param>
        /// <param name="b">The right value to compare.</param>
        /// <returns><c>true</c> if a almost equal to b, <c>false</c> otherwise</returns>
        /// <remarks>
        /// The code is using the technique described by Bruce Dawson in 
        /// <a href="http://randomascii.wordpress.com/2012/02/25/comparing-floating-point-numbers-2012-edition/">Comparing Floating point numbers 2012 edition</a>. 
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool NearEqual(float a, float b)
        {
            return WithinEpsilon(a, b, ZeroToleranceF);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool NearEqual(double a, double b)
        {
            // Check if the numbers are really close -- needed
            // when comparing numbers near zero.
            //if (IsZero(a - b))
            //    return true;

            //// Original from Bruce Dawson: http://randomascii.wordpress.com/2012/02/25/comparing-floating-point-numbers-2012-edition/
            //Int64 aInt = *(Int64*)&a;
            //Int64 bInt = *(Int64*)&b;

            //// Different signs means they do not match.
            //if ((aInt < 0) != (bInt < 0))
            //    return false;

            //// Find the difference in ULPs.
            //Int64 ulp = Math.Abs(aInt - bInt);

            //// Choose of maxUlp = 4
            //// according to http://code.google.com/p/googletest/source/browse/trunk/include/gtest/internal/gtest-internal.h
            //const int maxUlp = 4;
            //return ulp <= maxUlp;
            return WithinEpsilon(a, b, ZeroToleranceD);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool NearEqual(Vector3 a, Vector3 b)
        {
            return WithinEpsilon(a, b, ZeroToleranceD);
        }

        /// <summary>
        /// Checks if a - b are almost equals within a float epsilon.
        /// </summary>
        /// <param name="a">The left value to compare.</param>
        /// <param name="b">The right value to compare.</param>
        /// <param name="epsilon">Epsilon value</param>
        /// <returns><c>true</c> if a almost equal to b within a float epsilon, <c>false</c> otherwise</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool WithinEpsilon(float a, float b, float epsilon)
        {
            float absA = Math.Abs(a);
            float absB = Math.Abs(b);
            float diff = Math.Abs(a - b);

            if (a == b)
            { 
                // shortcut, handles infinities
                return true;
            }

            if (a == 0 || b == 0 || (absA + absB < FloatNormal))
            {
                // a or b is zero or both are extremely close to it
                // relative error is less meaningful here
                return diff <= epsilon; //*FloatNormal;
            }

            // use relative error
            return diff / Math.Min((absA + absB), Single.MaxValue) < epsilon;
        }

        /// <summary>
        /// Checks if a - b are almost equals within a float epsilon.
        /// </summary>
        /// <param name="a">The left value to compare.</param>
        /// <param name="b">The right value to compare.</param>
        /// <param name="epsilon">Epsilon value</param>
        /// <returns><c>true</c> if a almost equal to b within a float epsilon, <c>false</c> otherwise</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining|MethodImplOptions.AggressiveOptimization)]
        public static bool WithinEpsilon(double a, double b, double epsilon)
        {
            double absA = Math.Abs(a);
            double absB = Math.Abs(b);
            double diff = Math.Abs(a - b);

            if (a == b)
            { 
                // shortcut, handles infinities
                return true;
            }

            if (a == 0 || b == 0 || (absA + absB < DoubleNormal))
            {
                // a or b is zero or both are extremely close to it
                // relative error is less meaningful here
                return diff <= epsilon;
            }

            // use relative error
            return diff / Math.Min((absA + absB), Double.MaxValue) < epsilon;
        }

        public static bool WithinEpsilon(Vector3F a, Vector3F b, float epsilon)
        {
            return WithinEpsilon(a.X, b.X, epsilon) && WithinEpsilon(a.Y, b.Y, epsilon) && WithinEpsilon(a.Z, b.Z, epsilon);
        }

        public static bool WithinEpsilon(Vector3 a, Vector3 b, double epsilon)
        {
            return WithinEpsilon(a.X, b.X, epsilon) && WithinEpsilon(a.Y, b.Y, epsilon) && WithinEpsilon(a.Z, b.Z, epsilon);
        }
        
        public static bool WithinEpsilon(Vector2 a, Vector2 b, double epsilon)
        {
            return WithinEpsilon(a.X, b.X, epsilon) && WithinEpsilon(a.Y, b.Y, epsilon);
        }

        /// <summary>
        /// Interpolates between two values using a linear function by a given amount.
        /// </summary>
        /// <remarks>
        /// See http://www.encyclopediaofmath.org/index.php/Linear_interpolation and
        /// http://fgiesen.wordpress.com/2012/08/15/linear-interpolation-past-present-and-future/
        /// </remarks>
        /// <param name="from">Value to interpolate from.</param>
        /// <param name="to">Value to interpolate to.</param>
        /// <param name="amount">Interpolation amount.</param>
        /// <returns>The result of linear interpolation of values based on the amount.</returns>
        public static double Lerp(double from, double to, double amount)
        {
            return (1 - amount) * @from + amount * to;
        }

        /// <summary>
        /// Interpolates between two values using a linear function by a given amount.
        /// </summary>
        /// <remarks>
        /// See http://www.encyclopediaofmath.org/index.php/Linear_interpolation and
        /// http://fgiesen.wordpress.com/2012/08/15/linear-interpolation-past-present-and-future/
        /// </remarks>
        /// <param name="from">Value to interpolate from.</param>
        /// <param name="to">Value to interpolate to.</param>
        /// <param name="amount">Interpolation amount.</param>
        /// <returns>The result of linear interpolation of values based on the amount.</returns>
        public static float Lerp(float from, float to, float amount)
        {
            return (1 - amount) * @from + amount * to;
        }

        /// <summary>
        /// Interpolates between two values using a linear function by a given amount.
        /// </summary>
        /// <remarks>
        /// See http://www.encyclopediaofmath.org/index.php/Linear_interpolation and
        /// http://fgiesen.wordpress.com/2012/08/15/linear-interpolation-past-present-and-future/
        /// </remarks>
        /// <param name="from">Value to interpolate from.</param>
        /// <param name="to">Value to interpolate to.</param>
        /// <param name="amount">Interpolation amount.</param>
        /// <returns>The result of linear interpolation of values based on the amount.</returns>
        public static byte Lerp(byte from, byte to, float amount)
        {
            return (byte)Lerp((float)@from, (float)to, amount);
        }

        /// <summary>
        /// Performs smooth (cubic Hermite) interpolation between 0 and 1.
        /// </summary>
        /// <remarks>
        /// See https://en.wikipedia.org/wiki/Smoothstep
        /// </remarks>
        /// <param name="amount">Value between 0 and 1 indicating interpolation amount.</param>
        public static float SmoothStep(float amount)
        {
            if (amount <= 0)
                return 0;
            else if (amount >= 1)
                return 1;
            else
                return amount * amount * (3 - (2 * amount));
        }

        /// <summary>
        /// Performs smooth (cubic Hermite) interpolation between 0 and 1.
        /// </summary>
        /// <remarks>
        /// See https://en.wikipedia.org/wiki/Smoothstep
        /// </remarks>
        /// <param name="amount">Value between 0 and 1 indicating interpolation amount.</param>
        public static double SmoothStep(double amount)
        {
            if (amount <= 0)
                return 0;
            else if (amount >= 1)
                return 1;
            else
                return amount * amount * (3 - (2 * amount));
        }

        /// <summary>
        /// Performs a smooth(er) interpolation between 0 and 1 with 1st and 2nd order derivatives of zero at endpoints.
        /// </summary>
        /// <remarks>
        /// See https://en.wikipedia.org/wiki/Smoothstep
        /// </remarks>
        /// <param name="amount">Value between 0 and 1 indicating interpolation amount.</param>
        public static float SmootherStep(float amount)
        {
            if (amount <= 0)
                return 0;
            else if (amount >= 1)
                return 1;
            else
                return amount * amount * amount * (amount * ((amount * 6) - 15) + 10);
        }

        /// <summary>
        /// Calculates the modulo of the specified value.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <param name="modulo">The modulo.</param>
        /// <returns>The result of the modulo applied to value</returns>
        public static float Mod(float value, float modulo)
        {
            if (modulo == 0.0f)
            {
                return value;
            }

            return value % modulo;
        }


        /// <summary>
        /// Calculates the modulo 2*PI of the specified value.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <returns>The result of the modulo applied to value</returns>
        public static float Mod2PI(float value)
        {
            return Mod(value, TwoPi);
        }


        /// <summary>
        /// Determines whether the specified value is close to zero (0.0f).
        /// </summary>
        /// <param name="a">The floating value.</param>
        /// <returns><c>true</c> if the specified value is close to zero (0.0f); otherwise, <c>false</c>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsZero(float a)
        {
            return Math.Abs(a) < ZeroToleranceF;
        }

        /// <summary>
        /// Determines whether the specified value is close to zero (0.0f).
        /// </summary>
        /// <param name="a">The floating value.</param>
        /// <returns><c>true</c> if the specified value is close to zero (0.0f); otherwise, <c>false</c>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsZero(double a)
        {
            return Math.Abs(a) < ZeroToleranceD;
        }

        /// <summary>
        /// Determines whether the specified value is close to one (1.0f).
        /// </summary>
        /// <param name="a">The floating value.</param>
        /// <returns><c>true</c> if the specified value is close to one (1.0f); otherwise, <c>false</c>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsOne(float a)
        {
            return IsZero(a - 1.0f);
        }

        /// <summary>
        /// Determines whether the specified value is close to one (1.0f).
        /// </summary>
        /// <param name="a">The floating value.</param>
        /// <returns><c>true</c> if the specified value is close to one (1.0f); otherwise, <c>false</c>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsOne(double a)
        {
            return IsZero(a - 1.0f);
        }

        /// <summary>
        /// Converts degrees to radians.
        /// </summary>
        /// <param name="degree">The value to convert.</param>
        /// <returns>The converted value.</returns>
        public static float DegreesToRadians(float degree)
        {
            return degree * (Pi / 180.0f);
        }
        
        public static float DegreesToRadians(double degree)
        {
            return (float)(degree * (Pi / 180.0f));
        }

        /// <summary>
        /// Converts radians to degrees.
        /// </summary>
        /// <param name="radian">The value to convert.</param>
        /// <returns>The converted value.</returns>
        public static float RadiansToDegrees(float radian)
        {
            return radian * (180.0f / Pi);
        }

        /// <summary>
        /// Clamps the specified value.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <param name="min">The min.</param>
        /// <param name="max">The max.</param>
        /// <returns>The result of clamping a value between min and max</returns>
        public static float Clamp(float value, float min, float max)
        {
            return value < min ? min : value > max ? max : value;
        }

        /// <summary>
        /// Clamps the specified value.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <param name="min">The min.</param>
        /// <param name="max">The max.</param>
        /// <returns>The result of clamping a value between min and max</returns>
        public static double Clamp(double value, double min, double max)
        {
            return value < min ? min : value > max ? max : value;
        }

        /// <summary>
        /// Wraps the specified value into a range [min, max]
        /// </summary>
        /// <param name="value">The value to wrap.</param>
        /// <param name="min">The min.</param>
        /// <param name="max">The max.</param>
        /// <returns>Result of the wrapping.</returns>
        public static int Wrap(int value, int min, int max)
        {
            if (min > max)
            {
                min = max;
            }

            if (max < min)
            {
                max = min;
            }

            // Code from http://stackoverflow.com/a/707426/1356325
            int rangeSize = max - min + 1;

            if (value < min)
                value += rangeSize * ((min - value) / rangeSize + 1);

            return min + (value - min) % rangeSize;
        }

        /// <summary>
        /// Wraps the specified value into a range [min, max[
        /// </summary>
        /// <param name="value">The value.</param>
        /// <param name="min">The min.</param>
        /// <param name="max">The max.</param>
        /// <returns>Result of the wrapping.</returns>
        public static float Wrap(float value, float min, float max)
        {
            if (min > max)
            {
                min = max;
            }

            if (max < min)
            {
                max = min;
            }

            if (NearEqual(min, max)) return min;

            double mind = min;
            double maxd = max;
            double valued = value;

            var rangeSize = maxd - mind;
            return (float)(mind + (valued - mind) - (rangeSize * Math.Floor((valued - mind) / rangeSize)));
        }

        /// <summary>
        /// Wraps the specified value into a range [min, max[
        /// </summary>
        /// <param name="value">The value.</param>
        /// <param name="min">The min.</param>
        /// <param name="max">The max.</param>
        /// <returns>Result of the wrapping.</returns>
        public static double Wrap(double value, double min, double max)
        {
            if (min > max)
            {
                min = max;
            }

            if (max < min)
            {
                max = min;
            }

            if (NearEqual(min, max)) return min;

            double mind = min;
            double maxd = max;
            double valued = value;

            var rangeSize = maxd - mind;
            return (mind + (valued - mind) - (rangeSize * Math.Floor((valued - mind) / rangeSize)));
        }

        public static float AngleBetween2D(Vector3F vector0, Vector3F vector1)
        {
            var vector2 = vector1 - vector0;
            var angle = (float)Math.Atan2(vector2.Y, vector2.X);
            return angle;
        }

        public static float Cross2D(Vector3F v1, Vector3F v2)
        {
            return v1.X * v2.Y - v1.Y * v2.X;
        }
        
        public static double Cross2D(Vector2 v1, Vector2 v2)
        {
            return v1.X * v2.Y - v2.X * v1.Y;
        }

        public static float AngleBetween(Vector3F vector0, Vector3F vector1, Vector3F normal)
        {
            var dot = Vector3F.Dot(vector0, vector1);
            var det = Vector3F.Dot(Vector3F.Cross(vector1, vector0), normal);
            var angle = (float)Math.Atan2(det, dot);
            return angle;
        }
        
        public static float AngleBetween(Vector3 vector0, Vector3 vector1, Vector3 normal)
        {
            var dot = Vector3.Dot(vector0, vector1);
            var det = Vector3.Dot(Vector3.Cross(vector1, vector0), normal);
            var angle = (float)Math.Atan2(det, dot);
            return angle;
        }

        public static float AngleBetween(Vector3F vector0, Vector3F vector1, bool is360Degrees = true)
        {
            float angle = 0;
            if (is360Degrees)
            {
                var dot = Vector3F.Dot(vector0, vector1);
                // var determinant = Determinant(vector0, vector1);
                // angle = (float)Math.Atan2(determinant, dot);
                //atan2(v2.y,v2.x) - atan2(v1.y,v1.x)
                angle = (float)Math.Atan2(vector1.Y, vector1.X) - (float)Math.Atan2(vector0.Y, vector0.X);
                angle = RadiansToDegrees(angle);
                if (angle < 0)
                {
                    angle = 360 + angle;
                }
            }
            else
            {
                var dot = Vector3F.Dot(vector0, vector1);
                angle = RadiansToDegrees((float)Math.Acos(dot));
            }

            return angle;
        }

        public static Double AngleBetween(Vector2 vector0, Vector2 vector1, Vector2 vector3, Vector2 vector4)
        {
            // find vectors
            Vector2 v1 = vector1 - vector0;
            Vector2 v2 = vector4 - vector3;

            var len1 = v1.Length();
            var len2 = v2.Length();

            var dot = Vector2.Dot(v1, v2);
            double cos = dot / len1 / len2;

            var cross = Cross2D(v1, v2);
            double sin = cross / len1 / len2;
            
            // Find angle
            double angle = Math.Acos(cos);
            if (sin < 0)
            {
                angle = -angle;
            }

            return RadiansToDegrees(angle);
        }
        

        public static float AngleBetween(LineSegment2D s1, LineSegment2D s2, bool is360Degrees = true)
        {
            float angle = 0;
            if (is360Degrees)
            {
                var dot = Vector2.Dot(s1.DirectionNormalized, s2.DirectionNormalized);
                var determinant = Determinant(s1.DirectionNormalized, s2.DirectionNormalized);
                angle = (float)Math.Atan2(determinant, dot);
                angle = RadiansToDegrees(angle);
                if (angle < 0)
                {
                    angle = 360 + angle;
                }
            }
            else
            {
                var dot = Vector2.Dot(s1.DirectionNormalized, s2.DirectionNormalized);
                angle = (float)Math.Acos(dot);
            }

            return angle;
        }

        public static float Determinant(Vector3F v1, Vector3F v2)
        {
            return v1.X * v2.Y - v1.Y * v2.X;
        }

        public static double Determinant(Vector3 v1, Vector3 v2)
        {
            return v1.X * v2.Y - v1.Y * v2.X;
        }
        
        public static double Determinant(Vector2 v1, Vector2 v2)
        {
            return v1.X * v2.Y - v1.Y * v2.X;
        }

        public static bool IsClockwise(Vector2F point0, Vector2F point1, Vector2F point2)
        {
            var edge0 = point1 - point0;
            var edge1 = point2 - point0;

            var det = Vector2F.Determinant(edge0, edge1);

            return det > 0;

        }

        public static bool IsClockwise(Vector2 point0, Vector2 point1, Vector2 point2, Vector3F viewVector)
        {
            var edge0 = point1 - point0;
            var edge1 = point2 - point0;

            var n = Vector3F.Cross((Vector3F)edge0, (Vector3F)edge1);
            n.Normalize();
            var angle = Vector3F.Dot(n, viewVector);

            return angle > 0;

        }

        /// <summary>
        /// True if a simple polygon (ordered vertices, closed) is convex. Uses a consistent turn-direction
        /// test PLUS a single-revolution check (total turning ≈ ±2π), so star polygons such as a pentagram —
        /// which turn consistently but self-intersect (total turning ±4π) — are correctly rejected.
        /// </summary>
        public static bool IsConvex(IReadOnlyList<Vector2> points)
        {
            int n = points?.Count ?? 0;
            if (n < 3) return false;
            if (n == 3) return true;

            int sign = 0;
            double totalTurn = 0;
            for (int i = 0; i < n; i++)
            {
                var a = points[i];
                var b = points[(i + 1) % n];
                var c = points[(i + 2) % n];
                var e0 = b - a;
                var e1 = c - b;
                double cross = e0.X * e1.Y - e0.Y * e1.X;
                double dot = e0.X * e1.X + e0.Y * e1.Y;
                if (cross == 0 && dot < 0) return false;            // 180° reversal (spike) -> not convex
                int s = cross > 0 ? 1 : (cross < 0 ? -1 : 0);
                if (s != 0)
                {
                    if (sign == 0) sign = s;
                    else if (s != sign) return false;               // turn direction changed -> concave
                }
                totalTurn += Math.Atan2(cross, dot);
            }
            return Math.Abs(Math.Abs(totalTurn) - 2 * Math.PI) < 1e-3;  // exactly one revolution
        }

        public static QuaternionF GetRotationFromMatrix(Matrix4x4F matrix)
        {
            matrix.Decompose(out var scale, out var orientation, out var pos);
            return orientation;
        }

        /// <summary>
        /// Calculate Quaternion which will define rotation from one point to another face to face
        /// </summary>
        /// <param name="objectPosition">objectPosition is your object's position</param>
        /// <param name="targetPosition">objectToFacePosition is the position of the object to face</param>
        /// <param name="upVector">upVector is the nominal "up" vector (typically Vector3.Y)</param>
        /// <remarks>Note: this does not work when objectPosition is straight below or straight above objectToFacePosition</remarks>
        /// <returns></returns>
        public static QuaternionF RotateToFace(ref Vector3F targetPosition, ref Vector3F objectPosition, ref Vector3F upVector)
        {
            Vector3F D = (objectPosition - targetPosition);
            Vector3F right = Vector3F.Normalize(Vector3F.Cross(upVector, D));
            Vector3F backward = Vector3F.Normalize(Vector3F.Cross(right, upVector));
            Vector3F up = Vector3F.Cross(backward, right);
            Matrix4x4F rotationMatrix = new Matrix4x4F(right.X, right.Y, right.Z, 0, up.X, up.Y, up.Z, 0, backward.X, backward.Y, backward.Z, 0, 0, 0, 0, 1);
            QuaternionF orientation;
            QuaternionF.RotationMatrix(ref rotationMatrix, out orientation);
            return orientation;
        }

        public static QuaternionF RelativeRotation(QuaternionF a, QuaternionF b)
        {
            return QuaternionF.Invert(a) * b;
        }

        // --- DIVER MATH ---
        public static double RadiansToDegrees(double radians)
        {
            return ((radians * 180.0) / Math.PI);
        }
        
        public static double Dot(Vector2 v1, Vector2 v2)
        {
            return (v1.X * v2.X + v1.Y * v2.Y);
        }
        
        public static Vector2 LineSegmentToVector(Vector2 start, Vector2 end)
        {
            return new Vector2(end.X - start.X, end.Y - start.Y);
        }

        // find angle in degrees between to segments
        public static double DetermineAngleInDegrees(Vector2 v1Start, Vector2 v1End, Vector2 v2Start, Vector2 v2End)
        {
            Vector2 v1 = LineSegmentToVector(v1Start, v1End);
            Vector2 v2 = LineSegmentToVector(v2Start, v2End);

            double dot = Dot(v1, v2);

            double v1Length = v1.Length();
            double v2Length = v2.Length();

            double cos = dot / (v1Length * v2Length);

            sbyte sign = cos < 0 ? (sbyte)-1 : (sbyte)1;

            if (IsOne(Math.Abs(cos)))
            {
                cos = sign;
            }

            double rad = Math.Acos(cos);

            return RadiansToDegrees(rad);
        }

        public static double Distance(Vector2 v1, Vector2 v2)
        {
            //return Math.Sqrt(System.Math.Pow(v2.X - v1.X, 2) + System.Math.Pow(v2.Y - v1.Y, 2));
            return Math.Sqrt((v2.X - v1.X) * (v2.X - v1.X) + (v2.Y - v1.Y) * (v2.Y - v1.Y));
        }

        public static double Distance(double x1, double y1, double x2, double y2)
        {
            //return Math.Sqrt(System.Math.Pow(x2 - x1, 2) + System.Math.Pow(y2 - y1, 2));
            return Math.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1));
        }
        
        public static double DistanceToPoint(Vector2 start, Vector2 end, Vector2 point)
        {
            double dStart = Distance(point, start);
            double dEnd = Distance(point, end);
            double dStartEnd = Distance(start, end);

            if (dStart >= Distance(dEnd, dStartEnd, 0, 0))
            {
                return dEnd;
            }
            else if (dEnd >= Distance(dStart, dStartEnd, 0 ,0))
            {
                return dStart;
            }
            else
            {
                double a = end.Y - start.Y;
                double b = start.X - end.X;
                double c = -start.X * (end.Y - start.Y) + start.Y * (end.X - start.X);
                double t = Distance(a, b, 0, 0);

                if (c > 0)
                {
                    a = -a;
                    b = -b;
                    c = -c;
                }

                double ret = (a * point.X + b * point.Y + c) / t;

                return Math.Abs(ret);
            }
        }

        public static double PointToLineDistance(Vector2 start, Vector2 end, Vector2 point)
        {
            double res = 0;
            
            if (start != end)
            {
                var a = (end.Y - start.Y) * point.X;
                var b = (end.X - start.X) * point.Y;
                var c = end.X * start.Y;
                var d = end.Y * start.X;

                var e = (end.Y - start.Y) * (end.Y - start.Y);
                var f = (end.X - start.X) * (end.X - start.X);

                var num = Math.Abs(a - b + c - d);
                var den = Math.Sqrt(e + f);

                res = num / den;
            }
            else
            {
                res = (point - start).Length();
            }

            return res;
        }
        
        /// <summary>
        /// Determine the position of the point relative to line
        /// </summary>
        /// <param name="point">Point</param>
        /// <param name="start">Start of line</param>
        /// <param name="end">End of line</param>
        /// <returns>Positive value if on the right of line, negative if on the left</returns>
        private static double DeterminePointPosition (Vector2 point, Vector2 start, Vector2 end)
        {
            return (point.X - end.X) * (start.Y - end.Y) - (start.X - end.X) * (point.Y - end.Y);
        }

        public static bool IsPointInTriangle (Vector2 pt, Vector2 v1, Vector2 v2, Vector2 v3)
        {
            double d1 = DeterminePointPosition(pt, v1, v2);
            double d2 = DeterminePointPosition(pt, v2, v3);
            double d3 = DeterminePointPosition(pt, v3, v1);

            bool hasNeg = (d1 < 0) || (d2 < 0) || (d3 < 0);
            bool hasPos = (d1 > 0) || (d2 > 0) || (d3 > 0);

            return !(hasNeg && hasPos);
        }

        public static bool IsPointInShape(Vector2 pt, List<LineSegment2D> shape)
        {
            bool? side = null;
            
            foreach (var segment in shape)
            {
                var pos = DeterminePointPosition(pt, segment.Start, segment.End);

                if (pos == 0) continue;
                
                var curSide = pos > 0;

                side ??= curSide;

                if ((bool) side ^ curSide) return false;
            }

            return true;
        }
        
        public static List<Vector2> GetBSpline2(IEnumerable<Vector2> controlPoints, uint resolution)
        {
            static Vector2 Curve(Vector2[] controlPoints, int index, double u)
            {
                return (Blend03(u) * controlPoints[index - 1] + Blend13(u) * controlPoints[index] + Blend23(u) * controlPoints[index + 1]);
            }

            static double Blend03(double u)
            {
                return ((1 - u) * (1 - u) / 2.0);
            }

            static double Blend13(double u)
            {
                return (-(u * u) + u + 0.5);
            }

            static double Blend23(double u)
            {
                return ((u * u) / 2);
            }

            if (controlPoints == null) return new List<Vector2>();
            
            var points = controlPoints as Vector2[] ?? Enumerable.ToArray(controlPoints);
            if (points.Length < 3 ||
                resolution < 2)
            {
                // just return empty list
                return new List<Vector2>();
            }

            var curvePoints = new List<Vector2>();

            var start = 1;
            var end = points.Length - 1;

            for (var i = start; i < end; ++i)
            {
                double uDelta = 1.0 / resolution;

                for (double u = 0.0; u < 1.0; u += uDelta)
                {
                    curvePoints.Add(Curve(points, i, u));
                }

                // add last curve point
                if (i == end - 1)
                {
                    curvePoints.Add(Curve(points, i, 1));
                }
            }

            return curvePoints;
        }
        
        // Non-rational overload (all weights = 1): a plain B-spline evaluated with the general basis.
        public static Vector2[] GetNurbsCurve(IEnumerable<Vector2> points, int degree, bool isUniform, double stepSize)
            => GetNurbsCurve(points, null, degree, isUniform, stepSize);

        // RATIONAL: each control point i carries a weight w_i (the "R" in NURBS). A higher weight pulls the curve toward
        // that point; equal weights degenerate to a plain B-spline. Weights are what let a NURBS trace an EXACT conic
        // (circle/ellipse) that a B-spline can only approximate. weights == null or shorter than points -> the missing
        // ones default to 1.
        public static Vector2[] GetNurbsCurve(IEnumerable<Vector2> points, IReadOnlyList<double> weights,
            int degree, bool isUniform, double stepSize)
    {
        var result = new List<Vector2>();
        var hash = new HashSet<Vector2>();
        var pointsArray = points as Vector2[] ?? points.ToArray();

        var knots = CalculateKnots(degree, pointsArray.Length, isUniform);

        for (double i = 0; i < 1; i += stepSize)
        {
            var point = RationalBSplinePoint(pointsArray, weights, degree, knots, i);
            if (!hash.Contains(point))
            {
                hash.Add(point);
                result.Add(point);
            }
        }

        return result.ToArray();
    }

    private static Vector2 RationalBSplinePoint(Vector2[] points, System.Collections.Generic.IReadOnlyList<double> weights,
        int degree, double[] knots, double t)
    {
        double x = 0, y = 0;
        double rationalWeight = 0d;

        for (int i = 0; i < points.Length; i++)
        {
            var w = weights != null && i < weights.Count ? weights[i] : 1.0;
            rationalWeight += Nip(i, degree, knots, t) * w;
        }

        if (rationalWeight == 0) return points[0];

        for (int i = 0; i < points.Length; i++)
        {
            var w = weights != null && i < weights.Count ? weights[i] : 1.0;
            double temp = Nip(i, degree, knots, t) * w / rationalWeight;
            x += points[i].X * temp;
            y += points[i].Y * temp;
        }

        return new Vector2(x, y);
    }

    /// <summary>
    /// This code is translated to C# from the original C++  code given on page 74-75 in "The NURBS Book" by Les Piegl and Wayne Tiller 
    /// </summary>
    /// <param name="i">Current control pont</param>
    /// <param name="p">The piecewise polynomial degree</param>
    /// <param name="u">The knot vector</param>
    /// <param name="t">The value of the current curve point. Valid range from 0 <= u <= 1 </param>
    /// <returns>N_{i,p}(u)</returns>
    private static double Nip(int i, int p, double[] u, double t)
    {
        double[] N = new double[p + 1];
        double saved, temp;

        int m = u.Length - 1;
        if ((i == 0 && t == u[0]) || (i == (m - p - 1) && t == u[m]))
            return 1;

        if (t < u[i] || t >= u[i + p + 1])
            return 0;

        for (int j = 0; j <= p; j++)
        {
            if (t >= u[i + j] && t < u[i + j + 1])
                N[j] = 1d;
            else
                N[j] = 0d;
        }

        for (int k = 1; k <= p; k++)
        {
            if (N[0] == 0)
                saved = 0d;
            else
                saved = ((t - u[i]) * N[0]) / (u[i + k] - u[i]);

            for (int j = 0; j < p - k + 1; j++)
            {
                double Uleft = u[i + j + 1];
                double Uright = u[i + j + k + 1];

                if (N[j + 1] == 0)
                {
                    N[j] = saved;
                    saved = 0d;
                }
                else
                {
                    temp = N[j + 1] / (Uright - Uleft);
                    N[j] = saved + (Uright - t) * temp;
                    saved = (t - Uleft) * temp;
                }
            }
        }
        return N[0];
    }
    private static double[] CalculateKnots(int degree, int controlPointCount, bool isUniform)
    {
        if (degree + 1 > controlPointCount || controlPointCount == 0)
            return Array.Empty<double>();

        int n = controlPointCount;
        int m = n + degree + 1;
        int divisor = m - 1 - 2 * degree;

        var knots = new List<Double>();

        if (isUniform)
        {
            knots.Add(0);
            for (int i = 1; i < m; i++)
            {
                if (i >= m - 1)
                    knots.Add(1);
                else
                {
                    double dividend = m-1;
                    knots.Add(i/dividend);
                }
            }
        }
        else
        {
            knots.Add(0);
            for (int i = 1; i < m; i++)
            {
                if (i <= degree)
                    knots.Add(0);
                else if (i >= m - degree - 1)
                    knots.Add(1);
                else
                {
                    double dividend = i - degree;
                    knots.Add(dividend/divisor);
                }
            }
        }

        return knots.ToArray();
    }

        /// <summary>A Bezier span of ANY degree, sampled along its length. The span is the start point, its controls
        /// and its end - four points is the cubic, three the quadratic, five the quartic, and so on.
        /// <para>De Casteljau rather than the polynomial: it is the same curve, it needs no binomial coefficients, and
        /// it stays numerically well behaved at the degrees where writing the polynomial out starts to lose digits.</para>
        /// </summary>
        public static List<Vector2> GetBezier(IReadOnlyList<Vector2> span, uint sampleRate)
        {
            if (span == null || span.Count < 2) return new List<Vector2>(span ?? []);
            if (sampleRate < 2) return new List<Vector2>(span);

            var sampled = new List<Vector2>((int)sampleRate + 1);
            var work = new Vector2[span.Count];
            var step = 1.0 / sampleRate;

            for (uint i = 0; i <= sampleRate; i++)
            {
                var d = i * step;

                for (var k = 0; k < span.Count; k++) work[k] = span[k];

                for (var level = span.Count - 1; level > 0; level--)
                {
                    for (var k = 0; k < level; k++) work[k] = work[k] + (work[k + 1] - work[k]) * d;
                }

                sampled.Add(work[0]);
            }

            // because of possible float pointing precision issues
            sampled[0] = span[0];
            sampled[sampled.Count - 1] = span[span.Count - 1];

            return sampled;
        }

        public static List<Vector2> GetQuadraticBezier(Vector2 start, Vector2 control, Vector2 end, uint sampleRate)
        {
            if (sampleRate < 2) return new List<Vector2>() { start, control, end };
            
            var bezierPoints = new List<Vector2>();

            var t = 1.0 / sampleRate;

            for (double d = 0; d <= 1; d = Math.Round(d + t, 4))
            {
                var point = Math.Pow(1 - d, 2) * start + 2 * d * (1 - d) * control + Math.Pow(d, 2) * end;
                point = new Vector2(Math.Round(point.X, 4), Math.Round(point.Y, 4));
                bezierPoints.Add(point);
            }
            
            if (bezierPoints.Count == sampleRate) bezierPoints.Add(end);
            
            // because of possible float pointing precision issues
            bezierPoints[0] = start;
            bezierPoints[bezierPoints.Count - 1] = end;
            
            return bezierPoints;
        }
        
        public static List<Vector2> GetCubicBezier(Vector2 start, Vector2 control1, Vector2 control2, Vector2 end, uint sampleRate)
        {
            if (sampleRate < 2) return new List<Vector2>() { start, control1, control2, end };
            
            var bezierPoints = new List<Vector2>();

            var t = 1.0 / sampleRate;

            for (double d = 0; d <= 1; d += t)
            {
                var point = Math.Pow(1 - d, 3) * start +
                                    3 * d * Math.Pow(1 - d, 2) * control1 +
                                    3 * Math.Pow(d, 2) * (1 - d) * control2 +
                                    Math.Pow(d, 3) * end;
                point = new Vector2(Math.Round(point.X, 4), Math.Round(point.Y, 4));
                bezierPoints.Add(point);
            }

            if (bezierPoints.Count == sampleRate) bezierPoints.Add(end);
            
            // because of possible float pointing precision issues
            bezierPoints[0] = start;
            bezierPoints[bezierPoints.Count - 1] = end;
            
            return bezierPoints;
        }

        // Drops points closer than minSpacing to the last KEPT point (the first and last are always kept). Curve
        // tessellators sample by PARAMETER (uniform t), not arc length, so a "slow" stretch of the curve packs samples
        // sub-pixel-close; the GPU stroke expander then builds degenerate/overlapping quads there and the stroke looks
        // torn. Removing the sub-pixel samples leaves clean >= minSpacing segments (works for solid AND dashed strokes).
        public static Vector2[] SimplifyByMinSpacing(System.Collections.Generic.IReadOnlyList<Vector2> points, double minSpacing)
        {
            if (points == null || points.Count == 0) return System.Array.Empty<Vector2>();
            var minSq = minSpacing * minSpacing;
            var result = new System.Collections.Generic.List<Vector2>(points.Count) { points[0] };
            for (var i = 1; i < points.Count - 1; i++)
                if ((points[i] - result[result.Count - 1]).LengthSquared() >= minSq)
                    result.Add(points[i]);
            if (points.Count > 1)
            {
                var last = points[points.Count - 1];
                if ((last - result[result.Count - 1]).LengthSquared() >= minSq) result.Add(last);
                else result[result.Count - 1] = last;   // collapse a sub-min final segment onto the true endpoint
            }
            return result.ToArray();
        }

        // Total length of a polyline (sum of segment lengths).
        public static double PolylineLength(System.Collections.Generic.IReadOnlyList<Vector2> poly)
        {
            double len = 0;
            for (var i = 1; i < (poly?.Count ?? 0); i++) len += (poly[i] - poly[i - 1]).Length();
            return len;
        }

        // Resamples a polyline to `count` points spaced EVENLY BY ARC LENGTH (both endpoints preserved). Curve tessellators
        // sample by PARAMETER (uniform t), which bunches points where the curve is "slow" (sub-pixel-close -> the stroke
        // expander tears) and starves the "fast" flats (angular). Walking the flattened curve by arc length instead gives
        // uniform spacing: smooth strokes, no sub-pixel bunching, no under-sampled straights.
        public static Vector2[] ResampleByArcLength(System.Collections.Generic.IReadOnlyList<Vector2> poly, int count)
        {
            if (poly == null || poly.Count == 0) return System.Array.Empty<Vector2>();
            if (count < 2 || poly.Count < 2) return new[] { poly[0] };

            var cum = new double[poly.Count];
            for (var i = 1; i < poly.Count; i++) cum[i] = cum[i - 1] + (poly[i] - poly[i - 1]).Length();
            var total = cum[poly.Count - 1];
            if (total <= 1e-9) return new[] { poly[0], poly[poly.Count - 1] };

            var result = new Vector2[count];
            result[0] = poly[0];
            var seg = 1;
            for (var k = 1; k < count - 1; k++)
            {
                var target = total * k / (count - 1);
                while (seg < poly.Count - 1 && cum[seg] < target) seg++;
                var span = cum[seg] - cum[seg - 1];
                var t = span > 1e-9 ? (target - cum[seg - 1]) / span : 0.0;
                result[k] = poly[seg - 1] + (poly[seg] - poly[seg - 1]) * t;
            }
            result[count - 1] = poly[poly.Count - 1];
            return result;
        }

        private static (double startAngle, double sweepAngle, Vector2 center) GetArcData(Vector2 start, Vector2 end, double radius, bool clockwise = true)
        {
            var d = new Vector2((end.X - start.X) * 0.5, (end.Y - start.Y) * 0.5);
            var a = d.Length();
            
            if (a > radius) radius = a; //return null;

            var side = clockwise ? 1 : -1;

            var oc = Math.Sqrt(radius * radius - a * a);
            var ox = (start.X + end.X) * 0.5 - side * oc * d.Y / a;
            var oy = (start.Y + end.Y) * 0.5 + side * oc * d.X / a;

            var startAngle = Math.Atan2(start.Y - oy, start.X - ox) * 180.0 / Math.PI;
            var sweepAngle = side * 2.0 * Math.Asin(a / radius) * 180.0 / Math.PI;

            return (startAngle, sweepAngle, new Vector2(ox, oy));
        }

        public static List<Vector2> GetArcPoints(Vector2 start, Vector2 end, double radius, bool convex, double sampleRate)
        {
            var points = new List<Vector2>();

            var (arcStartAngle, arcSweepAngle, arcCenter) = GetArcData(start, end, radius, convex);

            var startAngle = convex ? Math.Min(arcStartAngle, arcStartAngle + arcSweepAngle) : Math.Max(arcStartAngle, arcStartAngle + arcSweepAngle);
            var endAngle = convex ? Math.Max(arcStartAngle, arcStartAngle + arcSweepAngle) : Math.Min(arcStartAngle, arcStartAngle + arcSweepAngle);

            Func<double, bool> compare = convex ? x => x <= DegreesToRadians(endAngle) : x => x >= DegreesToRadians(endAngle);
            
            sampleRate *= convex ? 1 : -1;

            for (double angle = DegreesToRadians(startAngle); compare(angle); angle += sampleRate) //You are using radians so you will have to increase by a very small amount
            {
                //This will have the coordinates  you want to draw a point at
                var point = new Vector2(arcCenter.X + radius * Math.Cos(angle),
                    arcCenter.Y + radius * Math.Sin(angle));
                point.X = Math.Round(point.X, 4);
                point.Y = Math.Round(point.Y, 4);
                points.Add(point);
            }
            
            points[0] = start;
            points[points.Count - 1] = end;

            return points;
        }

    }
}
