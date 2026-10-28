using System;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.MathTests
{
/*
 * Test suite to demonstrate a good method for comparing floating-point values
 * using an epsilon. Run via JUnit 4.
 *
 * Note: this function attempts a "one size fits all" solution. There may be
 * some edge cases for which it still produces unexpected results, and some of
 * the tests it was developed to pass probably specify behavior that is not
 * appropriate for some applications, especially concerning very small values
 * with differing signs.
 *
 * Before using it, make sure it's appropriate for your application!
 *
 * From http://floating-point-gui.de
 *
 * @author Michael Borgwardt
 */
    public class FloatNearlyEqualsTest
    {
        public static bool NearlyEqual(float a, float b, float epsilon)
        {
            return MathHelper.Scalar.WithinEpsilon(a, b, epsilon);
        }

        public static bool NearlyEqual(float a, float b)
        {
            return MathHelper.Scalar.WithinEpsilon(a, b, 1e-6f);
        }


        #region Float
        /** Regular large numbers - generally not problematic */
        [Test]
        public void Big()
        {
            Assert.IsTrue(NearlyEqual(1000000f, 1000001f));
            Assert.IsTrue(NearlyEqual(1000001f, 1000000f));
            Assert.IsFalse(NearlyEqual(10000f, 10001f));
            Assert.IsFalse(NearlyEqual(10001f, 10000f));
        }

        /** Negative large numbers */
        [Test]
        public void BigNeg()
        {
            Assert.IsTrue(NearlyEqual(-1000000f, -1000001f));
            Assert.IsTrue(NearlyEqual(-1000001f, -1000000f));
            Assert.IsFalse(NearlyEqual(-10000f, -10001f));
            Assert.IsFalse(NearlyEqual(-10001f, -10000f));
        }

        /** Numbers around 1 */
        [Test]
        public void Mid()
        {
            Assert.IsTrue(NearlyEqual(1.0000001f, 1.0000002f));
            Assert.IsTrue(NearlyEqual(1.0000002f, 1.0000001f));
            Assert.IsFalse(NearlyEqual(1.0002f, 1.0001f));
            Assert.IsFalse(NearlyEqual(1.0001f, 1.0002f));
        }

        /** Numbers around -1 */
        [Test]
        public void MidNeg()
        {
            Assert.IsTrue(NearlyEqual(-1.000001f, -1.000002f));
            Assert.IsTrue(NearlyEqual(-1.000002f, -1.000001f));
            Assert.IsFalse(NearlyEqual(-1.0001f, -1.0002f));
            Assert.IsFalse(NearlyEqual(-1.0002f, -1.0001f));
        }

        /** Numbers between 1 and 0 */
        [Test]
        public void Small()
        {
            Assert.IsTrue(NearlyEqual(0.000000001000001f, 0.000000001000002f));
            Assert.IsTrue(NearlyEqual(0.000000001000002f, 0.000000001000001f));
            Assert.IsFalse(NearlyEqual(0.000000000001002f, 0.000000000001001f));
            Assert.IsFalse(NearlyEqual(0.000000000001001f, 0.000000000001002f));
        }

        /** Numbers between -1 and 0 */
        [Test]
        public void SmallNeg()
        {
            Assert.IsTrue(NearlyEqual(-0.000000001000001f, -0.000000001000002f));
            Assert.IsTrue(NearlyEqual(-0.000000001000002f, -0.000000001000001f));
            Assert.IsFalse(NearlyEqual(-0.000000000001002f, -0.000000000001001f));
            Assert.IsFalse(NearlyEqual(-0.000000000001001f, -0.000000000001002f));
        }

        /** Small differences away from zero */
        [Test]
        public void SmallDiffs()
        {
            Assert.IsTrue(NearlyEqual(0.3f, 0.30000003f));
            Assert.IsTrue(NearlyEqual(-0.3f, -0.30000003f));
        }

        /** Comparisons involving zero. MathHelper.WithinEpsilon is deliberately absolute near zero (for the triangulator),
         *  so a value within epsilon of zero is nearly equal to it. */
        [Test]
        public void Zero()
        {
            Assert.IsTrue(NearlyEqual(0.0f, 0.0f));
            Assert.IsTrue(NearlyEqual(0.0f, -0.0f));
            Assert.IsTrue(NearlyEqual(-0.0f, -0.0f));

            // Within the default 1e-6 of zero -> equal (absolute near-zero comparison).
            Assert.IsTrue(NearlyEqual(0.0000001f, 0.0f));
            Assert.IsTrue(NearlyEqual(0.0f, -0.0000001f));
            // Clearly beyond 1e-6 -> not equal.
            Assert.IsFalse(NearlyEqual(0.01f, 0.0f));
            Assert.IsFalse(NearlyEqual(0.0f, 0.01f));

            // Explicit epsilon.
            Assert.IsTrue(NearlyEqual(1e-9f, 0.0f, 1e-6f));
            Assert.IsTrue(NearlyEqual(0.0f, -1e-9f, 1e-6f));
            Assert.IsFalse(NearlyEqual(1e-3f, 0.0f, 1e-6f));
            Assert.IsFalse(NearlyEqual(0.0f, -1e-3f, 1e-6f));
        }

        /**
         * Comparisons involving extreme values (overflow potential)
         */
        [Test]
        public void ExtremeMax()
        {
            Assert.IsTrue(NearlyEqual(Single.MaxValue, Single.MaxValue));
            Assert.IsFalse(NearlyEqual(Single.MaxValue, -Single.MaxValue));
            Assert.IsFalse(NearlyEqual(-Single.MaxValue, Single.MaxValue));
            Assert.IsFalse(NearlyEqual(Single.MaxValue, Single.MaxValue / 2));
            Assert.IsFalse(NearlyEqual(Single.MaxValue, -Single.MaxValue / 2));
            Assert.IsFalse(NearlyEqual(-Single.MaxValue, Single.MaxValue / 2));
        }

        /**
         * Comparisons involving infinities
         */
        [Test]
        public void Infinities()
        {
            Assert.IsTrue(NearlyEqual(Single.PositiveInfinity, Single.PositiveInfinity));
            Assert.IsTrue(NearlyEqual(Single.NegativeInfinity, Single.NegativeInfinity));
            Assert.IsFalse(NearlyEqual(Single.NegativeInfinity, Single.PositiveInfinity));
            Assert.IsFalse(NearlyEqual(Single.PositiveInfinity, Single.MaxValue));
            Assert.IsFalse(NearlyEqual(Single.NegativeInfinity, -Single.MaxValue));
        }

        /**
         * Comparisons involving NaN values
         */
        [Test]
        public void Nan()
        {
            Assert.IsFalse(NearlyEqual(Single.NaN, Single.NaN));
            Assert.IsFalse(NearlyEqual(Single.NaN, 0.0f));
            Assert.IsFalse(NearlyEqual(-0.0f, Single.NaN));
            Assert.IsFalse(NearlyEqual(Single.NaN, -0.0f));
            Assert.IsFalse(NearlyEqual(0.0f, Single.NaN));
            Assert.IsFalse(NearlyEqual(Single.NaN, Single.PositiveInfinity));
            Assert.IsFalse(NearlyEqual(Single.PositiveInfinity, Single.NaN));
            Assert.IsFalse(NearlyEqual(Single.NaN, Single.NegativeInfinity));
            Assert.IsFalse(NearlyEqual(Single.NegativeInfinity, Single.NaN));
            Assert.IsFalse(NearlyEqual(Single.NaN, Single.MaxValue));
            Assert.IsFalse(NearlyEqual(Single.MaxValue, Single.NaN));
            Assert.IsFalse(NearlyEqual(Single.NaN, -Single.MaxValue));
            Assert.IsFalse(NearlyEqual(-Single.MaxValue, Single.NaN));
            Assert.IsFalse(NearlyEqual(Single.NaN, Single.Epsilon));
            Assert.IsFalse(NearlyEqual(Single.Epsilon, Single.NaN));
            Assert.IsFalse(NearlyEqual(Single.NaN, -Single.Epsilon));
            Assert.IsFalse(NearlyEqual(-Single.Epsilon, Single.NaN));
        }

        /** Comparisons of numbers on opposite sides of 0 */
        [Test]
        public void Opposite()
        {
            Assert.IsFalse(NearlyEqual(1.000000001f, -1.0f));
            Assert.IsFalse(NearlyEqual(-1.0f, 1.000000001f));
            Assert.IsFalse(NearlyEqual(-1.000000001f, 1.0f));
            Assert.IsFalse(NearlyEqual(1.0f, -1.000000001f));
            // Tiny opposite-sign denormals fall in the near-zero band, so they compare equal under the
            // absolute near-zero rule (see the note on Zero).
            Assert.IsTrue(NearlyEqual(10 * Single.Epsilon, 10 * -Single.Epsilon));
            Assert.IsTrue(NearlyEqual(10000 * Single.Epsilon, 10000 * -Single.Epsilon));
        }

        /**
         * The really tricky part - comparisons of numbers very close to zero.
         */
        [Test]
        public void Ulp()
        {
            Assert.IsTrue(NearlyEqual(Single.Epsilon, Single.Epsilon));
            Assert.IsTrue(NearlyEqual(Single.Epsilon, -Single.Epsilon));
            Assert.IsTrue(NearlyEqual(-Single.Epsilon, Single.Epsilon));
            Assert.IsTrue(NearlyEqual(Single.Epsilon, 0));
            Assert.IsTrue(NearlyEqual(0, Single.Epsilon));
            Assert.IsTrue(NearlyEqual(-Single.Epsilon, 0));
            Assert.IsTrue(NearlyEqual(0, -Single.Epsilon));

            Assert.IsFalse(NearlyEqual(0.000000001f, -Single.Epsilon));
            Assert.IsFalse(NearlyEqual(0.000000001f, Single.Epsilon));
            Assert.IsFalse(NearlyEqual(Single.Epsilon, 0.000000001f));
            Assert.IsFalse(NearlyEqual(-Single.Epsilon, 0.000000001f));
        }
        #endregion
    }
}
