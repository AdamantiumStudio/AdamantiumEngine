using System;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.MathTests
{
    public class DoubleNearlyEqualsTest
    {
        public static bool NearlyEqual(double a, double b, double epsilon)
        {
            return MathHelper.Scalar.WithinEpsilon(a, b, epsilon);
        }

        public static bool NearlyEqual(double a, double b)
        {
            return MathHelper.Scalar.WithinEpsilon(a, b, 1e-8);
        }

        #region Double
        /** Regular large numbers - generally not problematic */
        [Test]
        public void Big()
        {
            Assert.IsTrue(NearlyEqual(1000000000000, 1000000000001));
            Assert.IsTrue(NearlyEqual(10000000001, 10000000000));
            Assert.IsFalse(NearlyEqual(100000000, 1000001));
            Assert.IsFalse(NearlyEqual(1000001, 100000));
        }

        /** Negative large numbers */
        [Test]
        public void BigNeg()
        {
            Assert.IsTrue(NearlyEqual(-1000000000000, -1000000000001));
            Assert.IsTrue(NearlyEqual(-10000000001, -10000000000));
            Assert.IsFalse(NearlyEqual(-100000000, -1000001));
            Assert.IsFalse(NearlyEqual(-1000001, -100000));
        }

        /** Numbers around 1 */
        [Test]
        public void Mid()
        {
            Assert.IsTrue(NearlyEqual(1.0000000000001, 1.0000000000002));
            Assert.IsTrue(NearlyEqual(1.0000000000002, 1.0000000000001));
            Assert.IsFalse(NearlyEqual(1.0000002, 1.0000001));
            Assert.IsFalse(NearlyEqual(1.0000001, 1.0000002));
        }

        /** Numbers around -1 */
        [Test]
        public void MidNeg()
        {
            Assert.IsTrue(NearlyEqual(-1.0000000001, -1.0000000002));
            Assert.IsTrue(NearlyEqual(-1.0000000002, -1.0000000001));
            Assert.IsFalse(NearlyEqual(-1.0000001, -1.0000002));
            Assert.IsFalse(NearlyEqual(-1.0000002, -1.0000001));
        }

        /** Numbers between 1 and 0 */
        [Test]
        public void Small()
        {
            Assert.IsTrue(NearlyEqual(0.000000000000000010000000001, 0.000000000000000010000000002));
            Assert.IsTrue(NearlyEqual(0.000000000000000010000000002, 0.000000000000000010000000001));
            Assert.IsFalse(NearlyEqual(0.000000000000000000000100002, 0.000000000000000000000100001));
            Assert.IsFalse(NearlyEqual(0.000000000000000000000100001, 0.000000000000000000000100002));
        }

        /** Numbers between -1 and 0 */
        [Test]
        public void SmallNeg()
        {
            Assert.IsTrue(NearlyEqual(-0.000000000000000010000000001, -0.000000000000000010000000002));
            Assert.IsTrue(NearlyEqual(-0.000000000000000010000000002, -0.000000000000000010000000001));
            Assert.IsFalse(NearlyEqual(-0.000000000000000000000100002, -0.000000000000000000000100001));
            Assert.IsFalse(NearlyEqual(-0.000000000000000000000100001, -0.000000000000000000000100002));
        }

        /** Small differences away rom zero */
        [Test]
        public void SmallDiffs()
        {
            Assert.IsTrue(NearlyEqual(0.3, 0.30000000000003));
            Assert.IsTrue(NearlyEqual(-0.3, -0.30000000000003));

            Assert.IsTrue(NearlyEqual(0.0499999999999758, 0.05, 1e-8));
            Assert.IsTrue(NearlyEqual(-0.0499999999999758, -0.05, 1e-8));
        }

        /** Comparisons involving zero */
        [Test]
        public void Zero()
        {
            Assert.IsTrue(NearlyEqual(0.0, 0.0));
            Assert.IsTrue(NearlyEqual(0.0, -0.0));
            Assert.IsTrue(NearlyEqual(-0.0, -0.0));
            Assert.IsFalse(NearlyEqual(0.00001, 0.0));
            Assert.IsFalse(NearlyEqual(0.0, 0.00001));
            Assert.IsFalse(NearlyEqual(-0.0000001, 0.0));
            Assert.IsFalse(NearlyEqual(0.0, -0.000001));

            Assert.IsTrue(NearlyEqual(0.0, 1e-40, 0.01));
            Assert.IsTrue(NearlyEqual(1e-40, 0.0, 0.01));
            Assert.IsFalse(NearlyEqual(1e-20, 0.0, 0.000000000000000000001));
            Assert.IsFalse(NearlyEqual(0.0, 1e-20, 0.000000000000000000001));

            Assert.IsTrue(NearlyEqual(0.0, -1e-40, 0.1));
            Assert.IsTrue(NearlyEqual(-1e-40, 0.0, 0.1));
            Assert.IsFalse(NearlyEqual(-1e-20, 0.0, 0.00000000000000000000001));
            Assert.IsFalse(NearlyEqual(0.0, -1e-20, 0.00000000000000000000001));

            Assert.IsTrue(NearlyEqual(1.74845540103046E-09, 0, 1e-8));
        }

        /*
         * Comparisons involving extreme values (overlow potential)
         */
        [Test]
        public void ExtremeMax()
        {
            Assert.IsTrue(NearlyEqual(Double.MaxValue, Double.MaxValue));
            Assert.IsFalse(NearlyEqual(Double.MaxValue, -Double.MaxValue));
            Assert.IsFalse(NearlyEqual(-Double.MaxValue, Double.MaxValue));
            Assert.IsFalse(NearlyEqual(Double.MaxValue, Double.MaxValue / 2));
            Assert.IsFalse(NearlyEqual(Double.MaxValue, -Double.MaxValue / 2));
            Assert.IsFalse(NearlyEqual(-Double.MaxValue, Double.MaxValue / 2));
        }

        /**
         * Comparisons involving ininities
         */
        [Test]
        public void Ininities()
        {
            Assert.IsTrue(NearlyEqual(Double.PositiveInfinity, Double.PositiveInfinity));
            Assert.IsTrue(NearlyEqual(Double.NegativeInfinity, Double.NegativeInfinity));
            Assert.IsFalse(NearlyEqual(Double.NegativeInfinity, Double.PositiveInfinity));
            Assert.IsFalse(NearlyEqual(Double.PositiveInfinity, Double.MaxValue));
            Assert.IsFalse(NearlyEqual(Double.NegativeInfinity, -Double.MaxValue));
        }

        /**
         * Comparisons involving NaN values
         */
        [Test]
        public void Nan()
        {
            Assert.IsFalse(NearlyEqual(Double.NaN, Double.NaN));
            Assert.IsFalse(NearlyEqual(Double.NaN, 0.0));
            Assert.IsFalse(NearlyEqual(-0.0, Double.NaN));
            Assert.IsFalse(NearlyEqual(Double.NaN, -0.0));
            Assert.IsFalse(NearlyEqual(0.0, Double.NaN));
            Assert.IsFalse(NearlyEqual(Double.NaN, Double.PositiveInfinity));
            Assert.IsFalse(NearlyEqual(Double.PositiveInfinity, Double.NaN));
            Assert.IsFalse(NearlyEqual(Double.NaN, Double.NegativeInfinity));
            Assert.IsFalse(NearlyEqual(Double.NegativeInfinity, Double.NaN));
            Assert.IsFalse(NearlyEqual(Double.NaN, Double.MaxValue));
            Assert.IsFalse(NearlyEqual(Double.MaxValue, Double.NaN));
            Assert.IsFalse(NearlyEqual(Double.NaN, -Double.MaxValue));
            Assert.IsFalse(NearlyEqual(-Double.MaxValue, Double.NaN));
            Assert.IsFalse(NearlyEqual(Double.NaN, Double.Epsilon));
            Assert.IsFalse(NearlyEqual(Double.Epsilon, Double.NaN));
            Assert.IsFalse(NearlyEqual(Double.NaN, -Double.Epsilon));
            Assert.IsFalse(NearlyEqual(-Double.Epsilon, Double.NaN));
        }

        /** Comparisons o numbers on opposite sides o 0 */
        [Test]
        public void Opposite()
        {
            Assert.IsFalse(NearlyEqual(1.00000001, -1.0));
            Assert.IsFalse(NearlyEqual(-1.0, 1.00000001));
            Assert.IsFalse(NearlyEqual(-1.00000000000000001, 1.0));
            Assert.IsFalse(NearlyEqual(1.0, -1.00000000000000001));
            Assert.IsTrue(NearlyEqual(10 * Double.Epsilon, 10 * -Double.Epsilon));
            Assert.IsTrue(NearlyEqual(100000000 * Double.Epsilon, 100000000 * -Double.Epsilon));
        }

        /**
         * The really tricky part - comparisons of numbers very close to zero.
         */
        [Test]
        public void Ulp()
        {
            Assert.IsTrue(NearlyEqual(Double.Epsilon, Double.Epsilon));
            Assert.IsTrue(NearlyEqual(Double.Epsilon, -Double.Epsilon));
            Assert.IsTrue(NearlyEqual(-Double.Epsilon, Double.Epsilon));
            Assert.IsTrue(NearlyEqual(Double.Epsilon, 0));
            Assert.IsTrue(NearlyEqual(0, Double.Epsilon));
            Assert.IsTrue(NearlyEqual(-Double.Epsilon, 0));
            Assert.IsTrue(NearlyEqual(0, -Double.Epsilon));

            Assert.IsFalse(NearlyEqual(0.00000000000000001, -Double.Epsilon));
            Assert.IsFalse(NearlyEqual(0.00000000000000001, Double.Epsilon));
            Assert.IsFalse(NearlyEqual(Double.Epsilon, 0.00000000000000001));
            Assert.IsFalse(NearlyEqual(-Double.Epsilon, 0.00000000000000001));
        }
        #endregion
    }
}
