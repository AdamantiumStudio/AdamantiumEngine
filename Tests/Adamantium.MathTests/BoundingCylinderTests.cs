using System;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.MathTests;

[TestFixture]
public class BoundingCylinderTests
{
    private static readonly BoundingCylinder Upright = new(new Vector3F(0, -1, 0), new Vector3F(0, 1, 0), 0.5f);

    [TestCase(-5f, 0f, 0f, 1f, 0f, 0f, 4.5f)]
    [TestCase(0f, 5f, 0f, 0f, -1f, 0f, 4f)]
    [TestCase(0.4f, 5f, 0.2f, 0f, -1f, 0f, 4f)]
    [TestCase(-5f, 0.99f, 0f, 1f, 0f, 0f, 4.5f)]
    public void ARay_MeetsTheSideOrAnEnd(float x, float y, float z, float dx, float dy, float dz, float expected)
    {
        var ray = new Ray<float>(new Vector3F(x, y, z), new Vector3F(dx, dy, dz));

        Assert.That(Upright.Intersects(ray, out float distance), Is.True);
        Assert.That(distance, Is.EqualTo(expected).Within(1e-4f));
    }

    [TestCase(-5f, 1.2f, 0f, 1f, 0f, 0f)]
    [TestCase(0.45f, 5f, 0.45f, 0f, -1f, 0f)]
    public void ARayAboveTheEndOrPastTheRim_Misses(float x, float y, float z, float dx, float dy, float dz)
    {
        var ray = new Ray<float>(new Vector3F(x, y, z), new Vector3F(dx, dy, dz));

        Assert.That(Upright.Intersects(ray, out float _), Is.False);
    }

    // Distances count in the ray's own direction, however short: a ray of a ten-thousandth still meets the side.
    [Test]
    public void AShortRay_MeetsTheSideInUnitsOfItsDirection()
    {
        var ray = new Ray<float>(new Vector3F(-5, 0, 0), new Vector3F(1e-4f, 0, 0));

        Assert.That(Upright.Intersects(ray, out float distance), Is.True);
        Assert.That(distance, Is.EqualTo(45000).Within(1));
        Assert.That(new BoundingCapsule(Upright.Start, Upright.End, 0.5f).Intersects(ray, out distance), Is.True);
        Assert.That(distance, Is.EqualTo(45000).Within(1));
    }

    [Test]
    public void ARayFromInside_MeetsItAtItsOrigin()
    {
        var ray = new Ray<float>(new Vector3F(0.1f, 0.9f, 0), Vector3F.UnitX);

        Assert.That(Upright.Intersects(ray, out float distance), Is.True);
        Assert.That(distance, Is.Zero);
    }

    [Test]
    public void Support_IsOnTheRimOfTheEndThatWay()
    {
        var support = Upright.Support(new Vector3F(1, 1, 0));
        var straightUp = Upright.Support(Vector3F.UnitY);

        Assert.That((support - new Vector3F(0.5f, 1, 0)).Length(), Is.LessThan(1e-5f));
        Assert.That(Vector3F.Dot(straightUp, Vector3F.UnitY), Is.EqualTo(1).Within(1e-5f));
    }

    [Test]
    public void TurnedCylinder_HoldsItsRimsInItsBounds_AndContainsOnlyWhatIsInside()
    {
        var turned = new BoundingCylinder(new Vector3F(1, 1, 1), new Vector3F(3, 2, 1), 0.5f);
        var bounds = turned.GetBoundingBox();
        var random = new Random(2);
        for (var index = 0; index < 100; index++)
        {
            var direction = new Vector3F((float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f,
                (float)random.NextDouble() - 0.5f);
            Assert.That(bounds.Contains(turned.Support(direction)), Is.Not.EqualTo(ContainmentType.Disjoint));
        }

        Assert.That(bounds.Maximum.Z - bounds.Minimum.Z, Is.EqualTo(1).Within(1e-5f), "the rims reach a radius each way");
        Assert.That(turned.Contains(turned.Center), Is.EqualTo(ContainmentType.Contains));
        Assert.That(turned.Contains(new Vector3F(0.5f, 0.75f, 1)), Is.EqualTo(ContainmentType.Disjoint), "past an end");
    }
}
