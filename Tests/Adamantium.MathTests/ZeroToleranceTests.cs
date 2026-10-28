using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.MathTests;

[TestFixture]
public class ZeroToleranceTests
{
    [TestCase(0f, true)]
    [TestCase(1e-7f, true)]
    [TestCase(-5e-7f, true)]
    [TestCase(1e-5f, false)]
    [TestCase(-0.01f, false)]
    public void IsZero_MeansCloseToZero(float value, bool zero)
    {
        Assert.That(MathHelper.Scalar.IsZero(value), Is.EqualTo(zero));
        Assert.That(MathHelper.Scalar.IsZero((double)value * 1e-3), Is.EqualTo(zero));
    }
}
